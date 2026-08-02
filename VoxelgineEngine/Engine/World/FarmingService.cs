using Voxelgine.Engine;
using Voxelgine.Engine.World.Structures;

namespace Voxelgine.Graphics;

public interface IFarmlandHydrationSource
{
	StructureBounds GetAffectedBounds(BlockCoordinate sourceCoordinate);
	bool Hydrates(ChunkMap map, BlockCoordinate farmlandCoordinate);
}

public sealed class WaterFarmlandHydrationSource : IFarmlandHydrationSource
{
	public StructureBounds GetAffectedBounds(BlockCoordinate source) => new(
		new BlockCoordinate(source.X - 4, source.Y, source.Z - 4),
		new BlockCoordinate(source.X + 4, source.Y, source.Z + 4));

	public bool Hydrates(ChunkMap map, BlockCoordinate farmland)
	{
		for (int offsetX = -4; offsetX <= 4; offsetX++)
		for (int offsetZ = -4; offsetZ <= 4; offsetZ++)
			if (map.GetBlock(farmland.X + offsetX, farmland.Y, farmland.Z + offsetZ) == BlockType.Water)
				return true;
		return false;
	}
}

/// <summary>Indexed hydration and fixed-point wheat growth, updated at one hertz.</summary>
public sealed class FarmingService : IDisposable
{
	public const ushort WheatGrowthIncrement = 547;
	private readonly ChunkMap map;
	private readonly WorldObjectStore objects;
	private readonly IFarmlandHydrationSource hydrationSource;
	private readonly HashSet<BlockCoordinate> farmland = new();
	private readonly HashSet<BlockCoordinate> hydrationQueue = new();
	private float accumulator;
	private bool disposed;

	public FarmingService(ChunkMap map, WorldObjectStore objects, IFarmlandHydrationSource hydrationSource = null)
	{
		this.map = map ?? throw new ArgumentNullException(nameof(map));
		this.objects = objects ?? throw new ArgumentNullException(nameof(objects));
		this.hydrationSource = hydrationSource ?? new WaterFarmlandHydrationSource();
		map.BlockChanged += OnBlockChanged;
	}

	public event Action<WorldPlantRecord> PlantLostSupport;

	public int IndexedFarmlandCount => farmland.Count;

	public void RebuildIndex()
	{
		farmland.Clear(); hydrationQueue.Clear();
		foreach (ChunkSnapshot chunk in map.CaptureChunks())
		for (int index = 0; index < chunk.Values.Count; index++)
		{
			BlockType type = chunk.Values[index].Type;
			if (type is not (BlockType.DryFarmland or BlockType.WetFarmland)) continue;
			int x = index % Chunk.ChunkSize;
			int yz = index / Chunk.ChunkSize;
			int y = yz % Chunk.ChunkSize;
			int z = yz / Chunk.ChunkSize;
			BlockCoordinate coordinate = new(
				chunk.ChunkX * Chunk.ChunkSize + x,
				chunk.ChunkY * Chunk.ChunkSize + y,
				chunk.ChunkZ * Chunk.ChunkSize + z);
			farmland.Add(coordinate); hydrationQueue.Add(coordinate);
		}
	}

	public void Update(float deltaTime)
	{
		if (!float.IsFinite(deltaTime) || deltaTime < 0) throw new ArgumentOutOfRangeException(nameof(deltaTime));
		accumulator += deltaTime;
		while (accumulator >= 1f)
		{
			accumulator -= 1f;
			DrainHydrationQueue();
			AdvanceWheat();
		}
	}

	public bool TryPlantWheat(BlockCoordinate support, PersistentWorldObjectKey key)
	{
		if (map.GetBlock(support.X, support.Y, support.Z) is not (BlockType.DryFarmland or BlockType.WetFarmland)) return false;
		BlockCoordinate position = new(support.X, checked(support.Y + 1), support.Z);
		if (map.GetBlock(position.X, position.Y, position.Z) != BlockType.None || objects.TryGetAt(position, out _)) return false;
		objects.ApplyTransaction(new[] { new WorldObjectOperation(
			WorldObjectOperationKind.Upsert,
			new WorldPlantRecord(key, WorldPlantType.Wheat, 0, byte.MaxValue, ItemIds.Wheat, support),
			default) });
		return true;
	}

	public bool TryHarvest(BlockCoordinate position, out WorldPlantRecord plant)
	{
		if (!objects.TryGetAt(position, out plant) || !plant.IsMature) return false;
		objects.ApplyTransaction(new[] { new WorldObjectOperation(WorldObjectOperationKind.Remove, default, plant.Key) });
		return true;
	}

	private void DrainHydrationQueue()
	{
		if (hydrationQueue.Count == 0) return;
		List<BlockMutationRequest> changes = new();
		foreach (BlockCoordinate coordinate in hydrationQueue)
		{
			BlockType current = map.GetBlock(coordinate.X, coordinate.Y, coordinate.Z);
			if (current is not (BlockType.DryFarmland or BlockType.WetFarmland)) continue;
			BlockType desired = hydrationSource.Hydrates(map, coordinate) ? BlockType.WetFarmland : BlockType.DryFarmland;
			if (current != desired) changes.Add(new BlockMutationRequest(coordinate.X, coordinate.Y, coordinate.Z, desired));
		}
		hydrationQueue.Clear();
		if (changes.Count != 0) map.ApplyBlockBatch(changes);
	}

	private void AdvanceWheat()
	{
		List<WorldObjectOperation> operations = new();
		foreach (WorldPlantRecord plant in objects.GetAll())
		{
			if (plant.PlantType != WorldPlantType.Wheat || plant.IsMature ||
				map.GetBlock(plant.Support.X, plant.Support.Y, plant.Support.Z) != BlockType.WetFarmland) continue;
			ushort progress = (ushort)Math.Min(ushort.MaxValue, plant.GrowthProgress + WheatGrowthIncrement);
			operations.Add(new WorldObjectOperation(WorldObjectOperationKind.Upsert, plant with { GrowthProgress = progress }, default));
		}
		if (operations.Count != 0) objects.ApplyTransaction(operations);
	}

	private void OnBlockChanged(BlockChange change)
	{
		BlockCoordinate coordinate = new(change.X, change.Y, change.Z);
		bool wasFarmland = change.OldType is BlockType.DryFarmland or BlockType.WetFarmland;
		bool isFarmland = change.NewType is BlockType.DryFarmland or BlockType.WetFarmland;
		if (wasFarmland) farmland.Remove(coordinate);
		if (isFarmland) { farmland.Add(coordinate); hydrationQueue.Add(coordinate); }
		if (change.OldType == BlockType.Water || change.NewType == BlockType.Water)
		{
			StructureBounds bounds = hydrationSource.GetAffectedBounds(coordinate);
			for (int x = bounds.Minimum.X; x <= bounds.Maximum.X; x++)
			for (int z = bounds.Minimum.Z; z <= bounds.Maximum.Z; z++)
			{
				BlockCoordinate candidate = new(x, coordinate.Y, z);
				if (farmland.Contains(candidate)) hydrationQueue.Add(candidate);
			}
		}
		BlockCoordinate plantPosition = new(change.X, checked(change.Y + 1), change.Z);
		if (!isFarmland && objects.TryGetAt(plantPosition, out WorldPlantRecord unsupported) && unsupported.Support == coordinate)
		{
			objects.ApplyTransaction(new[] { new WorldObjectOperation(WorldObjectOperationKind.Remove, default, unsupported.Key) });
			PlantLostSupport?.Invoke(unsupported);
		}
	}

	public void Dispose()
	{
		if (disposed) return;
		disposed = true;
		map.BlockChanged -= OnBlockChanged;
	}
}
