using Voxelgine.FishGfxClient.Rendering;

namespace UnitTest;

public sealed class RollingFrameTimelineTests
{
	[Fact]
	public void ReportsDeterministicRollingPercentiles()
	{
		RollingFrameTimeline timeline = new(windowSeconds: 10, capacity: 16);
		for (int index = 1; index <= 10; index++)
		{
			timeline.Update(index, index / 1000.0);
		}

		FramePercentiles result = timeline.Capture();

		Assert.Equal(10, result.SampleCount);
		Assert.Equal(5, result.MedianMilliseconds);
		Assert.Equal(10, result.P95Milliseconds);
		Assert.Equal(10, result.P99Milliseconds);
		Assert.Equal(10, result.MaximumMilliseconds);
	}

	[Fact]
	public void DropsSamplesOutsideTheRollingWindow()
	{
		RollingFrameTimeline timeline = new(windowSeconds: 1, capacity: 16);
		timeline.Update(0, 0.020);
		timeline.Update(0.5, 0.010);
		timeline.Update(1.1, 0.005);

		FramePercentiles result = timeline.Capture();

		Assert.Equal(2, result.SampleCount);
		Assert.Equal(5, result.MedianMilliseconds);
		Assert.Equal(10, result.MaximumMilliseconds);
	}
}
