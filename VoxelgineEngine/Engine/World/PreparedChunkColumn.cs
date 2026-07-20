#nullable enable
using Voxelgine.Engine;

namespace Voxelgine.Graphics;

/// <summary>
/// A decoded column whose storage is ready to be adopted by <see cref="ChunkMap"/>.
/// Preparation is intentionally CPU-only and safe to perform on a decode worker.
/// A prepared column is single-use.
/// </summary>
public sealed class PreparedChunkColumn : IDisposable
{
	private PreparedChunk[]? chunks;

	private PreparedChunkColumn(int x, int z, long revision, PreparedChunk[] chunks)
	{
		X = x;
		Z = z;
		Revision = revision;
		this.chunks = chunks;
	}

	public int X { get; }

	public int Z { get; }

	public long Revision { get; }

	public IReadOnlyList<PreparedChunk> Chunks => chunks
		?? throw new ObjectDisposedException(nameof(PreparedChunkColumn));

	public static PreparedChunkColumn Prepare(ChunkColumnSnapshot source)
	{
		ArgumentNullException.ThrowIfNull(source);
		PreparedChunk[] chunks = new PreparedChunk[source.Chunks.Count];
		for (int chunkIndex = 0; chunkIndex < chunks.Length; chunkIndex++)
		{
			ChunkSnapshot snapshot = source.Chunks[chunkIndex];
			PlacedBlock[] blocks = new PlacedBlock[ChunkSnapshot.BlockCount];
			ReadOnlySpan<BlockType> blockTypes = snapshot.BlockMemory.Span;
			for (int index = 0; index < blocks.Length; index++)
			{
				blocks[index] = new PlacedBlock(blockTypes[index]);
			}

			FogVoxel[]? fog = snapshot.NonEmptyFogCount == 0
				? null
				: snapshot.FogMemory.ToArray();
			chunks[chunkIndex] = new PreparedChunk(
				snapshot.ChunkX,
				snapshot.ChunkY,
				snapshot.ChunkZ,
				blocks,
				snapshot.NonAirBlockCount,
				fog,
				snapshot.NonEmptyFogCount
			);
		}

		return new PreparedChunkColumn(source.X, source.Z, source.Revision, chunks);
	}

	internal PreparedChunk[] Consume()
	{
		PreparedChunk[] result = Interlocked.Exchange(ref chunks, null)
			?? throw new ObjectDisposedException(nameof(PreparedChunkColumn));
		return result;
	}

	public void Dispose()
	{
		Interlocked.Exchange(ref chunks, null);
	}
}

public sealed class PreparedChunk
{
	internal PreparedChunk(
		int chunkX,
		int chunkY,
		int chunkZ,
		PlacedBlock[] blocks,
		int nonAirBlockCount,
		FogVoxel[]? fog,
		int nonEmptyFogCount)
	{
		ChunkX = chunkX;
		ChunkY = chunkY;
		ChunkZ = chunkZ;
		Blocks = blocks;
		NonAirBlockCount = nonAirBlockCount;
		Fog = fog;
		NonEmptyFogCount = nonEmptyFogCount;
	}

	public int ChunkX { get; }

	public int ChunkY { get; }

	public int ChunkZ { get; }

	internal PlacedBlock[] Blocks { get; }

	internal int NonAirBlockCount { get; }

	internal FogVoxel[]? Fog { get; }

	internal int NonEmptyFogCount { get; }
}
