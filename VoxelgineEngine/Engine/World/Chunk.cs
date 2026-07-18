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
		private readonly ChunkMap WorldMap;

		public bool NeedsRelighting;
		public AABB ModelAABB;
		public Vector3 GlobalChunkIndex;

		public Chunk(Vector3 globalChunkIndex, ChunkMap worldMap)
		{
			GlobalChunkIndex = globalChunkIndex;
			WorldMap = worldMap;
			Blocks = new PlacedBlock[ChunkSize * ChunkSize * ChunkSize];
			for (int index = 0; index < Blocks.Length; index++)
				Blocks[index] = new PlacedBlock(BlockType.None);

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

		public void To3D(int index, out int x, out int y, out int z)
		{
			z = index / (ChunkSize * ChunkSize);
			index -= z * ChunkSize * ChunkSize;
			y = index / ChunkSize;
			x = index % ChunkSize;
		}
	}
}
