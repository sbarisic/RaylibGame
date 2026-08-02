using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.FishGfxClient.Assets;
using Voxelgine.GUI;
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
	public void InspectorChoicesUseExactlyOneCanonicalStateZeroValuePerBlock()
	{
		var model = new VoxelMaterialInspectorModel(BlockType.Stone);

		Assert.Equal(VoxelMaterialPreviewState.AvailableBlockTypes, model.Choices.Select(choice => choice.Type));
		Assert.All(model.Choices, choice =>
		{
			Assert.Equal(choice.Type, choice.Value.Type);
			Assert.Equal(0, choice.Value.State);
		});
	}

	[Fact]
	public void InspectorFilteringMatchesNameAndNumericIdWithoutChangingSelection()
	{
		var model = new VoxelMaterialInspectorModel(BlockType.StoneStairs);

		Assert.Contains(model.Filter("stairs"), choice => choice.Type == BlockType.StoneStairs);
		Assert.Equal([BlockType.StoneStairs], model.Filter(((int)BlockType.StoneStairs).ToString())
			.Select(choice => choice.Type));
		Assert.Empty(model.Filter("definitely-no-such-block"));
		Assert.Equal(BlockType.StoneStairs, model.Selected);
		Assert.Equal(model.Choices, model.Filter(string.Empty));
	}

	[Theory]
	[InlineData(1280, 720, 380)]
	[InlineData(1920, 1080, 440)]
	[InlineData(300, 240, 268)]
	[InlineData(20, 100, 0)]
	public void InspectorLayoutStaysInsideViewportAndKeepsBlockListMinimum(
		float width, float height, float expectedWidth)
	{
		VoxelMaterialInspectorLayout layout = VoxelMaterialInspectorLayout.Calculate(width, height);

		Assert.Equal(expectedWidth, layout.Size.X);
		Assert.True(layout.Size.X <= Math.Max(0, width - 32));
		Assert.True(layout.Size.Y <= Math.Max(0, height - 32));
		Assert.Equal(180, layout.BlockListMinimumHeight);
		Assert.Equal(116, layout.CentralPosition.Y);
	}

	[Fact]
	public void InspectorStackHeightIncludesEveryCardAndSpacing()
	{
		float height = VoxelMaterialInspectorLayout.CalculateStackContentHeight(
			[180, 142, 276, 116]);

		Assert.Equal(750, height);
		Assert.True(height >= 180 + 142 + 276 + 116);
	}

	[Fact]
	public void FishUiManagerRequiresRuntimePathsAndOwnsCentralDiagnosticsPolicy()
	{
		var constructors = typeof(FishUIManager).GetConstructors();
		var constructor = Assert.Single(constructors);

		Assert.Equal([typeof(IGameWindow), typeof(IFishLogging), typeof(RuntimePaths)],
			constructor.GetParameters().Select(parameter => parameter.ParameterType));
		Assert.Equal(20_000, GameFishUIDiagnosticsPolicy.HistoryEventLimit);
		Assert.Equal(TimeSpan.FromSeconds(10), GameFishUIDiagnosticsPolicy.HistoryDuration);
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

	[Fact]
	public void ReloadSuppressionCanClearOnlyTheTargetAsset()
	{
		long timestamp = 1000;
		var queue = new AssetReloadQueue(0, () => timestamp);
		HashSet<string> ready = new(StringComparer.OrdinalIgnoreCase);
		queue.QueueAutomatic("voxel.surface-textures");
		queue.QueueManual("other");

		queue.Clear("voxel.surface-textures");
		queue.DrainReady(ready);

		Assert.Equal(["other"], ready);
	}
}
