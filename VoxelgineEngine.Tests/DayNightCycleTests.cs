using Voxelgine.Engine;

namespace VoxelgineEngine.Tests;

public sealed class DayNightCycleTests
{
	[Theory]
	[InlineData(0, 0.15f, "Night")]
	[InlineData(8, 1f, "Morning")]
	[InlineData(12, 1f, "Afternoon")]
	[InlineData(18, 0.575f, "Dusk")]
	public void AuthoritativeTimeProducesStableLighting(
		float hour,
		float expectedSkylight,
		string expectedPeriod)
	{
		DayNightCycle cycle = new();
		DayNightLightingState observed = default;
		cycle.LightingChanged += value => observed = value;

		cycle.SetTime(hour);

		Assert.Equal(hour, cycle.TimeOfDay);
		Assert.Equal(expectedSkylight, cycle.SkyLightMultiplier, 3);
		Assert.Equal(expectedPeriod, cycle.GetPeriodString());
		Assert.Equal(cycle.Lighting, observed);
	}

	[Fact]
	public void NonAuthoritySynchronizationWrapsAcrossMidnight()
	{
		DayNightCycle cycle = new() { IsAuthority = false };
		cycle.SetTime(23.9f);
		cycle.SetTime(0.1f);

		cycle.Update(0.25f);

		Assert.True(cycle.TimeOfDay < 1f || cycle.TimeOfDay > 23f);
	}

	[Fact]
	public void InvalidDeltaTimeIsRejected()
	{
		DayNightCycle cycle = new();
		Assert.Throws<ArgumentOutOfRangeException>(() => cycle.Update(-0.01f));
		Assert.Throws<ArgumentOutOfRangeException>(() => cycle.Update(float.NaN));
	}
}
