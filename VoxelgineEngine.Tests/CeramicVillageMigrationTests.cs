using System.Text.Json;
using Voxelgine.Engine;
using Voxelgine.Engine.World.Structures;
using Voxelgine.Graphics;
using Voxelgine.WorldGeneration;

namespace VoxelgineEngine.Tests;

public sealed class CeramicVillageMigrationTests
{
	private static string DefinitionPath => Path.Combine(AppContext.BaseDirectory, "data", "world", "ceramic-fish", "village.json");

	[Fact]
	public void ProductionDefinitionLoadsWithStableVoxelContract()
	{
		CeramicVillageCatalog first = CeramicVillageCatalog.Load(DefinitionPath);
		CeramicVillageCatalog second = CeramicVillageCatalog.Load(DefinitionPath);
		Assert.Equal(CeramicFishDefinition.CurrentFormatVersion, first.Definition.FormatVersion);
		Assert.Equal(first.Hash, second.Hash);
		Assert.All(first.Prefabs, prefab =>
		{
			Assert.Equal(3, prefab.SizeX);
			Assert.Equal(5, prefab.SizeY);
			Assert.Equal(3, prefab.SizeZ);
			Assert.Equal(4, prefab.Sockets.Count);
		});
		Assert.Contains(first.Prefabs, prefab => prefab.Tags.Contains("gate"));
		Assert.Contains(first.Prefabs, prefab => prefab.Tags.Contains("house-window"));
		Assert.Contains(first.Prefabs, prefab => prefab.Tags.Contains("next-room-door"));
		CeramicPrefabDefinition stairs = Assert.Single(first.Prefabs,
			prefab => prefab.Tags.Contains("house-stairs"));
		Assert.Contains(stairs.Entities, entity => (BlockType)entity.Value == BlockType.WoodStairs);
		CeramicInteriorFeaturePolicy stairPolicy = Assert.Single(
			first.Definition.InteriorFeaturePolicies);
		Assert.Equal(new CeramicCountRange(0, 1), stairPolicy.CountPerComponent);
	}

	[Fact]
	public void VoxelValidationRejectsUnknownBlockIds()
	{
		CeramicVillageCatalog catalog = CeramicVillageCatalog.Load(DefinitionPath);
		CeramicPrefabDefinition prefab = catalog.Prefabs[0] with
		{
			Entities = [new(ushort.MaxValue, 0, 0, 0)],
		};
		CeramicFishDefinition invalid = catalog.Definition with
		{
			Prefabs = [prefab, .. catalog.Prefabs.Skip(1)],
		};
		CeramicDefinitionException error = Assert.Throws<CeramicDefinitionException>(() =>
			CeramicVillageCatalog.ValidateVoxelDefinition(invalid));
		Assert.Contains(error.Errors, item => item.Code == "voxel-entity-value");
	}

	[Fact]
	public void EditingSessionSupportsUndoRedoAndCanonicalSaveReload()
	{
		CeramicVillageCatalog catalog = CeramicVillageCatalog.Load(DefinitionPath);
		CeramicVillageEditingSession session = new(catalog);
		CeramicPrefabDefinition source = session.Prefabs[0];
		session.ReplacePrefab(source with { Weight = source.Weight + 1 });
		Assert.True(session.IsDirty);
		Assert.Equal(source.Weight + 1, session.Get(source.Id).Weight);
		Assert.True(session.Undo());
		Assert.Equal(source.Weight, session.Get(source.Id).Weight);
		Assert.True(session.Redo());
		Assert.Equal(source.Weight + 1, session.Get(source.Id).Weight);

		string root = Path.Combine(Path.GetTempPath(), $"ceramic-village-edit-{Guid.NewGuid():N}");
		try
		{
			string first = Path.Combine(root, "source.json"), second = Path.Combine(root, "runtime.json");
			IReadOnlyList<CeramicVillageCatalog> saved = CeramicVillageCatalog.SaveSynchronized([first, second], session.Definition);
			Assert.Equal(saved[0].Hash, saved[1].Hash);
			Assert.Equal(source.Weight + 1, saved[0].Get(source.Id).Weight);
		}
		finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
	}

	[Fact]
	public void ProductionPreviewIsDeterministicAndFullyDiagnosed()
	{
		CeramicVillageCatalog catalog = CeramicVillageCatalog.Load(DefinitionPath);
		CeramicVillagePreviewResult first = CeramicVillagePlanner.PlanPreview(catalog.Definition, 24681357);
		CeramicVillagePreviewResult second = CeramicVillagePlanner.PlanPreview(catalog.Definition, 24681357);
		Assert.Null(first.Failure);
		Assert.NotNull(first.Layout);
		Assert.Equal(first.Layout!.GenerationSeed, second.Layout!.GenerationSeed);
		Assert.Equal(first.Layout.Placements, second.Layout.Placements);
		Assert.Equal(first.Layout.GateRoadCells, second.Layout.GateRoadCells);
		Assert.NotEmpty(first.Layout.Placements);
		Assert.All(first.Layout.Placements, placement => Assert.Contains(catalog.Prefabs, prefab => prefab.Id == placement.PrefabId));
		PlannedVillagePlacement[] defense = first.Layout.Placements
			.Where(placement => catalog.Get(placement.PrefabId).Tags.Contains("defense-wall", StringComparer.Ordinal))
			.ToArray();
		Assert.True(defense.Max(static placement => placement.Cell.X) - defense.Min(static placement => placement.Cell.X) + 1 >= 25,
			"The defense wall should use nearly the full 31-cell-wide preview zone.");
		Assert.True(defense.Max(static placement => placement.Cell.Z) - defense.Min(static placement => placement.Cell.Z) + 1 >= 25,
			"The defense wall should use nearly the full 31-cell-deep preview zone.");
		int houseWallCount = first.Layout.Placements.Count(placement =>
			catalog.Get(placement.PrefabId).Tags.Contains("house-wall", StringComparer.Ordinal));
		Assert.True(houseWallCount >= (first.Layout.Placements.Length * 15 + 99) / 100,
			"House walls should occupy at least 15% of the generated village region.");
		int stairCount = first.Layout.Placements.Count(placement =>
			catalog.Get(placement.PrefabId).Tags.Contains("house-stairs", StringComparer.Ordinal));
		int buildingCount = first.Layout.Placements.Count(placement =>
			catalog.Get(placement.PrefabId).Tags.Contains("house-door", StringComparer.Ordinal));
		Assert.True(stairCount > 0, "The fixed production preview should contain upper-floor houses.");
		Assert.InRange(stairCount, 1, buildingCount);
	}

	[Fact]
	public void UnsolvablePreviewProducesDeterministicEmptyReservationDiagnostic()
	{
		CeramicPrefabDefinition neutral = CeramicVillageEditingSession.EmptyPrefab("neutral");
		CeramicFishDefinition definition = new("unsolvable-village", [neutral], []);
		CeramicVillagePreviewResult first = CeramicVillagePlanner.PlanPreview(definition, 91);
		CeramicVillagePreviewResult second = CeramicVillagePlanner.PlanPreview(definition, 91);
		Assert.Null(first.Layout);
		Assert.NotNull(first.Failure);
		Assert.Equal(first.Failure, second.Failure);
		Assert.Equal("preview", first.Failure!.VillageId);
	}

	[Fact]
	public void VoxelizationBuildsDoorWindowFloorAndContinuousRoof()
	{
		CeramicVillageCatalog catalog = CeramicVillageCatalog.Load(DefinitionPath);
		WorldGenerationSettings settings = new(77, 32, 32, 64);
		byte[] heights = Enumerable.Repeat((byte)8, 32 * 32).ToArray();
		byte[] biomes = Enumerable.Repeat((byte)WorldBiome.Grassland, 32 * 32).ToArray();
		byte[] zero = new byte[32 * 32];
		byte[] land = Enumerable.Repeat((byte)255, 32 * 32).ToArray();
		PlannedWorldSite site = new("site", "unused", WorldStructureRole.Support, new(1, 8, 1), 0, new(0, 0, 2, 2), false);
		PlanPoint3 access = new(8, 8, 16);
		PlannedWorldRoute route = new("road", WorldFeatureKind.Road, "site", "site", [access]);
		PlanPoint[] footprint = (from x in Enumerable.Range(8, 16) from z in Enumerable.Range(8, 16) select new PlanPoint(x, z)).ToArray();
		PlannedVillageArea village = new("village", new(8, 8, 23, 23), 8, footprint, [access]);
		PlannedVillagePlacement[] placements =
		[
			new("house.corner", new(0, 0), CeramicRotation.Rot90CW),
			new("house.door", new(1, 0), CeramicRotation.Rot0),
			new("house.corner", new(2, 0), CeramicRotation.Rot180CW),
			new("house.straight", new(2, 1), CeramicRotation.Rot90CW),
			new("house.corner", new(2, 2), CeramicRotation.Rot270CW),
			new("house.window", new(1, 2), CeramicRotation.Rot0),
			new("house.corner", new(0, 2), CeramicRotation.Rot0),
			new("house.straight", new(0, 1), CeramicRotation.Rot90CW),
		];
		PlannedVillageLayout layout = new("village", new(9, 8, 9), placements, [new(8, 16)], 77, 1, 10, 10);
		WorldPlan plan = new(settings, heights, biomes, zero, land, zero, sites: [site], routes: [route], villages: [village],
			villageLayouts: [layout], ceramicFishDefinitionHash: catalog.Hash);

		WorldPlanBuildResult result = WorldPlanVoxelBuilder.Build(plan, null, catalog, CancellationToken.None);
		Assert.Equal(BlockType.None, BlockAt(result, 13, 9, 10));
		Assert.Equal(BlockType.Glass, BlockAt(result, 13, 10, 16));
		Assert.Equal(BlockType.Stone, BlockAt(result, 13, 8, 13));
		for (int x = 11; x <= 15; x++)
		for (int z = 11; z <= 15; z++)
			Assert.Equal(BlockType.Plank, BlockAt(result, x, 12, z));
		Assert.Equal(BlockType.None, BlockAt(result, 13, 13, 13));
	}

	[Fact]
	public void StairFeatureBuildsOnlyItsBuildingsSecondFloor()
	{
		CeramicVillageCatalog catalog = CeramicVillageCatalog.Load(DefinitionPath);
		WorldGenerationSettings settings = new(78, 32, 32, 64);
		byte[] heights = Enumerable.Repeat((byte)8, 32 * 32).ToArray();
		byte[] biomes = Enumerable.Repeat((byte)WorldBiome.Grassland, 32 * 32).ToArray();
		byte[] zero = new byte[32 * 32];
		byte[] land = Enumerable.Repeat((byte)255, 32 * 32).ToArray();
		PlannedWorldSite site = new("site", "unused", WorldStructureRole.Support,
			new(1, 8, 1), 0, new(0, 0, 2, 2), false);
		PlanPoint3 access = new(8, 8, 16);
		PlannedWorldRoute route = new("road", WorldFeatureKind.Road, "site", "site", [access]);
		PlannedVillagePlacement[] placements =
		[
			new("house.corner", new(0, 0), CeramicRotation.Rot90CW),
			new("house.door", new(1, 0), CeramicRotation.Rot0),
			new("house.tee", new(2, 0), CeramicRotation.Rot180CW),
			new("house.shared-room-door", new(2, 1), CeramicRotation.Rot90CW),
			new("house.tee", new(2, 2), CeramicRotation.Rot0),
			new("house.window", new(1, 2), CeramicRotation.Rot0),
			new("house.corner", new(0, 2), CeramicRotation.Rot0),
			new("house.straight", new(0, 1), CeramicRotation.Rot90CW),
			new("house.straight", new(3, 0), CeramicRotation.Rot0),
			new("house.corner", new(4, 0), CeramicRotation.Rot180CW),
			new("house.straight", new(4, 1), CeramicRotation.Rot90CW),
			new("house.corner", new(4, 2), CeramicRotation.Rot270CW),
			new("house.straight", new(3, 2), CeramicRotation.Rot0),
			new("house.stairs", new(1, 1), CeramicRotation.Rot0),
		];
		PlannedVillageArea village = new("village", new(8, 8, 23, 23), 8,
			(from x in Enumerable.Range(8, 16) from z in Enumerable.Range(8, 16)
			 select new PlanPoint(x, z)).ToArray(), [access]);
		PlannedVillageLayout layout = new("village", new(9, 8, 9), placements, [new(8, 16)],
			78, 1, 10, 10);
		WorldPlan plan = new(settings, heights, biomes, zero, land, zero,
			sites: [site], routes: [route], villages: [village], villageLayouts: [layout],
			ceramicFishDefinitionHash: catalog.Hash);

		WorldPlanBuildResult result = WorldPlanVoxelBuilder.Build(plan, null, catalog,
			CancellationToken.None);
		Assert.Equal(BlockType.WoodStairs, BlockAt(result, 12, 9, 12));
		Assert.Equal(BlockType.WoodStairs, BlockAt(result, 13, 12, 14));
		Assert.Equal(BlockType.Plank, BlockAt(result, 13, 12, 13));
		Assert.Equal(BlockType.None, BlockAt(result, 13, 13, 13));
		Assert.Equal(BlockType.Bricks, BlockAt(result, 10, 13, 13));
		Assert.Equal(BlockType.Glass, BlockAt(result, 13, 14, 16));
		Assert.Equal(BlockType.None, BlockAt(result, 16, 13, 13));
		Assert.Equal(BlockType.None, BlockAt(result, 16, 14, 13));
		Assert.Equal(BlockType.Bricks, BlockAt(result, 16, 15, 13));
		Assert.Equal(BlockType.Plank, BlockAt(result, 13, 17, 13));
	}

	[Fact]
	public async Task OldWorldPlanBundleVersionIsRejected()
	{
		WorldPlan plan = WorldPlanGenerator.Generate(new(123, 64, 64, 64));
		string root = Path.Combine(Path.GetTempPath(), $"old-world-plan-{Guid.NewGuid():N}");
		try
		{
			await WorldPlanBundle.SaveAsync(root, plan);
			string manifestPath = Path.Combine(root, WorldPlanBundle.ManifestFileName);
			using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath));
			Dictionary<string, object> manifest = JsonSerializer.Deserialize<Dictionary<string, object>>(document.RootElement.GetRawText())!;
			manifest["formatVersion"] = 5;
			await File.WriteAllTextAsync(manifestPath, JsonSerializer.Serialize(manifest));
			await Assert.ThrowsAsync<NotSupportedException>(() => WorldPlanBundle.LoadAsync(root));
		}
		finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
	}

	[Fact]
	public async Task WorldPlanBundleRejectsCeramicFishHashMismatch()
	{
		CeramicVillageCatalog catalog = CeramicVillageCatalog.Load(DefinitionPath);
		WorldPlan source = WorldPlanGenerator.Generate(new(456, 64, 64, 64));
		WorldPlan plan = new(source.Settings, source.Heights.Span, source.Biomes.Span, source.TreeDensity.Span,
			source.IslandMask.Span, source.HillMask.Span, source.Ponds, source.Sites, source.Routes, source.Villages,
			source.StructureCatalogHash, source.VillageLayouts, catalog.Hash, source.VillageFailures);
		string root = Path.Combine(Path.GetTempPath(), $"ceramic-hash-plan-{Guid.NewGuid():N}");
		try
		{
			await WorldPlanBundle.SaveAsync(root, plan);
			WorldPlan loaded = await WorldPlanBundle.LoadAsync(root, expectedCeramicFishDefinitionHash: catalog.Hash);
			Assert.Equal(catalog.Hash, loaded.CeramicFishDefinitionHash);
			await Assert.ThrowsAsync<InvalidDataException>(() => WorldPlanBundle.LoadAsync(root,
				expectedCeramicFishDefinitionHash: new string('a', 64)));
		}
		finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
	}

	private static BlockType BlockAt(WorldPlanBuildResult result, int x, int y, int z)
	{
		ChunkColumnSnapshot column = result.Columns.Single(value => value.X == x >> 4 && value.Z == z >> 4);
		ChunkSnapshot chunk = column.Chunks.Single(value => value.ChunkY == y >> 4);
		return chunk.GetBlock(x & 15, y & 15, z & 15);
	}
}
