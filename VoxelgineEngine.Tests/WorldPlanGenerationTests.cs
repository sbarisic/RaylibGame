using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Voxelgine.WorldGeneration;

namespace VoxelgineEngine.Tests;

public sealed class WorldPlanGenerationTests
{
	[Fact]
	public void FixedSeedProducesStableSemanticLayers()
	{
		WorldPlan first = WorldPlanGenerator.Generate(new(123456, 96, 80, 64));
		WorldPlan second = WorldPlanGenerator.Generate(new(123456, 96, 80, 64));
		Assert.Equal("FD3B0FF63EE230A957A3F11E689E433D64A95B6F75FCBC0E96470C3D2F4306D5", Hash(first.Heights.Span));
		Assert.Equal("A2AED37F55BC76BAF9531E4B49A105A10AF9CD4581B45E3CB175ABAEF6BF9BB3", Hash(first.Biomes.Span));
		Assert.Equal("64568CC15470C5D7B677333BA936256D66B0D90181D3BE9F765F2E93FAF9DB20", Hash(first.TreeDensity.Span));
		Assert.Equal("F6E28AD1753237D42BE72BE187A88F09A269097EEDA24BCF12908DB6BD361246", Hash(first.HillMask.Span));
		Assert.Equal(Hash(first.Heights.Span), Hash(second.Heights.Span));
		Assert.Equal(Hash(first.Biomes.Span), Hash(second.Biomes.Span));
		Assert.Equal(Hash(first.TreeDensity.Span), Hash(second.TreeDensity.Span));
		Assert.Equal(Hash(first.HillMask.Span), Hash(second.HillMask.Span));
		Assert.Equal(WorldPlanGenerator.DeriveTrees(first), WorldPlanGenerator.DeriveTrees(second));
	}

	[Fact]
	public async Task ParallelSeedsDoNotShareNoiseState()
	{
		WorldGenerationSettings a = new(1001, 80, 80, 64), b = new(2002, 80, 80, 64);
		WorldPlan[] parallel = await Task.WhenAll(WorldPlanGenerator.GenerateAsync(a), WorldPlanGenerator.GenerateAsync(b));
		WorldPlan sequentialA = WorldPlanGenerator.Generate(a), sequentialB = WorldPlanGenerator.Generate(b);
		Assert.Equal(Hash(sequentialA.Heights.Span), Hash(parallel[0].Heights.Span));
		Assert.Equal(Hash(sequentialB.Heights.Span), Hash(parallel[1].Heights.Span));
		Assert.NotEqual(Hash(parallel[0].Heights.Span), Hash(parallel[1].Heights.Span));
	}

	[Fact]
	public void TreesRespectSpacingAndExclusions()
	{
		WorldPlan plan = WorldPlanGenerator.Generate(new(777, 128, 128, 64));
		PlannedTree[] trees = WorldPlanGenerator.DeriveTrees(plan);
		for (int index = 0; index < trees.Length; index++)
		for (int other = index + 1; other < trees.Length; other++)
		{
			int dx = trees[index].X - trees[other].X, dz = trees[index].Z - trees[other].Z;
			Assert.True(dx * dx + dz * dz >= 100);
		}
		HashSet<PlanPoint> water = plan.Ponds.SelectMany(pond => pond.Cells).Select(cell => new PlanPoint(cell.X, cell.Z)).ToHashSet();
		Assert.DoesNotContain(trees, tree => water.Contains(new(tree.X, tree.Z)));
	}

	[Fact]
	public void StructureNetworksUseDeterministicLandRoutes()
	{
		StructureTemplateDescriptor[] templates = CreateTemplates();
		foreach (int seed in new[] { 5, 77, 9001, -4123 })
		{
			WorldGenerationSettings settings = new(seed, 256, 256, 64);
			WorldPlan first = WorldPlanGenerator.Generate(settings, templates, new string('a', 64));
			WorldPlan second = WorldPlanGenerator.Generate(settings, templates, new string('a', 64));
			Assert.Equal(1, first.Sites.Count(site => site.Role == WorldStructureRole.Shelter));
			Assert.Equal(3, first.Sites.Count(site => site.Role == WorldStructureRole.Relay));
			Assert.Equal(1, first.Sites.Count(site => site.Role == WorldStructureRole.GravityAnchor));
			Assert.Equal(3, first.Sites.Count(site => site.Role == WorldStructureRole.Shaft));
			Assert.NotEmpty(first.Routes);
			Assert.All(first.Routes, route => Assert.All(route.Cells, cell => Assert.True(first.IsLand(cell.X, cell.Z))));
			Assert.All(first.Sites, site =>
			{
				for (int x = site.Reservation.MinimumX; x <= site.Reservation.MaximumX; x++)
				for (int z = site.Reservation.MinimumZ; z <= site.Reservation.MaximumZ; z++) AssertFeatureTerrainSafe(first, x, z);
			});
			Assert.All(first.Routes, route => Assert.All(route.Cells, cell => AssertFeatureTerrainSafe(first, cell.X, cell.Z)));
			Assert.Equal(first.Routes.Select(RouteSignature), second.Routes.Select(RouteSignature));
		}
	}

	[Fact]
	public void TerrainHasCentralMountainAndBroadFlatOuterRing()
	{
		WorldPlan plan = WorldPlanGenerator.Generate(new(8142, 512, 512, 64));
		int centerX = plan.Width / 2, centerZ = plan.Length / 2;
		Assert.Equal(62, plan.GetHeight(centerX, centerZ));
		List<int> summit = [], shoulder = [];
		int outerMaximum = 0, outerMinimum = int.MaxValue, outerCells = 0;
		for (int x = 0; x < plan.Width; x++)
		for (int z = 0; z < plan.Length; z++)
		{
			double nx = (x - (plan.Width - 1) * 0.5) / (plan.Width * 0.5);
			double nz = (z - (plan.Length - 1) * 0.5) / (plan.Length * 0.5);
			double radius = Math.Sqrt(nx * nx + nz * nz);
			if (!plan.IsLand(x, z)) continue;
			int baseHeight = plan.GetHeight(x, z) - plan.GetHillHeight(x, z);
			if (radius is >= 0.03 and < 0.10) summit.Add(baseHeight);
			else if (radius is >= 0.10 and < 0.20) shoulder.Add(baseHeight);
			if (radius is < 0.25 or > 0.78) continue;
			outerMinimum = Math.Min(outerMinimum, baseHeight); outerMaximum = Math.Max(outerMaximum, baseHeight); outerCells++;
		}
		Assert.True(summit.Average() >= 50, $"Summit average was {summit.Average():F1}.");
		Assert.True(summit.Max() - summit.Min() >= 8, "The central massif lacks rugged relief.");
		Assert.True(plan.GetHeight(centerX, centerZ) - shoulder.Average() >= 20, "The central summit is not sufficiently pointed.");
		Assert.True(shoulder.Average() >= outerMaximum + 4, "The mountain shoulder does not remain above the outer plateau.");
		Assert.True(outerCells > plan.Width * plan.Length / 3);
		Assert.True(outerMaximum <= 30, $"Outer plateau reached {outerMaximum}.");
		Assert.True(outerMaximum - outerMinimum <= 6, $"Outer plateau range was {outerMinimum}..{outerMaximum}.");
	}

	[Fact]
	public void HeightPreviewExpandsWorldElevationWithoutChangingCanonicalPixels()
	{
		WorldPlan plan = WorldPlanGenerator.Generate(new(8142, 256, 256, 64));
		byte[] canonical = WorldPlanRendering.RenderHeight(plan);
		byte[] preview = WorldPlanRendering.RenderHeightVisualization(plan);
		int x = plan.Width / 2, z = plan.Length / 2, pixel = (z * plan.Width + x) * 4;
		Assert.Equal(plan.GetHeight(x, z), canonical[pixel]);
		Assert.Equal((byte)Math.Round(plan.GetHeight(x, z) * 255d / (plan.WorldHeight - 1)), preview[pixel]);
		Assert.True(preview[pixel] > canonical[pixel]);
		Assert.Equal(canonical[pixel + 3], preview[pixel + 3]);
	}

	[Fact]
	public void LakesAndHillsAreDeterministicAndRespectReservedFeatures()
	{
		WorldPlan plan = WorldPlanGenerator.Generate(new(44512, 512, 512, 64), CreateTemplates(), new string('d', 64));
		PlannedPond[] lakes = plan.Ponds.Where(pond => pond.Kind == HydrologyKind.Lake).ToArray();
		Assert.Equal(2, lakes.Length);
		Assert.All(lakes, lake =>
		{
			Assert.True(lake.Cells.Length >= 128);
			Assert.All(lake.Cells, cell =>
			{
				Assert.InRange(lake.WaterLevel - cell.Y, 1, 4);
				Assert.Equal(WorldBiome.Wetland, plan.GetBiome(cell.X, cell.Z));
				Assert.Equal(0, plan.GetHillHeight(cell.X, cell.Z));
				Assert.Equal(0, plan.GetTreeDensity(cell.X, cell.Z));
			});
		});
		int landCells = 0;
		for (int x = 0; x < plan.Width; x++) for (int z = 0; z < plan.Length; z++) if (plan.IsLand(x, z)) landCells++;
		Assert.True(plan.HillMask.Span.ToArray().Count(value => value != 0) > landCells / 10, "Rolling hills cover too little usable terrain.");
		Assert.InRange(plan.HillMask.Span.ToArray().Max(), (byte)5, (byte)13);

		HashSet<PlanPoint> reserved = plan.Ponds.SelectMany(pond => pond.Cells).Select(cell => new PlanPoint(cell.X, cell.Z)).ToHashSet();
		foreach (PlannedWorldSite site in plan.Sites)
			for (int x = site.Reservation.MinimumX; x <= site.Reservation.MaximumX; x++)
			for (int z = site.Reservation.MinimumZ; z <= site.Reservation.MaximumZ; z++) reserved.Add(new(x, z));
		foreach (PlannedWorldRoute route in plan.Routes)
		foreach (PlanPoint3 cell in route.Cells) AddRoadWidth(reserved, cell.X, cell.Z);
		foreach (PlannedVillageArea village in plan.Villages)
		{
			foreach (PlanPoint point in village.Footprint) reserved.Add(point);
			foreach (PlanPoint3 cell in village.AccessRoadCells) AddRoadWidth(reserved, cell.X, cell.Z);
		}
		Assert.All(reserved.Where(point => (uint)point.X < (uint)plan.Width && (uint)point.Z < (uint)plan.Length),
			point => Assert.Equal(0, plan.GetHillHeight(point.X, point.Z)));
		int maximumNeighborStep = 0;
		for (int x = 0; x < plan.Width; x++) for (int z = 0; z < plan.Length; z++)
		{
			if (x + 1 < plan.Width) maximumNeighborStep = Math.Max(maximumNeighborStep, Math.Abs(plan.GetHillHeight(x, z) - plan.GetHillHeight(x + 1, z)));
			if (z + 1 < plan.Length) maximumNeighborStep = Math.Max(maximumNeighborStep, Math.Abs(plan.GetHillHeight(x, z) - plan.GetHillHeight(x, z + 1)));
		}
		Assert.True(maximumNeighborStep <= 2, $"Hill clearance contains an abrupt {maximumNeighborStep}-block step.");
	}

	[Fact]
	public void VillagesAreLargeFlatConnectedAndExcludedFromTreeDensity()
	{
		WorldPlan plan = WorldPlanGenerator.Generate(new(93217, 640, 640, 64), CreateTemplates(), new string('c', 64));
		Assert.True(plan.Villages.Count >= 6);
		HashSet<PlanPoint> road = plan.Routes.Where(route => route.Kind == WorldFeatureKind.Road)
			.SelectMany(route => route.Cells).Select(cell => new PlanPoint(cell.X, cell.Z)).ToHashSet();
		foreach (PlannedVillageArea village in plan.Villages)
		{
			Assert.True(village.Reservation.MaximumX - village.Reservation.MinimumX + 1 >= 80);
			Assert.True(village.Reservation.MaximumZ - village.Reservation.MinimumZ + 1 >= 80);
			int boundingArea = (village.Reservation.MaximumX - village.Reservation.MinimumX + 1)
				* (village.Reservation.MaximumZ - village.Reservation.MinimumZ + 1);
			Assert.InRange(village.Footprint.Length / (double)boundingArea, 0.55, 0.90);
			Assert.Contains(new PlanPoint(village.AccessRoadCells[^1].X, village.AccessRoadCells[^1].Z), road);
			int minimum = int.MaxValue, maximum = int.MinValue;
			foreach (PlanPoint point in village.Footprint)
			{
				int height = plan.GetHeight(point.X, point.Z); minimum = Math.Min(minimum, height); maximum = Math.Max(maximum, height);
				Assert.Equal(0, plan.GetTreeDensity(point.X, point.Z));
			}
			Assert.True(maximum - minimum <= 1);
		}
		foreach (PlannedWorldRoute route in plan.Routes)
		foreach (PlanPoint3 cell in route.Cells)
		for (int dx = -1; dx <= 1; dx++)
		for (int dz = -1; dz <= 1; dz++)
			if ((uint)(cell.X + dx) < (uint)plan.Width && (uint)(cell.Z + dz) < (uint)plan.Length)
				Assert.Equal(0, plan.GetTreeDensity(cell.X + dx, cell.Z + dz));
	}

	[Fact]
	public void MaterializerStampsThreeBlockWideRoads()
	{
		Voxelgine.Engine.World.Structures.StructureBlueprintCatalog catalog = Voxelgine.Engine.World.Structures.StructureBlueprintCatalog.LoadDirectory(
			Path.Combine(AppContext.BaseDirectory, "data", "world", "structures"));
		WorldPlan plan = Voxelgine.Graphics.WorldPlanMaterializer.GeneratePlan(256, 256, 1776, catalog);
		Voxelgine.Graphics.ChunkMap map = new();
		Voxelgine.Graphics.WorldPlanMaterializer.MaterializeAtomically(map, plan, catalog);
		PlanPoint3 center = plan.Routes.Where(route => route.Kind == WorldFeatureKind.Road).SelectMany(route => route.Cells)
			.First(cell => Enumerable.Range(-1, 3).All(dx => Enumerable.Range(-1, 3).All(dz =>
				(uint)(cell.X + dx) < (uint)plan.Width && (uint)(cell.Z + dz) < (uint)plan.Length && plan.IsLand(cell.X + dx, cell.Z + dz))));
		for (int dx = -1; dx <= 1; dx++)
		for (int dz = -1; dz <= 1; dz++)
		{
			int x = center.X + dx, z = center.Z + dz, y = plan.GetHeight(x, z);
			Assert.Equal(Voxelgine.Engine.BlockType.Gravel, map.GetBlock(x, y, z));
		}
	}

	[Fact]
	public void MaterializerBuildsHillSurfacesAndFillsGeneratedLakes()
	{
		WorldPlan plan = WorldPlanGenerator.Generate(new(66791, 256, 256, 64));
		Voxelgine.Graphics.ChunkMap map = new();
		Voxelgine.Graphics.WorldPlanMaterializer.MaterializeAtomically(map, plan, null);
		PlannedPond lake = Assert.Single(plan.Ponds, pond => pond.Kind == HydrologyKind.Lake);
		PlanPoint3 lakeCell = lake.Cells.OrderBy(cell => cell.Y).First();
		Assert.Equal(Voxelgine.Engine.BlockType.Sand, map.GetBlock(lakeCell.X, lakeCell.Y, lakeCell.Z));
		for (int y = lakeCell.Y + 1; y <= lake.WaterLevel; y++)
			Assert.Equal(Voxelgine.Engine.BlockType.Water, map.GetBlock(lakeCell.X, y, lakeCell.Z));
		(int hillIndex, byte contribution) = plan.HillMask.Span.ToArray().Select((value, index) => (index, value)).MaxBy(pair => pair.value);
		Assert.True(contribution > 0);
		int hillX = hillIndex / plan.Length, hillZ = hillIndex % plan.Length, hillY = plan.GetHeight(hillX, hillZ);
		Assert.NotEqual(Voxelgine.Engine.BlockType.None, map.GetBlock(hillX, hillY, hillZ));
		Assert.Equal(Voxelgine.Engine.BlockType.None, map.GetBlock(hillX, hillY + 1, hillZ));
	}

	[Fact]
	public void GeneratedWaterBodiesHaveTwoBlockSandShores()
	{
		WorldPlan plan = WorldPlanGenerator.Generate(new(66791, 256, 256, 64));
		Assert.NotEmpty(plan.Ponds);
		HashSet<PlanPoint> water = plan.Ponds.SelectMany(pond => pond.Cells)
			.Select(cell => new PlanPoint(cell.X, cell.Z)).ToHashSet();
		HashSet<PlanPoint> expectedShore = [];
		foreach (PlanPoint cell in water)
		for (int dx = -2; dx <= 2; dx++)
		for (int dz = -2; dz <= 2; dz++)
		{
			int x = cell.X + dx, z = cell.Z + dz;
			PlanPoint point = new(x, z);
			if ((uint)x < (uint)plan.Width && (uint)z < (uint)plan.Length && plan.IsLand(x, z) && !water.Contains(point))
				expectedShore.Add(point);
		}
		Assert.NotEmpty(expectedShore);

		Voxelgine.Graphics.ChunkMap map = new();
		Voxelgine.Graphics.WorldPlanMaterializer.MaterializeAtomically(map, plan, null);
		foreach (PlanPoint shore in expectedShore)
		{
			Assert.Equal(WorldBiome.Sand, plan.GetBiome(shore.X, shore.Z));
			Assert.Equal(0, plan.GetTreeDensity(shore.X, shore.Z));
			Assert.Equal(Voxelgine.Engine.BlockType.Sand, map.GetBlock(shore.X, plan.GetHeight(shore.X, shore.Z), shore.Z));
		}
	}

	[Fact]
	public void MaterializerBuildsGrassDirtStoneAndSandStrata()
	{
		WorldPlan plan = WorldPlanGenerator.Generate(new(71037, 256, 256, 64));
		Voxelgine.Graphics.ChunkMap map = new();
		Voxelgine.Graphics.WorldPlanMaterializer.MaterializeAtomically(map, plan, null);
		HashSet<PlanPoint> treeCells = WorldPlanGenerator.DeriveTrees(plan).Select(tree => new PlanPoint(tree.X, tree.Z)).ToHashSet();
		HashSet<PlanPoint> waterCells = plan.Ponds.SelectMany(pond => pond.Cells).Select(cell => new PlanPoint(cell.X, cell.Z)).ToHashSet();
		foreach (PlanPoint coast in from x in Enumerable.Range(0, plan.Width)
			from z in Enumerable.Range(0, plan.Length)
			where plan.IsLand(x, z) && IsIslandBoundary(plan, x, z)
			select new PlanPoint(x, z))
		{
			Assert.Equal(WorldBiome.Sand, plan.GetBiome(coast.X, coast.Z));
			Assert.Equal(0, plan.GetTreeDensity(coast.X, coast.Z));
		}

		PlanPoint plateau = (from x in Enumerable.Range(0, plan.Width)
			from z in Enumerable.Range(0, plan.Length)
			let point = new PlanPoint(x, z)
			where plan.IsLand(x, z) && Radius(plan, x, z) is >= 0.45 and <= 0.65
				&& plan.GetBiome(x, z) is WorldBiome.Grassland or WorldBiome.Forest or WorldBiome.Wetland
				&& !treeCells.Contains(point) && !waterCells.Contains(point)
			select point).First();
		int plateauSurface = plan.GetHeight(plateau.X, plateau.Z);
		Assert.Equal(Voxelgine.Engine.BlockType.Grass, map.GetBlock(plateau.X, plateauSurface, plateau.Z));
		int dirtDepth = 0;
		while (map.GetBlock(plateau.X, plateauSurface - dirtDepth - 1, plateau.Z) == Voxelgine.Engine.BlockType.Dirt) dirtDepth++;
		Assert.InRange(dirtDepth, 10, 14);
		Assert.Equal(Voxelgine.Engine.BlockType.Stone, map.GetBlock(plateau.X, plateauSurface - dirtDepth - 1, plateau.Z));

		PlanPoint beach = (from x in Enumerable.Range(0, plan.Width)
			from z in Enumerable.Range(0, plan.Length)
			let point = new PlanPoint(x, z)
			where plan.IsLand(x, z) && Radius(plan, x, z) >= 0.78 && plan.GetBiome(x, z) == WorldBiome.Sand && !waterCells.Contains(point)
			select point).First();
		int beachSurface = plan.GetHeight(beach.X, beach.Z);
		Assert.Equal(Voxelgine.Engine.BlockType.Sand, map.GetBlock(beach.X, beachSurface, beach.Z));
		int sandDepth = 0;
		while (map.GetBlock(beach.X, beachSurface - sandDepth - 1, beach.Z) == Voxelgine.Engine.BlockType.Sand) sandDepth++;
		Assert.InRange(sandDepth, 3, 5);
		Assert.Equal(Voxelgine.Engine.BlockType.Stone, map.GetBlock(beach.X, beachSurface - sandDepth - 1, beach.Z));

		int centerX = plan.Width / 2, centerZ = plan.Length / 2, centerSurface = plan.GetHeight(centerX, centerZ);
		Assert.Equal(WorldBiome.Rocky, plan.GetBiome(centerX, centerZ));
		for (int y = centerSurface; y >= 0; y--)
			if (WorldPlanGenerator.IsSolid(plan, centerX, y, centerZ)) Assert.Equal(Voxelgine.Engine.BlockType.Stone, map.GetBlock(centerX, y, centerZ));
	}

	private static bool IsIslandBoundary(WorldPlan plan, int x, int z)
	{
		ReadOnlySpan<PlanPoint> neighbors = [new(x - 1, z), new(x + 1, z), new(x, z - 1), new(x, z + 1)];
		foreach (PlanPoint neighbor in neighbors)
			if ((uint)neighbor.X >= (uint)plan.Width || (uint)neighbor.Z >= (uint)plan.Length || !plan.IsLand(neighbor.X, neighbor.Z)) return true;
		return false;
	}

	[Fact]
	public async Task BundleRoundTripsAndRejectsTampering()
	{
		string root = Path.Combine(Path.GetTempPath(), $"world-plan-tests-{Guid.NewGuid():N}");
		try
		{
			WorldPlan source = WorldPlanGenerator.Generate(new(90125, 72, 64, 64));
			string bundle = Path.Combine(root, "plan");
			await WorldPlanBundle.SaveAsync(bundle, source);
			WorldPlan loaded = await WorldPlanBundle.LoadAsync(bundle, source.StructureCatalogHash);
			Assert.Equal(source.Heights.ToArray(), loaded.Heights.ToArray());
			Assert.Equal(source.Biomes.ToArray(), loaded.Biomes.ToArray());
			Assert.Equal(source.TreeDensity.ToArray(), loaded.TreeDensity.ToArray());
			Assert.Equal(source.HillMask.ToArray(), loaded.HillMask.ToArray());
			Assert.True(File.Exists(Path.Combine(bundle, "hill-mask.png")));
			Assert.True(File.Exists(Path.Combine(bundle, "combined.png")));

			string layer = Path.Combine(bundle, "tree-density.png");
			byte[] bytes = await File.ReadAllBytesAsync(layer); bytes[^1] ^= 1; await File.WriteAllBytesAsync(layer, bytes);
			InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() => WorldPlanBundle.LoadAsync(bundle));
			Assert.Contains("checksum", error.Message, StringComparison.OrdinalIgnoreCase);
		}
		finally
		{
			if (Directory.Exists(root)) Directory.Delete(root, true);
		}
	}

	[Fact]
	public async Task CancelledExportLeavesNoDestinationOrTemporaryDirectory()
	{
		string root = Path.Combine(Path.GetTempPath(), $"world-plan-tests-{Guid.NewGuid():N}");
		Directory.CreateDirectory(root);
		try
		{
			using CancellationTokenSource cancellation = new(); cancellation.Cancel();
			await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WorldPlanBundle.SaveAsync(Path.Combine(root, "cancelled"), WorldPlanGenerator.Generate(new(42, 64, 64, 64)), cancellation.Token));
			Assert.Empty(Directory.EnumerateFileSystemEntries(root));
		}
		finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
	}

	[Fact]
	public async Task BundleRejectsCatalogVersionPaletteAndDimensionMismatches()
	{
		string root = Path.Combine(Path.GetTempPath(), $"world-plan-tests-{Guid.NewGuid():N}");
		try
		{
			string catalogHash = new('a', 64);
			WorldPlan source = WorldPlanGenerator.Generate(new(3107, 64, 56, 64), structureCatalogHash: catalogHash);
			string bundle = Path.Combine(root, "plan");
			await WorldPlanBundle.SaveAsync(bundle, source);
			await Assert.ThrowsAsync<InvalidDataException>(() => WorldPlanBundle.LoadAsync(bundle, new string('b', 64)));
			string manifestPath = Path.Combine(bundle, WorldPlanBundle.ManifestFileName);
			string original = await File.ReadAllTextAsync(manifestPath);

			JsonObject version = JsonNode.Parse(original)!.AsObject(); version["formatVersion"] = 99;
			await File.WriteAllTextAsync(manifestPath, version.ToJsonString());
			await Assert.ThrowsAsync<NotSupportedException>(() => WorldPlanBundle.LoadAsync(bundle));

			JsonObject palette = JsonNode.Parse(original)!.AsObject(); palette["biomePalette"]![nameof(WorldBiome.Grassland)] = 0u;
			await File.WriteAllTextAsync(manifestPath, palette.ToJsonString());
			InvalidDataException paletteError = await Assert.ThrowsAsync<InvalidDataException>(() => WorldPlanBundle.LoadAsync(bundle));
			Assert.Contains("palette", paletteError.Message, StringComparison.OrdinalIgnoreCase);

			JsonObject dimensions = JsonNode.Parse(original)!.AsObject();
			dimensions["settings"]!["width"] = source.Width + 1;
			await File.WriteAllTextAsync(manifestPath, dimensions.ToJsonString());
			InvalidDataException dimensionError = await Assert.ThrowsAsync<InvalidDataException>(() => WorldPlanBundle.LoadAsync(bundle));
			Assert.Contains("dimensions", dimensionError.Message, StringComparison.OrdinalIgnoreCase);
		}
		finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
	}

	[Fact]
	public void AtomicMaterializationPreservesExistingMapOnCancellation()
	{
		Voxelgine.Graphics.ChunkMap map = new();
		map.SetBlock(7, 7, 7, Voxelgine.Engine.BlockType.Bricks);
		WorldPlan plan = WorldPlanGenerator.Generate(new(71, 64, 64, 64));
		using CancellationTokenSource cancellation = new(); cancellation.Cancel();
		Assert.ThrowsAny<OperationCanceledException>(() => Voxelgine.Graphics.WorldPlanMaterializer.MaterializeAtomically(map, plan, null, cancellation.Token));
		Assert.Equal(Voxelgine.Engine.BlockType.Bricks, map.GetBlock(7, 7, 7));
	}

	[Fact]
	public void MaterializationMatchesExactSurfaceAndProtectedLayers()
	{
		WorldPlan plan = WorldPlanGenerator.Generate(new(999, 64, 64, 64));
		Voxelgine.Graphics.ChunkMap map = new();
		Voxelgine.Graphics.WorldPlanMaterializer.MaterializeAtomically(map, plan, null);
		for (int x = 0; x < plan.Width; x += 5)
		for (int z = 0; z < plan.Length; z += 5)
		{
			if (!plan.IsLand(x, z)) continue;
			int surface = plan.GetHeight(x, z);
			Assert.NotEqual(Voxelgine.Engine.BlockType.None, map.GetBlock(x, surface, z));
			Assert.False(WorldPlanGenerator.IsSolid(plan, x, surface + 1, z));
			for (int depth = 1; depth <= 3; depth++) Assert.NotEqual(Voxelgine.Engine.BlockType.None, map.GetBlock(x, surface - depth, z));
		}
	}

	[Fact]
	public void FloatingIslandIsContiguousAndTapersTowardTheRimWithoutCaves()
	{
		WorldPlan plan = WorldPlanGenerator.Generate(new(20260803, 512, 512, 64));
		int centerX = plan.Width / 2, centerZ = plan.Length / 2;
		int centerThickness = SolidThickness(plan, centerX, centerZ);
		int outerX = centerX + (int)Math.Round(plan.Width * 0.35), outerZ = centerZ;
		Assert.True(plan.IsLand(outerX, outerZ));
		int outerThickness = SolidThickness(plan, outerX, outerZ);
		Assert.True(centerThickness >= 50, $"Center thickness was only {centerThickness} blocks.");
		Assert.True(outerThickness >= 12, $"Outer thickness was only {outerThickness} blocks.");
		Assert.True(centerThickness >= outerThickness + 25, $"Island did not taper: center={centerThickness}, outer={outerThickness}.");

		for (int x = 0; x < plan.Width; x += 17)
		for (int z = 0; z < plan.Length; z += 19)
		{
			if (!plan.IsLand(x, z)) continue;
			int surface = plan.GetHeight(x, z), firstSolid = -1;
			for (int y = 0; y <= surface; y++) if (WorldPlanGenerator.IsSolid(plan, x, y, z)) { firstSolid = y; break; }
			Assert.True(firstSolid >= 0);
			for (int y = firstSolid; y <= surface; y++) Assert.True(WorldPlanGenerator.IsSolid(plan, x, y, z), $"Cave found at ({x}, {y}, {z}).");
		}
	}

	private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
	private static int SolidThickness(WorldPlan plan, int x, int z)
	{
		int count = 0;
		for (int y = 0; y <= plan.GetHeight(x, z); y++) if (WorldPlanGenerator.IsSolid(plan, x, y, z)) count++;
		return count;
	}
	private static double Radius(WorldPlan plan, int x, int z)
	{
		double nx = (x - (plan.Width - 1) * 0.5) / (plan.Width * 0.5);
		double nz = (z - (plan.Length - 1) * 0.5) / (plan.Length * 0.5);
		return Math.Sqrt(nx * nx + nz * nz);
	}
	private static void AssertFeatureTerrainSafe(WorldPlan plan, int x, int z)
	{
		double nx = (x - (plan.Width - 1) * 0.5) / (plan.Width * 0.5);
		double nz = (z - (plan.Length - 1) * 0.5) / (plan.Length * 0.5);
		Assert.True(Math.Sqrt(nx * nx + nz * nz) >= 0.32, $"Feature entered the central mountain at ({x}, {z}).");
		int height = plan.GetHeight(x, z);
		foreach ((int dx, int dz) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
		{
			int neighborX = x + dx, neighborZ = z + dz;
			Assert.True((uint)neighborX < (uint)plan.Width && (uint)neighborZ < (uint)plan.Length && plan.IsLand(neighborX, neighborZ));
			Assert.True(Math.Abs(height - plan.GetHeight(neighborX, neighborZ)) <= 3, $"Feature crossed a steep slope at ({x}, {z}).");
		}
	}
	private static void AddRoadWidth(HashSet<PlanPoint> points, int x, int z)
	{
		for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++) points.Add(new(x + dx, z + dz));
	}
	private static string RouteSignature(PlannedWorldRoute route) => $"{route.Id}|{route.Kind}|{route.SourceSite}|{route.DestinationSite}|{string.Join(';', route.Cells)}";
	private static StructureTemplateDescriptor[] CreateTemplates() => Enum.GetValues<WorldStructureRole>().Select(role => new StructureTemplateDescriptor(
		role.ToString().ToLowerInvariant(), role, 3, 3, 1, 1, [0, 90, 180, 270],
		[
			new("road", WorldFeatureKind.Road, 1, 1, 0, 1),
			new("conduit", WorldFeatureKind.Conduit, 1, 1, 1, 0),
		])).ToArray();
}
