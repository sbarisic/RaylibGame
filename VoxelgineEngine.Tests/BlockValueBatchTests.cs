using Voxelgine.Engine;
using Voxelgine.Graphics;

namespace VoxelgineEngine.Tests;

public sealed class BlockValueBatchTests
{
	[Fact]
	public void NonStatefulBlocksRejectNonzeroState()
	{
		Assert.Throws<ArgumentOutOfRangeException>(() => new BlockValue(BlockType.Stone, 1));
		Assert.Throws<ArgumentOutOfRangeException>(() => new BlockValue(BlockType.Stone, 0b1000));
	}

	[Fact]
	public void AuthoritativeBatchCommitsBeforeObserversAndRevisesEachColumnOnce()
	{
		ChunkMap map = new();
		map.SetBlock(0, 0, 0, BlockType.Stone);
		map.SetBlock(16, 0, 0, BlockType.Stone);
		long firstBefore = map.GetColumnRevision(0, 0);
		long secondBefore = map.GetColumnRevision(1, 0);
		List<BlockChange> observed = new();
		map.BlockChanged += change =>
		{
			Assert.Equal(BlockType.Dirt, map.GetBlock(1, 0, 0));
			Assert.Equal(BlockType.Dirt, map.GetBlock(2, 0, 0));
			Assert.Equal(BlockType.Dirt, map.GetBlock(17, 0, 0));
			observed.Add(change);
		};

		IReadOnlyList<BlockChange> committed = map.ApplyBlockBatch(new[]
		{
			new BlockMutationRequest(1, 0, 0, BlockType.Dirt),
			new BlockMutationRequest(2, 0, 0, BlockType.Dirt),
			new BlockMutationRequest(17, 0, 0, BlockType.Dirt),
		});

		Assert.Equal(3, committed.Count);
		Assert.Equal(committed, observed);
		Assert.Equal(firstBefore + 1, map.GetColumnRevision(0, 0));
		Assert.Equal(secondBefore + 1, map.GetColumnRevision(1, 0));
		Assert.Equal(committed[0].ColumnRevision, committed[1].ColumnRevision);
	}

	[Fact]
	public void AuthoritativeBatchRejectsAllChangesBeforeMutation()
	{
		ChunkMap map = new();
		BlockMutationRequest duplicate = new(1, 2, 3, BlockType.Stone);
		Assert.Throws<ArgumentException>(() => map.ApplyBlockBatch(new[] { duplicate, duplicate }));
		Assert.Equal(BlockType.None, map.GetBlock(1, 2, 3));
	}
}
