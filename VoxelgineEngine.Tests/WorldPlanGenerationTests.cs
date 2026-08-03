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
		Assert.Equal("51E2B300F32337B3971F20E5CDC88DA8AC73E09B1A5A3299996ED9825EA056A1", Hash(first.Heights.Span));
		Assert.Equal("6FE5B792B210A2392B011A71410547557EA37051046635AD2B37C740D01F10FA", Hash(first.Biomes.Span));
		Assert.Equal("D81C946A320E2285B77D982B1F6747FA6DFC308E4D8E843AA67252D64AC15623", Hash(first.TreeDensity.Span));
		Assert.Equal(Hash(first.Heights.Span), Hash(second.Heights.Span));
		Assert.Equal(Hash(first.Biomes.Span), Hash(second.Biomes.Span));
		Assert.Equal(Hash(first.TreeDensity.Span), Hash(second.TreeDensity.Span));
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
		StructureTemplateDescriptor[] templates = Enum.GetValues<WorldStructureRole>().Select(role => new StructureTemplateDescriptor(
			role.ToString().ToLowerInvariant(), role, 3, 3, 1, 1, [0, 90, 180, 270],
			[
				new("road", WorldFeatureKind.Road, 1, 1, 0, 1),
				new("conduit", WorldFeatureKind.Conduit, 1, 1, 1, 0),
			])).ToArray();
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
			Assert.Equal(first.Routes.Select(RouteSignature), second.Routes.Select(RouteSignature));
		}
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

	private static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));
	private static string RouteSignature(PlannedWorldRoute route) => $"{route.Id}|{route.Kind}|{route.SourceSite}|{route.DestinationSite}|{string.Join(';', route.Cells)}";
}
