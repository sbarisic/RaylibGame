using System.Numerics;
using System.Threading;
using Voxelgine.Engine;

namespace Voxelgine.Graphics
{
	/// <summary>
	/// Authoritative 16-cubed block container. Rendering caches and GPU ownership
	/// live exclusively in the client implementation.
	/// </summary>
	public unsafe partial class Chunk
	{
		public const int ChunkSize = 16;
		public const float BlockSize = 1f;
		public const int AtlasSize = 16;

		public PlacedBlock[] Blocks;
		internal bool Dirty;
		private int NonAirBlockCount;
		private FogVoxel[] fog;
		private int nonEmptyFogCount;
		private readonly ChunkMap WorldMap;

		public bool NeedsRelighting;
		public AABB ModelAABB;
		public Vector3 GlobalChunkIndex;

		public Chunk(Vector3 globalChunkIndex, ChunkMap worldMap, bool initializeBlocks = true)
		{
			GlobalChunkIndex = globalChunkIndex;
			WorldMap = worldMap;
			Blocks = new PlacedBlock[ChunkSize * ChunkSize * ChunkSize];
			if (initializeBlocks)
			{
				for (int index = 0; index < Blocks.Length; index++)
					Blocks[index] = new PlacedBlock(BlockType.None);
			}

			Vector3 worldPosition = globalChunkIndex * ChunkSize;
			ModelAABB = new AABB(worldPosition, new Vector3(ChunkSize));
			Dirty = true;
		}

		public PlacedBlock GetBlock(int x, int y, int z)
		{
			if (x < 0 || x >= ChunkSize ||
				y < 0 || y >= ChunkSize ||
				z < 0 || z >= ChunkSize)
			{
				WorldMap.GetWorldPos(0, 0, 0, GlobalChunkIndex, out Vector3 origin);
				return WorldMap.GetPlacedBlock(
					(int)origin.X + x,
					(int)origin.Y + y,
					(int)origin.Z + z,
					out _);
			}

			return Blocks[x + ChunkSize * (y + ChunkSize * z)];
		}

		public PlacedBlock GetBlock(Vector3 position) =>
			GetBlock((int)position.X, (int)position.Y, (int)position.Z);

		public void SetBlock(int x, int y, int z, PlacedBlock block)
		{
			int index = x + ChunkSize * (y + ChunkSize * z);
			BlockType oldType = Blocks[index].Type;
			Blocks[index] = block;

			if (oldType == BlockType.None && block.Type != BlockType.None)
				Interlocked.Increment(ref NonAirBlockCount);
			else if (oldType != BlockType.None && block.Type == BlockType.None)
				Interlocked.Decrement(ref NonAirBlockCount);

			Dirty = true;
			SkyExposureCacheValid = false;
		}

		public void Fill(PlacedBlock block)
		{
			for (int index = 0; index < Blocks.Length; index++)
				Blocks[index] = block;

			NonAirBlockCount = block.Type == BlockType.None ? 0 : Blocks.Length;
			Dirty = true;
			SkyExposureCacheValid = false;
		}

		public void Fill(BlockType type) => Fill(new PlacedBlock(type));

		public int NonEmptyFogCount => nonEmptyFogCount;

		public FogVoxel GetFog(int x, int y, int z)
		{
			if (x < 0 || x >= ChunkSize
				|| y < 0 || y >= ChunkSize
				|| z < 0 || z >= ChunkSize)
			{
				WorldMap.GetWorldPos(0, 0, 0, GlobalChunkIndex, out Vector3 origin);
				return WorldMap.GetFog(
					(int)origin.X + x,
					(int)origin.Y + y,
					(int)origin.Z + z
				);
			}

			return fog?[x + ChunkSize * (y + ChunkSize * z)] ?? FogVoxel.Empty;
		}

		internal FogVoxel SetFog(int x, int y, int z, FogVoxel value)
		{
			int index = x + ChunkSize * (y + ChunkSize * z);
			FogVoxel oldValue = fog?[index] ?? FogVoxel.Empty;

			if (oldValue == value)
			{
				return oldValue;
			}

			if (!value.IsEmpty)
			{
				fog ??= new FogVoxel[ChunkSize * ChunkSize * ChunkSize];
				fog[index] = value;
			}
			else if (fog != null)
			{
				fog[index] = FogVoxel.Empty;
			}

			if (oldValue.IsEmpty && !value.IsEmpty)
			{
				nonEmptyFogCount++;
			}
			else if (!oldValue.IsEmpty && value.IsEmpty)
			{
				nonEmptyFogCount--;

				if (nonEmptyFogCount == 0)
				{
					fog = null;
				}
			}

			return oldValue;
		}

		internal void CopyFogTo(Span<FogVoxel> destination)
		{
			if (destination.Length != ChunkSize * ChunkSize * ChunkSize)
			{
				throw new ArgumentException(
					"Fog copies require one complete chunk.",
					nameof(destination)
				);
			}

			if (fog == null)
			{
				destination.Clear();
			}
			else
			{
				fog.CopyTo(destination);
			}
		}

		internal void ReplaceFog(ReadOnlySpan<FogVoxel> values, int nonEmptyCount)
		{
			if (values.Length != ChunkSize * ChunkSize * ChunkSize)
			{
				throw new ArgumentException(
					"Fog replacement requires one complete chunk.",
					nameof(values)
				);
			}

			if (nonEmptyCount < 0 || nonEmptyCount > values.Length)
			{
				throw new ArgumentOutOfRangeException(nameof(nonEmptyCount));
			}

			nonEmptyFogCount = nonEmptyCount;
			fog = nonEmptyCount == 0 ? null : values.ToArray();
		}

		internal void AdoptPreparedStorage(
			PlacedBlock[] preparedBlocks,
			int preparedNonAirCount,
			FogVoxel[] preparedFog,
			int preparedNonEmptyFogCount)
		{
			ArgumentNullException.ThrowIfNull(preparedBlocks);
			if (preparedBlocks.Length != ChunkSize * ChunkSize * ChunkSize)
				throw new ArgumentException("Prepared block storage must contain one complete chunk.", nameof(preparedBlocks));
			if (preparedFog != null && preparedFog.Length != preparedBlocks.Length)
				throw new ArgumentException("Prepared fog storage must contain one complete chunk.", nameof(preparedFog));

			Blocks = preparedBlocks;
			NonAirBlockCount = Math.Clamp(preparedNonAirCount, 0, preparedBlocks.Length);
			fog = preparedNonEmptyFogCount == 0 ? null : preparedFog;
			nonEmptyFogCount = Math.Clamp(preparedNonEmptyFogCount, 0, preparedBlocks.Length);
			Dirty = true;
			SkyExposureCacheValid = false;
		}

		public void MarkDirty() => Dirty = true;

		public void RecomputeNonAirBlockCount()
		{
			int count = 0;
			for (int index = 0; index < Blocks.Length; index++)
			{
				if (Blocks[index].Type != BlockType.None)
					count++;
			}

			NonAirBlockCount = count;
		}

		internal void SetNonAirBlockCount(int count)
		{
			NonAirBlockCount = Math.Clamp(count, 0, Blocks.Length);
		}

		public void To3D(int index, out int x, out int y, out int z)
		{
			z = index / (ChunkSize * ChunkSize);
			index -= z * ChunkSize * ChunkSize;
			y = index / ChunkSize;
			x = index % ChunkSize;
		}
	}
}
