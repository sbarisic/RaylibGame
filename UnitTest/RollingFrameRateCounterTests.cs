using Voxelgine.FishGfxClient.Rendering;

namespace UnitTest;

public sealed class RollingFrameRateCounterTests
{
	[Fact]
	public void ReportsRollingFramesPerSecond()
	{
		RollingFrameRateCounter counter = new(0.5);
		for (int frame = 1; frame <= 60; frame++)
		{
			counter.Update(frame / 60.0, 1.0 / 60.0);
		}

		Assert.InRange(counter.FramesPerSecond, 59.9, 60.1);
	}

	[Fact]
	public void ResetClearsDisplayedFrameRate()
	{
		RollingFrameRateCounter counter = new();
		counter.Update(1, 1.0 / 120.0);

		counter.Reset();

		Assert.Equal(0, counter.FramesPerSecond);
	}
}
