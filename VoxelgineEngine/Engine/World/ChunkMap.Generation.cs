using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.World.Structures;
using Voxelgine.WorldGeneration;

namespace Voxelgine.Graphics;

public unsafe partial class ChunkMap
{
	public void GenerateFloatingIsland(int width, int length, int seed = 666, CancellationToken cancellationToken = default)
	{
		WorldPlan plan = WorldPlanMaterializer.GeneratePlan(width, length, seed, null, cancellationToken);
		WorldPlanMaterializer.MaterializeAtomically(this, plan, null, cancellationToken);
	}

	public void GenerateFloatingIsland(int width, int length, StructureBlueprintCatalog structures, int seed = 666, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(structures);
		WorldPlan plan = WorldPlanMaterializer.GeneratePlan(width, length, seed, structures, cancellationToken);
		WorldPlanMaterializer.MaterializeAtomically(this, plan, structures, cancellationToken);
	}

	public void GenerateFloatingIsland(WorldPlan plan, StructureBlueprintCatalog structures, CancellationToken cancellationToken = default) =>
		WorldPlanMaterializer.MaterializeAtomically(this, plan, structures, cancellationToken);

	public List<Vector3> FindSpawnPoints(int count, int minSpacing = 5, CancellationToken cancellationToken = default)
	{
		int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue, minZ = int.MaxValue, maxZ = int.MinValue;
		foreach (KeyValuePair<Vector3, Chunk> entry in Chunks.Items)
		{
			int x = (int)entry.Key.X * Chunk.ChunkSize, y = (int)entry.Key.Y * Chunk.ChunkSize, z = (int)entry.Key.Z * Chunk.ChunkSize;
			minX = Math.Min(minX, x); maxX = Math.Max(maxX, x + Chunk.ChunkSize);
			minY = Math.Min(minY, y); maxY = Math.Max(maxY, y + Chunk.ChunkSize);
			minZ = Math.Min(minZ, z); maxZ = Math.Max(maxZ, z + Chunk.ChunkSize);
		}
		if (minX == int.MaxValue) return [];
		int centerX = (minX + maxX) / 2, centerZ = (minZ + maxZ) / 2;
		List<Vector3> result = new(count); float spacingSquared = minSpacing * minSpacing;

		bool TryAdd(int x, int z)
		{
			if (x <= minX || x >= maxX - 1 || z <= minZ || z >= maxZ - 1) return false;
			for (int y = maxY - 1; y >= minY; y--)
			{
				if (GetBlock(x, y, z) != BlockType.Grass || GetBlock(x, y + 1, z) != BlockType.None || GetBlock(x, y + 2, z) != BlockType.None || GetBlock(x, y + 3, z) != BlockType.None) continue;
				bool flat = true;
				for (int dx = -1; dx <= 1 && flat; dx++) for (int dz = -1; dz <= 1 && flat; dz++)
				{
					bool neighbor = false;
					for (int neighborY = y + 1; neighborY >= y - 1; neighborY--)
						if (BlockInfo.IsSolid(GetBlock(x + dx, neighborY, z + dz)) && GetBlock(x + dx, neighborY + 1, z + dz) == BlockType.None) { neighbor = true; break; }
					flat = neighbor;
				}
				if (!flat) continue;
				Vector3 candidate = new(x, y + 3, z);
				if (result.Any(selected => Vector2.DistanceSquared(new(candidate.X, candidate.Z), new(selected.X, selected.Z)) < spacingSquared)) return false;
				result.Add(candidate); return true;
			}
			return false;
		}

		int maximumRadius = Math.Max(maxX - minX, maxZ - minZ);
		for (int radius = 0; radius <= maximumRadius && result.Count < count; radius++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (radius == 0) { TryAdd(centerX, centerZ); continue; }
			for (int x = centerX - radius; x <= centerX + radius && result.Count < count; x++) { TryAdd(x, centerZ - radius); if (result.Count < count) TryAdd(x, centerZ + radius); }
			for (int z = centerZ - radius + 1; z < centerZ + radius && result.Count < count; z++) { TryAdd(centerX - radius, z); if (result.Count < count) TryAdd(centerX + radius, z); }
		}
		return result;
	}
}
