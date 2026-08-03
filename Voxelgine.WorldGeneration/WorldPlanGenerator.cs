namespace Voxelgine.WorldGeneration;

public sealed record WorldGenerationProgress(string Stage, double Fraction);

public static class WorldPlanGenerator
{
	private const int MinimumTreeSpacing = 10;
	private const double MountainFeatureExclusionRadius = 0.32;
	private const int MaximumFeatureSlope = 3;

	public static Task<WorldPlan> GenerateAsync(
		WorldGenerationSettings settings,
		IReadOnlyList<StructureTemplateDescriptor>? structures = null,
		string structureCatalogHash = "",
		IProgress<WorldGenerationProgress>? progress = null,
		CancellationToken cancellationToken = default,
		VillagePrefabCatalogDescriptor? villagePrefabs = null) =>
		Task.Run(() => Generate(settings, structures ?? [], structureCatalogHash, progress, cancellationToken, villagePrefabs), cancellationToken);

	public static WorldPlan Generate(
		WorldGenerationSettings settings,
		IReadOnlyList<StructureTemplateDescriptor>? structures = null,
		string structureCatalogHash = "",
		IProgress<WorldGenerationProgress>? progress = null,
		CancellationToken cancellationToken = default,
		VillagePrefabCatalogDescriptor? villagePrefabs = null)
	{
		ArgumentNullException.ThrowIfNull(settings);
		settings.Validate();
		structures ??= [];
		ValidateStructures(structures);

		int count = checked(settings.Width * settings.Length);
		byte[] heights = new byte[count];
		byte[] mask = new byte[count];
		byte[] hillMask = new byte[count];
		byte[] biomes = new byte[count];
		byte[] density = new byte[count];
		SeededNoise terrain = new(settings.Seed ^ 0x1642B17);
		SeededNoise moisture = new(settings.Seed ^ 0x5A17C3D);
		SeededNoise vegetation = new(settings.Seed ^ 0x27D4EB2);
		SeededNoise shoreline = new(settings.Seed ^ 0x6A09E66);

		progress?.Report(new("Terrain", 0));
		GenerateTerrain(settings, terrain, heights, mask, cancellationToken);
		progress?.Report(new("Hydrology", 0.18));
		PlannedPond[] naturalPonds = PlanPonds(settings, terrain, heights, mask, cancellationToken);
		PlannedPond[] lakes = PlanLakes(settings, terrain, heights, mask, naturalPonds, cancellationToken);
		PlannedPond[] hydrology = [.. naturalPonds, .. lakes];
		progress?.Report(new("Structures", 0.38));
		(PlannedWorldSite[] sites, PlannedWorldRoute[] routes) = PlanFeatures(
			settings, structures, heights, mask, hydrology, cancellationToken);
		progress?.Report(new("Villages", 0.56));
		PlannedVillageArea[] villages = PlanVillages(settings, heights, mask, hydrology, sites, routes, cancellationToken);
		PlannedVillageLayout[] villageLayouts = villagePrefabs is null
			? []
			: VillageLayoutPlanner.Plan(settings, villages, villagePrefabs, cancellationToken);
		progress?.Report(new("Hills", 0.68));
		GenerateHills(settings, terrain, heights, mask, hillMask, hydrology, sites, routes, villages, cancellationToken);
		progress?.Report(new("Biomes", 0.78));
		ClassifyBiomes(settings, moisture, shoreline, heights, mask, hydrology, biomes, cancellationToken);
		progress?.Report(new("Tree density", 0.88));
		GenerateTreeDensity(settings, vegetation, biomes, density, hydrology, sites, routes, villages, cancellationToken);
		progress?.Report(new("Validation", 0.94));
		WorldPlan plan = new(settings, heights, biomes, density, mask, hillMask, hydrology, sites, routes, villages,
			structureCatalogHash, villageLayouts, villagePrefabs?.Hash ?? string.Empty);
		_ = DeriveTrees(plan, cancellationToken);
		progress?.Report(new("Complete", 1));
		return plan;
	}

	public static PlannedTree[] DeriveTrees(WorldPlan plan, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(plan);
		HashSet<PlanPoint> excluded = BuildExclusions(plan);
		SeededNoise selector = new(plan.Seed ^ 0x13A7D);
		List<(uint Rank, int X, int Z)> candidates = [];
		for (int x = 4; x < plan.Width - 4; x++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			for (int z = 4; z < plan.Length - 4; z++)
			{
				byte chance = plan.GetTreeDensity(x, z);
				if (chance == 0 || excluded.Contains(new(x, z))) continue;
				uint rank = selector.Hash(x, z, 0x72EE);
				if ((rank & 0xff) >= chance) continue;
				candidates.Add((rank, x, z));
			}
		}

		candidates.Sort(static (a, b) => a.Rank != b.Rank
			? a.Rank.CompareTo(b.Rank)
			: a.X != b.X ? a.X.CompareTo(b.X) : a.Z.CompareTo(b.Z));
		List<PlannedTree> accepted = [];
		Dictionary<(int X, int Z), List<PlannedTree>> spatial = [];
		int spacingSquared = MinimumTreeSpacing * MinimumTreeSpacing;
		foreach ((uint rank, int x, int z) in candidates)
		{
			bool tooClose = false;
			int bucketX = x / MinimumTreeSpacing, bucketZ = z / MinimumTreeSpacing;
			for (int offsetX = -1; offsetX <= 1 && !tooClose; offsetX++)
			for (int offsetZ = -1; offsetZ <= 1 && !tooClose; offsetZ++)
			{
				if (!spatial.TryGetValue((bucketX + offsetX, bucketZ + offsetZ), out List<PlannedTree>? neighbors)) continue;
				foreach (PlannedTree tree in neighbors)
				{
					int dx = tree.X - x, dz = tree.Z - z;
					if (dx * dx + dz * dz < spacingSquared) { tooClose = true; break; }
				}
			}
			if (tooClose) continue;
			PlannedTree acceptedTree = new(x, z, plan.GetHeight(x, z), (byte)((rank >> 8) & 3));
			accepted.Add(acceptedTree);
			if (!spatial.TryGetValue((bucketX, bucketZ), out List<PlannedTree>? bucket)) spatial[(bucketX, bucketZ)] = bucket = [];
			bucket.Add(acceptedTree);
		}
		return accepted.ToArray();
	}

	public static bool IsSolid(WorldPlan plan, int x, int y, int z)
		=> new WorldPlanVolumeSampler(plan).IsSolid(x, y, z);

	public static WorldPlanVolumeSampler CreateVolumeSampler(WorldPlan plan) => new(plan);

	private static void GenerateTerrain(WorldGenerationSettings settings, SeededNoise noise, byte[] heights, byte[] mask, CancellationToken token)
	{
		double centerX = (settings.Width - 1) * 0.5, centerZ = (settings.Length - 1) * 0.5;
		double radiusX = Math.Max(1, settings.Width * 0.49), radiusZ = Math.Max(1, settings.Length * 0.49);
		for (int x = 0; x < settings.Width; x++)
		{
			token.ThrowIfCancellationRequested();
			for (int z = 0; z < settings.Length; z++)
			{
				int index = x * settings.Length + z;
				double nx = (x - centerX) / radiusX, nz = (z - centerZ) / radiusZ;
				double distance = Math.Sqrt(nx * nx + nz * nz);
				double coast = noise.Fractal2D(x * 0.0065, z * 0.0065, 4) * 0.18;
				if (distance > 0.93 + coast) continue;
				double plateauNoise = Math.Round(noise.Fractal2D(x * 0.0025 + 17, z * 0.0025 - 23, 3) * 1.5);
				double mountainT = Math.Clamp((0.30 - distance) / 0.30, 0, 1);
				double summitProfile = Math.Pow(mountainT, 1.75);
				double mountain = 36 * summitProfile;
				double ridge = 1 - Math.Abs(noise.Fractal2D(x * 0.019 + 131, z * 0.019 - 97, 4));
				double centerDetail = summitProfile * ((ridge - 0.46) * 9
					+ noise.Fractal2D(x * 0.041 + 41, z * 0.041 - 37, 3) * 4.5);
				double coastDrop = Math.Max(0, distance - 0.88) / 0.12 * 4;
				int surface = (int)Math.Round(settings.WorldHeight * 0.42 + plateauNoise + mountain + centerDetail - coastDrop);
				surface = Math.Clamp(surface, 5, settings.WorldHeight - 2);
				if (Math.Abs(x - centerX) <= 0.5 && Math.Abs(z - centerZ) <= 0.5) surface = settings.WorldHeight - 2;
				heights[index] = (byte)surface;
				mask[index] = 255;
			}
		}
	}

	private static PlannedPond[] PlanPonds(WorldGenerationSettings settings, SeededNoise noise, byte[] heights, byte[] mask, CancellationToken token)
	{
		List<PlannedPond> ponds = [];
		List<PlanPoint> centers = [];
		for (int x = 12; x < settings.Width - 12; x += 5)
		{
			token.ThrowIfCancellationRequested();
			for (int z = 12; z < settings.Length - 12; z += 5)
			{
				int centerIndex = x * settings.Length + z;
				if (mask[centerIndex] == 0 || IsFeatureTerrainExcluded(settings, heights, mask, x, z)
					|| noise.Sample2D((x + settings.Seed * 7) * 0.015, (z + settings.Seed * 7) * 0.015) < 0.44) continue;
				if (centers.Any(point => SquaredDistance(point.X, point.Z, x, z) < 40 * 40)) continue;
				int centerHeight = heights[centerIndex];
				int waterLevel = centerHeight + 2;
				Queue<PlanPoint> queue = new();
				HashSet<PlanPoint> cells = [];
				queue.Enqueue(new(x, z)); cells.Add(new(x, z));
				bool open = false;
				while (queue.Count > 0 && cells.Count <= 160)
				{
					PlanPoint p = queue.Dequeue();
					foreach (PlanPoint n in Neighbors(p))
					{
						if ((uint)n.X >= (uint)settings.Width || (uint)n.Z >= (uint)settings.Length) { open = true; continue; }
						int ni = n.X * settings.Length + n.Z;
						if (mask[ni] == 0) { open = true; continue; }
						if (heights[ni] >= waterLevel || cells.Contains(n)) continue;
						if (heights[ni] < waterLevel - 4) { open = true; continue; }
						if (Math.Abs(n.X - x) > 10 || Math.Abs(n.Z - z) > 10) { open = true; continue; }
						cells.Add(n); queue.Enqueue(n);
					}
				}
				if (open || cells.Count < 24 || cells.Count > 160
					|| cells.Any(point => IsFeatureTerrainExcluded(settings, heights, mask, point.X, point.Z))) continue;
				PlanPoint3[] pondCells = cells.OrderBy(p => p.X).ThenBy(p => p.Z)
					.Select(p => new PlanPoint3(p.X, heights[p.X * settings.Length + p.Z], p.Z)).ToArray();
				ponds.Add(new(waterLevel, pondCells)); centers.Add(new(x, z));
			}
		}
		return ponds.ToArray();
	}

	private static PlannedPond[] PlanLakes(
		WorldGenerationSettings settings,
		SeededNoise noise,
		byte[] heights,
		byte[] mask,
		IReadOnlyList<PlannedPond> naturalPonds,
		CancellationToken token)
	{
		int minimumDimension = Math.Min(settings.Width, settings.Length);
		if (minimumDimension < 192) return [];
		int desired = Math.Clamp(minimumDimension / 256, 1, 4);
		HashSet<PlanPoint> occupied = naturalPonds.SelectMany(pond => pond.Cells)
			.Select(cell => new PlanPoint(cell.X, cell.Z)).ToHashSet();
		HashSet<PlanPoint> naturalClearance = ExpandDisk(occupied, 11);
		List<(int X, int Z, int Radius)> acceptedCenters = [];
		SeededNoise picker = new(settings.Seed ^ 0x14A6E5);
		List<PlannedPond> lakes = [];
		for (int ordinal = 0; ordinal < desired; ordinal++)
		{
			bool accepted = false;
			for (int attempt = 0; attempt < 4096 && !accepted; attempt++)
			{
				token.ThrowIfCancellationRequested();
				uint hash = picker.Hash(ordinal * 4099 + attempt, ordinal, 0x1A4E);
				int radiusX = Math.Clamp(minimumDimension / 34 + (int)(hash % 13) - 6, 14, 38);
				int radiusZ = Math.Clamp(minimumDimension / 38 + (int)((hash >> 8) % 13) - 6, 12, 34);
				int margin = Math.Max(radiusX, radiusZ) + 6;
				int x = margin + (int)((hash >> 4) % (uint)Math.Max(1, settings.Width - margin * 2));
				int z = margin + (int)((hash >> 16) % (uint)Math.Max(1, settings.Length - margin * 2));
				int lakeRadius = Math.Max(radiusX, radiusZ);
				if (acceptedCenters.Any(center => SquaredDistance(center.X, center.Z, x, z)
					< (center.Radius + lakeRadius + 12) * (center.Radius + lakeRadius + 12))) continue;
				double nx = (x - (settings.Width - 1) * 0.5) / Math.Max(1, settings.Width * 0.5);
				double nz = (z - (settings.Length - 1) * 0.5) / Math.Max(1, settings.Length * 0.5);
				double radial = Math.Sqrt(nx * nx + nz * nz);
				if (radial is < 0.34 or > 0.74) continue;

				List<(PlanPoint Point, double Distance)> footprint = [];
				bool valid = true;
				long totalHeight = 0;
				for (int dx = -radiusX - 2; dx <= radiusX + 2 && valid; dx++)
				for (int dz = -radiusZ - 2; dz <= radiusZ + 2; dz++)
				{
					int px = x + dx, pz = z + dz;
					int index = px * settings.Length + pz;
					double ellipse = Math.Sqrt(dx * dx / (double)(radiusX * radiusX) + dz * dz / (double)(radiusZ * radiusZ));
					double edge = 1 + noise.Sample2D((px + ordinal * 71) * 0.075, (pz - ordinal * 53) * 0.075) * 0.12;
					if (ellipse > edge)
					{
						if (ellipse <= 1.13 && mask[index] == 0) { valid = false; break; }
						continue;
					}
					PlanPoint point = new(px, pz);
					if (mask[index] == 0 || IsFeatureTerrainExcluded(settings, heights, mask, px, pz)
						|| naturalClearance.Contains(point) || occupied.Contains(point)) { valid = false; break; }
					footprint.Add((point, Math.Clamp(ellipse / edge, 0, 1)));
					totalHeight += heights[index];
				}
				if (!valid || footprint.Count < 128) continue;
				int waterLevel = Math.Clamp((int)Math.Round(totalHeight / (double)footprint.Count), 7, settings.WorldHeight - 3);
				PlanPoint3[] cells = new PlanPoint3[footprint.Count];
				for (int index = 0; index < footprint.Count; index++)
				{
					(PlanPoint point, double distance) = footprint[index];
					int depth = Math.Clamp(1 + (int)Math.Round((1 - distance) * 3), 1, 4);
					int raster = point.X * settings.Length + point.Z;
					heights[raster] = (byte)(waterLevel - depth);
					cells[index] = new(point.X, heights[raster], point.Z);
					occupied.Add(point);
				}
				Array.Sort(cells, static (left, right) => left.X != right.X ? left.X.CompareTo(right.X) : left.Z.CompareTo(right.Z));
				lakes.Add(new(waterLevel, cells, HydrologyKind.Lake));
				acceptedCenters.Add((x, z, lakeRadius));
				accepted = true;
			}
		}
		return lakes.ToArray();
	}

	private static void GenerateHills(
		WorldGenerationSettings settings,
		SeededNoise noise,
		byte[] heights,
		byte[] mask,
		byte[] hillMask,
		IReadOnlyList<PlannedPond> hydrology,
		IReadOnlyList<PlannedWorldSite> sites,
		IReadOnlyList<PlannedWorldRoute> routes,
		IReadOnlyList<PlannedVillageArea> villages,
		CancellationToken token)
	{
		int minimumDimension = Math.Min(settings.Width, settings.Length);
		if (minimumDimension < 192) return;
		HashSet<PlanPoint> reserved = BuildFeatureExclusions(hydrology, sites, routes, villages);
		ushort[] featureDistances = BuildFeatureDistanceField(settings, reserved);
		SeededNoise picker = new(settings.Seed ^ 0x41115EED);
		double regionalFrequency = 5.25 / minimumDimension;
		for (int x = 0; x < settings.Width; x++)
		{
			token.ThrowIfCancellationRequested();
			for (int z = 0; z < settings.Length; z++)
			{
				int index = x * settings.Length + z;
				if (mask[index] == 0) continue;
				double featureBlend = FeatureBlend(featureDistances[index]);
				if (featureBlend <= 0) continue;
				double nx = (x - (settings.Width - 1) * 0.5) / Math.Max(1, settings.Width * 0.5);
				double nz = (z - (settings.Length - 1) * 0.5) / Math.Max(1, settings.Length * 0.5);
				double radial = Math.Sqrt(nx * nx + nz * nz);
				if (radial is < 0.27 or > 0.86) continue;
				double innerBlend = Math.Clamp((radial - 0.27) / 0.09, 0, 1);
				double outerBlend = Math.Clamp((0.86 - radial) / 0.08, 0, 1);
				innerBlend = innerBlend * innerBlend * (3 - 2 * innerBlend);
				outerBlend = outerBlend * outerBlend * (3 - 2 * outerBlend);
				double macro = Math.Max(
					noise.Fractal2D(x * regionalFrequency + 271, z * regionalFrequency - 193, 4),
					noise.Fractal2D(x * regionalFrequency * 0.83 - 743, z * regionalFrequency * 0.83 + 829, 4) * 0.92 - 0.04);
				double amount = Math.Clamp((macro + 0.045) / 0.36, 0, 1);
				if (amount <= 0) continue;
				amount = amount * amount * (3 - 2 * amount) * innerBlend * outerBlend * featureBlend;
				double ridge = 1 - Math.Abs(noise.Fractal2D(x * regionalFrequency * 2.1 + 619, z * regionalFrequency * 2.1 - 487, 3));
				int contribution = Math.Clamp((int)Math.Round(amount * (5 + ridge * 8)), 0, 13);
				if (contribution == 0) continue;
				int raised = Math.Min(settings.WorldHeight - 2, heights[index] + contribution);
				hillMask[index] = (byte)(raised - heights[index]);
				heights[index] = (byte)raised;
			}
		}

		int desired = Math.Clamp(minimumDimension / 70, 3, 14);
		for (int ordinal = 0; ordinal < desired; ordinal++)
		{
			bool accepted = false;
			for (int attempt = 0; attempt < 4096 && !accepted; attempt++)
			{
				token.ThrowIfCancellationRequested();
				uint hash = picker.Hash(ordinal * 4099 + attempt, ordinal, 0x4111);
				int radiusX = Math.Clamp(minimumDimension / 32 + (int)(hash % 21) - 10, 16, 48);
				int radiusZ = Math.Clamp(minimumDimension / 36 + (int)((hash >> 8) % 19) - 9, 14, 42);
				int margin = Math.Max(radiusX, radiusZ) + 3;
				int x = margin + (int)((hash >> 4) % (uint)Math.Max(1, settings.Width - margin * 2));
				int z = margin + (int)((hash >> 17) % (uint)Math.Max(1, settings.Length - margin * 2));
				double nx = (x - (settings.Width - 1) * 0.5) / Math.Max(1, settings.Width * 0.5);
				double nz = (z - (settings.Length - 1) * 0.5) / Math.Max(1, settings.Length * 0.5);
				double radial = Math.Sqrt(nx * nx + nz * nz);
				if (radial is < 0.32 or > 0.78) continue;

				List<(PlanPoint Point, double Distance)> footprint = [];
				int minimum = int.MaxValue, maximum = int.MinValue;
				bool valid = true;
				for (int dx = -radiusX; dx <= radiusX && valid; dx++)
				for (int dz = -radiusZ; dz <= radiusZ; dz++)
				{
					double ellipse = Math.Sqrt(dx * dx / (double)(radiusX * radiusX) + dz * dz / (double)(radiusZ * radiusZ));
					double edge = 1 + noise.Sample2D((x + dx + ordinal * 31) * 0.09, (z + dz - ordinal * 47) * 0.09) * 0.10;
					if (ellipse > edge) continue;
					PlanPoint point = new(x + dx, z + dz);
					int index = point.X * settings.Length + point.Z;
					if (mask[index] == 0) { valid = false; break; }
					int baseHeight = heights[index] - hillMask[index];
					minimum = Math.Min(minimum, baseHeight); maximum = Math.Max(maximum, baseHeight);
					footprint.Add((point, Math.Clamp(ellipse / edge, 0, 1)));
				}
				if (!valid || footprint.Count < 80 || maximum - minimum > 3) continue;
				int amplitude = 5 + (int)((hash >> 25) % 8);
				foreach ((PlanPoint point, double distance) in footprint)
				{
					double t = 1 - distance;
					double dome = t * t * (3 - 2 * t);
					double detail = noise.Sample2D(point.X * 0.11 + ordinal * 13, point.Z * 0.11 - ordinal * 17) * t * 0.8;
					int index = point.X * settings.Length + point.Z;
					int contribution = Math.Clamp((int)Math.Round((amplitude * dome + detail) * FeatureBlend(featureDistances[index])), 0, amplitude);
					int currentContribution = hillMask[index];
					int combinedContribution = Math.Clamp(Math.Max(currentContribution, contribution), 0, 15);
					int raised = Math.Min(settings.WorldHeight - 2, heights[index] - currentContribution + combinedContribution);
					hillMask[index] = (byte)(raised - (heights[index] - currentContribution));
					heights[index] = (byte)raised;
				}
				accepted = true;
			}
		}
	}

	private static void ClassifyBiomes(WorldGenerationSettings settings, SeededNoise moisture, SeededNoise shorelineNoise, byte[] heights, byte[] mask, PlannedPond[] ponds, byte[] biomes, CancellationToken token)
	{
		bool[] coastal = BuildCoastalMask(settings, mask, 6, shorelineNoise);
		HashSet<PlanPoint> water = [];
		HashSet<PlanPoint> shore = [];
		HashSet<PlanPoint> wet = [];
		foreach (PlannedPond pond in ponds)
			foreach (PlanPoint3 cell in pond.Cells)
			{
				water.Add(new(cell.X, cell.Z));
				for (int dx = -3; dx <= 3; dx++) for (int dz = -3; dz <= 3; dz++)
				{
					int distance = Math.Max(Math.Abs(dx), Math.Abs(dz));
					if (distance <= 2) shore.Add(new(cell.X + dx, cell.Z + dz));
					else wet.Add(new(cell.X + dx, cell.Z + dz));
				}
			}

		for (int x = 0; x < settings.Width; x++)
		{
			token.ThrowIfCancellationRequested();
			for (int z = 0; z < settings.Length; z++)
			{
				int index = x * settings.Length + z;
				if (mask[index] == 0) { biomes[index] = (byte)WorldBiome.Void; continue; }
				int height = heights[index];
				int slope = 0;
				foreach (PlanPoint n in Neighbors(new(x, z)))
					if ((uint)n.X < (uint)settings.Width && (uint)n.Z < (uint)settings.Length)
						slope = Math.Max(slope, Math.Abs(height - heights[n.X * settings.Length + n.Z]));
				double moist = moisture.Fractal2D(x * 0.009 + 91, z * 0.009 - 53, 4);
				double coastVariation = SampleShorelineNoise(shorelineNoise, x, z);
				double lowSandHeight = settings.WorldHeight * (0.41 + coastVariation * 0.045);
				PlanPoint point = new(x, z);
				WorldBiome biome = water.Contains(point) ? WorldBiome.Wetland
					: shore.Contains(point) ? WorldBiome.Sand
					: wet.Contains(point) ? WorldBiome.Wetland
					: coastal[index] ? WorldBiome.Sand
					: height <= lowSandHeight || (moist < -0.52 && slope < 3) ? WorldBiome.Sand
					: slope >= 4 || height >= settings.WorldHeight * 0.73 ? WorldBiome.Rocky
					: moist > 0.08 && slope <= 2 ? WorldBiome.Forest
					: WorldBiome.Grassland;
				biomes[index] = (byte)biome;
			}
		}
	}

	private static bool[] BuildCoastalMask(WorldGenerationSettings settings, byte[] mask, int baseWidth, SeededNoise shorelineNoise)
	{
		int maximumWidth = baseWidth + 4;
		byte[] distances = new byte[mask.Length]; Array.Fill(distances, byte.MaxValue);
		Queue<PlanPoint> pending = new();
		for (int x = 0; x < settings.Width; x++)
		for (int z = 0; z < settings.Length; z++)
		{
			int index = x * settings.Length + z;
			if (mask[index] == 0) continue;
			bool boundary = false;
			foreach (PlanPoint neighbor in Neighbors(new(x, z)))
				if ((uint)neighbor.X >= (uint)settings.Width || (uint)neighbor.Z >= (uint)settings.Length
					|| mask[neighbor.X * settings.Length + neighbor.Z] == 0) { boundary = true; break; }
			if (!boundary) continue;
			distances[index] = 0; pending.Enqueue(new(x, z));
		}
		while (pending.TryDequeue(out PlanPoint point))
		{
			int index = point.X * settings.Length + point.Z;
			if (distances[index] >= maximumWidth) continue;
			foreach (PlanPoint neighbor in Neighbors(point))
			{
				if ((uint)neighbor.X >= (uint)settings.Width || (uint)neighbor.Z >= (uint)settings.Length) continue;
				int neighborIndex = neighbor.X * settings.Length + neighbor.Z;
				if (mask[neighborIndex] == 0 || distances[neighborIndex] <= distances[index] + 1) continue;
				distances[neighborIndex] = (byte)(distances[index] + 1); pending.Enqueue(neighbor);
			}
		}
		bool[] coastal = new bool[mask.Length];
		for (int x = 0; x < settings.Width; x++)
		for (int z = 0; z < settings.Length; z++)
		{
			int index = x * settings.Length + z;
			int localWidth = Math.Clamp((int)Math.Round(baseWidth + SampleShorelineNoise(shorelineNoise, x, z) * 4), 3, maximumWidth);
			coastal[index] = distances[index] <= localWidth;
		}
		return coastal;
	}

	private static double SampleShorelineNoise(SeededNoise noise, int x, int z) =>
		noise.Fractal2D(x * 0.012 + 317, z * 0.012 - 211, 4) * 0.72
		+ noise.Fractal2D(x * 0.045 - 89, z * 0.045 + 137, 3) * 0.28;

	private static void GenerateTreeDensity(
		WorldGenerationSettings settings,
		SeededNoise noise,
		byte[] biomes,
		byte[] density,
		PlannedPond[] ponds,
		PlannedWorldSite[] sites,
		PlannedWorldRoute[] routes,
		PlannedVillageArea[] villages,
		CancellationToken token)
	{
		HashSet<PlanPoint> excluded = BuildFeatureExclusions(ponds, sites, routes, villages);
		for (int x = 0; x < settings.Width; x++)
		{
			token.ThrowIfCancellationRequested();
			for (int z = 0; z < settings.Length; z++)
			{
				int index = x * settings.Length + z;
				if (excluded.Contains(new(x, z))) continue;
				WorldBiome biome = (WorldBiome)biomes[index];
				int baseline = biome switch { WorldBiome.Forest => 185, WorldBiome.Grassland => 38, WorldBiome.Wetland => 20, _ => 0 };
				if (baseline == 0) continue;
				int variation = (int)Math.Round(noise.Fractal2D(x * 0.035, z * 0.035, 3) * 45);
				density[index] = (byte)Math.Clamp(baseline + variation, 1, 255);
			}
		}
	}

	private static PlannedVillageArea[] PlanVillages(
		WorldGenerationSettings settings,
		byte[] heights,
		byte[] mask,
		PlannedPond[] ponds,
		PlannedWorldSite[] sites,
		PlannedWorldRoute[] routes,
		CancellationToken token)
	{
		PlanPoint3[] roadCells = routes.Where(route => route.Kind == WorldFeatureKind.Road).SelectMany(route => route.Cells).Distinct()
			.OrderBy(cell => cell.X).ThenBy(cell => cell.Z).ToArray();
		if (roadCells.Length == 0) return [];
		int minimumDimension = Math.Min(settings.Width, settings.Length);
		int desired = minimumDimension >= 512 ? Math.Clamp(minimumDimension / 160, 6, 8) : Math.Clamp(minimumDimension / 128, 1, 3);
		int baseDiameter = Math.Clamp(minimumDimension / 18, 24, 56);
		HashSet<PlanPoint> pondCells = ponds.SelectMany(pond => pond.Cells).Select(cell => new PlanPoint(cell.X, cell.Z)).ToHashSet();
		HashSet<PlanPoint> featureTerrain = BuildFeatureTerrainExclusions(settings, heights, mask);
		SeededNoise picker = new(settings.Seed ^ 0x71A11A6E);
		SeededNoise shapeNoise = new(settings.Seed ^ 0x3C6EF372);
		List<PlannedVillageArea> villages = [];
		for (int ordinal = 0; ordinal < desired; ordinal++)
		{
			bool accepted = false;
			for (int attempt = 0; attempt < 16384 && !accepted; attempt++)
			{
				token.ThrowIfCancellationRequested();
				uint hash = picker.Hash(ordinal * 8191 + attempt, ordinal, 0xB117A6E);
				int minimumRadius = minimumDimension >= 512 ? 20 : 12;
				// Village reservations are twice the previous linear size in both horizontal axes.
				// Keep the original seeded radius variation, then scale it so the same seed retains its shape character.
				int radiusX = Math.Clamp(baseDiameter / 2 + (int)(hash % 13) - 6, minimumRadius, 34) * 2;
				int radiusZ = Math.Clamp(baseDiameter / 2 + (int)((hash >> 8) % 13) - 6, minimumRadius, 34) * 2;
				int margin = (int)Math.Ceiling(Math.Max(radiusX, radiusZ) * 1.28) + 5;
				double sector = Math.Tau / desired;
				double angleJitter = (((hash >> 3) & 0x7FFF) / 32767d - 0.5) * sector * 0.82;
				double angle = (ordinal + 0.5) * sector + angleJitter;
				double placementRadius = 0.43 + ((hash >> 18) & 0x3FFF) / 16383d * 0.27;
				int x = (int)Math.Round((settings.Width - 1) * 0.5 + Math.Cos(angle) * placementRadius * settings.Width * 0.5);
				int z = (int)Math.Round((settings.Length - 1) * 0.5 + Math.Sin(angle) * placementRadius * settings.Length * 0.5);
				if (x < margin || x >= settings.Width - margin || z < margin || z >= settings.Length - margin) continue;
				double nx = (x - (settings.Width - 1) * 0.5) / Math.Max(1, settings.Width * 0.5);
				double nz = (z - (settings.Length - 1) * 0.5) / Math.Max(1, settings.Length * 0.5);
				double radial = Math.Sqrt(nx * nx + nz * nz);
				if (radial is < 0.28 or > 0.78) continue;
				PlanPoint[] footprint = BuildVillageFootprint(shapeNoise, x, z, radiusX, radiusZ, ordinal);
				if (footprint.Length < 192) continue;
				PlanBounds bounds = BoundsOf(footprint);
				if (sites.Any(site => site.Reservation.Intersects(bounds)) || villages.Any(village => village.Reservation.Intersects(bounds))) continue;
				int minimum = int.MaxValue, maximum = int.MinValue; long total = 0; bool valid = true;
				foreach (PlanPoint point in footprint)
				{
					int index = point.X * settings.Length + point.Z;
					if (mask[index] == 0 || featureTerrain.Contains(point) || pondCells.Contains(point)) { valid = false; break; }
					int height = heights[index]; minimum = Math.Min(minimum, height); maximum = Math.Max(maximum, height); total += height;
				}
				if (!valid || maximum - minimum > 1) continue;
				HashSet<PlanPoint> footprintSet = footprint.ToHashSet();
				PlanPoint[] boundary = footprint.Where(point => Neighbors(point).Any(neighbor => !footprintSet.Contains(neighbor)))
					.OrderBy(point => point.X).ThenBy(point => point.Z).ToArray();
				(PlanPoint start, PlanPoint3 nearest) = ClosestVillageRoadPair(boundary, roadCells);
				HashSet<PlanPoint> blocked = pondCells.ToHashSet(); blocked.UnionWith(featureTerrain);
				foreach (PlannedVillageArea village in villages) blocked.UnionWith(village.Footprint);
				blocked.UnionWith(footprint); blocked.Remove(start); blocked.Remove(new(nearest.X, nearest.Z));
				PlanPoint3[] access = FindLandRoute(start, new(nearest.X, nearest.Z), settings, heights, mask, blocked);
				if (access.Length == 0) continue;
				byte surface = (byte)Math.Clamp((int)Math.Round(total / (double)footprint.Length), minimum, maximum);
				villages.Add(new($"village-{ordinal + 1:D2}", bounds, surface, footprint, access)); accepted = true;
			}
		}
		return villages.ToArray();
	}

	private static PlanPoint[] BuildVillageFootprint(SeededNoise noise, int centerX, int centerZ, int radiusX, int radiusZ, int ordinal)
	{
		uint phaseHash = noise.Hash(centerX, centerZ, ordinal ^ 0x56494C4C);
		double phase = phaseHash / (double)uint.MaxValue * Math.Tau;
		int extentX = (int)Math.Ceiling(radiusX * 1.28), extentZ = (int)Math.Ceiling(radiusZ * 1.28);
		HashSet<PlanPoint> candidates = [];
		for (int dx = -extentX; dx <= extentX; dx++)
		for (int dz = -extentZ; dz <= extentZ; dz++)
		{
			double normalizedX = dx / (double)radiusX, normalizedZ = dz / (double)radiusZ;
			double distance = Math.Sqrt(normalizedX * normalizedX + normalizedZ * normalizedZ);
			double angle = Math.Atan2(normalizedZ, normalizedX);
			double lobes = Math.Sin(angle * 3 + phase) * 0.11 + Math.Sin(angle * 5 - phase * 0.63) * 0.055;
			double detail = noise.Fractal2D((centerX + dx + ordinal * 97) * 0.055, (centerZ + dz - ordinal * 71) * 0.055, 3) * 0.11;
			double edge = Math.Clamp(1 + lobes + detail, 0.78, 1.24);
			if (distance <= edge) candidates.Add(new(centerX + dx, centerZ + dz));
		}

		PlanPoint center = new(centerX, centerZ);
		if (!candidates.Contains(center)) return [];
		HashSet<PlanPoint> connected = [center];
		Queue<PlanPoint> pending = new(); pending.Enqueue(center);
		while (pending.TryDequeue(out PlanPoint point))
			foreach (PlanPoint neighbor in Neighbors(point))
				if (candidates.Contains(neighbor) && connected.Add(neighbor)) pending.Enqueue(neighbor);
		return connected.OrderBy(point => point.X).ThenBy(point => point.Z).ToArray();
	}

	private static PlanBounds BoundsOf(IReadOnlyList<PlanPoint> points) => new(
		points.Min(point => point.X), points.Min(point => point.Z), points.Max(point => point.X), points.Max(point => point.Z));

	private static (PlanPoint Village, PlanPoint3 Road) ClosestVillageRoadPair(IEnumerable<PlanPoint> boundary, IReadOnlyList<PlanPoint3> roads)
	{
		PlanPoint bestVillage = default; PlanPoint3 bestRoad = default; long bestDistance = long.MaxValue;
		foreach (PlanPoint point in boundary)
		foreach (PlanPoint3 road in roads)
		{
			long distance = SquaredDistance(point, new(road.X, road.Z));
			if (distance >= bestDistance) continue;
			bestDistance = distance; bestVillage = point; bestRoad = road;
		}
		return (bestVillage, bestRoad);
	}

	private static (PlannedWorldSite[] Sites, PlannedWorldRoute[] Routes) PlanFeatures(WorldGenerationSettings settings, IReadOnlyList<StructureTemplateDescriptor> templates, byte[] heights, byte[] mask, PlannedPond[] ponds, CancellationToken token)
	{
		if (templates.Count == 0) return ([], []);
		SeededNoise picker = new(settings.Seed ^ 0x51AE17);
		Dictionary<WorldStructureRole, int> desired = new()
		{
			[WorldStructureRole.Shelter] = 1, [WorldStructureRole.Relay] = 3,
			[WorldStructureRole.GravityAnchor] = 1, [WorldStructureRole.Shaft] = 3,
			[WorldStructureRole.Support] = 16 + (int)(picker.Hash(0, 0, 0x5A77) % 13),
		};
		List<PlannedWorldSite> sites = [];
		HashSet<PlanPoint> pondCells = ponds.SelectMany(p => p.Cells).Select(c => new PlanPoint(c.X, c.Z)).ToHashSet();
		HashSet<PlanPoint> featureTerrain = BuildFeatureTerrainExclusions(settings, heights, mask);
		foreach ((WorldStructureRole role, int count) in desired)
		{
			StructureTemplateDescriptor? template = templates.Where(t => t.Role == role).OrderBy(t => t.Id, StringComparer.Ordinal).FirstOrDefault();
			if (template is null) continue;
			for (int ordinal = 0; ordinal < count; ordinal++)
			{
				token.ThrowIfCancellationRequested();
				bool placed = false;
				for (int attempt = 0; attempt < 8192 && !placed; attempt++)
				{
					uint hash = picker.Hash(ordinal * 4099 + attempt, (int)role, 0x51E);
					int x = 8 + (int)(hash % (uint)Math.Max(1, settings.Width - 16));
					int z = 8 + (int)((hash >> 12) % (uint)Math.Max(1, settings.Length - 16));
					int rotation = template.AllowedRotations.Length == 0 ? 0 : template.AllowedRotations[(hash >> 24) % template.AllowedRotations.Length];
					if (!TryCreateFeatureSite(settings, template, role, ordinal, x, z, rotation, heights, mask, pondCells, featureTerrain, sites, false, out PlannedWorldSite? site)) continue;
					sites.Add(site); placed = true;
				}
				if (!placed && role != WorldStructureRole.Support)
				{
					int rotation = template.AllowedRotations.FirstOrDefault();
					for (int x = 8; x < settings.Width - 8 && !placed; x += 3)
					for (int z = 8; z < settings.Length - 8 && !placed; z += 3)
						if (TryCreateFeatureSite(settings, template, role, ordinal, x, z, rotation, heights, mask, pondCells, featureTerrain, sites, true, out PlannedWorldSite? site))
						{
							sites.Add(site); placed = true;
						}
					if (!placed) throw new InvalidOperationException($"No safe terrain exists for required {role} feature {ordinal + 1}.");
				}
			}
		}

		List<PlannedWorldRoute> routes = [];
		Dictionary<string, StructureTemplateDescriptor> byTemplate = templates.ToDictionary(template => template.Id, StringComparer.Ordinal);
		foreach (WorldFeatureKind kind in Enum.GetValues<WorldFeatureKind>())
		{
			PlannedWorldSite[] nodes = sites.Where(site => byTemplate[site.TemplateId].Connectors.Any(connector => connector.Kind == kind))
				.OrderBy(site => site.Role == WorldStructureRole.Shelter ? 0 : 1).ThenBy(site => site.Id, StringComparer.Ordinal).ToArray();
			if (nodes.Length < 2) continue;
			HashSet<(int First, int Second)> edges = [];
			HashSet<int> connected = [0];
			while (connected.Count < nodes.Length)
			{
				(int First, int Second) best = default;
				long bestCost = long.MaxValue;
				int next = -1;
				foreach (int first in connected.Order())
				for (int second = 0; second < nodes.Length; second++)
				{
					if (connected.Contains(second)) continue;
					long cost = SquaredDistance(ConnectorEndpoint(nodes[first], byTemplate[nodes[first].TemplateId], kind), ConnectorEndpoint(nodes[second], byTemplate[nodes[second].TemplateId], kind));
					(int First, int Second) edge = first < second ? (first, second) : (second, first);
					if (cost < bestCost || cost == bestCost && CompareEdge(edge, best) < 0) { best = edge; bestCost = cost; next = second; }
				}
				edges.Add(best); connected.Add(next);
			}
			int loopCount = kind == WorldFeatureKind.Road ? 2 : 1;
			foreach ((int First, int Second) edge in AllEdges(nodes.Length).Where(edge => !edges.Contains(edge))
				.OrderBy(edge => SquaredDistance(ConnectorEndpoint(nodes[edge.First], byTemplate[nodes[edge.First].TemplateId], kind), ConnectorEndpoint(nodes[edge.Second], byTemplate[nodes[edge.Second].TemplateId], kind)))
				.ThenBy(edge => edge.First).ThenBy(edge => edge.Second).Take(loopCount)) edges.Add(edge);
			foreach ((int First, int Second) edge in edges.OrderBy(edge => edge.First).ThenBy(edge => edge.Second))
			{
				PlannedWorldSite source = nodes[edge.First], destination = nodes[edge.Second];
				PlanPoint start = ConnectorEndpoint(source, byTemplate[source.TemplateId], kind);
				PlanPoint end = ConnectorEndpoint(destination, byTemplate[destination.TemplateId], kind);
				HashSet<PlanPoint> blocked = pondCells.ToHashSet(); blocked.UnionWith(featureTerrain); blocked.Remove(start); blocked.Remove(end);
				PlanPoint3[] cells = FindLandRoute(start, end, settings, heights, mask, blocked);
				if (cells.Length == 0) throw new InvalidOperationException($"No land route exists between '{source.Id}' and '{destination.Id}'.");
				routes.Add(new($"{kind.ToString().ToLowerInvariant()}-{routes.Count + 1:D3}", kind, source.Id, destination.Id, cells));
			}
		}
		return (sites.ToArray(), routes.ToArray());
	}

	private static bool TryCreateFeatureSite(
		WorldGenerationSettings settings,
		StructureTemplateDescriptor template,
		WorldStructureRole role,
		int ordinal,
		int anchorX,
		int anchorZ,
		int rotation,
		byte[] heights,
		byte[] mask,
		IReadOnlySet<PlanPoint> pondCells,
		IReadOnlySet<PlanPoint> featureTerrain,
		IReadOnlyList<PlannedWorldSite> existing,
		bool emergencyFallback,
		out PlannedWorldSite site)
	{
		site = null!;
		int width = rotation % 180 == 0 ? template.Width : template.Length;
		int length = rotation % 180 == 0 ? template.Length : template.Width;
		PlanBounds reservation = new(anchorX - template.AnchorX - 3, anchorZ - template.AnchorZ - 3,
			anchorX - template.AnchorX + width + 2, anchorZ - template.AnchorZ + length + 2);
		if (reservation.MinimumX < 1 || reservation.MinimumZ < 1
			|| reservation.MaximumX >= settings.Width - 1 || reservation.MaximumZ >= settings.Length - 1
			|| existing.Any(value => value.Reservation.Intersects(reservation))) return false;
		int minimum = int.MaxValue, maximum = int.MinValue;
		for (int x = reservation.MinimumX; x <= reservation.MaximumX; x++)
		for (int z = reservation.MinimumZ; z <= reservation.MaximumZ; z++)
		{
			int index = x * settings.Length + z;
			PlanPoint point = new(x, z);
			if (mask[index] == 0 || pondCells.Contains(point) || featureTerrain.Contains(point)) return false;
			minimum = Math.Min(minimum, heights[index]); maximum = Math.Max(maximum, heights[index]);
		}
		if (maximum - minimum > 5) return false;
		string id = $"{role.ToString().ToLowerInvariant()}-{ordinal + 1:D2}";
		site = new(id, template.Id, role,
			new(anchorX - template.AnchorX, heights[anchorX * settings.Length + anchorZ] + 1, anchorZ - template.AnchorZ),
			rotation, reservation, emergencyFallback);
		return true;
	}

	private static PlanPoint ConnectorEndpoint(PlannedWorldSite site, StructureTemplateDescriptor template, WorldFeatureKind kind)
	{
		StructureConnectorDescriptor connector = template.Connectors.First(value => value.Kind == kind);
		PlanPoint local = Rotate(new(connector.X, connector.Z), template.Width, template.Length, site.Rotation);
		return new(site.Origin.X + local.X, site.Origin.Z + local.Z);
	}

	private static PlanPoint Rotate(PlanPoint point, int width, int length, int rotation) => rotation switch
	{
		0 => point,
		90 => new(length - 1 - point.Z, point.X),
		180 => new(width - 1 - point.X, length - 1 - point.Z),
		270 => new(point.Z, width - 1 - point.X),
		_ => throw new ArgumentOutOfRangeException(nameof(rotation)),
	};

	private static IEnumerable<(int First, int Second)> AllEdges(int count)
	{
		for (int first = 0; first < count; first++) for (int second = first + 1; second < count; second++) yield return (first, second);
	}

	private static int CompareEdge((int First, int Second) left, (int First, int Second) right)
		=> left.First != right.First ? left.First.CompareTo(right.First) : left.Second.CompareTo(right.Second);

	private static long SquaredDistance(PlanPoint left, PlanPoint right)
	{
		long dx = left.X - right.X, dz = left.Z - right.Z;
		return dx * dx + dz * dz;
	}

	private static PlanPoint3[] FindLandRoute(
		PlanPoint start,
		PlanPoint end,
		WorldGenerationSettings settings,
		byte[] heights,
		byte[] mask,
		IReadOnlySet<PlanPoint>? blocked = null)
	{
		if ((uint)start.X >= (uint)settings.Width || (uint)start.Z >= (uint)settings.Length
			|| (uint)end.X >= (uint)settings.Width || (uint)end.Z >= (uint)settings.Length
			|| mask[start.X * settings.Length + start.Z] == 0 || mask[end.X * settings.Length + end.Z] == 0) return [];
		int corridorMargin = Math.Max(48, Math.Min(settings.Width, settings.Length) / 5);
		int minimumX = Math.Max(0, Math.Min(start.X, end.X) - corridorMargin), maximumX = Math.Min(settings.Width - 1, Math.Max(start.X, end.X) + corridorMargin);
		int minimumZ = Math.Max(0, Math.Min(start.Z, end.Z) - corridorMargin), maximumZ = Math.Min(settings.Length - 1, Math.Max(start.Z, end.Z) + corridorMargin);
		int localLength = maximumZ - minimumZ + 1, count = checked((maximumX - minimumX + 1) * localLength);
		int[] costs = new int[count], parents = new int[count]; bool[] closed = new bool[count];
		Array.Fill(costs, int.MaxValue); Array.Fill(parents, -1);
		int startIndex = (start.X - minimumX) * localLength + start.Z - minimumZ;
		int endIndex = (end.X - minimumX) * localLength + end.Z - minimumZ;
		costs[startIndex] = 0;
		PriorityQueue<int, (int Cost, int X, int Z)> open = new();
		open.Enqueue(startIndex, (RouteHeuristic(start.X, start.Z, end.X, end.Z), start.X, start.Z));
		ReadOnlySpan<(int X, int Z)> directions = [(-1, 0), (0, -1), (1, 0), (0, 1)];
		while (open.TryDequeue(out int current, out _))
		{
			if (closed[current]) continue;
			closed[current] = true;
			if (current == endIndex) break;
			int x = current / localLength + minimumX, z = current % localLength + minimumZ;
			int currentHeight = heights[x * settings.Length + z];
			foreach ((int offsetX, int offsetZ) in directions)
			{
				int nextX = x + offsetX, nextZ = z + offsetZ;
				if (nextX < minimumX || nextX > maximumX || nextZ < minimumZ || nextZ > maximumZ) continue;
				int worldIndex = nextX * settings.Length + nextZ;
				if (mask[worldIndex] == 0 || blocked?.Contains(new(nextX, nextZ)) == true) continue;
				int next = (nextX - minimumX) * localLength + nextZ - minimumZ;
				if (closed[next]) continue;
				int candidate = costs[current] + 10 + Math.Abs(heights[worldIndex] - currentHeight) * 18;
				if (candidate >= costs[next]) continue;
				costs[next] = candidate; parents[next] = current;
				open.Enqueue(next, (candidate + RouteHeuristic(nextX, nextZ, end.X, end.Z), nextX, nextZ));
			}
		}
		if (endIndex != startIndex && parents[endIndex] < 0) return [];
		List<PlanPoint3> reversed = [];
		for (int current = endIndex; current >= 0; current = parents[current])
		{
			int x = current / localLength + minimumX, z = current % localLength + minimumZ;
			reversed.Add(new(x, heights[x * settings.Length + z], z));
			if (current == startIndex) break;
		}
		reversed.Reverse(); return reversed.ToArray();
	}

	private static int RouteHeuristic(int x, int z, int endX, int endZ) => (Math.Abs(endX - x) + Math.Abs(endZ - z)) * 10;

	private static HashSet<PlanPoint> BuildExclusions(WorldPlan plan)
	{
		return BuildFeatureExclusions(plan.Ponds, plan.Sites, plan.Routes, plan.Villages);
	}

	private static HashSet<PlanPoint> BuildFeatureExclusions(
		IEnumerable<PlannedPond> ponds,
		IEnumerable<PlannedWorldSite> sites,
		IEnumerable<PlannedWorldRoute> routes,
		IEnumerable<PlannedVillageArea> villages)
	{
		HashSet<PlanPoint> excluded = ponds.SelectMany(p => p.Cells).Select(c => new PlanPoint(c.X, c.Z)).ToHashSet();
		foreach (PlannedWorldSite site in sites)
			for (int x = site.Reservation.MinimumX; x <= site.Reservation.MaximumX; x++)
				for (int z = site.Reservation.MinimumZ; z <= site.Reservation.MaximumZ; z++) excluded.Add(new(x, z));
		foreach (PlannedWorldRoute route in routes)
			foreach (PlanPoint3 cell in route.Cells)
				for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++) excluded.Add(new(cell.X + dx, cell.Z + dz));
		foreach (PlannedVillageArea village in villages)
		{
			foreach (PlanPoint point in village.Footprint) excluded.Add(point);
			foreach (PlanPoint3 cell in village.AccessRoadCells)
				for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++) excluded.Add(new(cell.X + dx, cell.Z + dz));
		}
		return excluded;
	}

	private static ushort[] BuildFeatureDistanceField(WorldGenerationSettings settings, IEnumerable<PlanPoint> reserved)
	{
		const ushort infinity = ushort.MaxValue;
		ushort[] distances = new ushort[checked(settings.Width * settings.Length)];
		Array.Fill(distances, infinity);
		foreach (PlanPoint point in reserved)
			if ((uint)point.X < (uint)settings.Width && (uint)point.Z < (uint)settings.Length)
				distances[point.X * settings.Length + point.Z] = 0;

		for (int x = 0; x < settings.Width; x++)
		for (int z = 0; z < settings.Length; z++)
		{
			int index = x * settings.Length + z;
			ushort best = distances[index];
			if (x > 0) best = MinimumDistance(best, distances[index - settings.Length], 3);
			if (z > 0) best = MinimumDistance(best, distances[index - 1], 3);
			if (x > 0 && z > 0) best = MinimumDistance(best, distances[index - settings.Length - 1], 4);
			if (x > 0 && z + 1 < settings.Length) best = MinimumDistance(best, distances[index - settings.Length + 1], 4);
			distances[index] = best;
		}
		for (int x = settings.Width - 1; x >= 0; x--)
		for (int z = settings.Length - 1; z >= 0; z--)
		{
			int index = x * settings.Length + z;
			ushort best = distances[index];
			if (x + 1 < settings.Width) best = MinimumDistance(best, distances[index + settings.Length], 3);
			if (z + 1 < settings.Length) best = MinimumDistance(best, distances[index + 1], 3);
			if (x + 1 < settings.Width && z + 1 < settings.Length) best = MinimumDistance(best, distances[index + settings.Length + 1], 4);
			if (x + 1 < settings.Width && z > 0) best = MinimumDistance(best, distances[index + settings.Length - 1], 4);
			distances[index] = best;
		}
		return distances;
	}

	private static ushort MinimumDistance(ushort current, ushort neighbor, int cost)
		=> neighbor == ushort.MaxValue ? current : (ushort)Math.Min(current, neighbor + cost);

	private static double FeatureBlend(ushort distance)
	{
		const double fullHeightDistance = 72; // 24 horizontal cells at chamfer cost 3.
		double blend = Math.Clamp(distance / fullHeightDistance, 0, 1);
		return blend * blend * (3 - 2 * blend);
	}

	private static HashSet<PlanPoint> ExpandDisk(IEnumerable<PlanPoint> points, int radius)
	{
		HashSet<PlanPoint> expanded = [];
		int radiusSquared = radius * radius;
		foreach (PlanPoint point in points)
			for (int dx = -radius; dx <= radius; dx++)
			for (int dz = -radius; dz <= radius; dz++)
				if (dx * dx + dz * dz <= radiusSquared) expanded.Add(new(point.X + dx, point.Z + dz));
		return expanded;
	}

	private static void ValidateStructures(IReadOnlyList<StructureTemplateDescriptor> structures)
	{
		if (structures.Select(s => s.Id).Distinct(StringComparer.Ordinal).Count() != structures.Count)
			throw new ArgumentException("Structure template IDs must be unique.", nameof(structures));
		foreach (StructureTemplateDescriptor structure in structures)
			if (string.IsNullOrWhiteSpace(structure.Id) || structure.Width <= 0 || structure.Length <= 0)
				throw new ArgumentException("Structure descriptors require an ID and positive footprint.", nameof(structures));
	}

	private static IEnumerable<PlanPoint> Neighbors(PlanPoint point)
	{
		yield return new(point.X + 1, point.Z); yield return new(point.X - 1, point.Z);
		yield return new(point.X, point.Z + 1); yield return new(point.X, point.Z - 1);
	}

	private static HashSet<PlanPoint> BuildFeatureTerrainExclusions(WorldGenerationSettings settings, byte[] heights, byte[] mask)
	{
		HashSet<PlanPoint> excluded = [];
		for (int x = 0; x < settings.Width; x++)
		for (int z = 0; z < settings.Length; z++)
			if (IsFeatureTerrainExcluded(settings, heights, mask, x, z)) excluded.Add(new(x, z));
		return excluded;
	}

	private static bool IsFeatureTerrainExcluded(WorldGenerationSettings settings, byte[] heights, byte[] mask, int x, int z)
	{
		if ((uint)x >= (uint)settings.Width || (uint)z >= (uint)settings.Length || mask[x * settings.Length + z] == 0) return true;
		double nx = (x - (settings.Width - 1) * 0.5) / Math.Max(1, settings.Width * 0.5);
		double nz = (z - (settings.Length - 1) * 0.5) / Math.Max(1, settings.Length * 0.5);
		if (Math.Sqrt(nx * nx + nz * nz) < MountainFeatureExclusionRadius) return true;
		int height = heights[x * settings.Length + z];
		foreach (PlanPoint neighbor in Neighbors(new(x, z)))
		{
			if ((uint)neighbor.X >= (uint)settings.Width || (uint)neighbor.Z >= (uint)settings.Length
				|| mask[neighbor.X * settings.Length + neighbor.Z] == 0) return true;
			if (Math.Abs(height - heights[neighbor.X * settings.Length + neighbor.Z]) > MaximumFeatureSlope) return true;
		}
		return false;
	}

	private static int SquaredDistance(int ax, int az, int bx, int bz) { int dx = ax - bx, dz = az - bz; return dx * dx + dz * dz; }
}

public sealed class WorldPlanVolumeSampler
{
	private readonly WorldPlan plan;

	internal WorldPlanVolumeSampler(WorldPlan plan)
	{
		this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
	}

	public bool IsSolid(int x, int y, int z)
	{
		if ((uint)x >= (uint)plan.Width || (uint)z >= (uint)plan.Length || y < 0 || y >= plan.WorldHeight || !plan.IsLand(x, z)) return false;
		int surface = plan.GetHeight(x, z);
		if (y > surface) return false;
		double radialX = (x + 0.5 - plan.Width * 0.5) / (plan.Width * 0.5), radialZ = (z + 0.5 - plan.Length * 0.5) / (plan.Length * 0.5);
		double radial = Math.Sqrt(radialX * radialX + radialZ * radialZ);
		double interior = Math.Pow(Math.Clamp(1 - radial / 0.98, 0, 1), 1.35);
		int thickness = 12 + (int)Math.Round(interior * (plan.WorldHeight - 16));
		int underside = Math.Max(0, surface - thickness + 1);
		return y >= underside;
	}
}
