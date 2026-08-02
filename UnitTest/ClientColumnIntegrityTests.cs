using FishGfx.Voxels;
using Voxelgine.Engine;
using Voxelgine.Graphics;
using Voxelgine.States;

namespace UnitTest;

public sealed class ClientColumnIntegrityTests
{
	[Fact]
	public void InspectDetectsMissingFocusedDomainColumnBesideResidentTerrain()
	{
		ChunkMap map = new();
		AddChunks(map, 42, 40, 3);

		ClientColumnIntegrityResult result = ClientColumnIntegrity.Inspect(
			map,
			new System.Numerics.Vector3(697, 72, 644),
			static _ => false);

		Assert.Equal(ClientColumnIntegrityProblem.MissingDomainColumn, result.Problem);
		Assert.Equal(new ChunkColumnCoordinate(43, 40), result.Column);
	}

	[Fact]
	public void InspectDetectsVerticalDomainHoleSupportedByNeighborColumns()
	{
		ChunkMap map = new();
		AddChunks(map, 43, 40, 4);
		AddChunks(map, 42, 40, 3, 4);
		AddChunks(map, 44, 40, 3, 4);

		ClientColumnIntegrityResult result = ClientColumnIntegrity.Inspect(
			map,
			new System.Numerics.Vector3(697, 72, 644),
			static _ => true);

		Assert.Equal(ClientColumnIntegrityProblem.MissingDomainChunk, result.Problem);
		Assert.Equal(3, result.ChunkY);
	}

	[Fact]
	public void InspectDetectsFishGfxMirrorHoleForResidentDomainChunk()
	{
		ChunkMap map = new();
		AddChunks(map, 43, 40, 3, 4);

		ClientColumnIntegrityResult result = ClientColumnIntegrity.Inspect(
			map,
			new System.Numerics.Vector3(697, 72, 644),
			coordinate => coordinate.Y != 3);

		Assert.Equal(ClientColumnIntegrityProblem.MissingRenderChunk, result.Problem);
		Assert.Equal(3, result.ChunkY);
	}

	[Fact]
	public void InspectDoesNotTreatIsolatedSparseAirAsAStreamHole()
	{
		ChunkMap map = new();
		AddChunks(map, 43, 40, 4);

		ClientColumnIntegrityResult result = ClientColumnIntegrity.Inspect(
			map,
			new System.Numerics.Vector3(697, 72, 644),
			static _ => true);

		Assert.True(result.IsHealthy);
	}

	private static void AddChunks(ChunkMap map, int x, int z, params int[] chunkYs)
	{
		foreach (int y in chunkYs)
			map.SetBlock(x * Chunk.ChunkSize, y * Chunk.ChunkSize, z * Chunk.ChunkSize, BlockType.Stone);
	}
}
