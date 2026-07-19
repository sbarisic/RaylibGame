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
		private int _bulkWorldMutationDepth;

		// Preserve the legacy server's lighting-work horizon. The headless process
		// has no camera, so its stable observation origin is the world origin.
		private const float LightingUpdateRadiusBlocks = 52f;
		private static readonly Vector3 LightingObservationOrigin = Vector3.Zero;

		private readonly Random Rnd = new();

		public event Action<BlockChange> BlockChanged;
		public event Action<FogChange> FogChanged;
		public event Action<ChunkColumnSnapshot> ColumnLoaded;
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

		/// <summary>
		/// Client prediction treats columns not yet streamed as collision boundaries.
		/// Authoritative server maps leave this disabled and treat absent columns as air.
		/// </summary>
		public bool UnknownColumnsAreBoundaries { get; set; }

		public bool IsColumnResident(int chunkX, int chunkZ) =>
			_columnRevisions.ContainsKey(new ChunkColumnCoordinate(chunkX, chunkZ));

		public bool TryGetBlock(int x, int y, int z, out BlockType type)
		{
			TranslateChunkPos(x, y, z, out Vector3 chunkIndex, out Vector3 blockPosition);
			if (!Chunks.TryGetValue(chunkIndex, out Chunk chunk))
			{
				type = BlockType.None;
				return false;
			}

			type = chunk.GetBlock((int)blockPosition.X, (int)blockPosition.Y, (int)blockPosition.Z).Type;
			return true;
		}

		public long GetColumnRevision(int chunkX, int chunkZ)
		{
			return _columnRevisions.TryGetValue(
				new ChunkColumnCoordinate(chunkX, chunkZ),
				out long revision)
				? revision
				: 0;
		}

		public ChunkColumnCoordinate[] GetColumnCoordinates()
		{
			ChunkColumnCoordinate[] coordinates = _columnRevisions.Keys.ToArray();
			Array.Sort(coordinates, static (left, right) =>
			{
				int comparison = left.X.CompareTo(right.X);
				return comparison != 0 ? comparison : left.Z.CompareTo(right.Z);
			});
			return coordinates;
		}

		public ChunkColumnSnapshot CaptureColumn(int chunkX, int chunkZ)
		{
			List<ChunkSnapshot> snapshots = new();
			ChunkColumnCoordinate column = new(chunkX, chunkZ);
			if (!_columnChunks.TryGetValue(column, out List<Vector3> coordinates))
				return new ChunkColumnSnapshot(chunkX, chunkZ, GetColumnRevision(chunkX, chunkZ), Array.Empty<ChunkSnapshot>());
			foreach (Vector3 coordinate in coordinates)
			{
				if (!Chunks.TryGetValue(coordinate, out Chunk chunk))
					continue;

				BlockType[] blocks = new BlockType[ChunkSnapshot.BlockCount];
				FogVoxel[] fog = new FogVoxel[ChunkSnapshot.BlockCount];
				for (int index = 0; index < blocks.Length; index++)
					blocks[index] = chunk.Blocks[index].Type;
				chunk.CopyFogTo(fog);
				snapshots.Add(new ChunkSnapshot(
					chunkX,
					(int)coordinate.Y,
					chunkZ,
					blocks,
					fog: fog,
					nonEmptyFogCount: chunk.NonEmptyFogCount
				));
			}

			snapshots.Sort(static (left, right) => left.ChunkY.CompareTo(right.ChunkY));
			return new ChunkColumnSnapshot(
				chunkX,
				chunkZ,
				GetColumnRevision(chunkX, chunkZ),
				snapshots.ToArray());
		}

		public void ApplyColumn(ChunkColumnSnapshot column)
		{
			ArgumentNullException.ThrowIfNull(column);
			ApplyColumnCore(column);
			ColumnLoaded?.Invoke(column);
		}

		public void ReplaceAllColumns(IReadOnlyList<ChunkColumnSnapshot> columns)
		{
			ArgumentNullException.ThrowIfNull(columns);
			ExecuteWorldReset(() =>
			{
				Chunks.Clear();
				_columnRevisions.Clear();
				_columnChunks.Clear();
				foreach (ChunkColumnSnapshot column in columns)
					ApplyColumnCore(column);
			});
		}

		private void ApplyColumnCore(ChunkColumnSnapshot column)
		{
			ChunkColumnCoordinate columnCoordinate = new(column.X, column.Z);
			if (_columnChunks.Remove(columnCoordinate, out List<Vector3> existing))
			{
				foreach (Vector3 coordinate in existing)
					Chunks.Remove(coordinate);
			}

			List<Vector3> inserted = new(column.Chunks.Count);
			foreach (ChunkSnapshot snapshot in column.Chunks)
			{
				if (snapshot.ChunkX != column.X || snapshot.ChunkZ != column.Z)
					throw new InvalidDataException("A column snapshot contains a chunk from another column.");

				Vector3 coordinate = new(snapshot.ChunkX, snapshot.ChunkY, snapshot.ChunkZ);
				Chunk chunk = new(coordinate, this, initializeBlocks: false);
				ReadOnlySpan<BlockType> blocks = snapshot.BlockMemory.Span;
				for (int index = 0; index < blocks.Length; index++)
				{
					BlockType type = blocks[index];
					chunk.Blocks[index] = new PlacedBlock(type);
				}
				chunk.SetNonAirBlockCount(snapshot.NonAirBlockCount);
				chunk.ReplaceFog(snapshot.FogMemory.Span, snapshot.NonEmptyFogCount);
				Chunks.Add(coordinate, chunk);
				inserted.Add(coordinate);
			}

			_columnChunks[columnCoordinate] = inserted;
			_columnRevisions[columnCoordinate] = Math.Max(1, column.Revision);
		}

		internal void InitializeColumnRevisions()
		{
			_columnRevisions.Clear();
			_columnChunks.Clear();
			foreach (KeyValuePair<Vector3, Chunk> item in Chunks.Items)
			{
				ChunkColumnCoordinate column = new((int)item.Key.X, (int)item.Key.Z);
				_columnRevisions.TryAdd(column, 1);
				if (!_columnChunks.TryGetValue(column, out List<Vector3> coordinates))
				{
					coordinates = new List<Vector3>();
					_columnChunks.Add(column, coordinates);
				}
				coordinates.Add(item.Key);
			}
		}

		private long IncrementColumnRevision(int chunkX, int chunkZ)
		{
			ChunkColumnCoordinate coordinate = new(chunkX, chunkZ);
			long revision = _columnRevisions.TryGetValue(coordinate, out long current)
				? checked(current + 1)
				: 1;
			_columnRevisions[coordinate] = revision;
			return revision;
		}

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
			chunk.SetFog(localX, localY, localZ, value);
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
			if (sizeX < 0 || sizeY < 0 || sizeZ < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(sizeX));
			}

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

			targetChunk.SetFog(
				(int)blockPosition.X,
				(int)blockPosition.Y,
				(int)blockPosition.Z,
				value
			);
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
