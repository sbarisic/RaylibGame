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

		internal ChunkSnapshot(int chunkX, int chunkY, int chunkZ, BlockType[] blocks)
		{
			ArgumentNullException.ThrowIfNull(blocks);
			if (blocks.Length != BlockCount)
				throw new ArgumentException($"A chunk snapshot must contain exactly {BlockCount} blocks.", nameof(blocks));

			ChunkX = chunkX;
			ChunkY = chunkY;
			ChunkZ = chunkZ;
			Blocks = new ReadOnlyCollection<BlockType>(blocks);
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
