using Voxelgine.Engine;
using Voxelgine.Engine.World.Structures;
using Voxelgine.WorldGeneration;

namespace Voxelgine.Graphics;

internal sealed class WorldPlanVoxelBuilder
{
	private readonly WorldPlan plan;
	private readonly int maximumY;
	private readonly WorldPlanVolumeSampler volumeSampler;
	private readonly Dictionary<(int X, int Y, int Z), BlockValue[]> chunks = [];
	private readonly Dictionary<(int X, int Y, int Z), FogVoxel[]> fogChunks = [];

	private WorldPlanVoxelBuilder(WorldPlan plan)
	{
		this.plan = plan;
		maximumY = plan.WorldHeight + Chunk.ChunkSize - 1;
		volumeSampler = WorldPlanGenerator.CreateVolumeSampler(plan);
	}

	internal static WorldPlanBuildResult Build(WorldPlan plan, StructureBlueprintCatalog catalog, VillagePrefabCatalog villagePrefabs, CancellationToken cancellationToken)
	{
		WorldPlanVoxelBuilder builder = new(plan);
		builder.BuildTerrain(cancellationToken);
		builder.BuildHydrology();
		WorldFeaturePlan features = catalog is null ? WorldFeaturePlan.Empty : ConvertFeatures(plan, catalog);
		System.Diagnostics.Stopwatch stampTimer = System.Diagnostics.Stopwatch.StartNew();
		if (catalog is not null) builder.StampFeatures(catalog, features, cancellationToken);
		if (villagePrefabs is not null) builder.StampVillages(villagePrefabs, cancellationToken);
		builder.StampTrees(cancellationToken);
		builder.StampFoliage(features, cancellationToken);
		return new(builder.CreateColumns(), features, new(TimeSpan.Zero, TimeSpan.Zero, stampTimer.Elapsed));
	}

	private void StampVillages(VillagePrefabCatalog catalog, CancellationToken cancellationToken)
	{
		foreach (PlannedVillageLayout layout in plan.VillageLayouts)
		{
			foreach (PlannedVillageModule module in layout.Modules.OrderBy(static value => value.Floor).ThenBy(static value => value.Origin.Y))
			{
				VillagePrefab prefab = catalog.Get(module.PrefabId);
				for (int y = 0; y < VillagePrefabDescriptor.Height; y++)
				for (int z = 0; z < VillagePrefabDescriptor.Length; z++)
				for (int x = 0; x < VillagePrefabDescriptor.Width; x++)
				{
					BlockCoordinate rotated = WorldStructurePlanner.Rotate(new(x, y, z),
						new(VillagePrefabDescriptor.Width, VillagePrefabDescriptor.Height, VillagePrefabDescriptor.Length), module.Rotation);
					BlockValue value = prefab.GetCell(x, y, z);
					if (y == 0 && value.Type == BlockType.None) continue;
					SetBlock(module.Origin.X + rotated.X, module.Origin.Y + rotated.Y, module.Origin.Z + rotated.Z, value.Type);
				}
			}
			foreach (PlanPoint3 cell in SharedInteriorWallCells(layout, catalog))
				SetBlock(cell.X, cell.Y, cell.Z, BlockType.None);
		}
	}

	internal static IEnumerable<PlanPoint3> SharedInteriorWallCells(PlannedVillageLayout layout, VillagePrefabCatalog catalog)
	{
		Dictionary<(int Floor, int X, int Z), PlannedVillageModule> modules = layout.Modules.ToDictionary(
			static module => (module.Floor, module.Origin.X, module.Origin.Z));
		foreach (PlannedVillageModule module in layout.Modules)
		{
			if (modules.TryGetValue((module.Floor, module.Origin.X + VillagePrefabDescriptor.Width, module.Origin.Z),
				out PlannedVillageModule east) && HasInteriorConnection(module, east, VillageSocketDirection.PositiveX, catalog))
			{
				for (int y = 1; y < VillagePrefabDescriptor.Height - 1; y++)
				for (int z = 0; z < VillagePrefabDescriptor.Length; z++)
				{
					yield return new(module.Origin.X + VillagePrefabDescriptor.Width - 1, module.Origin.Y + y, module.Origin.Z + z);
					yield return new(east.Origin.X, east.Origin.Y + y, east.Origin.Z + z);
				}
			}
			if (modules.TryGetValue((module.Floor, module.Origin.X, module.Origin.Z + VillagePrefabDescriptor.Length),
				out PlannedVillageModule south) && HasInteriorConnection(module, south, VillageSocketDirection.PositiveZ, catalog))
			{
				for (int y = 1; y < VillagePrefabDescriptor.Height - 1; y++)
				for (int x = 0; x < VillagePrefabDescriptor.Width; x++)
				{
					yield return new(module.Origin.X + x, module.Origin.Y + y, module.Origin.Z + VillagePrefabDescriptor.Length - 1);
					yield return new(south.Origin.X + x, south.Origin.Y + y, south.Origin.Z);
				}
			}
		}
	}

	private static bool HasInteriorConnection(PlannedVillageModule first, PlannedVillageModule second,
		VillageSocketDirection direction, VillagePrefabCatalog catalog)
	{
		VillageSocketDescriptor firstSocket = WorldSocket(catalog.Get(first.PrefabId).Descriptor, first.Rotation, direction);
		VillageSocketDescriptor secondSocket = WorldSocket(catalog.Get(second.PrefabId).Descriptor, second.Rotation,
			VillageSocketCompatibility.Opposite(direction));
		return firstSocket.Types.Intersect(secondSocket.Types, StringComparer.Ordinal)
			.Any(static semantic => semantic.StartsWith("house.", StringComparison.Ordinal)
				|| semantic.StartsWith("interior.", StringComparison.Ordinal));
	}

	private static VillageSocketDescriptor WorldSocket(VillagePrefabDescriptor prefab, int rotation,
		VillageSocketDirection worldDirection)
	{
		if (worldDirection is VillageSocketDirection.PositiveY or VillageSocketDirection.NegativeY)
			return prefab.Socket(worldDirection);
		int sourceDirection = ((int)worldDirection - rotation / 90) & 3;
		return prefab.Socket((VillageSocketDirection)sourceDirection);
	}

	private void BuildTerrain(CancellationToken cancellationToken)
	{
		for (int x = 0; x < plan.Width; x++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			for (int z = 0; z < plan.Length; z++)
			{
				if (!plan.IsLand(x, z)) continue;
				int surface = plan.GetHeight(x, z);
				for (int y = 0; y <= surface; y++)
				{
					if (!volumeSampler.IsSolid(x, y, z)) continue;
					SetBlock(x, y, z, TerrainBlock(x, y, z, surface));
				}
			}
		}
	}

	private void BuildHydrology()
	{
		foreach (PlannedPond pond in plan.Ponds) foreach (PlanPoint3 cell in pond.Cells)
		{
			SetBlock(cell.X, cell.Y, cell.Z, BlockType.Sand);
			for (int y = cell.Y + 1; y <= pond.WaterLevel; y++) SetBlock(cell.X, y, cell.Z, BlockType.Water);
		}
	}

	private void StampFeatures(StructureBlueprintCatalog catalog, WorldFeaturePlan features, CancellationToken cancellationToken)
	{
		foreach (PlannedSite site in features.Sites)
		{
			cancellationToken.ThrowIfCancellationRequested();
			StructureBlueprint blueprint = catalog.Get(site.BlueprintId);
			ClearReservedWater(site.Reservation);
			if (site.EmergencyFallback) PrepareEmergencyFoundation(site, blueprint);
			for (int y = 0; y < blueprint.Size.Y; y++) for (int z = 0; z < blueprint.Size.Z; z++) for (int x = 0; x < blueprint.Size.X; x++)
			{
				char symbol = blueprint.GetCell(x, y, z);
				if (symbol == '.') continue;
				BlockCoordinate world = site.Origin + WorldStructurePlanner.Rotate(new(x, y, z), blueprint.Size, site.Rotation);
				SetBlock(world.X, world.Y, world.Z, symbol == '_' ? BlockType.None : blueprint.Palette[symbol]);
			}
			foreach (StructureFogVolume fog in blueprint.FogVolumes)
			{
				BlockCoordinate minimum = site.Origin + WorldStructurePlanner.RotateBoundsMinimum(fog.Minimum, fog.Size, blueprint.Size, site.Rotation);
				BlockCoordinate size = WorldStructurePlanner.RotateBoundsSize(fog.Size, site.Rotation);
				for (int x = 0; x < size.X; x++) for (int y = 0; y < size.Y; y++) for (int z = 0; z < size.Z; z++) SetFog(minimum.X + x, minimum.Y + y, minimum.Z + z, fog.Fog);
			}
			foreach (PlannedConnector connector in site.Connectors)
			{
				BlockCoordinate exit = connector.Position + connector.Direction;
				if (!site.Reservation.Contains(exit)) throw new InvalidDataException($"Connector {site.Id}/{connector.Id} exits its reservation.");
				if (connector.Kind == StructureConnectorKind.Conduit) SetBlock(exit.X, exit.Y, exit.Z, BlockType.PowerConduit);
				else
				{
					SetBlock(exit.X, exit.Y, exit.Z, BlockType.None);
					if (connector.Kind == StructureConnectorKind.Road) SetBlock(exit.X, exit.Y + 1, exit.Z, BlockType.None);
				}
			}
		}
		foreach (PlannedRoute route in features.Routes) foreach (BlockCoordinate cell in route.Cells)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (route.Kind == StructureConnectorKind.Conduit) SetBlock(cell.X, cell.Y, cell.Z, BlockType.PowerConduit);
			else StampRoadCell(cell.X, cell.Z);
		}
		foreach (PlannedVillageArea village in plan.Villages)
		foreach (PlanPoint3 cell in village.AccessRoadCells)
		{
			cancellationToken.ThrowIfCancellationRequested();
			StampRoadCell(cell.X, cell.Z);
		}
	}

	private void StampRoadCell(int centerX, int centerZ)
	{
		for (int offsetX = -1; offsetX <= 1; offsetX++)
		for (int offsetZ = -1; offsetZ <= 1; offsetZ++)
		{
			int x = centerX + offsetX, z = centerZ + offsetZ;
			if ((uint)x >= (uint)plan.Width || (uint)z >= (uint)plan.Length || !plan.IsLand(x, z)) continue;
			int y = plan.GetHeight(x, z);
			SetBlock(x, y, z, BlockType.Gravel);
			SetBlock(x, y + 1, z, BlockType.None);
			SetBlock(x, y + 2, z, BlockType.None);
		}
	}

	private void StampTrees(CancellationToken cancellationToken)
	{
		foreach (PlannedTree tree in WorldPlanGenerator.DeriveTrees(plan, cancellationToken))
		{
			int trunkHeight = 6 + tree.Variant, radius = 2 + (tree.Variant & 1), canopyBase = tree.SurfaceY + trunkHeight - 3;
			for (int y = tree.SurfaceY + 1; y <= tree.SurfaceY + trunkHeight; y++) SetBlock(tree.X, y, tree.Z, BlockType.Wood);
			SetBlock(tree.X, tree.SurfaceY, tree.Z, BlockType.Dirt);
			for (int y = canopyBase; y <= tree.SurfaceY + trunkHeight + 1; y++) for (int dx = -radius; dx <= radius; dx++) for (int dz = -radius; dz <= radius; dz++)
			{
				if (dx * dx + dz * dz > radius * radius + 1 || dx == 0 && dz == 0 && y <= tree.SurfaceY + trunkHeight) continue;
				if (GetBlock(tree.X + dx, y, tree.Z + dz) == BlockType.None) SetBlock(tree.X + dx, y, tree.Z + dz, BlockType.Leaf);
			}
		}
	}

	private void StampFoliage(WorldFeaturePlan features, CancellationToken cancellationToken)
	{
		for (int x = 2; x < plan.Width - 2; x++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			for (int z = 2; z < plan.Length - 2; z++)
			{
				if (!plan.IsLand(x, z) || features.Sites.Any(site => site.Reservation.ContainsHorizontal(x, z))) continue;
				int y = plan.GetHeight(x, z);
				if (GetBlock(x, y, z) != BlockType.Grass || GetBlock(x, y + 1, z) != BlockType.None) continue;
				uint sample = unchecked((uint)(x * 73856093 ^ z * 19349663 ^ plan.Seed * 83492791));
				if ((sample & 255) < plan.GetTreeDensity(x, z) / 5) SetBlock(x, y + 1, z, BlockType.Foliage);
			}
		}
	}

	private void ClearReservedWater(StructureBounds bounds)
	{
		for (int x = Math.Max(0, bounds.Minimum.X); x <= Math.Min(plan.Width - 1, bounds.Maximum.X); x++)
		for (int z = Math.Max(0, bounds.Minimum.Z); z <= Math.Min(plan.Length - 1, bounds.Maximum.Z); z++)
		for (int y = Math.Max(0, bounds.Minimum.Y); y <= Math.Min(maximumY, bounds.Maximum.Y); y++)
			if (GetBlock(x, y, z) == BlockType.Water) SetBlock(x, y, z, BlockType.None);
	}

	private void PrepareEmergencyFoundation(PlannedSite site, StructureBlueprint blueprint)
	{
		int width = site.Rotation is 90 or 270 ? blueprint.Size.Z : blueprint.Size.X;
		int depth = site.Rotation is 90 or 270 ? blueprint.Size.X : blueprint.Size.Z;
		int floorY = site.Origin.Y - 1;
		for (int x = site.Origin.X; x < site.Origin.X + width; x++) for (int z = site.Origin.Z; z < site.Origin.Z + depth; z++)
		{
			SetBlock(x, floorY, z, BlockType.SteelFrame);
			for (int y = Math.Max(site.ModificationBounds.Minimum.Y, floorY - 12); y < floorY; y++) if (GetBlock(x, y, z) == BlockType.None) SetBlock(x, y, z, BlockType.Stone);
		}
	}

	private ChunkColumnSnapshot[] CreateColumns() => chunks.Keys.Union(fogChunks.Keys).Distinct()
		.GroupBy(static key => (key.X, key.Z)).OrderBy(static group => group.Key.X).ThenBy(static group => group.Key.Z)
		.Select(group => new ChunkColumnSnapshot(group.Key.X, group.Key.Z, 1, group.OrderBy(static key => key.Y).Select(key =>
		{
			BlockValue[] blocks = chunks.TryGetValue(key, out BlockValue[] values) ? values : new BlockValue[ChunkSnapshot.BlockCount];
			FogVoxel[] fog = fogChunks.TryGetValue(key, out FogVoxel[] fogValues) ? fogValues : new FogVoxel[ChunkSnapshot.BlockCount];
			return new ChunkSnapshot(key.X, key.Y, key.Z, blocks, fog: fog);
		}).ToArray())).ToArray();

	private BlockType GetBlock(int x, int y, int z)
	{
		if (!InBounds(x, y, z)) return BlockType.None;
		(int cx, int cy, int cz, int index) = Locate(x, y, z);
		return chunks.TryGetValue((cx, cy, cz), out BlockValue[] values) ? values[index].Type : BlockType.None;
	}

	private void SetBlock(int x, int y, int z, BlockType block)
	{
		if (!InBounds(x, y, z)) return;
		(int cx, int cy, int cz, int index) = Locate(x, y, z);
		if (!chunks.TryGetValue((cx, cy, cz), out BlockValue[] values))
		{
			if (block == BlockType.None) return;
			chunks[(cx, cy, cz)] = values = new BlockValue[ChunkSnapshot.BlockCount];
		}
		values[index] = new(block);
	}

	private void SetFog(int x, int y, int z, FogVoxel fog)
	{
		if (!InBounds(x, y, z) || fog.IsEmpty) return;
		(int cx, int cy, int cz, int index) = Locate(x, y, z);
		if (!fogChunks.TryGetValue((cx, cy, cz), out FogVoxel[] values)) fogChunks[(cx, cy, cz)] = values = new FogVoxel[ChunkSnapshot.BlockCount];
		values[index] = fog;
	}

	private bool InBounds(int x, int y, int z) => (uint)x < (uint)plan.Width && (uint)z < (uint)plan.Length && y >= 0 && y <= maximumY;
	private static (int X, int Y, int Z, int Index) Locate(int x, int y, int z)
	{
		int localX = x & 15, localY = y & 15, localZ = z & 15;
		return (x >> 4, y >> 4, z >> 4, localX + Chunk.ChunkSize * (localY + Chunk.ChunkSize * localZ));
	}
	private BlockType TerrainBlock(int x, int y, int z, int surface)
	{
		WorldBiome biome = plan.GetBiome(x, z);
		if (y == surface) return SurfaceBlock(biome);
		int depth = surface - y;
		if (biome == WorldBiome.Rocky) return BlockType.Stone;
		double radial = NormalizedRadius(x, z);
		if (biome == WorldBiome.Sand)
		{
			int sandDepth = 3 + (int)Math.Round(Math.Clamp((radial - 0.72) / 0.24, 0, 1) * 2);
			return depth <= sandDepth ? BlockType.Sand : BlockType.Stone;
		}

		double mountainFade = SmoothStep(Math.Clamp((radial - 0.18) / 0.20, 0, 1));
		double rimFade = 1 - SmoothStep(Math.Clamp((radial - 0.74) / 0.22, 0, 1));
		int dirtDepth = Math.Clamp((int)Math.Round(2 + 12 * mountainFade * rimFade), 2, 14);
		return depth <= dirtDepth ? BlockType.Dirt : BlockType.Stone;
	}

	private double NormalizedRadius(int x, int z)
	{
		double nx = (x + 0.5 - plan.Width * 0.5) / (plan.Width * 0.5);
		double nz = (z + 0.5 - plan.Length * 0.5) / (plan.Length * 0.5);
		return Math.Sqrt(nx * nx + nz * nz);
	}

	private static double SmoothStep(double value) => value * value * (3 - 2 * value);
	private static BlockType SurfaceBlock(WorldBiome biome) => biome switch { WorldBiome.Sand => BlockType.Sand, WorldBiome.Rocky => BlockType.Stone, _ => BlockType.Grass };

	private static WorldFeaturePlan ConvertFeatures(WorldPlan plan, StructureBlueprintCatalog catalog)
	{
		List<PlannedSite> sites = [];
		foreach (PlannedWorldSite source in plan.Sites)
		{
			StructureBlueprint blueprint = catalog.Get(source.TemplateId); GeneratedSiteId id = new(source.Id);
			BlockCoordinate origin = new(source.Origin.X, source.Origin.Y, source.Origin.Z);
			StructureBounds reservation = new(new(source.Reservation.MinimumX, 0, source.Reservation.MinimumZ), new(source.Reservation.MaximumX, plan.WorldHeight + 15, source.Reservation.MaximumZ));
			sites.Add(WorldStructurePlanner.BuildSite(id, (StructureRole)(byte)source.Role, blueprint, origin, source.Rotation, reservation, source.EmergencyFallback, reservation));
		}
		Dictionary<string, PlannedSite> byId = sites.ToDictionary(site => site.Id.Value, StringComparer.Ordinal); List<PlannedRoute> routes = [];
		foreach (PlannedWorldRoute source in plan.Routes)
		{
			if (!byId.TryGetValue(source.SourceSite, out PlannedSite from) || !byId.TryGetValue(source.DestinationSite, out PlannedSite to)) throw new InvalidDataException($"World-plan route '{source.Id}' references an unknown site.");
			StructureConnectorKind kind = source.Kind == WorldFeatureKind.Conduit ? StructureConnectorKind.Conduit : StructureConnectorKind.Road;
			PlannedConnector fromConnector = from.Connectors.FirstOrDefault(connector => connector.Kind == kind), toConnector = to.Connectors.FirstOrDefault(connector => connector.Kind == kind);
			if (fromConnector.Id is null || toConnector.Id is null) throw new InvalidDataException($"World-plan route '{source.Id}' references sites without matching connectors.");
			routes.Add(new(source.Id, kind, from.Id, fromConnector.Id, to.Id, toConnector.Id, source.Cells.Select(cell => new BlockCoordinate(cell.X, cell.Y + (kind == StructureConnectorKind.Conduit ? 1 : 0), cell.Z)).ToArray()));
		}
		return new(sites.ToArray(), routes.ToArray());
	}
}

internal sealed record WorldPlanBuildResult(ChunkColumnSnapshot[] Columns, WorldFeaturePlan Features, StructureGenerationTimings Timings);
