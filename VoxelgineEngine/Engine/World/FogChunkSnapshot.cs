#nullable enable
using System.Buffers;

namespace Voxelgine.Graphics;

public readonly record struct FogChunkBounds
{
	public FogChunkBounds(
		int minimumX,
		int minimumY,
		int minimumZ,
		int maximumXExclusive,
		int maximumYExclusive,
		int maximumZExclusive)
	{
		if (maximumXExclusive < minimumX
			|| maximumYExclusive < minimumY
			|| maximumZExclusive < minimumZ)
		{
			throw new ArgumentException("Fog chunk bounds must have non-negative extents.");
		}

		MinimumX = minimumX;
		MinimumY = minimumY;
		MinimumZ = minimumZ;
		MaximumXExclusive = maximumXExclusive;
		MaximumYExclusive = maximumYExclusive;
		MaximumZExclusive = maximumZExclusive;
	}

	public int MinimumX { get; }

	public int MinimumY { get; }

	public int MinimumZ { get; }

	public int MaximumXExclusive { get; }

	public int MaximumYExclusive { get; }

	public int MaximumZExclusive { get; }

	public bool Contains(int x, int y, int z) =>
		x >= MinimumX && x < MaximumXExclusive
		&& y >= MinimumY && y < MaximumYExclusive
		&& z >= MinimumZ && z < MaximumZExclusive;
}

/// <summary>
/// Pooled immutable fog-only snapshot used by client render workers. The lease
/// must be disposed after the worker has finished reading <see cref="Fog"/>.
/// </summary>
public sealed class FogChunkSnapshotLease : IDisposable
{
	private FogVoxel[]? buffer;

	internal FogChunkSnapshotLease(
		int chunkX,
		int chunkY,
		int chunkZ,
		FogVoxel[] buffer,
		int nonEmptyFogCount)
	{
		ChunkX = chunkX;
		ChunkY = chunkY;
		ChunkZ = chunkZ;
		this.buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
		NonEmptyFogCount = nonEmptyFogCount;
	}

	public int ChunkX { get; }

	public int ChunkY { get; }

	public int ChunkZ { get; }

	public int NonEmptyFogCount { get; }

	public ReadOnlyMemory<FogVoxel> Fog => (buffer
		?? throw new ObjectDisposedException(nameof(FogChunkSnapshotLease)))
		.AsMemory(0, ChunkSnapshot.BlockCount);

	public void Dispose()
	{
		FogVoxel[]? released = Interlocked.Exchange(ref buffer, null);
		if (released != null)
		{
			ArrayPool<FogVoxel>.Shared.Return(released, clearArray: false);
		}
	}
}
