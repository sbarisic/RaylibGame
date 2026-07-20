#if WINDOWS
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Voxelgine.FishGfxClient.Assets;

internal sealed class AssetReloadQueue
{
	private readonly ConcurrentQueue<string> manual = new();
	private readonly ConcurrentDictionary<string, long> automatic =
		new(StringComparer.OrdinalIgnoreCase);
	private readonly long debounceTicks;
	private readonly Func<long> getTimestamp;

	internal AssetReloadQueue(long debounceTicks, Func<long> getTimestamp)
	{
		if (debounceTicks < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(debounceTicks));
		}
		this.getTimestamp = getTimestamp ?? throw new ArgumentNullException(nameof(getTimestamp));
		this.debounceTicks = debounceTicks;
	}

	internal static AssetReloadQueue CreateDefault()
	{
		return new AssetReloadQueue((long)(Stopwatch.Frequency * 0.2), Stopwatch.GetTimestamp);
	}

	internal void QueueManual(string assetId)
	{
		automatic.TryRemove(assetId, out _);
		manual.Enqueue(assetId);
	}

	internal void QueueAutomatic(string assetId)
	{
		long due = getTimestamp() + debounceTicks;
		automatic.AddOrUpdate(assetId, due, (_, _) => due);
	}

	internal void DrainReady(ISet<string> destination)
	{
		ArgumentNullException.ThrowIfNull(destination);
		while (manual.TryDequeue(out string assetId))
		{
			destination.Add(assetId);
			automatic.TryRemove(assetId, out _);
		}

		long now = getTimestamp();
		ICollection<KeyValuePair<string, long>> scheduled = automatic;
		foreach (KeyValuePair<string, long> entry in automatic)
		{
			if (entry.Value <= now && scheduled.Remove(entry))
			{
				destination.Add(entry.Key);
			}
		}
	}

	internal void Clear()
	{
		while (manual.TryDequeue(out _))
		{
		}
		automatic.Clear();
	}
}
#endif
