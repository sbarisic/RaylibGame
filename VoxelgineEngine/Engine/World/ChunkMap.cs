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
		private readonly List<WorldMutation> _worldMutationLog = new();
		private readonly Dictionary<ChunkColumnCoordinate, long> _columnRevisions = new();
		private readonly Dictionary<ChunkColumnCoordinate, List<Vector3>> _columnChunks = new();
		private readonly HashSet<Vector3> _fogChunks = new();
		private int _nonEmptyFogVoxelCount;
		private int _bulkWorldMutationDepth;

		// Preserve the legacy server's lighting-work horizon. The headless process
		// has no camera, so its stable observation origin is the world origin.
		private const float LightingUpdateRadiusBlocks = 52f;
		private static readonly Vector3 LightingObservationOrigin = Vector3.Zero;

		public event Action<BlockChange> BlockChanged;
		public event Action<FogChange> FogChanged;
		public event Action<ChunkColumnSnapshot> ColumnLoaded;
		public event Action<ChunkColumnCoordinate> ColumnCommitted;
		public event Action WorldReset;
		public event Action<ChunkMap, int, int, int, BlockType> OnBlockPlaced;
		public event Action<ChunkMap, int, int, int, BlockType> OnBlockRemoved;

		public ChunkMap()
		{
			Chunks = new SpatialHashGrid<Chunk>();
		}

		public IReadOnlyList<BlockChange> GetPendingChanges() => _blockChangeLog;

		public IReadOnlyList<WorldMutation> GetPendingWorldMutations() =>
			_worldMutationLog;

		public void ClearPendingChanges()
		{
			_blockChangeLog.Clear();
			_worldMutationLog.Clear();
		}

		public Chunk[] GetAllChunks() => Chunks.Values.ToArray();

		public int ColumnCount => _columnRevisions.Count;

		public int NonEmptyFogVoxelCount => _nonEmptyFogVoxelCount;

		public void CaptureFogChunks(
			in FogChunkBounds bounds,
			ICollection<FogChunkSnapshotLease> destination)
		{
			ArgumentNullException.ThrowIfNull(destination);
			foreach (Vector3 coordinate in _fogChunks)
			{
				int chunkX = (int)coordinate.X;
				int chunkY = (int)coordinate.Y;
				int chunkZ = (int)coordinate.Z;
				if (!bounds.Contains(chunkX, chunkY, chunkZ)
					|| !Chunks.TryGetValue(coordinate, out Chunk chunk)
					|| chunk.NonEmptyFogCount == 0)
				{
					continue;
				}

				FogVoxel[] fog = System.Buffers.ArrayPool<FogVoxel>.Shared.Rent(
					ChunkSnapshot.BlockCount
				);
				try
				{
					chunk.CopyFogTo(fog.AsSpan(0, ChunkSnapshot.BlockCount));
					destination.Add(new FogChunkSnapshotLease(
						chunkX,
						chunkY,
						chunkZ,
						fog,
						chunk.NonEmptyFogCount
					));
				}
				catch
				{
					System.Buffers.ArrayPool<FogVoxel>.Shared.Return(fog, clearArray: false);
					throw;
				}
			}
		}

		/// <summary>
		/// Client prediction treats columns not yet streamed as collision boundaries.
		/// Authoritative server maps leave this disabled and treat absent columns as air.
		/// </summary>
		public bool UnknownColumnsAreBoundaries { get; set; }

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
				FogVoxel[] fog = new FogVoxel[ChunkSnapshot.BlockCount];
				for (int blockIndex = 0; blockIndex < blocks.Length; blockIndex++)
					blocks[blockIndex] = entry.Value.Blocks[blockIndex].Type;
				entry.Value.CopyFogTo(fog);

				snapshots[chunkIndex] = new ChunkSnapshot(
					(int)entry.Key.X,
					(int)entry.Key.Y,
					(int)entry.Key.Z,
					blocks,
					fog: fog,
					nonEmptyFogCount: entry.Value.NonEmptyFogCount);
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

		public FogVoxel GetFog(int x, int y, int z)
		{
			TranslateChunkPos(x, y, z, out Vector3 chunkIndex, out Vector3 blockPosition);

			if (!Chunks.TryGetValue(chunkIndex, out Chunk chunk))
			{
				return FogVoxel.Empty;
			}

			return chunk.GetFog(
				(int)blockPosition.X,
				(int)blockPosition.Y,
				(int)blockPosition.Z
			);
		}

		public void SetFog(int x, int y, int z, FogVoxel value)
		{
			TranslateChunkPos(x, y, z, out Vector3 chunkIndex, out Vector3 blockPosition);

			if (!Chunks.TryGetValue(chunkIndex, out Chunk chunk))
			{
				if (value.IsEmpty)
				{
					return;
				}

				chunk = new Chunk(chunkIndex, this);
				Chunks.Add(chunkIndex, chunk);
				ChunkColumnCoordinate newColumn = new(
					(int)chunkIndex.X,
					(int)chunkIndex.Z
				);

				if (!_columnChunks.TryGetValue(newColumn, out List<Vector3> coordinates))
				{
					coordinates = new List<Vector3>();
					_columnChunks.Add(newColumn, coordinates);
				}

				coordinates.Add(chunkIndex);
			}

			int localX = (int)blockPosition.X;
			int localY = (int)blockPosition.Y;
			int localZ = (int)blockPosition.Z;
			FogVoxel oldValue = chunk.GetFog(localX, localY, localZ);

			if (oldValue == value)
			{
				return;
			}

			long revision = IncrementColumnRevision(
				(int)chunkIndex.X,
				(int)chunkIndex.Z
			);
			int previousFogCount = chunk.NonEmptyFogCount;
			chunk.SetFog(localX, localY, localZ, value);
			UpdateFogIndex(chunkIndex, previousFogCount, chunk.NonEmptyFogCount);
			FogChange change = new(x, y, z, oldValue, value, revision);

			if (_bulkWorldMutationDepth == 0)
			{
				_worldMutationLog.Add(WorldMutation.FromFog(change));
				FogChanged?.Invoke(change);
			}
		}

		public int FillFog(
			int x,
			int y,
			int z,
			int sizeX,
			int sizeY,
			int sizeZ,
			FogVoxel value
		)
		{
			if (sizeX < 0)
				throw new ArgumentOutOfRangeException(nameof(sizeX));
			if (sizeY < 0)
				throw new ArgumentOutOfRangeException(nameof(sizeY));
			if (sizeZ < 0)
				throw new ArgumentOutOfRangeException(nameof(sizeZ));

			int changed = 0;

			for (int offsetZ = 0; offsetZ < sizeZ; offsetZ++)
			{
				for (int offsetY = 0; offsetY < sizeY; offsetY++)
				{
					for (int offsetX = 0; offsetX < sizeX; offsetX++)
					{
						int worldX = checked(x + offsetX);
						int worldY = checked(y + offsetY);
						int worldZ = checked(z + offsetZ);

						if (GetFog(worldX, worldY, worldZ) == value)
						{
							continue;
						}

						SetFog(worldX, worldY, worldZ, value);
						changed++;
					}
				}
			}

			return changed;
		}

		public int ClearFog(
			int x,
			int y,
			int z,
			int sizeX,
			int sizeY,
			int sizeZ
		)
		{
			return FillFog(
				x,
				y,
				z,
				sizeX,
				sizeY,
				sizeZ,
				FogVoxel.Empty
			);
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
			{
				Chunks.Add(chunkIndex, new Chunk(chunkIndex, this));
				ChunkColumnCoordinate newColumn = new((int)chunkIndex.X, (int)chunkIndex.Z);
				if (!_columnChunks.TryGetValue(newColumn, out List<Vector3> coordinates))
				{
					coordinates = new List<Vector3>();
					_columnChunks.Add(newColumn, coordinates);
				}
				coordinates.Add(chunkIndex);
			}

			Chunks.TryGetValue(chunkIndex, out Chunk targetChunk);
			BlockType oldType = targetChunk.GetBlock(localX, localY, localZ).Type;
			bool typeChanged = oldType != block.Type;
			BlockChange change = default;
			if (typeChanged)
			{
				long columnRevision = IncrementColumnRevision((int)chunkIndex.X, (int)chunkIndex.Z);
				change = new BlockChange(x, y, z, oldType, block.Type, columnRevision);
				if (_bulkWorldMutationDepth == 0)
				{
					_blockChangeLog.Add(change);
					_worldMutationLog.Add(WorldMutation.FromBlock(change));
				}
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

		public bool TryApplyReplicatedBlockChange(
			int x,
			int y,
			int z,
			BlockType type,
			long expectedColumnRevision)
		{
			TranslateChunkPos(x, y, z, out Vector3 chunkIndex, out Vector3 blockPosition);
			ChunkColumnCoordinate column = new((int)chunkIndex.X, (int)chunkIndex.Z);
			if (!_columnRevisions.TryGetValue(column, out long currentRevision) ||
				!Chunks.TryGetValue(chunkIndex, out Chunk targetChunk))
			{
				return false;
			}

			int localX = (int)blockPosition.X;
			int localY = (int)blockPosition.Y;
			int localZ = (int)blockPosition.Z;
			BlockType oldType = targetChunk.GetBlock(localX, localY, localZ).Type;
			if (expectedColumnRevision == currentRevision && oldType == type)
				return true;
			if (expectedColumnRevision != currentRevision + 1 || oldType == type)
				return false;

			targetChunk.SetBlock(localX, localY, localZ, new PlacedBlock(type));
			targetChunk.MarkDirty();
			_columnRevisions[column] = expectedColumnRevision;
			BlockChange change = new(x, y, z, oldType, type, expectedColumnRevision);
			BlockChanged?.Invoke(change);
			if (oldType != BlockType.None)
				OnBlockRemoved?.Invoke(this, x, y, z, oldType);
			if (type != BlockType.None)
				OnBlockPlaced?.Invoke(this, x, y, z, type);
			return true;
		}

		public bool TryApplyReplicatedFogChange(
			int x,
			int y,
			int z,
			FogVoxel value,
			long expectedColumnRevision
		)
		{
			TranslateChunkPos(x, y, z, out Vector3 chunkIndex, out Vector3 blockPosition);
			ChunkColumnCoordinate column = new((int)chunkIndex.X, (int)chunkIndex.Z);

			if (!_columnRevisions.TryGetValue(column, out long currentRevision))
			{
				return false;
			}

			Chunks.TryGetValue(chunkIndex, out Chunk targetChunk);
			FogVoxel oldValue = targetChunk?.GetFog(
				(int)blockPosition.X,
				(int)blockPosition.Y,
				(int)blockPosition.Z
			) ?? FogVoxel.Empty;

			if (expectedColumnRevision == currentRevision && oldValue == value)
			{
				return true;
			}

			if (expectedColumnRevision != currentRevision + 1 || oldValue == value)
			{
				return false;
			}

			if (targetChunk == null)
			{
				if (value.IsEmpty)
				{
					return false;
				}

				targetChunk = new Chunk(chunkIndex, this);
				Chunks.Add(chunkIndex, targetChunk);
				if (!_columnChunks.TryGetValue(column, out List<Vector3> coordinates))
				{
					coordinates = new List<Vector3>();
					_columnChunks.Add(column, coordinates);
				}
				coordinates.Add(chunkIndex);
			}

			int previousFogCount = targetChunk.NonEmptyFogCount;
			targetChunk.SetFog(
				(int)blockPosition.X,
				(int)blockPosition.Y,
				(int)blockPosition.Z,
				value
			);
			UpdateFogIndex(chunkIndex, previousFogCount, targetChunk.NonEmptyFogCount);
			_columnRevisions[column] = expectedColumnRevision;
			FogChanged?.Invoke(new FogChange(
				x,
				y,
				z,
				oldValue,
				value,
				expectedColumnRevision
			));
			return true;
		}

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
			GetBlock(
				FloorWorldCoordinate(position.X, nameof(position)),
				FloorWorldCoordinate(position.Y, nameof(position)),
				FloorWorldCoordinate(position.Z, nameof(position))
			);

		public Chunk GetChunk(Vector3 chunkIndex)
		{
			Chunks.TryGetValue(chunkIndex, out Chunk chunk);
			return chunk;
		}

		public bool IsWaterAt(Vector3 position) => BlockInfo.IsWater(GetBlock(position));

		public bool IsWaterAt(int x, int y, int z) => BlockInfo.IsWater(GetBlock(x, y, z));

		private static void TransPosScalar(int scalar, out int chunkIndex, out int blockPosition)
		{
			chunkIndex = Utils.FloorDiv(scalar, Chunk.ChunkSize);
			blockPosition = Utils.Mod(scalar, Chunk.ChunkSize);
		}

		private static int FloorWorldCoordinate(float value, string parameterName)
		{
			double floored = Math.Floor(value);
			if (!double.IsFinite(floored) || floored < int.MinValue || floored > int.MaxValue)
				throw new ArgumentOutOfRangeException(parameterName, "World coordinates must be finite 32-bit values.");
			return (int)floored;
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
