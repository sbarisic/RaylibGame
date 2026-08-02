using FishGfx.Voxels;

namespace Voxelgine.FishGfxClient.Voxels;

internal static class StreamingBackpressurePolicy
{
	internal const int ResumeLightingPending = 65_536;
	internal const int PauseLightingPending = 262_144;

	internal static bool Update(
		bool currentlyBackpressured,
		in VoxelRendererWorkload workload,
		int lightingPending)
	{
		if (currentlyBackpressured)
		{
			return workload.IsBackpressured
				|| lightingPending > ResumeLightingPending;
		}

		return workload.IsBackpressured
			|| lightingPending >= PauseLightingPending;
	}
}
