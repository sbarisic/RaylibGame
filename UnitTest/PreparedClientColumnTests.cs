using FishGfx.Voxels;
using Voxelgine.Engine;
using Voxelgine.FishGfxClient.Voxels;
using Voxelgine.Graphics;

namespace UnitTest;

public sealed class PreparedClientColumnTests
{
	[Fact]
	public void AirCellsDoNotRequirePaletteEntries()
	{
		ChunkMap map = new();
		map.SetBlock(1, 2, 3, BlockType.Stone);
		ChunkColumnSnapshot source = map.CaptureColumn(0, 0);
		Dictionary<BlockType, ushort> materialIds = new()
		{
			[BlockType.Stone] = 7,
		};

		using PreparedClientColumn prepared = PreparedClientColumn.Prepare(source, materialIds);
		PreparedRenderChunk renderChunk = Assert.Single(prepared.RenderChunks);
		using PreparedVoxelChunk storage = renderChunk.ConsumeStorage();

		Assert.Equal(1, storage.NonAirCount);
	}

	[Fact]
	public void MissingNonAirPaletteEntryReportsTheBlockType()
	{
		ChunkMap map = new();
		map.SetBlock(0, 0, 0, BlockType.Stone);
		ChunkColumnSnapshot source = map.CaptureColumn(0, 0);

		InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
			() => PreparedClientColumn.Prepare(source, new Dictionary<BlockType, ushort>()));

		Assert.Contains(nameof(BlockType.Stone), exception.Message, StringComparison.Ordinal);
	}
}
