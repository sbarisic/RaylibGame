using FishGfx.Graphics;

namespace Voxelgine.Engine;

public unsafe partial class ClientPlayer
{
	public void RenderFishGfxViewModel(RenderPass pass)
	{
		if (!LocalPlayer)
		{
			return;
		}

		FishGfxViewModelRenderer?.Render(pass, this, ViewMdl);
	}

	public override void Dispose()
	{
		FishGfxViewModelRenderer?.Dispose();
		FishGfxViewModelRenderer = null;
		ViewMdl?.Dispose();
		ViewMdl = null;
	}
}
