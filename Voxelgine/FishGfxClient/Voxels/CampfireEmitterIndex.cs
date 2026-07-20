using System.Collections.ObjectModel;
using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Graphics;

namespace Voxelgine.FishGfxClient.Voxels;

/// <summary>
/// Maintains campfire audio positions and campfire/torch particle sources in an
/// authoritative <see cref="ChunkMap"/> mirror. Reset scans are deterministic.
/// </summary>
public sealed class CampfireEmitterIndex
{
	private readonly HashSet<BlockPosition> campfireBlocks = new();
	private readonly HashSet<BlockPosition> torchBlocks = new();
	private IReadOnlyList<Vector3> positions = Array.Empty<Vector3>();
	private IReadOnlyList<VoxelFireEmitter> particleEmitters = Array.Empty<VoxelFireEmitter>();

	public int Count => campfireBlocks.Count;

	public int TorchCount => torchBlocks.Count;

	/// <summary>
	/// A stable snapshot ordered by world X, then Y, then Z. Positions point at
	/// block centers so they can be passed directly to a spatial audio emitter.
	/// </summary>
	public IReadOnlyList<Vector3> Positions => positions;

	/// <summary>
	/// A stable snapshot of every voxel which produces visible flame particles.
	/// Campfire-only audio continues to use <see cref="Positions"/>.
	/// </summary>
	public IReadOnlyList<VoxelFireEmitter> ParticleEmitters => particleEmitters;

	public void Reset(IReadOnlyList<ChunkSnapshot> snapshots)
	{
		ArgumentNullException.ThrowIfNull(snapshots);
		campfireBlocks.Clear();
		torchBlocks.Clear();

		foreach (ChunkSnapshot snapshot in snapshots)
		{
			for (int z = 0; z < ChunkSnapshot.Size; z++)
			{
				for (int y = 0; y < ChunkSnapshot.Size; y++)
				{
					for (int x = 0; x < ChunkSnapshot.Size; x++)
					{
						BlockType type = snapshot.GetBlock(x, y, z);
						if (type is not (BlockType.Campfire or BlockType.Torch))
						{
							continue;
						}

						BlockPosition position = new(
							snapshot.ChunkX * ChunkSnapshot.Size + x,
							snapshot.ChunkY * ChunkSnapshot.Size + y,
							snapshot.ChunkZ * ChunkSnapshot.Size + z
						);
						GetBlocks(type).Add(position);
					}
				}
			}
		}

		RebuildPositions();
	}

	public void Apply(in BlockChange change)
	{
		bool changed = false;
		BlockPosition position = new(change.X, change.Y, change.Z);

		if (change.OldType is BlockType.Campfire or BlockType.Torch)
		{
			changed |= GetBlocks(change.OldType).Remove(position);
		}

		if (change.NewType is BlockType.Campfire or BlockType.Torch)
		{
			changed |= GetBlocks(change.NewType).Add(position);
		}

		if (changed)
		{
			RebuildPositions();
		}
	}

	private void RebuildPositions()
	{
		Vector3[] snapshot = campfireBlocks
			.OrderBy(static position => position.X)
			.ThenBy(static position => position.Y)
			.ThenBy(static position => position.Z)
			.Select(static position => new Vector3(
				position.X + 0.5f,
				position.Y + 0.5f,
				position.Z + 0.5f
			))
			.ToArray();
		positions = new ReadOnlyCollection<Vector3>(snapshot);

		VoxelFireEmitter[] emitters = campfireBlocks
			.Select(static position => new VoxelFireEmitter(
				BlockType.Campfire,
				ToCenter(position)
			))
			.Concat(torchBlocks.Select(static position => new VoxelFireEmitter(
				BlockType.Torch,
				ToCenter(position)
			)))
			.OrderBy(static emitter => emitter.Position.X)
			.ThenBy(static emitter => emitter.Position.Y)
			.ThenBy(static emitter => emitter.Position.Z)
			.ThenBy(static emitter => emitter.Type)
			.ToArray();
		particleEmitters = new ReadOnlyCollection<VoxelFireEmitter>(emitters);
	}

	public void ReplaceColumn(ChunkColumnSnapshot column)
	{
		ArgumentNullException.ThrowIfNull(column);
		campfireBlocks.RemoveWhere(position =>
			FloorChunk(position.X) == column.X && FloorChunk(position.Z) == column.Z);
		torchBlocks.RemoveWhere(position =>
			FloorChunk(position.X) == column.X && FloorChunk(position.Z) == column.Z);

		foreach (ChunkSnapshot snapshot in column.Chunks)
		{
			for (int z = 0; z < ChunkSnapshot.Size; z++)
			{
				for (int y = 0; y < ChunkSnapshot.Size; y++)
				{
					for (int x = 0; x < ChunkSnapshot.Size; x++)
					{
						BlockType type = snapshot.GetBlock(x, y, z);
						if (type is not (BlockType.Campfire or BlockType.Torch))
							continue;
						GetBlocks(type).Add(new BlockPosition(
							snapshot.ChunkX * ChunkSnapshot.Size + x,
							snapshot.ChunkY * ChunkSnapshot.Size + y,
							snapshot.ChunkZ * ChunkSnapshot.Size + z));
					}
				}
			}
		}
		RebuildPositions();
	}

	public void ReplaceColumn(
		int chunkX,
		int chunkZ,
		IReadOnlyList<VoxelFireEmitter> emitters)
	{
		ArgumentNullException.ThrowIfNull(emitters);
		campfireBlocks.RemoveWhere(position =>
			FloorChunk(position.X) == chunkX && FloorChunk(position.Z) == chunkZ);
		torchBlocks.RemoveWhere(position =>
			FloorChunk(position.X) == chunkX && FloorChunk(position.Z) == chunkZ);
		foreach (VoxelFireEmitter emitter in emitters)
		{
			BlockPosition position = new(
				(int)MathF.Floor(emitter.Position.X),
				(int)MathF.Floor(emitter.Position.Y),
				(int)MathF.Floor(emitter.Position.Z)
			);
			GetBlocks(emitter.Type).Add(position);
		}
		RebuildPositions();
	}

	private HashSet<BlockPosition> GetBlocks(BlockType type)
	{
		return type switch
		{
			BlockType.Campfire => campfireBlocks,
			BlockType.Torch => torchBlocks,
			_ => throw new ArgumentOutOfRangeException(nameof(type), type, null),
		};
	}

	private static Vector3 ToCenter(BlockPosition position)
	{
		return new Vector3(position.X + 0.5f, position.Y + 0.5f, position.Z + 0.5f);
	}

	private static int FloorChunk(int coordinate) =>
		(int)Math.Floor((double)coordinate / ChunkSnapshot.Size);

	private readonly record struct BlockPosition(int X, int Y, int Z);
}

public readonly record struct VoxelFireEmitter(BlockType Type, Vector3 Position);
