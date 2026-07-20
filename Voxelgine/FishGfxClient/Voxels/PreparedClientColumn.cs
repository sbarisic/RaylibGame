#if WINDOWS
#nullable enable
using FishGfx.Voxels;
using Voxelgine.Engine;
using Voxelgine.Graphics;

namespace Voxelgine.FishGfxClient.Voxels;

public sealed class PreparedClientColumn : IDisposable
{
	private PreparedRenderChunk[]? renderChunks;

	private PreparedClientColumn(
		PreparedChunkColumn domainColumn,
		PreparedRenderChunk[] renderChunks,
		VoxelFireEmitter[] emitters)
	{
		DomainColumn = domainColumn;
		this.renderChunks = renderChunks;
		Emitters = emitters;
	}

	public PreparedChunkColumn DomainColumn { get; }
	public IReadOnlyList<PreparedRenderChunk> RenderChunks => renderChunks
		?? throw new ObjectDisposedException(nameof(PreparedClientColumn));
	public long Revision => DomainColumn.Revision;
	internal IReadOnlyList<VoxelFireEmitter> Emitters { get; }

	internal static PreparedClientColumn Prepare(
		ChunkColumnSnapshot source,
		IReadOnlyDictionary<BlockType, ushort> materialIds)
	{
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(materialIds);
		PreparedChunkColumn domain = PreparedChunkColumn.Prepare(source);
		PreparedRenderChunk[] render = new PreparedRenderChunk[source.Chunks.Count];
		List<VoxelFireEmitter> emitters = new();
		int highestY = source.Chunks.Count == 0
			? int.MinValue
			: source.Chunks.Max(static chunk => chunk.ChunkY);

		for (int chunkIndex = 0; chunkIndex < render.Length; chunkIndex++)
		{
			ChunkSnapshot snapshot = source.Chunks[chunkIndex];
			VoxelCell[] cells = new VoxelCell[VoxelWorld.ChunkVolume];
			ReadOnlySpan<BlockType> blocks = snapshot.BlockMemory.Span;
			for (int index = 0; index < cells.Length; index++)
			{
				BlockType block = blocks[index];
				cells[index] = new VoxelCell(materialIds[block]);
				if (block is not (BlockType.Campfire or BlockType.Torch))
					continue;

				int z = index / (ChunkSnapshot.Size * ChunkSnapshot.Size);
				int remainder = index - z * ChunkSnapshot.Size * ChunkSnapshot.Size;
				int y = remainder / ChunkSnapshot.Size;
				int x = remainder % ChunkSnapshot.Size;
				emitters.Add(new VoxelFireEmitter(
					block,
					new System.Numerics.Vector3(
						snapshot.ChunkX * ChunkSnapshot.Size + x + 0.5f,
						snapshot.ChunkY * ChunkSnapshot.Size + y + 0.5f,
						snapshot.ChunkZ * ChunkSnapshot.Size + z + 0.5f
					)
				));
			}

			render[chunkIndex] = new PreparedRenderChunk(
				new ChunkCoordinate(snapshot.ChunkX, snapshot.ChunkY, snapshot.ChunkZ),
				PreparedVoxelChunk.TakeOwnership(cells),
				snapshot.ChunkY == highestY
			);
		}

		return new PreparedClientColumn(domain, render, emitters.ToArray());
	}

	internal PreparedRenderChunk[] ConsumeRenderChunks() =>
		Interlocked.Exchange(ref renderChunks, null)
		?? throw new ObjectDisposedException(nameof(PreparedClientColumn));

	public void Dispose()
	{
		DomainColumn.Dispose();
		PreparedRenderChunk[]? chunks = Interlocked.Exchange(ref renderChunks, null);
		if (chunks == null)
			return;
		foreach (PreparedRenderChunk chunk in chunks)
			chunk.Dispose();
	}
}

public sealed class PreparedRenderChunk : IDisposable
{
	private PreparedVoxelChunk? storage;

	internal PreparedRenderChunk(
		ChunkCoordinate coordinate,
		PreparedVoxelChunk storage,
		bool skyExposedAbove)
	{
		Coordinate = coordinate;
		this.storage = storage;
		SkyExposedAbove = skyExposedAbove;
	}

	public ChunkCoordinate Coordinate { get; }
	public bool SkyExposedAbove { get; }
	internal PreparedVoxelChunk ConsumeStorage() =>
		Interlocked.Exchange(ref storage, null)
		?? throw new ObjectDisposedException(nameof(PreparedRenderChunk));
	public void Dispose() => Interlocked.Exchange(ref storage, null)?.Dispose();
}
#endif
