using Voxelgine.Engine.World.Structures;
using Voxelgine.Engine;
using Voxelgine.Graphics;
using Voxelgine.WorldGeneration;

namespace VoxelgineEngine.Tests;

[Collection(WorldGenerationCollection.Name)]
public sealed class VillagePrefabGenerationTests
{
	[Fact]
	public void CatalogRoundTripsExactVoxelAndSemanticData()
	{
		VillagePrefabCatalog source = LoadCatalog();
		string directory = Path.Combine(Path.GetTempPath(), $"voxelgine-prefabs-{Guid.NewGuid():N}");
		string path = Path.Combine(directory, "catalog.json");
		try
		{
			VillagePrefabCatalog.Save(path, source.Prefabs, source.SocketSemantics.Append("service"));
			VillagePrefabCatalog loaded = VillagePrefabCatalog.Load(path);
			Assert.Equal(source.SocketSemantics.Append("service"), loaded.SocketSemantics);
			Assert.Equal(source.Prefabs.Select(static prefab => prefab.Descriptor.Id), loaded.Prefabs.Select(static prefab => prefab.Descriptor.Id));
			foreach (VillagePrefab expected in source.Prefabs)
			{
				VillagePrefab actual = loaded.Get(expected.Descriptor.Id);
				Assert.Equal(expected.Descriptor.Kind, actual.Descriptor.Kind);
				Assert.Equal(expected.Descriptor.Levels, actual.Descriptor.Levels);
				Assert.Equal(expected.Descriptor.DisplayName, actual.Descriptor.DisplayName);
				foreach (VillageSocketDescriptor socket in expected.Descriptor.Sockets)
				{
					VillageSocketDescriptor loadedSocket = actual.Descriptor.Socket(socket.Direction);
					Assert.Equal(socket.Types, loadedSocket.Types);
					Assert.Equal(socket.Openings, loadedSocket.Openings);
				}
				for (int y = 0; y < 5; y++) for (int z = 0; z < 5; z++) for (int x = 0; x < 5; x++)
					Assert.Equal(expected.GetCell(x, y, z), actual.GetCell(x, y, z));
			}
		}
		finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
	}

	[Fact]
	public void CatalogRejectsUndefinedSocketSemantic()
	{
		VillagePrefabCatalog source = LoadCatalog();
		VillagePrefabDescriptor prefab = source.Prefabs[0].Descriptor;
		VillagePrefabDescriptor invalid = prefab with
		{
			Sockets = prefab.Sockets.Where(static socket => socket.Direction != VillageSocketDirection.NegativeZ)
				.Append(new(VillageSocketDirection.NegativeZ, ["undefined-socket"], new byte[25]))
				.ToArray()
		};
		Assert.Throws<InvalidDataException>(() => new VillagePrefabCatalogDescriptor(
			source.Prefabs.Skip(1).Select(static value => value.Descriptor).Prepend(invalid),
			socketSemantics: source.SocketSemantics));
	}

	[Fact]
	public void EditingSessionPersistsNewSemanticAndMultiFaceSelections()
	{
		VillagePrefabCatalog source = LoadCatalog();
		VillagePrefabEditingSession session = new(source);
		string semantic = session.AddSemantic("service_hall");
		VillagePrefab original = session.Prefabs.First(static prefab => prefab.Descriptor.Kind == VillageModuleKind.Room);
		VillageSocketDescriptor[] sockets = original.Descriptor.Sockets.Select(socket =>
			socket.Direction is VillageSocketDirection.PositiveX or VillageSocketDirection.PositiveZ
				? socket with { Types = ["open", semantic] } : socket).ToArray();
		session.Replace(new VillagePrefab(original.Descriptor with { Sockets = sockets },
			Enumerable.Range(0, 125).Select(index => original.GetCell(index % 5, index / 25, index / 5 % 5))));
		string directory = Path.Combine(Path.GetTempPath(), $"voxelgine-prefab-session-{Guid.NewGuid():N}");
		try
		{
			(VillagePrefab[] modules, string[] semantics, long revision) = session.Snapshot();
			string first = Path.Combine(directory, "source", "catalog.json"), second = Path.Combine(directory, "runtime", "catalog.json");
			_ = VillagePrefabCatalog.SaveSynchronized([first, second], modules, semantics); session.MarkSaved(revision);
			Assert.False(session.IsDirty);
			VillagePrefab loaded = VillagePrefabCatalog.Load(second).Get(original.Descriptor.Id);
			Assert.Contains(semantic, VillagePrefabCatalog.Load(first).SocketSemantics);
			Assert.Equal(["open", semantic], loaded.Descriptor.Socket(VillageSocketDirection.PositiveX).Types);
			Assert.Equal(["open", semantic], loaded.Descriptor.Socket(VillageSocketDirection.PositiveZ).Types);
		}
		finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
	}

	[Fact]
	public void SocketCompatibilityUsesSemanticOverlapOnly()
	{
		byte[] leftMask = new byte[25], matching = new byte[25], mismatch = new byte[25];
		leftMask[1] = leftMask[6] = 1; matching[3] = matching[8] = 1; mismatch[2] = mismatch[7] = 1;
		VillageSocketDescriptor left = new(VillageSocketDirection.PositiveX, ["door", "hall"], leftMask);
		Assert.True(VillageSocketCompatibility.Matches(left, new(VillageSocketDirection.NegativeX, ["hall"], matching)));
		Assert.True(VillageSocketCompatibility.Matches(left, new(VillageSocketDirection.NegativeX, ["hall"], mismatch)));
		Assert.True(VillageSocketCompatibility.Matches(left, new(VillageSocketDirection.NegativeX, ["any"], mismatch)));
		Assert.False(VillageSocketCompatibility.Matches(left, new(VillageSocketDirection.NegativeX, ["road"], matching)));
		Assert.False(VillageSocketCompatibility.Matches(left, new(VillageSocketDirection.NegativeX, [], matching)));
		Assert.False(VillageSocketCompatibility.Matches(new(VillageSocketDirection.PositiveX, [], leftMask), new(VillageSocketDirection.NegativeX, ["any"], matching)));
	}

	[Fact]
	public void CoordinateSocketLabelsAndOppositesAreStable()
	{
		Assert.Equal("+X", VillageSocketCompatibility.Label(VillageSocketDirection.PositiveX));
		Assert.Equal("-X", VillageSocketCompatibility.Label(VillageSocketDirection.NegativeX));
		Assert.Equal("+Y", VillageSocketCompatibility.Label(VillageSocketDirection.PositiveY));
		Assert.Equal("-Y", VillageSocketCompatibility.Label(VillageSocketDirection.NegativeY));
		Assert.Equal("+Z", VillageSocketCompatibility.Label(VillageSocketDirection.PositiveZ));
		Assert.Equal("-Z", VillageSocketCompatibility.Label(VillageSocketDirection.NegativeZ));
		foreach (VillageSocketDirection direction in Enum.GetValues<VillageSocketDirection>())
			Assert.Equal(direction, VillageSocketCompatibility.Opposite(VillageSocketCompatibility.Opposite(direction)));
	}

	[Fact]
	public void FormatOneCatalogsAreRejectedAfterMigration()
	{
		string source = Path.Combine(AppContext.BaseDirectory, "data", "world", "village-prefabs", "catalog.json");
		string path = Path.Combine(Path.GetTempPath(), $"legacy-prefabs-{Guid.NewGuid():N}.json");
		try
		{
			File.WriteAllText(path, File.ReadAllText(source).Replace("\"formatVersion\": 2", "\"formatVersion\": 1", StringComparison.Ordinal));
			Assert.Throws<InvalidDataException>(() => VillagePrefabCatalog.Load(path));
		}
		finally { File.Delete(path); }
	}

	[Fact]
	public void SemanticSocketsDoNotValidateBoundaryOpeningGeometry()
	{
		VillagePrefabCatalog source = LoadCatalog(); VillagePrefab original = source.Prefabs.First(static prefab => prefab.Descriptor.Kind == VillageModuleKind.Room);
		BlockValue[] cells = Enumerable.Range(0, 125).Select(index => original.GetCell(index % 5, index / 25, index / 5 % 5)).ToArray();
		for (int y = 0; y < 2; y++) for (int x = 0; x < 5; x++) cells[(y * 5 + 0) * 5 + x] = new(BlockType.StoneBrick);
		VillageSocketDescriptor[] sockets = original.Descriptor.Sockets.Select(socket => socket.Direction == VillageSocketDirection.NegativeZ
			? socket with { Types = ["door"] } : socket).ToArray();
		VillagePrefab geometryIndependent = new(original.Descriptor with { Sockets = sockets }, cells);
		string path = Path.Combine(Path.GetTempPath(), $"semantic-door-{Guid.NewGuid():N}.json");
		try
		{
			VillagePrefabCatalog.Save(path, source.Prefabs.Where(prefab => prefab.Descriptor.Id != original.Descriptor.Id).Append(geometryIndependent), source.SocketSemantics);
			Assert.Equal(["door"], VillagePrefabCatalog.Load(path).Get(original.Descriptor.Id).Descriptor.Socket(VillageSocketDirection.NegativeZ).Types);
		}
		finally { File.Delete(path); }
	}

	[Fact]
	public void EmptySocketSemanticSetDisablesThatFaceAndRoundTrips()
	{
		VillagePrefabCatalog source = LoadCatalog();
		VillagePrefab original = source.Prefabs.First(static prefab => prefab.Descriptor.Kind == VillageModuleKind.Plaza);
		VillageSocketDescriptor[] sockets = original.Descriptor.Sockets.Select(socket => socket.Direction == VillageSocketDirection.PositiveX
			? socket with { Types = [] } : socket).ToArray();
		VillagePrefab updated = new(original.Descriptor with { Sockets = sockets },
			Enumerable.Range(0, 125).Select(index => original.GetCell(index % 5, index / 25, index / 5 % 5)));
		string path = Path.Combine(Path.GetTempPath(), $"empty-socket-{Guid.NewGuid():N}.json");
		try
		{
			VillagePrefabCatalog.Save(path, source.Prefabs.Where(prefab => prefab.Descriptor.Id != original.Descriptor.Id).Append(updated), source.SocketSemantics);
			Assert.Empty(VillagePrefabCatalog.Load(path).Get(original.Descriptor.Id).Descriptor.Socket(VillageSocketDirection.PositiveX).Types);
		}
		finally { File.Delete(path); }
	}

	[Fact]
	public void RoomClearanceAllowsFloorAtYZeroAndRejectsHeadroomObstruction()
	{
		VillagePrefabCatalog source = LoadCatalog();
		VillagePrefab original = source.Prefabs.First(static prefab => prefab.Descriptor.Kind == VillageModuleKind.Hallway);
		BlockValue[] cells = Enumerable.Range(0, 125).Select(index => original.GetCell(index % 5, index / 25, index / 5 % 5)).ToArray();
		for (int z = 1; z < 4; z++) for (int x = 1; x < 4; x++) cells[(0 * 5 + z) * 5 + x] = new(BlockType.Gravel);

		string path = Path.Combine(Path.GetTempPath(), $"room-clearance-{Guid.NewGuid():N}.json");
		try
		{
			VillagePrefab validFloor = new(original.Descriptor, cells);
			VillagePrefabCatalog.Save(path, source.Prefabs.Where(prefab => prefab.Descriptor.Id != original.Descriptor.Id).Append(validFloor), source.SocketSemantics);
			Assert.Equal(BlockType.Gravel, VillagePrefabCatalog.Load(path).Get(original.Descriptor.Id).GetCell(2, 0, 2).Type);

			cells[(2 * 5 + 2) * 5 + 2] = new(BlockType.StoneBrick);
			VillagePrefab obstructed = new(original.Descriptor, cells);
			Assert.Throws<InvalidDataException>(() => VillagePrefabCatalog.Save(path,
				source.Prefabs.Where(prefab => prefab.Descriptor.Id != original.Descriptor.Id).Append(obstructed), source.SocketSemantics));
		}
		finally { File.Delete(path); }
	}

	[Fact]
	public void SynchronizedSaveWritesAndReloadsEveryTarget()
	{
		VillagePrefabCatalog source = LoadCatalog();
		string directory = Path.Combine(Path.GetTempPath(), $"voxelgine-prefab-sync-{Guid.NewGuid():N}");
		string first = Path.Combine(directory, "source", "catalog.json");
		string second = Path.Combine(directory, "runtime", "catalog.json");
		try
		{
			IReadOnlyList<VillagePrefabCatalog> saved = VillagePrefabCatalog.SaveSynchronized([first, second], source.Prefabs, source.SocketSemantics);
			Assert.Equal(2, saved.Count);
			Assert.Equal(File.ReadAllBytes(first), File.ReadAllBytes(second));
			Assert.All(saved, catalog => Assert.Equal(source.SocketSemantics, catalog.SocketSemantics));
		}
		finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
	}

	[Fact]
	public void SameSeedProducesStableMultiFloorVillageLayouts()
	{
		StructureBlueprintCatalog structures = StructureBlueprintCatalog.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "data", "world", "structures"));
		VillagePrefabCatalog prefabs = LoadCatalog();
		WorldPlan first = WorldPlanMaterializer.GeneratePlan(512, 512, 24681357, structures, villagePrefabs: prefabs);
		WorldPlan second = WorldPlanMaterializer.GeneratePlan(512, 512, 24681357, structures, villagePrefabs: prefabs);

		Assert.Equal(6, first.VillageLayouts.Count);
		Assert.Equal(first.VillagePrefabCatalogHash, prefabs.Hash);
		Assert.Equal(first.VillageLayouts.SelectMany(static layout => layout.Modules), second.VillageLayouts.SelectMany(static layout => layout.Modules));
		foreach (PlannedVillageLayout layout in first.VillageLayouts)
		{
			PlannedVillageArea village = first.Villages.Single(value => value.Id == layout.VillageId);
			Assert.InRange(layout.GroundAttempts, 1, 64);
			Assert.True(layout.Modules.Where(static module => module.ComponentId > 0).Select(static module => module.ComponentId).Distinct().Count() >= 3);
			Assert.Contains(layout.Modules, static module => module.Floor == 0 && module.Kind == VillageModuleKind.Gate);
			Assert.Contains(layout.Modules, static module => module.Kind == VillageModuleKind.Roof);
			Assert.NotEmpty(layout.InternalRoadCells);
			Assert.All(layout.Modules.Where(static module => module.Kind != VillageModuleKind.Roof), module =>
				Assert.Equal(village.SurfaceY + module.Floor * VillagePrefabDescriptor.Height, module.Origin.Y));
			foreach (IGrouping<int, PlannedVillageModule> roofs in layout.Modules.Where(static module => module.Kind == VillageModuleKind.Roof)
				.GroupBy(static module => module.ComponentId))
			{
				int topFloorY = layout.Modules.Where(module => module.ComponentId == roofs.Key && module.Kind != VillageModuleKind.Roof).Max(static module => module.Origin.Y);
				Assert.All(roofs, roof => Assert.Equal(topFloorY + VillagePrefabDescriptor.Height, roof.Origin.Y));
			}
		}

		WorldPlan withoutVillageLayouts = new(first.Settings, first.Heights.Span, first.Biomes.Span, first.TreeDensity.Span,
			first.IslandMask.Span, first.HillMask.Span, first.Ponds, first.Sites, first.Routes, first.Villages,
			first.StructureCatalogHash);
		ChunkMap terrainMap = new();
		WorldPlanMaterializer.MaterializeAtomically(terrainMap, withoutVillageLayouts, structures);
		ChunkMap map = new();
		WorldPlanMaterializer.MaterializeAtomically(map, first, structures, villagePrefabs: prefabs);
		Assert.Contains(map.CaptureChunks().SelectMany(static chunk => chunk.Blocks), static block => block == BlockType.StoneBrick);
		Assert.Contains(map.CaptureChunks().SelectMany(static chunk => chunk.Blocks), static block => block == BlockType.Bricks);
		AssertEmbeddedFloorSemantics(first, prefabs, terrainMap, map);
	}

	[Fact]
	public void FeatureLayersExposeEveryVillageStorey()
	{
		StructureBlueprintCatalog structures = StructureBlueprintCatalog.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "data", "world", "structures"));
		VillagePrefabCatalog prefabs = LoadCatalog();
		WorldPlan plan = WorldPlanMaterializer.GeneratePlan(512, 512, 666, structures, villagePrefabs: prefabs);
		Assert.Contains(WorldPlanRendering.RenderFeatures(plan), static value => value != 0);
		Assert.Contains(WorldPlanRendering.RenderVillageFloor(plan, 1), static value => value != 0);
		Assert.Contains(WorldPlanRendering.RenderVillageFloor(plan, 2), static value => value != 0);
		Assert.Contains(WorldPlanRendering.RenderVillageFloor(plan, 3), static value => value != 0);
	}

	private static VillagePrefabCatalog LoadCatalog() => VillagePrefabCatalog.Load(
		Path.Combine(AppContext.BaseDirectory, "data", "world", "village-prefabs", "catalog.json"));

	private static void AssertEmbeddedFloorSemantics(WorldPlan plan, VillagePrefabCatalog prefabs, ChunkMap terrainMap, ChunkMap map)
	{
		bool verifiedReplacement = false, verifiedPreservation = false, verifiedRoadOverride = false;
		foreach (PlannedVillageLayout layout in plan.VillageLayouts)
		{
			HashSet<(int X, int Z)> roadSurface = layout.InternalRoadCells
				.SelectMany(static road => Enumerable.Range(-1, 3).SelectMany(dx => Enumerable.Range(-1, 3)
					.Select(dz => (road.X + dx, road.Z + dz)))).ToHashSet();
			foreach (PlannedVillageModule module in layout.Modules.Where(static module => module.Floor == 0))
			{
				VillagePrefab prefab = prefabs.Get(module.PrefabId);
				for (int z = 0; z < VillagePrefabDescriptor.Length; z++) for (int x = 0; x < VillagePrefabDescriptor.Width; x++)
				{
					BlockCoordinate rotated = WorldStructurePlanner.Rotate(new(x, 0, z),
						new(VillagePrefabDescriptor.Width, VillagePrefabDescriptor.Height, VillagePrefabDescriptor.Length), module.Rotation);
					int worldX = module.Origin.X + rotated.X, worldZ = module.Origin.Z + rotated.Z;
					BlockType authored = prefab.GetCell(x, 0, z).Type;
					if (authored != BlockType.None)
					{
						Assert.Equal(authored, map.GetBlock(worldX, module.Origin.Y, worldZ));
						verifiedReplacement = true;
						if (roadSurface.Contains((worldX, worldZ)) && authored != BlockType.Gravel) verifiedRoadOverride = true;
					}
					else if (plan.GetHeight(worldX, worldZ) == module.Origin.Y)
					{
						BlockType expected = roadSurface.Contains((worldX, worldZ)) ? BlockType.Gravel : terrainMap.GetBlock(worldX, module.Origin.Y, worldZ);
						Assert.Equal(expected, map.GetBlock(worldX, module.Origin.Y, worldZ));
						verifiedPreservation = true;
					}
				}
			}
		}
		Assert.True(verifiedReplacement, "No authored ground-floor voxel was available to verify terrain replacement.");
		Assert.True(verifiedPreservation, "No empty ground-floor voxel was available to verify terrain preservation.");
		Assert.True(verifiedRoadOverride, "No authored ground-floor voxel overlapped a generated gravel road.");
	}
}
