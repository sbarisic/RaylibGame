using Voxelgine.Engine;
using Voxelgine.FishGfxClient.Assets;
using Voxelgine.States;

namespace UnitTest;

public sealed class VoxelMaterialPreviewTests
{
	[Fact]
	public void BlockChoicesContainEveryNonAirValueOnceInNumericOrder()
	{
		BlockType[] expected = Enum.GetValues<BlockType>()
			.Where(static block => block != BlockType.None)
			.OrderBy(static block => (int)block)
			.ToArray();

		Assert.Equal(expected, VoxelMaterialPreviewState.AvailableBlockTypes);
		Assert.Equal(expected.Length, VoxelMaterialPreviewState.AvailableBlockTypes.Distinct().Count());
		Assert.Equal(BlockType.Stone, VoxelMaterialPreviewState.DefaultBlockType);
	}

	[Fact]
	public void AutomaticWatcherRequestsAreDebouncedAndCoalesced()
	{
		long timestamp = 0;
		var queue = new AssetReloadQueue(200, () => timestamp);
		HashSet<string> ready = new(StringComparer.OrdinalIgnoreCase);

		queue.QueueAutomatic("voxel.surface-textures");
		timestamp = 150;
		queue.QueueAutomatic("voxel.surface-textures");
		timestamp = 349;
		queue.DrainReady(ready);
		Assert.Empty(ready);

		timestamp = 350;
		queue.DrainReady(ready);
		Assert.Equal(["voxel.surface-textures"], ready);
	}

	[Fact]
	public void ManualRequestBypassesPendingAutomaticDebounce()
	{
		long timestamp = 0;
		var queue = new AssetReloadQueue(200, () => timestamp);
		HashSet<string> ready = new(StringComparer.OrdinalIgnoreCase);

		queue.QueueAutomatic("voxel.surface-textures");
		queue.QueueManual("voxel.surface-textures");
		queue.DrainReady(ready);

		Assert.Equal(["voxel.surface-textures"], ready);
		ready.Clear();
		timestamp = 500;
		queue.DrainReady(ready);
		Assert.Empty(ready);
	}
}
