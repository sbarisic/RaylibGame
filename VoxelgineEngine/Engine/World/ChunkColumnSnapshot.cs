using System;

namespace Voxelgine.Graphics;

public readonly record struct ChunkColumnCoordinate(int X, int Z);

/// <summary>
/// Immutable renderer-free snapshot of every stored vertical chunk in one X/Z column.
/// </summary>
public sealed class ChunkColumnSnapshot
{
	public ChunkColumnSnapshot(int x, int z, long revision, ChunkSnapshot[] chunks)
	{
		ArgumentNullException.ThrowIfNull(chunks);
		X = x;
		Z = z;
		Revision = revision;
		Chunks = chunks;
	}

	public int X { get; }
	public int Z { get; }
	public long Revision { get; }
	public IReadOnlyList<ChunkSnapshot> Chunks { get; }
}
