using System.Numerics;
using FishGfx.Voxels;
using Voxelgine.Engine;
using Voxelgine.Graphics;

namespace Voxelgine.States;

internal enum ClientColumnIntegrityProblem
{
	None,
	MissingDomainColumn,
	MissingDomainChunk,
	MissingRenderChunk,
}

internal readonly record struct ClientColumnIntegrityResult(
	ClientColumnIntegrityProblem Problem,
	ChunkColumnCoordinate Column,
	int ChunkY)
{
	internal bool IsHealthy => Problem == ClientColumnIntegrityProblem.None;
}

internal static class ClientColumnIntegrity
{
	private const int VerticalChunksToInspect = 4;

	internal static ClientColumnIntegrityResult Inspect(
		ChunkMap map,
		Vector3 focus,
		Func<ChunkCoordinate, bool> isRenderChunkResident)
	{
		ArgumentNullException.ThrowIfNull(map);
		ArgumentNullException.ThrowIfNull(isRenderChunkResident);

		int chunkX = FloorDiv((int)MathF.Floor(focus.X), Chunk.ChunkSize);
		int chunkY = FloorDiv((int)MathF.Floor(focus.Y), Chunk.ChunkSize);
		int chunkZ = FloorDiv((int)MathF.Floor(focus.Z), Chunk.ChunkSize);
		ChunkColumnCoordinate column = new(chunkX, chunkZ);

		if (!map.IsColumnResident(chunkX, chunkZ))
		{
			return CountResidentNeighborColumns(map, chunkX, chunkZ) == 0
				? default
				: new ClientColumnIntegrityResult(
					ClientColumnIntegrityProblem.MissingDomainColumn,
					column,
					chunkY);
		}

		for (int offset = 0; offset < VerticalChunksToInspect; offset++)
		{
			int inspectedY = chunkY - offset;
			ChunkCoordinate coordinate = new(chunkX, inspectedY, chunkZ);
			if (map.IsChunkResident(chunkX, inspectedY, chunkZ))
			{
				if (!isRenderChunkResident(coordinate))
				{
					return new ClientColumnIntegrityResult(
						ClientColumnIntegrityProblem.MissingRenderChunk,
						column,
						inspectedY);
				}
				continue;
			}

			if (CountResidentNeighborChunks(map, chunkX, inspectedY, chunkZ) >= 2)
			{
				return new ClientColumnIntegrityResult(
					ClientColumnIntegrityProblem.MissingDomainChunk,
					column,
					inspectedY);
			}
		}

		return default;
	}

	private static int CountResidentNeighborColumns(ChunkMap map, int x, int z)
	{
		int count = 0;
		if (map.IsColumnResident(x - 1, z)) count++;
		if (map.IsColumnResident(x + 1, z)) count++;
		if (map.IsColumnResident(x, z - 1)) count++;
		if (map.IsColumnResident(x, z + 1)) count++;
		return count;
	}

	private static int CountResidentNeighborChunks(ChunkMap map, int x, int y, int z)
	{
		int count = 0;
		if (map.IsChunkResident(x - 1, y, z)) count++;
		if (map.IsChunkResident(x + 1, y, z)) count++;
		if (map.IsChunkResident(x, y, z - 1)) count++;
		if (map.IsChunkResident(x, y, z + 1)) count++;
		return count;
	}

	private static int FloorDiv(int value, int divisor)
	{
		int quotient = Math.DivRem(value, divisor, out int remainder);
		return remainder < 0 ? quotient - 1 : quotient;
	}
}
