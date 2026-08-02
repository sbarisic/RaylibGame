using Voxelgine.Engine;
using Voxelgine.Engine.World.Structures;

namespace Voxelgine.Graphics;

public readonly record struct PersistentWorldObjectId
{
	public PersistentWorldObjectId(ulong value)
	{
		if (value == 0) throw new ArgumentOutOfRangeException(nameof(value));
		Value = value;
	}
	public ulong Value { get; }
}

public enum PersistentWorldObjectKeyKind : byte { Generated, Placed }

public readonly record struct PersistentWorldObjectKey
{
	private PersistentWorldObjectKey(
		PersistentWorldObjectKeyKind kind,
		GeneratedMarkerId generatedMarkerId,
		PersistentWorldObjectId persistentWorldObjectId)
	{
		Kind = kind;
		GeneratedMarkerId = generatedMarkerId;
		PersistentWorldObjectId = persistentWorldObjectId;
	}

	public PersistentWorldObjectKeyKind Kind { get; }
	public GeneratedMarkerId GeneratedMarkerId { get; }
	public PersistentWorldObjectId PersistentWorldObjectId { get; }
	public static PersistentWorldObjectKey Generated(GeneratedMarkerId id) => new(PersistentWorldObjectKeyKind.Generated, id, default);
	public static PersistentWorldObjectKey Placed(PersistentWorldObjectId id) => new(PersistentWorldObjectKeyKind.Placed, default, id);
}

public enum WorldPlantType : byte { Wheat = 1 }

public readonly record struct WorldPlantRecord(
	PersistentWorldObjectKey Key,
	WorldPlantType PlantType,
	ushort GrowthProgress,
	byte Health,
	ItemId HarvestItem,
	BlockCoordinate Support)
{
	public BlockCoordinate Position => new(Support.X, checked(Support.Y + 1), Support.Z);
	public byte GrowthStage => (byte)Math.Min(7, ((uint)GrowthProgress * 8) >> 16);
	public bool IsMature => GrowthProgress == ushort.MaxValue;
}

public enum WorldObjectOperationKind : byte { Upsert, Remove }
public readonly record struct WorldObjectOperation(WorldObjectOperationKind Kind, WorldPlantRecord Record, PersistentWorldObjectKey Key);
public readonly record struct WorldObjectDeltaRecord(long BaseRevision, long Revision, IReadOnlyList<WorldObjectOperation> Operations);
public readonly record struct WorldObjectColumnState(int X, int Z, ulong Epoch, long Revision, IReadOnlyList<WorldPlantRecord> Records);

/// <summary>Persistent, column-indexed lightweight objects. It deliberately has no EntityManager dependency.</summary>
public sealed class WorldObjectStore
{
	public const int MaximumColumnRecords = 16_384;
	public const int MaximumRetainedDeltaRevisions = 256;
	private readonly Dictionary<PersistentWorldObjectKey, WorldPlantRecord> byKey = new();
	private readonly Dictionary<BlockCoordinate, PersistentWorldObjectKey> byPosition = new();
	private readonly Dictionary<ChunkColumnCoordinate, ColumnData> columns = new();
	private ulong nextPlacedId = 1;
	private ulong nextEpoch = 1;

	public int Count => byKey.Count;
	public event Action<WorldObjectColumnState, WorldObjectDeltaRecord> ColumnChanged;
	public event Action<WorldObjectColumnState> ColumnReplaced;

	public PersistentWorldObjectKey AllocatePlacedKey() =>
		PersistentWorldObjectKey.Placed(new PersistentWorldObjectId(nextPlacedId++));

	public bool TryGetAt(BlockCoordinate position, out WorldPlantRecord record)
	{
		if (byPosition.TryGetValue(position, out PersistentWorldObjectKey key))
			return byKey.TryGetValue(key, out record);
		record = default;
		return false;
	}

	public bool TryGet(PersistentWorldObjectKey key, out WorldPlantRecord record) => byKey.TryGetValue(key, out record);

	public IReadOnlyList<WorldPlantRecord> GetAll() => byKey.Values
		.OrderBy(static record => record.Position.X).ThenBy(static record => record.Position.Z).ThenBy(static record => record.Position.Y).ToArray();

	public WorldObjectColumnState GetColumn(int x, int z)
	{
		ChunkColumnCoordinate coordinate = new(x, z);
		ColumnData column = GetOrCreateColumn(coordinate);
		return Snapshot(coordinate, column);
	}

	public bool TryGetDeltas(int x, int z, long baseRevision, out IReadOnlyList<WorldObjectDeltaRecord> deltas)
	{
		ColumnData column = GetOrCreateColumn(new ChunkColumnCoordinate(x, z));
		if (baseRevision == column.Revision) { deltas = Array.Empty<WorldObjectDeltaRecord>(); return true; }
		WorldObjectDeltaRecord[] result = column.History.Where(delta => delta.Revision > baseRevision).ToArray();
		if (result.Length == 0 || result[0].BaseRevision != baseRevision) { deltas = null; return false; }
		deltas = result;
		return true;
	}

	public void InstallColumnSnapshot(int x, int z, ulong epoch, long revision, IReadOnlyList<WorldPlantRecord> records)
	{
		if (epoch == 0 || revision < 1 || records.Count > MaximumColumnRecords) throw new InvalidDataException("Invalid world-object column snapshot.");
		ChunkColumnCoordinate coordinate = new(x, z);
		HashSet<PersistentWorldObjectKey> previousKeys = columns.TryGetValue(coordinate, out ColumnData previous)
			? previous.Keys.ToHashSet() : new HashSet<PersistentWorldObjectKey>();
		HashSet<PersistentWorldObjectKey> incomingKeys = new();
		HashSet<BlockCoordinate> incomingPositions = new();
		foreach (WorldPlantRecord record in records)
		{
			Validate(record);
			if (ColumnOf(record.Position) != coordinate || !incomingKeys.Add(record.Key) || !incomingPositions.Add(record.Position) ||
				(byPosition.TryGetValue(record.Position, out PersistentWorldObjectKey occupied) && !previousKeys.Contains(occupied)))
				throw new InvalidDataException("World-object column snapshot contains an invalid duplicate or coordinate.");
		}
		foreach (PersistentWorldObjectKey key in previousKeys) if (byKey.Remove(key, out WorldPlantRecord record)) byPosition.Remove(record.Position);
		ColumnData column = new(epoch, revision);
		foreach (WorldPlantRecord record in records)
		{
			column.Keys.Add(record.Key); byKey.Add(record.Key, record); byPosition.Add(record.Position, record.Key);
		}
		columns[coordinate] = column;
		ColumnReplaced?.Invoke(Snapshot(coordinate, column));
	}

	public bool TryApplyReplicatedDelta(int x, int z, ulong epoch, long baseRevision, long revision, IReadOnlyList<WorldObjectOperation> operations)
	{
		ChunkColumnCoordinate coordinate = new(x, z);
		if (!columns.TryGetValue(coordinate, out ColumnData column) || column.Epoch != epoch || column.Revision != baseRevision || revision != baseRevision + 1)
			return false;
		try
		{
			Dictionary<PersistentWorldObjectKey, WorldPlantRecord> candidate = column.Keys.ToDictionary(key => key, key => byKey[key]);
			foreach (WorldObjectOperation operation in operations)
			{
				if (operation.Kind == WorldObjectOperationKind.Remove)
				{
					if (!candidate.Remove(operation.Key)) return false;
				}
				else
				{
					Validate(operation.Record);
					if (ColumnOf(operation.Record.Position) != coordinate) return false;
					candidate[operation.Record.Key] = operation.Record;
				}
			}
			if (candidate.Count > MaximumColumnRecords || candidate.Values.Select(static record => record.Position).Distinct().Count() != candidate.Count ||
				candidate.Any(pair => byPosition.TryGetValue(pair.Value.Position, out PersistentWorldObjectKey occupied) && !column.Keys.Contains(occupied))) return false;
			foreach (PersistentWorldObjectKey key in column.Keys) if (byKey.Remove(key, out WorldPlantRecord old)) byPosition.Remove(old.Position);
			column.Keys.Clear();
			foreach ((PersistentWorldObjectKey key, WorldPlantRecord record) in candidate)
			{
				byKey[key]=record; byPosition[record.Position]=key; column.Keys.Add(key);
			}
			column.Revision = revision;
			ColumnChanged?.Invoke(Snapshot(coordinate, column), new WorldObjectDeltaRecord(baseRevision, revision, operations));
			return true;
		}
		catch (ArgumentException) { return false; }
	}

	public void ApplyTransaction(IReadOnlyList<WorldObjectOperation> operations)
	{
		ArgumentNullException.ThrowIfNull(operations);
		if (operations.Count > 1024) throw new ArgumentOutOfRangeException(nameof(operations));
		Dictionary<ChunkColumnCoordinate, List<WorldObjectOperation>> grouped = new();
		HashSet<PersistentWorldObjectKey> keys = new();
		HashSet<BlockCoordinate> positions = new();
		foreach (WorldObjectOperation operation in operations)
		{
			PersistentWorldObjectKey key = operation.Kind == WorldObjectOperationKind.Upsert ? operation.Record.Key : operation.Key;
			if (!keys.Add(key)) throw new ArgumentException("A world-object transaction contains a duplicate key.", nameof(operations));
			if (operation.Kind == WorldObjectOperationKind.Upsert)
			{
				Validate(operation.Record);
				if (byKey.TryGetValue(key, out WorldPlantRecord existing) &&
					ColumnOf(existing.Position) != ColumnOf(operation.Record.Position))
					throw new InvalidOperationException("World objects cannot move between columns in one upsert.");
				if (!positions.Add(operation.Record.Position)) throw new ArgumentException("A world-object transaction contains a duplicate position.", nameof(operations));
				if (byPosition.TryGetValue(operation.Record.Position, out PersistentWorldObjectKey occupied) && occupied != key)
					throw new InvalidOperationException("A world-object position is occupied.");
			}
			if (operation.Kind == WorldObjectOperationKind.Remove && !byKey.ContainsKey(key))
				throw new KeyNotFoundException("A removed world object does not exist.");
			WorldPlantRecord basis = operation.Kind == WorldObjectOperationKind.Upsert ? operation.Record : byKey[key];
			ChunkColumnCoordinate column = ColumnOf(basis.Position);
			if (!grouped.TryGetValue(column, out List<WorldObjectOperation> list)) grouped[column] = list = new();
			list.Add(operation);
		}

		foreach ((ChunkColumnCoordinate coordinate, List<WorldObjectOperation> columnOperations) in grouped)
		{
			ColumnData column = GetOrCreateColumn(coordinate);
			int additions = columnOperations.Count(operation => operation.Kind == WorldObjectOperationKind.Upsert && !byKey.ContainsKey(operation.Record.Key));
			if (column.Keys.Count + additions > MaximumColumnRecords) throw new InvalidOperationException("World-object column record limit exceeded.");
		}

		foreach ((ChunkColumnCoordinate coordinate, List<WorldObjectOperation> columnOperations) in grouped)
		{
			ColumnData column = GetOrCreateColumn(coordinate);
			long baseRevision = column.Revision;
			foreach (WorldObjectOperation operation in columnOperations)
			{
				if (operation.Kind == WorldObjectOperationKind.Remove)
				{
					WorldPlantRecord removed = byKey[operation.Key];
					byKey.Remove(operation.Key); byPosition.Remove(removed.Position); column.Keys.Remove(operation.Key);
				}
				else
				{
					if (byKey.TryGetValue(operation.Record.Key, out WorldPlantRecord previous)) byPosition.Remove(previous.Position);
					byKey[operation.Record.Key] = operation.Record; byPosition[operation.Record.Position] = operation.Record.Key; column.Keys.Add(operation.Record.Key);
				}
			}
			column.Revision = checked(column.Revision + 1);
			WorldObjectDeltaRecord delta = new(baseRevision, column.Revision, columnOperations.ToArray());
			column.History.Enqueue(delta);
			while (column.History.Count > MaximumRetainedDeltaRevisions) column.History.Dequeue();
			ColumnChanged?.Invoke(Snapshot(coordinate, column), delta);
		}
	}

	public void Restore(IEnumerable<WorldPlantRecord> records)
	{
		byKey.Clear(); byPosition.Clear(); columns.Clear(); nextPlacedId = 1; nextEpoch = 1;
		foreach (WorldPlantRecord record in records)
		{
			Validate(record);
			if (!byKey.TryAdd(record.Key, record) || !byPosition.TryAdd(record.Position, record.Key))
				throw new InvalidDataException("World-object archive contains duplicate identity or position.");
			ColumnData column = GetOrCreateColumn(ColumnOf(record.Position));
			if (column.Keys.Count >= MaximumColumnRecords) throw new InvalidDataException("World-object archive exceeds the per-column record limit.");
			column.Keys.Add(record.Key);
			if (record.Key.Kind == PersistentWorldObjectKeyKind.Placed)
				nextPlacedId = Math.Max(nextPlacedId, checked(record.Key.PersistentWorldObjectId.Value + 1));
		}
	}

	private ColumnData GetOrCreateColumn(ChunkColumnCoordinate coordinate)
	{
		if (!columns.TryGetValue(coordinate, out ColumnData column))
			columns[coordinate] = column = new ColumnData(nextEpoch++);
		return column;
	}

	private WorldObjectColumnState Snapshot(ChunkColumnCoordinate coordinate, ColumnData column) => new(
		coordinate.X, coordinate.Z, column.Epoch, column.Revision,
		column.Keys.Select(key => byKey[key]).OrderBy(static record => record.Position.Y).ThenBy(static record => record.Position.X).ThenBy(static record => record.Position.Z).ToArray());

	private static ChunkColumnCoordinate ColumnOf(BlockCoordinate position) => new(
		Voxelgine.Utils.FloorDiv(position.X, Chunk.ChunkSize), Voxelgine.Utils.FloorDiv(position.Z, Chunk.ChunkSize));

	private static void Validate(WorldPlantRecord record)
	{
		if (!Enum.IsDefined(record.PlantType) || record.Health == 0 || record.HarvestItem.IsEmpty)
			throw new ArgumentException("World plant record is invalid.", nameof(record));
	}

	private sealed class ColumnData
	{
		public ColumnData(ulong epoch, long revision = 1) { Epoch = epoch; Revision = revision; }
		public ulong Epoch { get; }
		public long Revision { get; set; }
		public HashSet<PersistentWorldObjectKey> Keys { get; } = new();
		public Queue<WorldObjectDeltaRecord> History { get; } = new();
	}
}
