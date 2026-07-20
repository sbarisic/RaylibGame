using FishGfx.Voxels;
using Voxelgine.FishGfxClient.Voxels;

namespace UnitTest;

public sealed class VoxelLeafMaterialTests
{
	[Fact]
	public void LeavesUseCoveragePreservingCutoutRendering()
	{
		VoxelMaterial leaf = FishGfxVoxelAssets.CreateLeafMaterial();

		Assert.Equal(VoxelRenderMode.Cutout, leaf.RenderMode);
		Assert.False(leaf.OccludesFaces);
		Assert.Equal(1, leaf.Light.Opacity);
		Assert.Equal(VoxelShadowCasterMode.AlphaTest, leaf.ShadowCasterMode);
		Assert.Equal(FishGfxVoxelAssets.CutoutAlphaCutoff, leaf.ShadowAlphaCutoff);
		Assert.Equal(VoxelRendererOptions.DefaultAlphaCutoff, leaf.ShadowAlphaCutoff);
	}
}
