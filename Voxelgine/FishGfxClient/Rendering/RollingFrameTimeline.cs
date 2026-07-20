namespace Voxelgine.FishGfxClient.Rendering;

internal readonly record struct FramePercentiles(
	double MedianMilliseconds,
	double P95Milliseconds,
	double P99Milliseconds,
	double MaximumMilliseconds,
	int SampleCount);

/// <summary>
/// Fixed-capacity rolling frame history. It allocates once and reuses its sort
/// scratch so enabling renderer profiling cannot itself create frame garbage.
/// </summary>
internal sealed class RollingFrameTimeline
{
	private readonly double windowSeconds;
	private readonly double[] timestamps;
	private readonly double[] durations;
	private readonly double[] sortScratch;
	private int start;
	private int count;

	internal RollingFrameTimeline(double windowSeconds = 10, int capacity = 2400)
	{
		if (!double.IsFinite(windowSeconds) || windowSeconds <= 0)
			throw new ArgumentOutOfRangeException(nameof(windowSeconds));
		if (capacity <= 0)
			throw new ArgumentOutOfRangeException(nameof(capacity));

		this.windowSeconds = windowSeconds;
		timestamps = new double[capacity];
		durations = new double[capacity];
		sortScratch = new double[capacity];
	}

	internal void Update(double timestamp, double durationSeconds)
	{
		if (!double.IsFinite(timestamp) || !double.IsFinite(durationSeconds)
			|| durationSeconds < 0)
		{
			return;
		}

		int index;
		if (count == timestamps.Length)
		{
			index = start;
			start = (start + 1) % timestamps.Length;
		}
		else
		{
			index = (start + count) % timestamps.Length;
			count++;
		}

		timestamps[index] = timestamp;
		durations[index] = durationSeconds * 1000;
		double cutoff = timestamp - windowSeconds;
		while (count > 1 && timestamps[start] < cutoff)
		{
			start = (start + 1) % timestamps.Length;
			count--;
		}
	}

	internal FramePercentiles Capture()
	{
		for (int offset = 0; offset < count; offset++)
		{
			sortScratch[offset] = durations[(start + offset) % durations.Length];
		}

		Array.Sort(sortScratch, 0, count);
		return count == 0
			? default
			: new FramePercentiles(
				Percentile(0.5),
				Percentile(0.95),
				Percentile(0.99),
				sortScratch[count - 1],
				count
			);
	}

	internal void Reset()
	{
		start = 0;
		count = 0;
	}

	private double Percentile(double percentile)
	{
		int index = (int)Math.Ceiling(percentile * count) - 1;
		return sortScratch[Math.Clamp(index, 0, count - 1)];
	}
}
