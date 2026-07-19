using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Voxelgine.Engine;

namespace Voxelgine.Graphics
{
	/// <summary>
	/// Immutable, renderer-free copy of one 16 x 16 x 16 chunk.
	/// Block values use the same X-fastest layout as <see cref="Chunk"/>.
	/// </summary>
	public sealed class ChunkSnapshot
	{
		public const int Size = 16;
		public const int BlockCount = Size * Size * Size;

		public int ChunkX { get; }
		public int ChunkY { get; }
		public int ChunkZ { get; }
		public IReadOnlyList<BlockType> Blocks { get; }
		public ReadOnlyMemory<BlockType> BlockMemory { get; }
		public int NonAirBlockCount { get; }
		public IReadOnlyList<FogVoxel> Fog { get; }
		public ReadOnlyMemory<FogVoxel> FogMemory { get; }
		public int NonEmptyFogCount { get; }

		internal ChunkSnapshot(
			int chunkX,
			int chunkY,
			int chunkZ,
			BlockType[] blocks,
			int nonAirBlockCount = -1,
			FogVoxel[] fog = null,
			int nonEmptyFogCount = -1)
		{
			ArgumentNullException.ThrowIfNull(blocks);
			if (blocks.Length != BlockCount)
				throw new ArgumentException($"A chunk snapshot must contain exactly {BlockCount} blocks.", nameof(blocks));
			if (nonAirBlockCount < -1 || nonAirBlockCount > BlockCount)
				throw new ArgumentOutOfRangeException(nameof(nonAirBlockCount));
			fog ??= new FogVoxel[BlockCount];
			if (fog.Length != BlockCount)
				throw new ArgumentException($"A chunk snapshot must contain exactly {BlockCount} fog values.", nameof(fog));
			if (nonEmptyFogCount < -1 || nonEmptyFogCount > BlockCount)
				throw new ArgumentOutOfRangeException(nameof(nonEmptyFogCount));

			ChunkX = chunkX;
			ChunkY = chunkY;
			ChunkZ = chunkZ;
			Blocks = new ReadOnlyCollection<BlockType>(blocks);
			BlockMemory = blocks;
			NonAirBlockCount = nonAirBlockCount >= 0
				? nonAirBlockCount
				: blocks.Count(static block => block != BlockType.None);
			Fog = new ReadOnlyCollection<FogVoxel>(fog);
			FogMemory = fog;
			NonEmptyFogCount = nonEmptyFogCount >= 0
				? nonEmptyFogCount
				: fog.Count(static value => !value.IsEmpty);
		}

		public FogVoxel GetFog(int x, int y, int z)
		{
			if ((uint)x >= Size || (uint)y >= Size || (uint)z >= Size)
				throw new ArgumentOutOfRangeException(nameof(x), "Chunk-local coordinates must be between 0 and 15.");

			return Fog[x + Size * (y + Size * z)];
		}

		/// <summary>Returns the block type at chunk-local coordinates.</summary>
		public BlockType GetBlock(int x, int y, int z)
		{
			if ((uint)x >= Size || (uint)y >= Size || (uint)z >= Size)
				throw new ArgumentOutOfRangeException(nameof(x), "Chunk-local coordinates must be between 0 and 15.");

			return Blocks[x + Size * (y + Size * z)];
		}
	}
}
