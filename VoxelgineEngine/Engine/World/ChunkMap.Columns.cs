using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.World.Structures;

namespace Voxelgine.Graphics
{
	public unsafe partial class ChunkMap
	{
		public bool IsColumnResident(int chunkX, int chunkZ) =>
			_columnRevisions.ContainsKey(new ChunkColumnCoordinate(chunkX, chunkZ));

		public bool TryGetBlock(int x, int y, int z, out BlockType type)
		{
			bool found = TryGetBlockValue(x, y, z, out BlockValue value);
			type = value.Type;
			return found;
		}

		public bool TryGetBlockValue(int x, int y, int z, out BlockValue value)
		{
			TranslateChunkPos(x, y, z, out Vector3 chunkIndex, out Vector3 blockPosition);
			if (!Chunks.TryGetValue(chunkIndex, out Chunk chunk))
			{
				value = BlockValue.Empty;
				return false;
			}

			value = chunk.GetBlock((int)blockPosition.X, (int)blockPosition.Y, (int)blockPosition.Z).Value;
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

				BlockValue[] blocks = new BlockValue[ChunkSnapshot.BlockCount];
				FogVoxel[] fog = new FogVoxel[ChunkSnapshot.BlockCount];
				for (int index = 0; index < blocks.Length; index++)
					blocks[index] = chunk.Blocks[index].Value;
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
			ColumnCommitted?.Invoke(new ChunkColumnCoordinate(column.X, column.Z));
		}

		public void CommitPreparedColumn(PreparedChunkColumn column)
		{
			ArgumentNullException.ThrowIfNull(column);
			PreparedChunk[] chunks = column.Consume();
			ApplyPreparedColumnCore(column.X, column.Z, column.Revision, chunks);
			ColumnCommitted?.Invoke(new ChunkColumnCoordinate(column.X, column.Z));
		}

		public void ReplaceAllColumns(IReadOnlyList<ChunkColumnSnapshot> columns)
		{
			ArgumentNullException.ThrowIfNull(columns);
			ExecuteWorldReset(() =>
			{
				Chunks.Clear();
				_columnRevisions.Clear();
				_columnChunks.Clear();
				_fogChunks.Clear();
				_infrastructureBlocks.Clear();
				_columnInfrastructure.Clear();
				_nonEmptyFogVoxelCount = 0;
				foreach (ChunkColumnSnapshot column in columns)
					ApplyColumnCore(column);
			});
		}

		private void ApplyColumnCore(ChunkColumnSnapshot column)
		{
			ChunkColumnCoordinate columnCoordinate = new(column.X, column.Z);
			List<(Vector3 Coordinate, Chunk Chunk)> replacement = new(column.Chunks.Count);
			List<(BlockCoordinate Coordinate, BlockType Block)> infrastructure = new();
			HashSet<Vector3> coordinates = new();
			foreach (ChunkSnapshot snapshot in column.Chunks)
			{
				if (snapshot.ChunkX != column.X || snapshot.ChunkZ != column.Z)
					throw new InvalidDataException("A column snapshot contains a chunk from another column.");

				Vector3 coordinate = new(snapshot.ChunkX, snapshot.ChunkY, snapshot.ChunkZ);
				if (!coordinates.Add(coordinate))
					throw new InvalidDataException($"Column ({column.X}, {column.Z}) contains duplicate chunk Y {snapshot.ChunkY}.");

				Chunk chunk = new(coordinate, this, initializeBlocks: false);
				ReadOnlySpan<BlockValue> blocks = snapshot.ValueMemory.Span;
				for (int index = 0; index < blocks.Length; index++)
				{
					BlockValue value = blocks[index];
					BlockType type = value.Type;
					chunk.Blocks[index] = new PlacedBlock(value);
					if (InfrastructureBlockCatalog.TryGet(type, out _))
					{
						int localX = index % Chunk.ChunkSize;
						int yz = index / Chunk.ChunkSize;
						int localY = yz % Chunk.ChunkSize;
						int localZ = yz / Chunk.ChunkSize;
						infrastructure.Add((new BlockCoordinate(snapshot.ChunkX * Chunk.ChunkSize + localX,
							snapshot.ChunkY * Chunk.ChunkSize + localY, snapshot.ChunkZ * Chunk.ChunkSize + localZ), type));
					}
				}
				chunk.SetNonAirBlockCount(snapshot.NonAirBlockCount);
				chunk.ReplaceFog(snapshot.FogMemory.Span, snapshot.NonEmptyFogCount);
				replacement.Add((coordinate, chunk));
			}

			CommitColumnReplacement(columnCoordinate, Math.Max(1, column.Revision), replacement, infrastructure);
		}

		private void ApplyPreparedColumnCore(
			int columnX,
			int columnZ,
			long revision,
			PreparedChunk[] preparedChunks)
		{
			ChunkColumnCoordinate columnCoordinate = new(columnX, columnZ);
			List<(Vector3 Coordinate, Chunk Chunk)> replacement = new(preparedChunks.Length);
			List<(BlockCoordinate Coordinate, BlockType Block)> infrastructure = new();
			HashSet<Vector3> coordinates = new();
			foreach (PreparedChunk prepared in preparedChunks)
			{
				if (prepared.ChunkX != columnX || prepared.ChunkZ != columnZ)
					throw new InvalidDataException("A prepared column contains a chunk from another column.");

				Vector3 coordinate = new(prepared.ChunkX, prepared.ChunkY, prepared.ChunkZ);
				if (!coordinates.Add(coordinate))
					throw new InvalidDataException($"Column ({columnX}, {columnZ}) contains duplicate chunk Y {prepared.ChunkY}.");
				ValidatePreparedChunk(prepared);

				Chunk chunk = new(coordinate, this, initializeBlocks: false);
				chunk.AdoptPreparedStorage(
					prepared.Blocks,
					prepared.NonAirBlockCount,
					prepared.Fog,
					prepared.NonEmptyFogCount
				);
				replacement.Add((coordinate, chunk));
				for (int index = 0; index < prepared.Blocks.Length; index++)
				{
					BlockType type = prepared.Blocks[index].Type;
					if (!InfrastructureBlockCatalog.TryGet(type, out _)) continue;
					int localX = index % Chunk.ChunkSize;
					int yz = index / Chunk.ChunkSize;
					infrastructure.Add((new BlockCoordinate(columnX * Chunk.ChunkSize + localX,
						prepared.ChunkY * Chunk.ChunkSize + yz % Chunk.ChunkSize,
						columnZ * Chunk.ChunkSize + yz / Chunk.ChunkSize), type));
				}
			}

			CommitColumnReplacement(columnCoordinate, Math.Max(1, revision), replacement, infrastructure);
		}

		private static void ValidatePreparedChunk(PreparedChunk prepared)
		{
			if (prepared.Blocks.Length != ChunkSnapshot.BlockCount)
				throw new InvalidDataException("Prepared block storage must contain one complete chunk.");
			if (prepared.NonAirBlockCount is < 0 or > ChunkSnapshot.BlockCount)
				throw new InvalidDataException("Prepared non-air count is outside the chunk range.");
			if (prepared.Fog != null && prepared.Fog.Length != ChunkSnapshot.BlockCount)
				throw new InvalidDataException("Prepared fog storage must contain one complete chunk.");
			if (prepared.NonEmptyFogCount is < 0 or > ChunkSnapshot.BlockCount)
				throw new InvalidDataException("Prepared fog count is outside the chunk range.");
			if (prepared.NonEmptyFogCount > 0 && prepared.Fog == null)
				throw new InvalidDataException("Prepared non-empty fog storage is missing.");
		}

		private void CommitColumnReplacement(
			ChunkColumnCoordinate columnCoordinate,
			long revision,
			List<(Vector3 Coordinate, Chunk Chunk)> replacement,
			List<(BlockCoordinate Coordinate, BlockType Block)> infrastructure)
		{
			ColumnReplacing?.Invoke(columnCoordinate);
			RemoveColumnStorage(columnCoordinate);
			List<Vector3> inserted = new(replacement.Count);
			foreach ((Vector3 coordinate, Chunk chunk) in replacement)
			{
				Chunks.Add(coordinate, chunk);
				TrackFogChunk(coordinate, chunk);
				inserted.Add(coordinate);
			}

			_columnChunks[columnCoordinate] = inserted;
			_columnRevisions[columnCoordinate] = revision;
			foreach ((BlockCoordinate coordinate, BlockType block) in infrastructure)
				TrackInfrastructureBlock(coordinate, block);
		}

		private void RemoveColumnStorage(ChunkColumnCoordinate columnCoordinate)
		{
			if (_columnInfrastructure.Remove(columnCoordinate, out HashSet<BlockCoordinate> infrastructure))
				foreach (BlockCoordinate coordinate in infrastructure) _infrastructureBlocks.Remove(coordinate);
			if (!_columnChunks.Remove(columnCoordinate, out List<Vector3> existing))
				return;

			foreach (Vector3 coordinate in existing)
			{
				if (Chunks.TryGetValue(coordinate, out Chunk removed))
				{
					_nonEmptyFogVoxelCount -= removed.NonEmptyFogCount;
					_fogChunks.Remove(coordinate);
				}
				Chunks.Remove(coordinate);
			}
		}

		internal void InitializeColumnRevisions()
		{
			_columnRevisions.Clear();
			_columnChunks.Clear();
			_fogChunks.Clear();
			_nonEmptyFogVoxelCount = 0;
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
				TrackFogChunk(item.Key, item.Value);
			}
		}

		private void TrackFogChunk(Vector3 coordinate, Chunk chunk)
		{
			int count = chunk.NonEmptyFogCount;
			if (count <= 0)
			{
				_fogChunks.Remove(coordinate);
				return;
			}

			if (_fogChunks.Add(coordinate))
				_nonEmptyFogVoxelCount = checked(_nonEmptyFogVoxelCount + count);
		}

		private void UpdateFogIndex(
			Vector3 coordinate,
			int previousCount,
			int currentCount)
		{
			_nonEmptyFogVoxelCount = checked(
				_nonEmptyFogVoxelCount + currentCount - previousCount
			);
			if (currentCount == 0)
				_fogChunks.Remove(coordinate);
			else
				_fogChunks.Add(coordinate);
		}

		private long IncrementColumnRevision(int chunkX, int chunkZ)
		{
			ChunkColumnCoordinate coordinate = new(chunkX, chunkZ);
			if (_activeBlockBatchRevisions != null &&
				_activeBlockBatchRevisions.TryGetValue(coordinate, out long batchRevision))
			{
				return batchRevision;
			}
			long revision = _columnRevisions.TryGetValue(coordinate, out long current)
				? checked(current + 1)
				: 1;
			_columnRevisions[coordinate] = revision;
			_activeBlockBatchRevisions?.Add(coordinate, revision);
			return revision;
		}

	}
}
