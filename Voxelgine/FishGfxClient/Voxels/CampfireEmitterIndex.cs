using System.Collections.ObjectModel;
using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Graphics;

namespace Voxelgine.FishGfxClient.Voxels;

/// <summary>
/// Maintains the block-centered positions of campfires in an authoritative
/// <see cref="ChunkMap"/> mirror. Reset scans are deterministic and subsequent
/// updates are constant-time.
/// </summary>
public sealed class CampfireEmitterIndex
{
	private readonly HashSet<BlockPosition> blocks = new();
	private IReadOnlyList<Vector3> positions = Array.Empty<Vector3>();

	public int Count => blocks.Count;

	/// <summary>
	/// A stable snapshot ordered by world X, then Y, then Z. Positions point at
	/// block centers so they can be passed directly to a spatial audio emitter.
	/// </summary>
	public IReadOnlyList<Vector3> Positions => positions;

	public void Reset(IReadOnlyList<ChunkSnapshot> snapshots)
	{
		ArgumentNullException.ThrowIfNull(snapshots);
		blocks.Clear();

		foreach (ChunkSnapshot snapshot in snapshots)
		{
			for (int z = 0; z < ChunkSnapshot.Size; z++)
			{
				for (int y = 0; y < ChunkSnapshot.Size; y++)
				{
					for (int x = 0; x < ChunkSnapshot.Size; x++)
					{
						if (snapshot.GetBlock(x, y, z) != BlockType.Campfire)
						{
							continue;
						}

						blocks.Add(new BlockPosition(
							snapshot.ChunkX * ChunkSnapshot.Size + x,
							snapshot.ChunkY * ChunkSnapshot.Size + y,
							snapshot.ChunkZ * ChunkSnapshot.Size + z
						));
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

		if (change.OldType == BlockType.Campfire)
		{
			changed |= blocks.Remove(position);
		}

		if (change.NewType == BlockType.Campfire)
		{
			changed |= blocks.Add(position);
		}

		if (changed)
		{
			RebuildPositions();
		}
	}

	private void RebuildPositions()
	{
		Vector3[] snapshot = blocks
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
	}

	private readonly record struct BlockPosition(int X, int Y, int Z);
}
