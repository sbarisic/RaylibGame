namespace Voxelgine.WorldGeneration;

public sealed record WorldGenerationProgress(string Stage, double Fraction);

public static class WorldPlanGenerator
{
	private const int MinimumTreeSpacing = 10;

	public static Task<WorldPlan> GenerateAsync(
		WorldGenerationSettings settings,
		IReadOnlyList<StructureTemplateDescriptor>? structures = null,
		string structureCatalogHash = "",
		IProgress<WorldGenerationProgress>? progress = null,
		CancellationToken cancellationToken = default) =>
		Task.Run(() => Generate(settings, structures ?? [], structureCatalogHash, progress, cancellationToken), cancellationToken);

	public static WorldPlan Generate(
		WorldGenerationSettings settings,
		IReadOnlyList<StructureTemplateDescriptor>? structures = null,
		string structureCatalogHash = "",
		IProgress<WorldGenerationProgress>? progress = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(settings);
		settings.Validate();
		structures ??= [];
		ValidateStructures(structures);

		int count = checked(settings.Width * settings.Length);
		byte[] heights = new byte[count];
		byte[] mask = new byte[count];
		byte[] biomes = new byte[count];
		byte[] density = new byte[count];
		SeededNoise terrain = new(settings.Seed ^ 0x1642B17);
		SeededNoise moisture = new(settings.Seed ^ 0x5A17C3D);
		SeededNoise vegetation = new(settings.Seed ^ 0x27D4EB2);

		progress?.Report(new("Terrain", 0));
		GenerateTerrain(settings, terrain, heights, mask, cancellationToken);
		progress?.Report(new("Hydrology", 0.25));
		PlannedPond[] ponds = PlanPonds(settings, terrain, heights, mask, cancellationToken);
		progress?.Report(new("Biomes", 0.42));
		ClassifyBiomes(settings, moisture, heights, mask, ponds, biomes, cancellationToken);
		progress?.Report(new("Structures", 0.55));
		(PlannedWorldSite[] sites, PlannedWorldRoute[] routes) = PlanFeatures(
			settings, structures, heights, mask, ponds, cancellationToken);
		progress?.Report(new("Villages", 0.72));
		PlannedVillageArea[] villages = PlanVillages(settings, heights, mask, ponds, sites, routes, cancellationToken);
		progress?.Report(new("Tree density", 0.84));
		GenerateTreeDensity(settings, vegetation, biomes, density, ponds, sites, routes, villages, cancellationToken);
		progress?.Report(new("Validation", 0.9));
		WorldPlan plan = new(settings, heights, biomes, density, mask, ponds, sites, routes, villages, structureCatalogHash);
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
				double plateauNoise = Math.Round(noise.Fractal2D(x * 0.0025 + 17, z * 0.0025 - 23, 3) * 2);
				double mountainT = Math.Clamp((0.22 - distance) / 0.22, 0, 1);
				double mountain = 28 * mountainT * mountainT * (3 - 2 * mountainT);
				double centerDetail = noise.Fractal2D(x * 0.028 + 41, z * 0.028 - 37, 3) * (0.35 + mountainT * 1.4);
				double coastDrop = Math.Max(0, distance - 0.88) / 0.12 * 4;
				int surface = (int)Math.Round(settings.WorldHeight * 0.47 + plateauNoise + mountain + centerDetail - coastDrop);
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
				if (mask[centerIndex] == 0 || noise.Sample2D((x + settings.Seed * 7) * 0.015, (z + settings.Seed * 7) * 0.015) < 0.44) continue;
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
				if (open || cells.Count < 24 || cells.Count > 160) continue;
				PlanPoint3[] pondCells = cells.OrderBy(p => p.X).ThenBy(p => p.Z)
					.Select(p => new PlanPoint3(p.X, heights[p.X * settings.Length + p.Z], p.Z)).ToArray();
				ponds.Add(new(waterLevel, pondCells)); centers.Add(new(x, z));
			}
		}
		return ponds.ToArray();
	}

	private static void ClassifyBiomes(WorldGenerationSettings settings, SeededNoise moisture, byte[] heights, byte[] mask, PlannedPond[] ponds, byte[] biomes, CancellationToken token)
	{
		HashSet<PlanPoint> water = [];
		HashSet<PlanPoint> shore = [];
		HashSet<PlanPoint> wet = [];
		foreach (PlannedPond pond in ponds)
			foreach (PlanPoint3 cell in pond.Cells)
			{
				water.Add(new(cell.X, cell.Z));
				for (int dx = -3; dx <= 3; dx++) for (int dz = -3; dz <= 3; dz++)
				{
					int distance = dx * dx + dz * dz;
					if (distance <= 2) shore.Add(new(cell.X + dx, cell.Z + dz));
					else if (distance <= 10) wet.Add(new(cell.X + dx, cell.Z + dz));
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
				PlanPoint point = new(x, z);
				WorldBiome biome = water.Contains(point) || wet.Contains(point) ? WorldBiome.Wetland
					: shore.Contains(point) ? WorldBiome.Sand
					: height <= settings.WorldHeight * 0.42 || (moist < -0.52 && slope < 3) ? WorldBiome.Sand
					: slope >= 4 || height >= settings.WorldHeight * 0.73 ? WorldBiome.Rocky
					: moist > 0.08 && slope <= 2 ? WorldBiome.Forest
					: WorldBiome.Grassland;
				biomes[index] = (byte)biome;
			}
		}
	}

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
		PlanPoint3[] roadCells = routes.Where(route => route.Kind == WorldFeatureKind.Road).SelectMany(route => route.Cells).Distinct().ToArray();
		if (roadCells.Length == 0) return [];
		int minimumDimension = Math.Min(settings.Width, settings.Length);
		int desired = Math.Clamp(minimumDimension / 320, 1, 3);
		int size = Math.Clamp(minimumDimension / 16, 24, 48);
		int half = size / 2;
		HashSet<PlanPoint> pondCells = ponds.SelectMany(pond => pond.Cells).Select(cell => new PlanPoint(cell.X, cell.Z)).ToHashSet();
		SeededNoise picker = new(settings.Seed ^ 0x71A11A6E);
		List<PlannedVillageArea> villages = [];
		for (int ordinal = 0; ordinal < desired; ordinal++)
		{
			bool accepted = false;
			for (int attempt = 0; attempt < 8192 && !accepted; attempt++)
			{
				token.ThrowIfCancellationRequested();
				uint hash = picker.Hash(ordinal * 8191 + attempt, ordinal, 0xB117A6E);
				int x = half + 4 + (int)(hash % (uint)Math.Max(1, settings.Width - size - 8));
				int z = half + 4 + (int)((hash >> 12) % (uint)Math.Max(1, settings.Length - size - 8));
				double nx = (x - (settings.Width - 1) * 0.5) / Math.Max(1, settings.Width * 0.5);
				double nz = (z - (settings.Length - 1) * 0.5) / Math.Max(1, settings.Length * 0.5);
				double radial = Math.Sqrt(nx * nx + nz * nz);
				if (radial is < 0.28 or > 0.78) continue;
				PlanBounds bounds = new(x - half, z - half, x - half + size - 1, z - half + size - 1);
				if (sites.Any(site => site.Reservation.Intersects(bounds)) || villages.Any(village => village.Reservation.Intersects(bounds))) continue;
				int minimum = int.MaxValue, maximum = int.MinValue; long total = 0; bool valid = true;
				for (int bx = bounds.MinimumX; bx <= bounds.MaximumX && valid; bx++)
				for (int bz = bounds.MinimumZ; bz <= bounds.MaximumZ; bz++)
				{
					int index = bx * settings.Length + bz;
					if (mask[index] == 0 || pondCells.Contains(new(bx, bz))) { valid = false; break; }
					int height = heights[index]; minimum = Math.Min(minimum, height); maximum = Math.Max(maximum, height); total += height;
				}
				if (!valid || maximum - minimum > 1) continue;
				PlanPoint start = new(x, z);
				PlanPoint3 nearest = roadCells.OrderBy(cell => SquaredDistance(cell.X, cell.Z, x, z)).ThenBy(cell => cell.X).ThenBy(cell => cell.Z).First();
				PlanPoint3[] access = FindLandRoute(start, new(nearest.X, nearest.Z), settings, heights, mask);
				if (access.Length == 0) continue;
				byte surface = (byte)Math.Clamp((int)Math.Round(total / (double)(size * size)), minimum, maximum);
				villages.Add(new($"village-{ordinal + 1:D2}", bounds, surface, access)); accepted = true;
			}
		}
		return villages.ToArray();
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
		foreach ((WorldStructureRole role, int count) in desired)
		{
			StructureTemplateDescriptor? template = templates.Where(t => t.Role == role).OrderBy(t => t.Id, StringComparer.Ordinal).FirstOrDefault();
			if (template is null) continue;
			for (int ordinal = 0; ordinal < count; ordinal++)
			{
				token.ThrowIfCancellationRequested();
				bool placed = false;
				for (int attempt = 0; attempt < 4096 && !placed; attempt++)
				{
					uint hash = picker.Hash(ordinal * 4099 + attempt, (int)role, 0x51E);
					int x = 8 + (int)(hash % (uint)Math.Max(1, settings.Width - 16));
					int z = 8 + (int)((hash >> 12) % (uint)Math.Max(1, settings.Length - 16));
					int rotation = template.AllowedRotations.Length == 0 ? 0 : template.AllowedRotations[(hash >> 24) % template.AllowedRotations.Length];
					int width = rotation % 180 == 0 ? template.Width : template.Length;
					int length = rotation % 180 == 0 ? template.Length : template.Width;
					PlanBounds reservation = new(x - template.AnchorX - 3, z - template.AnchorZ - 3, x - template.AnchorX + width + 2, z - template.AnchorZ + length + 2);
					if (reservation.MinimumX < 1 || reservation.MinimumZ < 1 || reservation.MaximumX >= settings.Width - 1 || reservation.MaximumZ >= settings.Length - 1) continue;
					if (sites.Any(site => site.Reservation.Intersects(reservation))) continue;
					bool invalid = false; int min = int.MaxValue, max = int.MinValue;
					for (int sx = reservation.MinimumX; sx <= reservation.MaximumX && !invalid; sx++) for (int sz = reservation.MinimumZ; sz <= reservation.MaximumZ; sz++)
					{
						int i = sx * settings.Length + sz;
						if (mask[i] == 0 || pondCells.Contains(new(sx, sz))) { invalid = true; break; }
						min = Math.Min(min, heights[i]); max = Math.Max(max, heights[i]);
					}
					if (invalid || max - min > 5) continue;
					string id = $"{role.ToString().ToLowerInvariant()}-{ordinal + 1:D2}";
					sites.Add(new(id, template.Id, role, new(x - template.AnchorX, heights[x * settings.Length + z] + 1, z - template.AnchorZ), rotation, reservation, false));
					placed = true;
				}
				if (!placed && role != WorldStructureRole.Support)
				{
					int x = settings.Width / 2 + ordinal * 12, z = settings.Length / 2 + (int)role * 12;
					int i = Math.Clamp(x, 0, settings.Width - 1) * settings.Length + Math.Clamp(z, 0, settings.Length - 1);
					sites.Add(new($"{role.ToString().ToLowerInvariant()}-{ordinal + 1:D2}", template.Id, role, new(x, heights[i] + 1, z), 0, new(x - 3, z - 3, x + template.Width + 2, z + template.Length + 2), true));
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
				PlanPoint3[] cells = FindLandRoute(start, end, settings, heights, mask);
				if (cells.Length == 0) throw new InvalidOperationException($"No land route exists between '{source.Id}' and '{destination.Id}'.");
				routes.Add(new($"{kind.ToString().ToLowerInvariant()}-{routes.Count + 1:D3}", kind, source.Id, destination.Id, cells));
			}
		}
		return (sites.ToArray(), routes.ToArray());
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

	private static PlanPoint3[] FindLandRoute(PlanPoint start, PlanPoint end, WorldGenerationSettings settings, byte[] heights, byte[] mask)
	{
		if ((uint)start.X >= (uint)settings.Width || (uint)start.Z >= (uint)settings.Length
			|| (uint)end.X >= (uint)settings.Width || (uint)end.Z >= (uint)settings.Length
			|| mask[start.X * settings.Length + start.Z] == 0 || mask[end.X * settings.Length + end.Z] == 0) return [];
		const int corridorMargin = 48;
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
				if (mask[worldIndex] == 0) continue;
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
			for (int x = village.Reservation.MinimumX; x <= village.Reservation.MaximumX; x++)
			for (int z = village.Reservation.MinimumZ; z <= village.Reservation.MaximumZ; z++) excluded.Add(new(x, z));
			foreach (PlanPoint3 cell in village.AccessRoadCells)
				for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++) excluded.Add(new(cell.X + dx, cell.Z + dz));
		}
		return excluded;
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

	private static int SquaredDistance(int ax, int az, int bx, int bz) { int dx = ax - bx, dz = az - bz; return dx * dx + dz * dz; }
}

public sealed class WorldPlanVolumeSampler
{
	private readonly WorldPlan plan;
	private readonly SeededNoise volume;
	private readonly SeededNoise caves;

	internal WorldPlanVolumeSampler(WorldPlan plan)
	{
		this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
		volume = new(plan.Seed ^ 0x6C8E9CF);
		caves = new(plan.Seed ^ 0x2B992DD);
	}

	public bool IsSolid(int x, int y, int z)
	{
		if ((uint)x >= (uint)plan.Width || (uint)z >= (uint)plan.Length || y < 0 || y >= plan.WorldHeight || !plan.IsLand(x, z)) return false;
		int surface = plan.GetHeight(x, z);
		if (y > surface) return false;
		if (y >= surface - 3) return true;
		double radialX = (x + 0.5 - plan.Width * 0.5) / (plan.Width * 0.5), radialZ = (z + 0.5 - plan.Length * 0.5) / (plan.Length * 0.5);
		double radial = Math.Sqrt(radialX * radialX + radialZ * radialZ);
		double underside = surface - (10 + (1 - Math.Min(1, radial)) * 18);
		if (y < underside) return false;
		return volume.Sample3D(x * 0.02, y * 0.01, z * 0.02) > -0.58 && caves.Sample3D(x * 0.08, y * 0.08, z * 0.08) < 0.65;
	}
}
