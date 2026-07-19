using System;
using System.IO;
using System.IO.Compression;
using Voxelgine.Engine;

namespace Voxelgine.Graphics;

/// <summary>Shared archive/network codec for a complete horizontal voxel column.</summary>
public static class WorldColumnCodec
{
	public static byte[] Encode(ChunkColumnSnapshot column)
	{
		ArgumentNullException.ThrowIfNull(column);
		using MemoryStream uncompressed = new();
		using (BinaryWriter writer = new(uncompressed, System.Text.Encoding.UTF8, leaveOpen: true))
		{
			writer.Write(column.Chunks.Count);
			foreach (ChunkSnapshot chunk in column.Chunks.OrderBy(static chunk => chunk.ChunkY))
			{
				writer.Write(chunk.ChunkY);
				ReadOnlySpan<BlockType> blocks = chunk.BlockMemory.Span;
				for (int index = 0; index < blocks.Length;)
				{
					BlockType type = blocks[index];
					int end = index + 1;
					while (end < blocks.Length && blocks[end] == type && end - index < ushort.MaxValue)
						end++;
					writer.Write((ushort)(end - index));
					writer.Write((ushort)type);
					index = end;
				}
			}
		}

		uncompressed.Position = 0;
		using MemoryStream compressed = new();
		using (DeflateStream deflate = new(compressed, CompressionLevel.Fastest, leaveOpen: true))
			uncompressed.CopyTo(deflate);
		return compressed.ToArray();
	}

	public static ChunkColumnSnapshot Decode(int x, int z, long revision, ReadOnlySpan<byte> payload)
	{
		using MemoryStream compressed = new(payload.ToArray(), writable: false);
		using DeflateStream deflate = new(compressed, CompressionMode.Decompress);
		using BinaryReader reader = new(deflate);
		int chunkCount = reader.ReadInt32();
		if (chunkCount < 0 || chunkCount > 4096)
			throw new InvalidDataException($"Invalid chunk count {chunkCount} in column ({x}, {z}).");

		ChunkSnapshot[] chunks = new ChunkSnapshot[chunkCount];
		HashSet<int> chunkYs = new();
		for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
		{
			int chunkY = reader.ReadInt32();
			if (!chunkYs.Add(chunkY))
				throw new InvalidDataException($"Duplicate chunk Y={chunkY} in column ({x}, {z}).");

			BlockType[] blocks = new BlockType[ChunkSnapshot.BlockCount];
			int nonAirBlockCount = 0;
			for (int index = 0; index < blocks.Length;)
			{
				ushort runLength = reader.ReadUInt16();
				BlockType type = (BlockType)reader.ReadUInt16();
				if (runLength == 0 || index + runLength > blocks.Length)
					throw new InvalidDataException($"Invalid RLE run in column ({x}, {z}) chunk Y={chunkY}.");
				Array.Fill(blocks, type, index, runLength);
				if (type != BlockType.None)
					nonAirBlockCount += runLength;
				index += runLength;
			}

			chunks[chunkIndex] = new ChunkSnapshot(x, chunkY, z, blocks, nonAirBlockCount);
		}

		return new ChunkColumnSnapshot(x, z, revision, chunks);
	}

	public static uint ComputeChecksum(ReadOnlySpan<byte> payload)
	{
		uint hash = 2166136261u;
		foreach (byte value in payload)
		{
			hash ^= value;
			hash *= 16777619u;
		}
		return hash;
	}
}
