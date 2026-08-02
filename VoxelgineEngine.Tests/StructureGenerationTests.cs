using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.Engine.Server;
using Voxelgine.Engine.World.Structures;
using Voxelgine.Graphics;

namespace VoxelgineEngine.Tests;

public sealed class StructureGenerationTests
{
	[Fact]
	public void InfrastructureCatalog_DefinesEveryInfrastructureBlockAndRejectsOrdinaryBlocks()
	{
		BlockType[] expected =
		[
			BlockType.SteelFrame, BlockType.MachineCasing, BlockType.PowerCell,
			BlockType.PowerConduit, BlockType.ControlTerminal, BlockType.LogicCore,
			BlockType.RelayEmitter, BlockType.GravityCoil, BlockType.LinearActuator,
			BlockType.FabricatorCore,
		];

		Assert.Equal(expected, InfrastructureBlockCatalog.All.Select(static definition => definition.Block));
		Assert.False(InfrastructureBlockCatalog.TryGet(BlockType.Stone, out _));
		Assert.Throws<KeyNotFoundException>(() => InfrastructureBlockCatalog.Get(BlockType.Stone));
	}

	[Fact]
	public void BlueprintCatalog_LoadsVersionedServerContent()
	{
		StructureBlueprintCatalog catalog = LoadCatalog();

		Assert.Contains(catalog.Blueprints, blueprint => blueprint.Role == StructureRole.Shelter);
		Assert.Contains(catalog.Blueprints, blueprint => blueprint.Role == StructureRole.Relay);
		Assert.Contains(catalog.Blueprints, blueprint => blueprint.Role == StructureRole.GravityAnchor);
		Assert.All(catalog.Blueprints.SelectMany(static blueprint => blueprint.Markers), marker =>
			Assert.Matches("^[a-z][a-z0-9._-]{0,63}$", marker.Id));
	}

	[Fact]
	public void MachineBlueprints_KeepAllAuthoredInfrastructureConnectedToTheirFunction()
	{
		foreach (StructureBlueprint blueprint in LoadCatalog().Blueprints
			.Where(blueprint => blueprint.Markers.Any(static marker => marker.Kind == StructureMarkerKind.MachineFunction)))
		{
			HashSet<BlockCoordinate> infrastructure = new();
			for (int y = 0; y < blueprint.Size.Y; y++)
				for (int z = 0; z < blueprint.Size.Z; z++)
					for (int x = 0; x < blueprint.Size.X; x++)
					{
						char cell = blueprint.GetCell(x, y, z);
						if (blueprint.Palette.TryGetValue(cell, out BlockType block) && InfrastructureBlockCatalog.TryGet(block, out _))
							infrastructure.Add(new BlockCoordinate(x, y, z));
					}
			BlockCoordinate start = blueprint.Markers.Single(static marker => marker.Kind == StructureMarkerKind.MachineFunction).Position;
			HashSet<BlockCoordinate> visited = new();
			Queue<BlockCoordinate> queue = new();
			queue.Enqueue(start);
			BlockCoordinate[] directions =
			[
				new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0),
				new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
			];
			while (queue.TryDequeue(out BlockCoordinate coordinate))
			{
				if (!infrastructure.Contains(coordinate) || !visited.Add(coordinate))
					continue;
				foreach (BlockCoordinate direction in directions)
					queue.Enqueue(coordinate + direction);
			}

			Assert.Equal(infrastructure.Order(), visited.Order());
		}
	}

	[Fact]
	public void Planner_IsDeterministicAndGuaranteesCriticalRoles()
	{
		const int size = 512;
		int[] heights = Enumerable.Repeat(48, size * size).ToArray();
		StructureBlueprintCatalog catalog = LoadCatalog();

		WorldFeaturePlan first = WorldStructurePlanner.Plan(catalog, heights, size, size, 12345).Plan;
		WorldFeaturePlan second = WorldStructurePlanner.Plan(catalog, heights, size, size, 12345).Plan;

		Assert.InRange(first.Sites.Count, 24, 36);
		Assert.Single(first.Sites, static site => site.Role == StructureRole.Shelter);
		Assert.Equal(3, first.Sites.Count(static site => site.Role == StructureRole.Relay));
		Assert.Single(first.Sites, static site => site.Role == StructureRole.GravityAnchor);
		Assert.Equal(3, first.Sites.Count(static site => site.Role == StructureRole.Shaft));
		Assert.Equal(
			first.Sites.Select(static site => (site.Id, site.Origin, site.Rotation)),
			second.Sites.Select(static site => (site.Id, site.Origin, site.Rotation)));
		Assert.Equal(first.Routes.Select(static route => route.Id), second.Routes.Select(static route => route.Id));
		Assert.Equal(
			first.Routes.SelectMany(static route => route.Cells),
			second.Routes.SelectMany(static route => route.Cells));
		WorldFeaturePlan different = WorldStructurePlanner.Plan(catalog, heights, size, size, 12346).Plan;
		Assert.NotEqual(
			first.Sites.Select(static site => site.Origin),
			different.Sites.Select(static site => site.Origin));
	}

	[Fact]
	public void PlannedRoads_UseGroundBelowConnectorFeetSpace()
	{
		const int size = 512;
		WorldFeaturePlan plan = WorldStructurePlanner.Plan(
			LoadCatalog(),
			Enumerable.Repeat(48, size * size).ToArray(),
			size,
			size,
			12345).Plan;
		Dictionary<GeneratedSiteId, PlannedSite> sites = plan.Sites.ToDictionary(static site => site.Id);

		foreach (PlannedRoute route in plan.Routes.Where(static route => route.Kind == StructureConnectorKind.Road))
		{
			PlannedConnector source = sites[route.SourceSite].Connectors.Single(connector => connector.Id == route.SourceConnector);
			PlannedConnector destination = sites[route.DestinationSite].Connectors.Single(connector => connector.Id == route.DestinationConnector);
			Assert.Equal(source.Position + new BlockCoordinate(0, -1, 0), route.Cells[0]);
			Assert.Equal(destination.Position + new BlockCoordinate(0, -1, 0), route.Cells[^1]);
			Assert.DoesNotContain(source.Position, route.Cells);
			Assert.DoesNotContain(destination.Position, route.Cells);
		}
	}

	[Fact]
	public void PlannedRoutes_DoNotOverwriteUnrelatedSiteReservations()
	{
		const int size = 512;
		WorldFeaturePlan plan = WorldStructurePlanner.Plan(
			LoadCatalog(),
			Enumerable.Repeat(48, size * size).ToArray(),
			size,
			size,
			12345).Plan;

		foreach (PlannedRoute route in plan.Routes)
		{
			foreach (PlannedSite site in plan.Sites.Where(site => site.Id != route.SourceSite && site.Id != route.DestinationSite))
			{
				Assert.DoesNotContain(route.Cells, site.Reservation.Contains);
			}
		}
	}

	[Theory]
	[InlineData(1)]
	[InlineData(17)]
	[InlineData(666)]
	[InlineData(12345)]
	[InlineData(-97)]
	public void Planner_FixedSeedSuiteAlwaysPlacesCriticalRolesAndConnectedRoutes(int seed)
	{
		const int size = 512;
		int[] heights = Enumerable.Repeat(48, size * size).ToArray();
		WorldFeaturePlan plan = WorldStructurePlanner.Plan(LoadCatalog(), heights, size, size, seed).Plan;

		Assert.Single(plan.Sites, static site => site.Role == StructureRole.Shelter);
		Assert.Equal(3, plan.Sites.Count(static site => site.Role == StructureRole.Relay));
		Assert.Single(plan.Sites, static site => site.Role == StructureRole.GravityAnchor);
		Assert.Equal(3, plan.Sites.Count(static site => site.Role == StructureRole.Shaft));
		AssertRouteNetworkConnectsAllSites(plan, StructureConnectorKind.Road);
		AssertRouteNetworkConnectsAllSites(plan, StructureConnectorKind.Conduit);
	}

	[Fact]
	public void Planner_EmergencyFallbacksRemainInsideBoundsAndNeverOverlap()
	{
		const int size = 512;
		int[] hostileHeights = new int[size * size];
		for (int x = 0; x < size; x++)
		{
			for (int z = 0; z < size; z++)
				hostileHeights[x * size + z] = 20 + (x + z) % 31;
		}

		WorldFeatureGenerationResult result = WorldStructurePlanner.Plan(LoadCatalog(), hostileHeights, size, size, 77);
		PlannedSite[] emergency = result.Plan.Sites.Where(static site => site.EmergencyFallback).ToArray();
		Assert.NotEmpty(emergency);
		for (int index = 0; index < emergency.Length; index++)
		{
			StructureBounds bounds = emergency[index].ModificationBounds;
			Assert.InRange(bounds.Minimum.X, 0, size - 1);
			Assert.InRange(bounds.Maximum.X, 0, size - 1);
			Assert.InRange(bounds.Minimum.Z, 0, size - 1);
			Assert.InRange(bounds.Maximum.Z, 0, size - 1);
			for (int other = index + 1; other < emergency.Length; other++)
				Assert.False(bounds.Intersects(emergency[other].ModificationBounds));
		}
	}

	[Fact]
	public void Archive_RoundTripsGeneratedSiteMachineAndProgressionMetadata()
	{
		ChunkMap map = new();
		BlockType[] blocks = new BlockType[ChunkSnapshot.BlockCount];
		blocks[0] = BlockType.RelayEmitter;
		map.ApplyColumn(new ChunkColumnSnapshot(0, 0, 1, [new ChunkSnapshot(0, 0, 0, blocks)]));
		GeneratedSiteId siteId = new("relay-01");
		MachineKey key = new(new BlockCoordinate(0, 0, 0), InfrastructureFunctionKind.Relay);
		WorldFeaturePlan plan = new([
			new PlannedSite(siteId, StructureRole.Relay, "relay.basic", new BlockCoordinate(0, 0, 0), 0,
				new StructureBounds(new BlockCoordinate(-1, -1, -1), new BlockCoordinate(8, 8, 8)), false,
				new StructureBounds(new BlockCoordinate(-1, -1, -1), new BlockCoordinate(8, 8, 8)),
				[new PlannedMarker(new GeneratedMarkerId(siteId, "relay_function"), StructureMarkerKind.MachineFunction,
					new BlockCoordinate(0, 0, 0), BlockType.RelayEmitter, string.Empty)], Array.Empty<PlannedConnector>())
		], Array.Empty<PlannedRoute>());
		WorldArchiveMetadata metadata = new(7, Vector3.One, Vector3.UnitX, Vector3.UnitZ, plan,
			[new PersistedMachineIntent(key, true)], HabitatMilestone.RelaysOnline);
		using MemoryStream stream = new();

		WorldArchive.Write(stream, map, metadata);
		stream.Position = 0;
		WorldArchiveReadResult loaded = WorldArchive.Read(stream);

		Assert.Equal("relay-01", loaded.Metadata.GeneratedFeatures.Sites[0].Id.Value);
		Assert.Equal(key, loaded.Metadata.MachineIntents[0].Key);
		Assert.True(loaded.Metadata.MachineIntents[0].RequestedEnabled);
		Assert.Equal(HabitatMilestone.RelaysOnline, loaded.Metadata.Milestone);
	}

	[Fact]
	public void InfrastructureStatePacket_RoundTripsDerivedMachineDiagnostics()
	{
		InfrastructureStatePacket source = new()
		{
			X = -17,
			Y = 42,
			Z = 9,
			Function = InfrastructureFunctionKind.GravityAnchor,
			RequestedEnabled = true,
			State = InfrastructureMachineState.InsufficientPower,
			PowerSupply = 12,
			PowerDemand = 15,
			StructuralPoints = 30,
			MissingRequirements = "power 12/15",
		};

		InfrastructureStatePacket decoded = Assert.IsType<InfrastructureStatePacket>(Packet.Deserialize(source.Serialize()));
		Assert.Equal(source.X, decoded.X);
		Assert.Equal(source.Y, decoded.Y);
		Assert.Equal(source.Z, decoded.Z);
		Assert.Equal(source.Function, decoded.Function);
		Assert.Equal(source.RequestedEnabled, decoded.RequestedEnabled);
		Assert.Equal(source.State, decoded.State);
		Assert.Equal(source.PowerSupply, decoded.PowerSupply);
		Assert.Equal(source.PowerDemand, decoded.PowerDemand);
		Assert.Equal(source.StructuralPoints, decoded.StructuralPoints);
		Assert.Equal(source.MissingRequirements, decoded.MissingRequirements);
	}

	[Fact]
	public void DirtyInfrastructureNetwork_DisablesImmediatelyAndRebuildsDeterministically()
	{
		ChunkMap map = new();
		for (int x = 0; x < 8; x++) map.SetBlock(x, 0, 0, BlockType.SteelFrame);
		map.SetBlock(0, 1, 0, BlockType.RelayEmitter);
		map.SetBlock(1, 1, 0, BlockType.ControlTerminal);
		map.SetBlock(2, 1, 0, BlockType.LogicCore);
		map.SetBlock(3, 1, 0, BlockType.PowerCell);
		map.SetBlock(4, 1, 0, BlockType.PowerCell);
		using InfrastructureMachineService service = new(map, WorldFeaturePlan.Empty, new NullLogging());
		MachineKey key = new(new BlockCoordinate(0, 1, 0), InfrastructureFunctionKind.Relay);
		service.SetRequestedEnabled(key, true);
		service.Update(32);
		Assert.True(service.TryGet(key, out InfrastructureMachineSnapshot active));
		Assert.Equal(InfrastructureMachineState.Active, active.State);

		map.SetBlock(2, 1, 0, BlockType.None);
		Assert.True(service.TryGet(key, out InfrastructureMachineSnapshot dirty));
		Assert.Equal(InfrastructureMachineState.UnpoweredDirty, dirty.State);
		service.Update(32);
		Assert.True(service.TryGet(key, out InfrastructureMachineSnapshot rebuilt));
		Assert.Equal(InfrastructureMachineState.MissingComponents, rebuilt.State);
	}

	[Fact]
	public void SplitInfrastructureNetwork_RebuildsEveryResultingComponent()
	{
		ChunkMap map = new();
		BuildRelay(map, 0);
		BuildRelay(map, 16);
		for (int x = 5; x < 16; x++)
			map.SetBlock(x, 1, 0, BlockType.PowerConduit);

		using InfrastructureMachineService service = new(map, WorldFeaturePlan.Empty, new NullLogging());
		MachineKey leftKey = new(new BlockCoordinate(0, 1, 0), InfrastructureFunctionKind.Relay);
		MachineKey rightKey = new(new BlockCoordinate(16, 1, 0), InfrastructureFunctionKind.Relay);
		Assert.True(service.SetRequestedEnabled(leftKey, true));
		Assert.True(service.SetRequestedEnabled(rightKey, true));
		service.Update(32);
		Assert.Equal(InfrastructureMachineState.Active, service.Machines.Single(machine => machine.Key == leftKey).State);
		Assert.Equal(InfrastructureMachineState.Active, service.Machines.Single(machine => machine.Key == rightKey).State);

		map.SetBlock(10, 1, 0, BlockType.None);

		Assert.Equal(InfrastructureMachineState.UnpoweredDirty, service.Machines.Single(machine => machine.Key == leftKey).State);
		Assert.Equal(InfrastructureMachineState.UnpoweredDirty, service.Machines.Single(machine => machine.Key == rightKey).State);
		service.Update(32);
		Assert.Equal(InfrastructureMachineState.Active, service.Machines.Single(machine => machine.Key == leftKey).State);
		Assert.Equal(InfrastructureMachineState.Active, service.Machines.Single(machine => machine.Key == rightKey).State);
	}

	[Fact]
	public void SharedPowerNetwork_DoesNotShareRecipePartsOrChargeDisabledFunctions()
	{
		ChunkMap map = new();
		BuildRelay(map, 0);
		for (int x = 5; x < 16; x++)
			map.SetBlock(x, 1, 0, BlockType.PowerConduit);
		map.SetBlock(16, 1, 0, BlockType.RelayEmitter);

		using InfrastructureMachineService service = new(map, WorldFeaturePlan.Empty, new NullLogging());
		MachineKey complete = new(new BlockCoordinate(0, 1, 0), InfrastructureFunctionKind.Relay);
		MachineKey incomplete = new(new BlockCoordinate(16, 1, 0), InfrastructureFunctionKind.Relay);
		Assert.True(service.SetRequestedEnabled(complete, true));
		service.Update(32);
		Assert.Equal(InfrastructureMachineState.Active, service.Machines.Single(machine => machine.Key == complete).State);

		Assert.True(service.SetRequestedEnabled(incomplete, true));
		service.Update(32);
		Assert.Equal(InfrastructureMachineState.MissingComponents, service.Machines.Single(machine => machine.Key == incomplete).State);
		Assert.Equal(InfrastructureMachineState.Active, service.Machines.Single(machine => machine.Key == complete).State);
	}

	[Fact]
	public void DestroyedFunction_PublishesRemovalAndClearsMachineIndex()
	{
		ChunkMap map = new();
		BuildRelay(map, 0);
		using InfrastructureMachineService service = new(map, WorldFeaturePlan.Empty, new NullLogging());
		MachineKey key = new(new BlockCoordinate(0, 1, 0), InfrastructureFunctionKind.Relay);
		List<InfrastructureMachineSnapshot> changes = new();
		service.StateChanged += changes.Add;

		map.SetBlock(0, 1, 0, BlockType.None);

		Assert.False(service.TryGet(key, out _));
		InfrastructureMachineSnapshot removed = Assert.Single(changes, change => change.Key == key && change.State == InfrastructureMachineState.Removed);
		Assert.False(removed.RequestedEnabled);
	}

	[Theory]
	[InlineData(0, 1, 2, 3, 2, 1, 4)]
	[InlineData(90, 5, 2, 1, 4, 1, 2)]
	[InlineData(180, 7, 2, 5, 2, 1, 4)]
	[InlineData(270, 3, 2, 7, 4, 1, 2)]
	public void RotatedVolumeBounds_PreserveEveryCell(
		int rotation,
		int expectedX,
		int expectedY,
		int expectedZ,
		int expectedSizeX,
		int expectedSizeY,
		int expectedSizeZ)
	{
		BlockCoordinate minimum = new(1, 2, 3);
		BlockCoordinate size = new(2, 1, 4);
		BlockCoordinate container = new(10, 8, 12);

		Assert.Equal(
			new BlockCoordinate(expectedX, expectedY, expectedZ),
			WorldStructurePlanner.RotateBoundsMinimum(minimum, size, container, rotation));
		Assert.Equal(
			new BlockCoordinate(expectedSizeX, expectedSizeY, expectedSizeZ),
			WorldStructurePlanner.RotateBoundsSize(size, rotation));
	}

	[Fact]
	public void Planner_RotatesDynamicFogBoundsRatherThanOnlyTheirFirstCell()
	{
		const int worldSize = 512;
		StructureBlueprintCatalog catalog = LoadCatalog();
		WorldFeaturePlan plan = WorldStructurePlanner.Plan(
			catalog,
			Enumerable.Repeat(48, worldSize * worldSize).ToArray(),
			worldSize,
			worldSize,
			12345).Plan;
		StructureBlueprint support = catalog.Get("support.basic");
		StructureMarker source = support.Markers.Single(marker => marker.Id == "coolant_hazard");
		BlockCoordinate sourceSize = new(3, 1, 3);

		foreach (PlannedSite site in plan.Sites.Where(static site => site.BlueprintId == "support.basic"))
		{
			PlannedMarker marker = site.Markers.Single(value => value.Id.BlueprintMarkerId == source.Id);
			BlockCoordinate expected = site.Origin + WorldStructurePlanner.RotateBoundsMinimum(
				source.Position, sourceSize, support.Size, site.Rotation);
			Assert.Equal(expected, marker.Position);
		}
	}

	[Theory]
	[InlineData(0, 0f, 0f, 1f)]
	[InlineData(90, -1f, 0f, 0f)]
	[InlineData(180, 0f, 0f, -1f)]
	[InlineData(270, 1f, 0f, 0f)]
	public void GeneratedDoorFacing_FollowsSiteRotation(int rotation, float x, float y, float z)
	{
		GeneratedSiteId siteId = new("shelter-01");
		PlannedMarker marker = new(new GeneratedMarkerId(siteId, "front_door"), StructureMarkerKind.Door,
			new BlockCoordinate(10, 4, 10), null, string.Empty);
		StructureBounds bounds = new(new BlockCoordinate(0, 0, 0), new BlockCoordinate(20, 20, 20));
		PlannedSite site = new(siteId, StructureRole.Shelter, "shelter.basic", new BlockCoordinate(0, 0, 0),
			rotation, bounds, false, bounds, [marker], Array.Empty<PlannedConnector>());

		Assert.Equal(new Vector3(x, y, z), ServerLoop.GetDoorFacing(new WorldFeaturePlan([site], []), marker));
	}

	[Fact]
	public void BlueprintLoader_ValidatesExpectedBlocksForEveryMarkerKind()
	{
		string path = WriteTemporaryBlueprint(
			"\"markers\":[{\"id\":\"door\",\"kind\":\"Door\",\"position\":[0,0,0],\"expectedBlock\":\"Dirt\"}],\"fogVolumes\":[]");
		try
		{
			Assert.Throws<InvalidDataException>(() => StructureBlueprintLoader.Load(path));
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public void BlueprintLoader_RejectsDuplicateFogVolumeIds()
	{
		const string fog = "{\"id\":\"mist\",\"minimum\":[0,0,0],\"size\":[1,1,1],\"density\":64,\"color\":[32,32,32]}";
		string path = WriteTemporaryBlueprint($"\"markers\":[],\"fogVolumes\":[{fog},{fog}]");
		try
		{
			Assert.Throws<InvalidDataException>(() => StructureBlueprintLoader.Load(path));
		}
		finally
		{
			File.Delete(path);
		}
	}

	[Fact]
	public void StoryRelaysUnlockAnchorAndStabilizationClearsAuthoritativeFog()
	{
		ChunkMap map = new();
		GeneratedSiteId[] relaySites = [new("relay-01"), new("relay-02"), new("relay-03")];
		List<PlannedSite> sites = new();
		List<MachineKey> relayKeys = new();
		for (int index = 0; index < relaySites.Length; index++)
		{
			int x = index * 16;
			BuildRelay(map, x);
			MachineKey key = new(new BlockCoordinate(x, 1, 0), InfrastructureFunctionKind.Relay);
			relayKeys.Add(key);
			sites.Add(CreateMachineSite(relaySites[index], StructureRole.Relay, key, BlockType.RelayEmitter));
		}
		const int anchorX = 64;
		BuildGravityAnchor(map, anchorX);
		GeneratedSiteId anchorSite = new("gravityanchor-01");
		MachineKey anchorKey = new(new BlockCoordinate(anchorX, 1, 0), InfrastructureFunctionKind.GravityAnchor);
		sites.Add(CreateMachineSite(anchorSite, StructureRole.GravityAnchor, anchorKey, BlockType.GravityCoil));
		PlannedMarker fogMarker = new(
			new GeneratedMarkerId(anchorSite, "dynamic_fog"),
			StructureMarkerKind.Effect,
			new BlockCoordinate(anchorX, 1, 4),
			null,
			"{\"dynamicFog\":true,\"size\":[2,1,2],\"color\":[80,120,180],\"density\":100}");
		PlannedSite anchor = sites[^1];
		sites[^1] = anchor with { Markers = anchor.Markers.Append(fogMarker).OrderBy(static marker => marker.Id.BlueprintMarkerId, StringComparer.Ordinal).ToArray() };
		WorldFeaturePlan features = new(sites.OrderBy(static site => site.Id).ToArray(), Array.Empty<PlannedRoute>());
		FogVoxel fog = FogVoxel.FromStraight(new Rgba32(80, 120, 180), 100);
		map.FillFog(anchorX, 1, 4, 2, 1, 2, fog);

		using InfrastructureMachineService infrastructure = new(map, features, new NullLogging());
		using HabitatProgressionService progression = new(map, features, infrastructure, new NullLogging());
		progression.RestoreMilestone(HabitatMilestone.None);
		foreach (MachineKey key in relayKeys)
			Assert.True(infrastructure.SetRequestedEnabled(key, true));
		infrastructure.Update(32);
		progression.Update(1);
		Assert.Equal(HabitatMilestone.RelaysOnline, progression.Milestone);
		Assert.True(progression.GravityAnchorUnlocked);

		Assert.True(infrastructure.SetRequestedEnabled(anchorKey, true));
		infrastructure.Update(32);
		progression.Update(2);
		Assert.Equal(HabitatMilestone.Stabilized, progression.Milestone);
		Assert.Equal(FogVoxel.Empty, map.GetFog(anchorX, 1, 4));
	}

	private static StructureBlueprintCatalog LoadCatalog() =>
		StructureBlueprintCatalog.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "data", "world", "structures"));

	private static string WriteTemporaryBlueprint(string collections)
	{
		string path = Path.Combine(Path.GetTempPath(), $"structure-{Guid.NewGuid():N}.json");
		File.WriteAllText(path,
			"{\"formatVersion\":1,\"markerDataVersion\":1,\"id\":\"test.blueprint\",\"role\":\"Support\"," +
			"\"critical\":false,\"size\":[1,1,1],\"anchor\":[0,0,0],\"rotations\":[0]," +
			"\"palette\":{\"A\":\"Stone\"},\"layers\":[[\"A\"]],\"connectors\":[]," + collections + "}");
		return path;
	}

	private static void AssertRouteNetworkConnectsAllSites(WorldFeaturePlan plan, StructureConnectorKind kind)
	{
		GeneratedSiteId[] expected = plan.Sites
			.Where(site => site.Connectors.Any(connector => connector.Kind == kind))
			.Select(static site => site.Id)
			.ToArray();
		if (expected.Length == 0)
			return;
		HashSet<GeneratedSiteId> visited = [expected[0]];
		bool changed;
		do
		{
			changed = false;
			foreach (PlannedRoute route in plan.Routes.Where(route => route.Kind == kind))
			{
				if (visited.Contains(route.SourceSite) && visited.Add(route.DestinationSite)) changed = true;
				if (visited.Contains(route.DestinationSite) && visited.Add(route.SourceSite)) changed = true;
			}
		} while (changed);
		Assert.All(expected, site => Assert.Contains(site, visited));
	}

	private static PlannedSite CreateMachineSite(
		GeneratedSiteId site,
		StructureRole role,
		MachineKey key,
		BlockType expectedBlock)
	{
		BlockCoordinate position = key.FunctionCoordinate;
		StructureBounds bounds = new(position - new BlockCoordinate(1, 1, 1), position + new BlockCoordinate(12, 4, 5));
		return new PlannedSite(site, role, $"{role.ToString().ToLowerInvariant()}.test", position, 0,
			bounds, false, bounds,
			[new PlannedMarker(new GeneratedMarkerId(site, "machine_function"), StructureMarkerKind.MachineFunction,
				position, expectedBlock, string.Empty)], Array.Empty<PlannedConnector>());
	}

	private static void BuildRelay(ChunkMap map, int x)
	{
		for (int offset = 0; offset < 4; offset++) map.SetBlock(x + offset, 0, 0, BlockType.SteelFrame);
		map.SetBlock(x, 1, 0, BlockType.RelayEmitter);
		map.SetBlock(x + 1, 1, 0, BlockType.ControlTerminal);
		map.SetBlock(x + 2, 1, 0, BlockType.LogicCore);
		map.SetBlock(x + 3, 1, 0, BlockType.PowerCell);
		map.SetBlock(x + 4, 1, 0, BlockType.PowerCell);
	}

	private static void BuildGravityAnchor(ChunkMap map, int x)
	{
		for (int offset = 0; offset < 12; offset++) map.SetBlock(x + offset, 0, 0, BlockType.SteelFrame);
		for (int offset = 0; offset < 4; offset++) map.SetBlock(x + offset, 1, 0, BlockType.GravityCoil);
		map.SetBlock(x + 4, 1, 0, BlockType.ControlTerminal);
		map.SetBlock(x + 5, 1, 0, BlockType.LogicCore);
		map.SetBlock(x + 6, 1, 0, BlockType.LogicCore);
		for (int offset = 7; offset <= 10; offset++) map.SetBlock(x + offset, 1, 0, BlockType.PowerCell);
	}

	private sealed class NullLogging : IFishLogging
	{
		public void Init(bool IsServer = false) { }
		public void WriteLine(string message) { }
		public void ServerWriteLine(string message) { }
		public void ClientWriteLine(string message) { }
		public void ServerNetworkWriteLine(string message) { }
		public void ClientNetworkWriteLine(string message) { }
	}
}
