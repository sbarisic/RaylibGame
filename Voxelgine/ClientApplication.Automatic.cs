using FishGfx;
using Voxelgine.Engine;
using Voxelgine.FishGfxClient;
using Voxelgine.States;

namespace Voxelgine;

internal sealed partial class ClientApplication
{
	private int GetAutomaticFrameCount()
	{
		if (arguments.Contains("--fishgfx-auto-voxel-material", StringComparer.OrdinalIgnoreCase))
			return 120;
		return arguments.Contains("--fishgfx-auto-gameplay", StringComparer.OrdinalIgnoreCase) ? 90 : 4;
	}

	private static void ValidateAutomaticFrame(IFishGfxGameWindow window, GameStateImpl state)
	{
		if (state is FishGfxGameplaySmokeState gameplay && !gameplay.IsFogVolumeReady)
			throw new InvalidOperationException("The automatic gameplay fog volume was not uploaded in time.");
		if (state is VoxelMaterialPreviewState materialPreview && !materialPreview.IsReady)
			throw new InvalidOperationException("The automatic voxel material preview did not finish meshing in time.");
		if (state is VoxelMaterialPreviewState validatedMaterialPreview)
			validatedMaterialPreview.ValidateAutomaticSnapshotBundle();

		const int channelTolerance = 16;
		const int minimumForegroundPixels = 64;
		Color clear = state.GetRenderSettings(window.RenderWindow.FramebufferSize).ClearColor;
		int foregroundPixels = 0;
		window.RenderWindow.ReadPixels();
		foreach (Color pixel in window.RenderWindow.PixelData.Span)
		{
			bool differs = Math.Abs(pixel.R - clear.R) > channelTolerance
				|| Math.Abs(pixel.G - clear.G) > channelTolerance
				|| Math.Abs(pixel.B - clear.B) > channelTolerance;
			if (differs && ++foregroundPixels >= minimumForegroundPixels)
				return;
		}
		throw new InvalidOperationException($"FishGfx automatic run produced fewer than {minimumForegroundPixels} foreground pixels.");
	}
}
