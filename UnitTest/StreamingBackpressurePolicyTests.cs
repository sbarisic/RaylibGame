using FishGfx.Voxels;
using Voxelgine.FishGfxClient.Voxels;

namespace UnitTest;

public sealed class StreamingBackpressurePolicyTests
{
	[Fact]
	public void InactiveDirtyMeshesDoNotKeepStreamingPaused()
	{
		VoxelRendererWorkload workload = new(
			DirtyMeshes: 132,
			InFlightMeshes: 0,
			CompletedMeshes: 0,
			PendingUploadJobs: 0,
			PendingUploadBytes: 0,
			IsBackpressured: false);

		bool paused = StreamingBackpressurePolicy.Update(
			currentlyBackpressured: true,
			workload,
			lightingPending: 0);

		Assert.False(paused);
	}

	[Fact]
	public void RendererPressureStillPausesStreaming()
	{
		VoxelRendererWorkload workload = new(
			DirtyMeshes: 0,
			InFlightMeshes: 4,
			CompletedMeshes: 128,
			PendingUploadJobs: 128,
			PendingUploadBytes: 32 * 1024 * 1024,
			IsBackpressured: true);

		Assert.True(StreamingBackpressurePolicy.Update(
			currentlyBackpressured: false,
			workload,
			lightingPending: 0));
	}

	[Fact]
	public void LightingPressureRetainsHysteresis()
	{
		VoxelRendererWorkload workload = default;

		Assert.True(StreamingBackpressurePolicy.Update(
			currentlyBackpressured: false,
			workload,
			lightingPending: StreamingBackpressurePolicy.PauseLightingPending));
		Assert.True(StreamingBackpressurePolicy.Update(
			currentlyBackpressured: true,
			workload,
			lightingPending: StreamingBackpressurePolicy.ResumeLightingPending + 1));
		Assert.False(StreamingBackpressurePolicy.Update(
			currentlyBackpressured: true,
			workload,
			lightingPending: StreamingBackpressurePolicy.ResumeLightingPending));
	}
}
