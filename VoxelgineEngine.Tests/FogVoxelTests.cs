using Voxelgine.Engine;
using Voxelgine.Graphics;

namespace VoxelgineEngine.Tests;

public sealed class FogVoxelTests
{
	[Fact]
	public void FogVoxelPremultipliesStraightColorAndRejectsInvalidValues()
	{
		FogVoxel value = FogVoxel.FromStraight(new Rgba32(255, 128, 64), 128);

		Assert.Equal((byte)128, value.R);
		Assert.Equal((byte)64, value.G);
		Assert.Equal((byte)32, value.B);
		Assert.Equal((byte)128, value.Density);
		Assert.Equal(value, FogVoxel.FromPacked(value.Packed));
		Assert.Throws<ArgumentException>(() => new FogVoxel(11, 0, 0, 10));
	}

	[Fact]
	public void FogCoexistsWithSolidBlocksAndUsesOrderedColumnRevisions()
	{
		ChunkMap map = new();
		IReadOnlyList<WorldMutation> mutations = map.GetPendingWorldMutations();
		FogVoxel fog = FogVoxel.FromStraight(new Rgba32(80, 160, 255), 96);

		map.SetBlock(1, 2, 3, BlockType.Stone);
		map.SetFog(1, 2, 3, fog);
		map.SetBlock(1, 2, 3, BlockType.None);

		Assert.Equal(fog, map.GetFog(1, 2, 3));
		Assert.Equal(3, mutations.Count);
		Assert.Equal(WorldMutationKind.Block, mutations[0].Kind);
		Assert.Equal(WorldMutationKind.Fog, mutations[1].Kind);
		Assert.Equal(WorldMutationKind.Block, mutations[2].Kind);
		Assert.Equal(
			new long[] { 1, 2, 3 },
			mutations.Select(value => value.Kind == WorldMutationKind.Block
				? value.Block.ColumnRevision
				: value.Fog.ColumnRevision)
		);
	}

	[Fact]
	public void ColumnCodecRoundTripsFogAndIncludesItInChecksum()
	{
		BlockType[] blocks = new BlockType[ChunkSnapshot.BlockCount];
		FogVoxel[] fog = new FogVoxel[ChunkSnapshot.BlockCount];
		fog[0] = FogVoxel.FromStraight(new Rgba32(255, 32, 16), 200);
		fog[^1] = FogVoxel.FromStraight(new Rgba32(20, 255, 80), 64);
		ChunkColumnSnapshot source = new(0, 0, 9, [
			new ChunkSnapshot(0, 0, 0, blocks, fog: fog),
		]);

		byte[] encoded = WorldColumnCodec.Encode(source);
		ChunkColumnSnapshot decoded = WorldColumnCodec.Decode(0, 0, 9, encoded);

		Assert.Equal(fog, decoded.Chunks[0].Fog);
		byte[] changed = (byte[])encoded.Clone();
		changed[^1] ^= 1;
		Assert.NotEqual(
			WorldColumnCodec.ComputeChecksum(encoded),
			WorldColumnCodec.ComputeChecksum(changed)
		);
	}

	[Fact]
	public void ClearingLastFogCellReturnsChunkToLazyEmptyState()
	{
		ChunkMap map = new();
		FogVoxel fog = FogVoxel.FromStraight(new Rgba32(255, 255, 255), 32);
		map.SetFog(-1, 4, -1, fog);
		map.ClearFog(-1, 4, -1, 1, 1, 1);

		ChunkSnapshot snapshot = map.CaptureChunks().Single();
		Assert.Equal(0, snapshot.NonEmptyFogCount);
		Assert.All(snapshot.Fog, value => Assert.True(value.IsEmpty));
	}
}
