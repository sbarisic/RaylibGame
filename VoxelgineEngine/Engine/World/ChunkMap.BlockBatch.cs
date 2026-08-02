using System.Numerics;
using Voxelgine.Engine.World.Structures;

namespace Voxelgine.Graphics;

public readonly record struct BlockMutationRequest(int X, int Y, int Z, BlockValue Value)
{
	public BlockMutationRequest(int x, int y, int z, Voxelgine.Engine.BlockType type)
		: this(x, y, z, new BlockValue(type)) { }
}

public unsafe partial class ChunkMap
{
	public const int MaximumBlockBatchChanges = 4096;

	public IReadOnlyList<BlockChange> ApplyBlockBatch(IReadOnlyList<BlockMutationRequest> requests)
	{
		ArgumentNullException.ThrowIfNull(requests);
		if (requests.Count > MaximumBlockBatchChanges)
			throw new ArgumentOutOfRangeException(nameof(requests), requests.Count, "A block batch may contain at most 4,096 changes.");
		if (requests.Count == 0)
			return Array.Empty<BlockChange>();
		if (_activeBlockBatchRevisions != null)
			throw new InvalidOperationException("Nested authoritative block batches are not supported.");

		HashSet<(int X, int Y, int Z)> positions = new();
		HashSet<ChunkColumnCoordinate> columns = new();
		List<BlockMutationRequest> changed = new(requests.Count);
		foreach (BlockMutationRequest request in requests)
		{
			BlockStateCatalog.Validate(request.Value.Type, request.Value.State);
			if (!positions.Add((request.X, request.Y, request.Z)))
				throw new ArgumentException($"A block batch contains duplicate position {request.X},{request.Y},{request.Z}.", nameof(requests));
			if (GetBlockValue(request.X, request.Y, request.Z) != request.Value)
			{
				changed.Add(request);
				columns.Add(new ChunkColumnCoordinate(
					Voxelgine.Utils.FloorDiv(request.X, Chunk.ChunkSize),
					Voxelgine.Utils.FloorDiv(request.Z, Chunk.ChunkSize)));
			}
		}

		if (changed.Count == 0)
			return Array.Empty<BlockChange>();
		foreach (ChunkColumnCoordinate column in columns)
			if (_columnRevisions.TryGetValue(column, out long revision) && revision == long.MaxValue)
				throw new OverflowException($"Column ({column.X}, {column.Z}) revision is exhausted.");

		_activeBlockBatchRevisions = new Dictionary<ChunkColumnCoordinate, long>();
		_deferredBlockBatchChanges = new List<BlockChange>(changed.Count);
		_activeBlockBatchDirtyChunks = new HashSet<Vector3>();
		_activeBlockBatchLightingOrigins = new HashSet<Vector3>();
		try
		{
			foreach (BlockMutationRequest request in changed)
				SetBlock(request.X, request.Y, request.Z, request.Value);

			BlockChange[] committed = _deferredBlockBatchChanges.ToArray();
			FinalizeBlockBatchWork();
			_activeBlockBatchRevisions = null;
			_deferredBlockBatchChanges = null;
			_activeBlockBatchDirtyChunks = null;
			_activeBlockBatchLightingOrigins = null;
			foreach (BlockChange change in committed)
			{
				_blockChangeLog.Add(change);
				_worldMutationLog.Add(WorldMutation.FromBlock(change));
			}

			BlockBatchChanged?.Invoke(committed);
			foreach (BlockChange change in committed)
			{
				BlockChanged?.Invoke(change);
				if (change.OldType != change.NewType && change.OldType != Voxelgine.Engine.BlockType.None)
					OnBlockRemoved?.Invoke(this, change.X, change.Y, change.Z, change.OldType);
				if (change.OldType != change.NewType && change.NewType != Voxelgine.Engine.BlockType.None)
					OnBlockPlaced?.Invoke(this, change.X, change.Y, change.Z, change.NewType);
			}
			return committed;
		}
		finally
		{
			_activeBlockBatchRevisions = null;
			_deferredBlockBatchChanges = null;
			_activeBlockBatchDirtyChunks = null;
			_activeBlockBatchLightingOrigins = null;
		}
	}

	private void FinalizeBlockBatchWork()
	{
		foreach (Vector3 coordinate in _activeBlockBatchDirtyChunks)
			if (Chunks.TryGetValue(coordinate, out Chunk dirty)) dirty.MarkDirty();
		if (_activeBlockBatchLightingOrigins.Count == 0)
			return;

		const int lightRangeInChunks = 1;
		float halfChunk = Chunk.ChunkSize * 0.5f;
		float lightingDistanceSquared = LightingUpdateRadiusBlocks * LightingUpdateRadiusBlocks;
		HashSet<Chunk> chunksToUpdate = new();
		foreach (Vector3 origin in _activeBlockBatchLightingOrigins)
		{
			for (int chunkX = -lightRangeInChunks; chunkX <= lightRangeInChunks; chunkX++)
			for (int chunkY = -lightRangeInChunks; chunkY <= lightRangeInChunks; chunkY++)
			for (int chunkZ = -lightRangeInChunks; chunkZ <= lightRangeInChunks; chunkZ++)
			{
				Vector3 neighborIndex = origin + new Vector3(chunkX, chunkY, chunkZ);
				if (!Chunks.TryGetValue(neighborIndex, out Chunk neighbor))
					continue;
				Vector3 chunkCenter = neighborIndex * Chunk.ChunkSize + new Vector3(halfChunk);
				if (Vector3.DistanceSquared(LightingObservationOrigin, chunkCenter) <= lightingDistanceSquared)
					chunksToUpdate.Add(neighbor);
				else
				{
					neighbor.NeedsRelighting = true;
					neighbor.MarkDirty();
				}
			}
		}
		foreach (Chunk chunk in chunksToUpdate) chunk.ResetLighting();
		if (chunksToUpdate.Count != 0) ComputeLightingParallel(chunksToUpdate.ToArray());
		foreach (Chunk chunk in chunksToUpdate) chunk.MarkDirty();
	}

	public bool TryApplyReplicatedBlockBatch(
		int columnX,
		int columnZ,
		long expectedColumnRevision,
		IReadOnlyList<BlockMutationRequest> requests)
	{
		ArgumentNullException.ThrowIfNull(requests);
		if (requests.Count is < 1 or > MaximumBlockBatchChanges)
			return false;
		ChunkColumnCoordinate column = new(columnX, columnZ);
		if (!_columnRevisions.TryGetValue(column, out long currentRevision))
			return false;

		HashSet<(int X, int Y, int Z)> positions = new();
		List<(Chunk Chunk, int LocalX, int LocalY, int LocalZ, BlockMutationRequest Request, BlockValue OldValue)> pending = new(requests.Count);
		foreach (BlockMutationRequest request in requests)
		{
			if (!BlockStateCatalog.IsValid(request.Value.Type, request.Value.State) ||
				!positions.Add((request.X, request.Y, request.Z)) ||
				Voxelgine.Utils.FloorDiv(request.X, Chunk.ChunkSize) != columnX ||
				Voxelgine.Utils.FloorDiv(request.Z, Chunk.ChunkSize) != columnZ)
				return false;
			TranslateChunkPos(request.X, request.Y, request.Z, out Vector3 chunkIndex, out Vector3 blockPosition);
			if (!Chunks.TryGetValue(chunkIndex, out Chunk chunk))
				return false;
			int localX = (int)blockPosition.X;
			int localY = (int)blockPosition.Y;
			int localZ = (int)blockPosition.Z;
			pending.Add((chunk, localX, localY, localZ, request, chunk.GetBlock(localX, localY, localZ).Value));
		}

		if (expectedColumnRevision == currentRevision)
			return pending.All(static item => item.OldValue == item.Request.Value);
		if (expectedColumnRevision != checked(currentRevision + 1) ||
			pending.All(static item => item.OldValue == item.Request.Value))
			return false;

		List<BlockChange> committed = new(pending.Count);
		foreach (var item in pending)
		{
			if (item.OldValue == item.Request.Value)
				continue;
			item.Chunk.SetBlock(item.LocalX, item.LocalY, item.LocalZ, new PlacedBlock(item.Request.Value));
			MarkReplicatedMutationDirty(item.Request.X, item.Request.Y, item.Request.Z);
			if (item.OldValue.Type != item.Request.Value.Type)
				TrackInfrastructureBlock(new BlockCoordinate(item.Request.X, item.Request.Y, item.Request.Z), item.Request.Value.Type);
			committed.Add(new BlockChange(
				item.Request.X, item.Request.Y, item.Request.Z,
				item.OldValue, item.Request.Value, expectedColumnRevision));
		}
		_columnRevisions[column] = expectedColumnRevision;
		BlockBatchChanged?.Invoke(committed);
		foreach (BlockChange change in committed)
		{
			BlockChanged?.Invoke(change);
			if (change.OldType != change.NewType && change.OldType != Voxelgine.Engine.BlockType.None)
				OnBlockRemoved?.Invoke(this, change.X, change.Y, change.Z, change.OldType);
			if (change.OldType != change.NewType && change.NewType != Voxelgine.Engine.BlockType.None)
				OnBlockPlaced?.Invoke(this, change.X, change.Y, change.Z, change.NewType);
		}
		return true;
	}

	private void MarkReplicatedMutationDirty(int x, int y, int z)
	{
		TranslateChunkPos(x, y, z, out Vector3 chunkIndex, out Vector3 blockPosition);
		int minimumX = blockPosition.X == 0 ? -1 : 0;
		int maximumX = blockPosition.X == Chunk.ChunkSize - 1 ? 1 : 0;
		int minimumY = blockPosition.Y == 0 ? -1 : 0;
		int maximumY = blockPosition.Y == Chunk.ChunkSize - 1 ? 1 : 0;
		int minimumZ = blockPosition.Z == 0 ? -1 : 0;
		int maximumZ = blockPosition.Z == Chunk.ChunkSize - 1 ? 1 : 0;
		for (int offsetX = minimumX; offsetX <= maximumX; offsetX++)
		for (int offsetY = minimumY; offsetY <= maximumY; offsetY++)
		for (int offsetZ = minimumZ; offsetZ <= maximumZ; offsetZ++)
			if (Chunks.TryGetValue(chunkIndex + new Vector3(offsetX, offsetY, offsetZ), out Chunk chunk))
				chunk.MarkDirty();
	}
}
