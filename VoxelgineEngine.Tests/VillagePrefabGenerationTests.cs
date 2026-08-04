using Voxelgine.Engine;
using Voxelgine.Engine.World.Structures;
using Voxelgine.Graphics;
using Voxelgine.WorldGeneration;

namespace VoxelgineEngine.Tests;

[Collection(WorldGenerationCollection.Name)]
public sealed class VillagePrefabGenerationTests
{
    [Fact]
    public void FormatFourCatalogRoundTripsCategoryFreeData()
    {
        VillagePrefabCatalog source = LoadCatalog();
        string directory = Path.Combine(Path.GetTempPath(), $"voxelgine-prefabs-{Guid.NewGuid():N}");
        string path = Path.Combine(directory, "catalog.json");
        try
        {
            VillagePrefabCatalog.Save(path, source.Prefabs, source.SocketSemantics, source.ExternalEntrySemantic, source.AdjacencyRules);
            VillagePrefabCatalog loaded = VillagePrefabCatalog.Load(path);
            Assert.Equal("road", loaded.ExternalEntrySemantic);
            Assert.DoesNotContain("any", loaded.SocketSemantics);
            Assert.DoesNotContain("closed", loaded.SocketSemantics);
			Assert.Equal(source.AdjacencyRules, loaded.AdjacencyRules);
            Assert.Equal(source.Prefabs.Select(static value => value.Descriptor.Id), loaded.Prefabs.Select(static value => value.Descriptor.Id));
            Assert.All(loaded.Prefabs, static value => Assert.Equal([0, 90, 180, 270], value.Descriptor.AllowedRotations));
            foreach (VillagePrefab expected in source.Prefabs)
            {
                VillagePrefab actual = loaded.Get(expected.Descriptor.Id);
                Assert.Equal(expected.Descriptor.Weight, actual.Descriptor.Weight);
                Assert.Equal(expected.Descriptor.RotationSignatures, actual.Descriptor.RotationSignatures);
                for (int y = 0; y < 5; y++) for (int z = 0; z < 5; z++) for (int x = 0; x < 5; x++)
                    Assert.Equal(expected.GetCell(x, y, z), actual.GetCell(x, y, z));
            }
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void SocketCompatibilityUsesExactOverlapAndSealedFaces()
    {
        byte[] mask = new byte[25];
        VillageSocketDescriptor hall = new(VillageSocketDirection.PositiveX, ["hall", "door"], mask);
        Assert.True(VillageSocketCompatibility.Matches(hall, new(VillageSocketDirection.NegativeX, ["hall"], mask)));
        Assert.False(VillageSocketCompatibility.Matches(hall, new(VillageSocketDirection.NegativeX, ["road"], mask)));
        Assert.False(VillageSocketCompatibility.Matches(hall, new(VillageSocketDirection.NegativeX, [], mask)));
        Assert.True(VillageSocketCompatibility.Matches(new(VillageSocketDirection.PositiveX, [], mask), new(VillageSocketDirection.NegativeX, [], mask)));
        Assert.False(VillageSocketCompatibility.CreatesConnection(new(VillageSocketDirection.PositiveX, [], mask), new(VillageSocketDirection.NegativeX, [], mask)));
    }

	[Fact]
	public void AdjacencyRulesSupportWildcardsRelationsAndHardExclusion()
	{
		VillageAdjacencyRuleDescriptor rule = new("road-spacing", "road.*", "road.*", 0,
			VillageAdjacencyRelation.Disconnected);
		rule.Validate();
		Assert.True(rule.Matches("road.corner", "road.straight"));
		Assert.False(rule.Matches("road.corner", "house.cottage"));
		Assert.True(rule.AppliesTo(connected: false));
		Assert.False(rule.AppliesTo(connected: true));
	}

    [Fact]
	public void FourPieceHouseKitUsesOneClosedRoleSpecificSocketCycle()
    {
        VillagePrefabCatalog catalog = LoadCatalog();
        string[] ids =
        [
            "house.2x2.entry-corner",
            "house.2x2.front-corner",
            "house.2x2.back-left",
            "house.2x2.back-right",
        ];
        VillagePrefab[] pieces = ids.Select(catalog.Get).ToArray();
        string[] privateSemantics =
        [
            "house.2x2.front",
            "house.2x2.left",
            "house.2x2.right",
            "house.2x2.back",
        ];

        Assert.All(pieces, static piece => Assert.Equal([0, 90, 180, 270], piece.Descriptor.AllowedRotations));
        Assert.Equal(["road"], pieces[0].Descriptor.Socket(VillageSocketDirection.NegativeZ).Types);
        foreach (string semantic in privateSemantics)
        {
            Assert.Contains(semantic, catalog.SocketSemantics);
            Assert.Equal(2, pieces.SelectMany(static piece => piece.Descriptor.Sockets)
                .Count(socket => socket.Types.Contains(semantic, StringComparer.Ordinal)));
            Assert.DoesNotContain(catalog.Prefabs.Except(pieces), prefab => prefab.Descriptor.Sockets
                .Any(socket => socket.Types.Contains(semantic, StringComparer.Ordinal)));
        }
        Assert.Equal(BlockType.None, pieces[0].GetCell(2, 1, 0).Type);
        Assert.Equal(BlockType.None, pieces[0].GetCell(2, 2, 0).Type);
		Assert.NotEqual(BlockType.None, pieces[0].GetCell(2, 0, 0).Type);
	}

	[Fact]
	public void ConnectedHouseSegmentsRemoveBothSharedWallPlanesButKeepFloorAndCeiling()
	{
		VillagePrefabCatalog catalog = LoadCatalog();
		PlannedVillageModule entry = new("house.2x2.entry-corner", 0, 0, new(0, 20, 0), 1);
		PlannedVillageModule front = new("house.2x2.front-corner", 0, 0, new(5, 20, 0), 1);
		PlannedVillageLayout layout = new("test", [entry, front], 1);

		PlanPoint3[] removed = WorldPlanVoxelBuilder.SharedInteriorWallCells(layout, catalog).ToArray();

		Assert.Equal(30, removed.Length);
		Assert.All(Enumerable.Range(1, 3).SelectMany(y => Enumerable.Range(0, 5).SelectMany(z =>
			new[] { new PlanPoint3(4, 20 + y, z), new PlanPoint3(5, 20 + y, z) })),
			cell => Assert.Contains(cell, removed));
		Assert.DoesNotContain(removed, static cell => cell.Y is 20 or 24);
	}

    [Fact]
    public void EditingSessionPersistsVocabularyExternalEntryAndSelections()
    {
        VillagePrefabCatalog source = LoadCatalog();
        VillagePrefabEditingSession session = new(source);
        string semantic = session.AddSemantic("village.entry");
        session.SetExternalEntrySemantic(semantic);
        VillagePrefab original = session.Prefabs[0];
        VillageSocketDescriptor[] sockets = original.Descriptor.Sockets.Select(socket =>
            socket.Direction is VillageSocketDirection.PositiveX or VillageSocketDirection.PositiveZ
                ? socket with { Types = [semantic] } : socket).ToArray();
        session.Replace(new VillagePrefab(original.Descriptor with { Sockets = sockets }, CopyCells(original)));
        string directory = Path.Combine(Path.GetTempPath(), $"voxelgine-prefab-session-{Guid.NewGuid():N}");
        try
        {
            (VillagePrefab[] modules, string[] semantics, string externalEntry, VillageAdjacencyRuleDescriptor[] rules, long revision) = session.Snapshot();
            string first = Path.Combine(directory, "source", "catalog.json"), second = Path.Combine(directory, "runtime", "catalog.json");
            _ = VillagePrefabCatalog.SaveSynchronized([first, second], modules, semantics, externalEntry, rules);
            session.MarkSaved(revision);
            VillagePrefabCatalog loaded = VillagePrefabCatalog.Load(second);
            Assert.Equal(semantic, loaded.ExternalEntrySemantic);
            Assert.Equal([semantic], loaded.Get(original.Descriptor.Id).Descriptor.Socket(VillageSocketDirection.PositiveX).Types);
            Assert.False(session.IsDirty);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true); }
    }

    [Fact]
    public void UndefinedAndLegacySocketSemanticsAreRejected()
    {
        VillagePrefabCatalog source = LoadCatalog();
        VillagePrefabDescriptor prefab = source.Prefabs[0].Descriptor;
        VillagePrefabDescriptor invalid = prefab with { Sockets = prefab.Sockets.Select(socket => socket.Direction == VillageSocketDirection.NegativeZ
            ? socket with { Types = ["undefined"] } : socket).ToArray() };
        Assert.Throws<InvalidDataException>(() => new VillagePrefabCatalogDescriptor(
            source.Prefabs.Skip(1).Select(static value => value.Descriptor).Prepend(invalid), socketSemantics: source.SocketSemantics));
        Assert.Throws<InvalidDataException>(() => new VillagePrefabCatalogDescriptor(source.Prefabs.Select(static value => value.Descriptor),
            socketSemantics: source.SocketSemantics.Append("any")));
    }

    [Fact]
	public void RoadAndRoadsideHouseCatalogGeneratesVillagesDeterministically()
	{
		VillagePrefabCatalog prefabs = LoadCatalog();
		VillagePrefabCatalogDescriptor descriptor = new(prefabs.Prefabs.Select(static prefab => prefab.Descriptor),
			prefabs.Hash, prefabs.SocketSemantics, prefabs.ExternalEntrySemantic, prefabs.AdjacencyRules);
		PlanBounds reservation = new(0, 0, 24, 24);
		PlannedVillageArea village = new("test-village", reservation, 24,
			Enumerable.Range(0, 25).SelectMany(static x => Enumerable.Range(0, 25).Select(z => new PlanPoint(x, z))).ToArray(),
			[new PlanPoint3(12, 24, -1)]);
		WorldGenerationSettings settings = new(666, 32, 32, 64);

		PlannedVillageLayout[] first = VillageLayoutPlanner.Plan(settings, [village], descriptor, CancellationToken.None);
		PlannedVillageLayout[] second = VillageLayoutPlanner.Plan(settings, [village], descriptor, CancellationToken.None);

		Assert.Single(first);
		Assert.NotEmpty(first[0].Modules);
		Assert.True(first[0].Modules.Count(static module => module.Floor == 0) >= 15,
			"The connected ground layout should occupy most of this 5x5-cell reservation.");
		Assert.DoesNotContain(first[0].Modules,
			static module => module.PrefabId == "empty.sealed");
		PlannedVillageModule entry = Assert.Single(first[0].Modules, module =>
			Math.Abs(module.Origin.X + 2 - village.AccessRoadCells[0].X)
				+ Math.Abs(module.Origin.Z + 2 - village.AccessRoadCells[0].Z) == 5);
		Assert.StartsWith("road.", entry.PrefabId, StringComparison.Ordinal);
		Dictionary<(int X, int Z), PlannedVillageModule> ground = first[0].Modules.Where(static module => module.Floor == 0)
			.ToDictionary(static module => (module.Origin.X, module.Origin.Z));
		int adjacentRoadPairs = 0, disconnectedRoadPairs = 0;
		foreach (PlannedVillageModule module in ground.Values.Where(static module => module.PrefabId.StartsWith("road.", StringComparison.Ordinal)))
		foreach ((int dx, int dz, VillageSocketDirection direction) in new[]
			{ (5, 0, VillageSocketDirection.PositiveX), (0, 5, VillageSocketDirection.PositiveZ) })
			if (ground.TryGetValue((module.Origin.X + dx, module.Origin.Z + dz), out PlannedVillageModule adjacent)
				&& adjacent.PrefabId.StartsWith("road.", StringComparison.Ordinal))
			{
				adjacentRoadPairs++;
				VillagePrefabVariant left = new(prefabs.Get(module.PrefabId).Descriptor, module.Rotation, 1);
				VillagePrefabVariant right = new(prefabs.Get(adjacent.PrefabId).Descriptor, adjacent.Rotation, 1);
				if (!VillageSocketCompatibility.CreatesConnection(left.Socket(direction),
					right.Socket(VillageSocketCompatibility.Opposite(direction)))) disconnectedRoadPairs++;
			}
		Assert.True(disconnectedRoadPairs * 4 <= Math.Max(1, adjacentRoadPairs),
			$"The spacing rule left {disconnectedRoadPairs} disconnected pairs among {adjacentRoadPairs} adjacent road pairs.");
		Assert.Single(second);
		Assert.Equal(first[0].VillageId, second[0].VillageId);
		Assert.Equal(first[0].GroundAttempts, second[0].GroundAttempts);
		Assert.Equal(first[0].Modules.AsEnumerable(), second[0].Modules);
	}

    private static BlockValue[] CopyCells(VillagePrefab prefab) => Enumerable.Range(0, 125)
        .Select(index => prefab.GetCell(index % 5, index / 25, index / 5 % 5)).ToArray();
    private static VillagePrefabCatalog LoadCatalog() => VillagePrefabCatalog.Load(
        Path.Combine(AppContext.BaseDirectory, "data", "world", "village-prefabs", "catalog.json"));
}
