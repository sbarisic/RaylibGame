using System.Numerics;
using Voxelgine.Engine;

namespace Voxelgine.Graphics
{
	/// <summary>
	/// Authoritative voxel storage and mutation API. Rendering maintains its own
	/// client-side mirror and is not represented here.
	/// </summary>
	public unsafe partial class ChunkMap
	{
		private readonly SpatialHashGrid<Chunk> Chunks;
		private readonly List<BlockChange> _blockChangeLog = new();
		private int _bulkWorldMutationDepth;

		// Preserve the legacy server's lighting-work horizon. The headless process
		// has no camera, so its stable observation origin is the world origin.
		private const float LightingUpdateRadiusBlocks = 52f;
		private static readonly Vector3 LightingObservationOrigin = Vector3.Zero;

		private readonly Random Rnd = new();

		public event Action<BlockChange> BlockChanged;
		public event Action WorldReset;
		public event Action<ChunkMap, int, int, int, BlockType> OnBlockPlaced;
		public event Action<ChunkMap, int, int, int, BlockType> OnBlockRemoved;

		public ChunkMap()
		{
			Chunks = new SpatialHashGrid<Chunk>();
		}

		public IReadOnlyList<BlockChange> GetPendingChanges() => _blockChangeLog;

		public void ClearPendingChanges() => _blockChangeLog.Clear();

		public Chunk[] GetAllChunks() => Chunks.Values.ToArray();

		public IReadOnlyList<ChunkSnapshot> CaptureChunks()
		{
			KeyValuePair<Vector3, Chunk>[] chunks = Chunks.Items.ToArray();
			Array.Sort(chunks, static (left, right) =>
			{
				int comparison = left.Key.X.CompareTo(right.Key.X);
				if (comparison != 0)
					return comparison;

				comparison = left.Key.Y.CompareTo(right.Key.Y);
				return comparison != 0
					? comparison
					: left.Key.Z.CompareTo(right.Key.Z);
			});

			ChunkSnapshot[] snapshots = new ChunkSnapshot[chunks.Length];
			for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
			{
				KeyValuePair<Vector3, Chunk> entry = chunks[chunkIndex];
				BlockType[] blocks = new BlockType[ChunkSnapshot.BlockCount];
				for (int blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
					blocks[blockIndex] = entry.Value.Blocks[blockIndex].Type;

				snapshots[chunkIndex] = new ChunkSnapshot(
					(int)entry.Key.X,
					(int)entry.Key.Y,
					(int)entry.Key.Z,
					blocks);
			}

			return snapshots;
		}

		private void ExecuteWorldReset(Action mutation)
		{
			_bulkWorldMutationDepth++;
			try
			{
				mutation();
			}
			finally
			{
				_bulkWorldMutationDepth--;
				if (_bulkWorldMutationDepth == 0)
					WorldReset?.Invoke();
			}
		}

		public void MarkAllChunksDirty()
		{
			foreach (Chunk chunk in Chunks.Values)
				chunk.MarkDirty();
		}

		public void Tick()
		{
		}

		public void SetPlacedBlock(int x, int y, int z, PlacedBlock block)
		{
			TranslateChunkPos(x, y, z, out Vector3 chunkIndex, out Vector3 blockPosition);

			int localX = (int)blockPosition.X;
			int localY = (int)blockPosition.Y;
			int localZ = (int)blockPosition.Z;
			const int maximumBlock = Chunk.ChunkSize - 1;

			Span<Vector3> affectedChunks = stackalloc Vector3[8];
			int affectedCount = 0;

			int minimumX = localX == 0 ? -1 : 0;
			int maximumX = localX == maximumBlock ? 1 : 0;
			int minimumY = localY == 0 ? -1 : 0;
			int maximumY = localY == maximumBlock ? 1 : 0;
			int minimumZ = localZ == 0 ? -1 : 0;
			int maximumZ = localZ == maximumBlock ? 1 : 0;

			for (int xOffset = minimumX; xOffset <= maximumX; xOffset++)
			{
				for (int yOffset = minimumY; yOffset <= maximumY; yOffset++)
				{
					for (int zOffset = minimumZ; zOffset <= maximumZ; zOffset++)
					{
						Vector3 affectedPosition = chunkIndex + new Vector3(xOffset, yOffset, zOffset);
						bool found = false;
						for (int index = 0; index < affectedCount; index++)
						{
							if (affectedChunks[index] == affectedPosition)
							{
								found = true;
								break;
							}
						}

						if (!found)
							affectedChunks[affectedCount++] = affectedPosition;
					}
				}
			}

			for (int index = 0; index < affectedCount; index++)
			{
				if (Chunks.TryGetValue(affectedChunks[index], out Chunk affectedChunk))
					affectedChunk.MarkDirty();
			}

			if (!Chunks.ContainsKey(chunkIndex))
				Chunks.Add(chunkIndex, new Chunk(chunkIndex, this));

			Chunks.TryGetValue(chunkIndex, out Chunk targetChunk);
			BlockType oldType = targetChunk.GetBlock(localX, localY, localZ).Type;
			bool typeChanged = oldType != block.Type;
			BlockChange change = default;
			if (typeChanged)
			{
				change = new BlockChange(x, y, z, oldType, block.Type);
				if (_bulkWorldMutationDepth == 0)
					_blockChangeLog.Add(change);
			}

			targetChunk.SetBlock(localX, localY, localZ, block);

			if (typeChanged && _bulkWorldMutationDepth == 0)
			{
				BlockChanged?.Invoke(change);
				if (oldType != BlockType.None)
					OnBlockRemoved?.Invoke(this, x, y, z, oldType);
				if (block.Type != BlockType.None)
					OnBlockPlaced?.Invoke(this, x, y, z, block.Type);
			}

			bool needsLightingUpdate = BlockInfo.EmitsLight(block.Type) ||
				!BlockInfo.IsRendered(block.Type) ||
				BlockInfo.IsOpaque(block.Type);
			if (!needsLightingUpdate)
				return;

			const int lightRangeInChunks = 1;
			float halfChunk = Chunk.ChunkSize * 0.5f;
			float lightingDistanceSquared = LightingUpdateRadiusBlocks * LightingUpdateRadiusBlocks;
			List<Chunk> chunksToUpdate = new();

			for (int chunkX = -lightRangeInChunks; chunkX <= lightRangeInChunks; chunkX++)
			{
				for (int chunkY = -lightRangeInChunks; chunkY <= lightRangeInChunks; chunkY++)
				{
					for (int chunkZ = -lightRangeInChunks; chunkZ <= lightRangeInChunks; chunkZ++)
					{
						Vector3 neighborIndex = chunkIndex + new Vector3(chunkX, chunkY, chunkZ);
						if (!Chunks.TryGetValue(neighborIndex, out Chunk neighbor))
							continue;

						Vector3 chunkCenter = neighborIndex * Chunk.ChunkSize + new Vector3(halfChunk);
						if (Vector3.DistanceSquared(LightingObservationOrigin, chunkCenter) <= lightingDistanceSquared)
						{
							chunksToUpdate.Add(neighbor);
						}
						else
						{
							neighbor.NeedsRelighting = true;
							neighbor.MarkDirty();
						}
					}
				}
			}

			if (chunksToUpdate.Count == 0)
				return;

			foreach (Chunk chunk in chunksToUpdate)
				chunk.ResetLighting();

			ComputeLightingParallel(chunksToUpdate.ToArray());

			foreach (Chunk chunk in chunksToUpdate)
				chunk.MarkDirty();
		}

		public void SetPlacedBlockNoLighting(int x, int y, int z, PlacedBlock block)
		{
			TranslateChunkPos(x, y, z, out Vector3 chunkIndex, out Vector3 blockPosition);
			if (!Chunks.TryGetValue(chunkIndex, out Chunk targetChunk))
				return;

			targetChunk.SetBlock(
				(int)blockPosition.X,
				(int)blockPosition.Y,
				(int)blockPosition.Z,
				block);
		}

		public void SetBlock(int x, int y, int z, BlockType type) =>
			SetPlacedBlock(x, y, z, new PlacedBlock(type));

		public PlacedBlock GetPlacedBlock(int x, int y, int z, out Chunk chunk)
		{
			TranslateChunkPos(x, y, z, out Vector3 chunkIndex, out Vector3 blockPosition);
			if (Chunks.TryGetValue(chunkIndex, out chunk))
				return chunk.GetBlock((int)blockPosition.X, (int)blockPosition.Y, (int)blockPosition.Z);

			chunk = null;
			return new PlacedBlock(BlockType.None);
		}

		public BlockType GetBlock(int x, int y, int z) =>
			GetPlacedBlock(x, y, z, out _).Type;

		public BlockType GetBlock(Vector3 position) =>
			GetBlock((int)position.X, (int)position.Y, (int)position.Z);

		public Chunk GetChunk(Vector3 chunkIndex)
		{
			Chunks.TryGetValue(chunkIndex, out Chunk chunk);
			return chunk;
		}

		public bool IsWaterAt(Vector3 position) => BlockInfo.IsWater(GetBlock(position));

		public bool IsWaterAt(int x, int y, int z) => BlockInfo.IsWater(GetBlock(x, y, z));

		private static void TransPosScalar(int scalar, out int chunkIndex, out int blockPosition)
		{
			chunkIndex = (int)Math.Floor((float)scalar / Chunk.ChunkSize);
			blockPosition = Utils.Mod(scalar, Chunk.ChunkSize);
		}

		private static void TranslateChunkPos(
			int x,
			int y,
			int z,
			out Vector3 chunkIndex,
			out Vector3 blockPosition)
		{
			TransPosScalar(x, out int chunkX, out int blockX);
			TransPosScalar(y, out int chunkY, out int blockY);
			TransPosScalar(z, out int chunkZ, out int blockZ);
			chunkIndex = new Vector3(chunkX, chunkY, chunkZ);
			blockPosition = new Vector3(blockX, blockY, blockZ);
		}

		public void GetWorldPos(
			int x,
			int y,
			int z,
			Vector3 chunkIndex,
			out Vector3 globalPosition)
		{
			globalPosition = chunkIndex * Chunk.ChunkSize + new Vector3(x, y, z);
		}
	}
}
