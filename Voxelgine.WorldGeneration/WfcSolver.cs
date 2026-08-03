// Algorithm adapted from Maxim Gumin's WaveFunctionCollapse Model.cs.
// Copyright (C) 2016 Maxim Gumin. Licensed under the MIT License.
namespace Voxelgine.WorldGeneration;

internal sealed class WfcSolver<T>
{
	private readonly int width;
	private readonly int height;
	private readonly T[] patterns;
	private readonly int[] weights;
	private readonly bool[,,] compatible;
	internal int LastFailureObservations { get; private set; }
	internal (int X, int Y)? LastContradictionCell { get; private set; }

	public WfcSolver(int width, int height, IReadOnlyList<T> patterns, IReadOnlyList<int> weights, Func<T, T, int, bool> compatible)
	{
		if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
		ArgumentNullException.ThrowIfNull(patterns); ArgumentNullException.ThrowIfNull(weights); ArgumentNullException.ThrowIfNull(compatible);
		if (patterns.Count == 0 || patterns.Count != weights.Count || weights.Any(static weight => weight <= 0))
			throw new ArgumentException("WFC patterns require matching positive weights.");
		this.width = width; this.height = height; this.patterns = patterns.ToArray(); this.weights = weights.ToArray();
		this.compatible = new bool[4, patterns.Count, patterns.Count];
		for (int direction = 0; direction < 4; direction++)
		for (int left = 0; left < patterns.Count; left++)
		for (int right = 0; right < patterns.Count; right++)
			this.compatible[direction, left, right] = compatible(this.patterns[left], this.patterns[right], direction);
	}

	public bool TryRun(ulong seed, Func<int, int, T, bool>? allowed, CancellationToken cancellationToken, out T[] result)
	{
		LastFailureObservations = 0;
		LastContradictionCell = null;
		bool[,] wave = new bool[checked(width * height), patterns.Length];
		for (int cell = 0; cell < width * height; cell++)
		for (int pattern = 0; pattern < patterns.Length; pattern++)
			wave[cell, pattern] = allowed?.Invoke(cell % width, cell / width, patterns[pattern]) ?? true;
		DeterministicRandom random = new(seed);
		while (true)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (!Propagate(wave)) { result = []; return false; }
			int selected = SelectCell(wave);
			if (selected < 0) break;
			int chosen = Choose(wave, selected, ref random);
			if (chosen < 0) { result = []; return false; }
			LastFailureObservations++;
			for (int pattern = 0; pattern < patterns.Length; pattern++) wave[selected, pattern] = pattern == chosen;
		}
		result = new T[width * height];
		for (int cell = 0; cell < result.Length; cell++)
		{
			int pattern = First(wave, cell);
			if (pattern < 0) { result = []; return false; }
			result[cell] = patterns[pattern];
		}
		return true;
	}

	private bool Propagate(bool[,] wave)
	{
		bool changed;
		do
		{
			changed = false;
			for (int cell = 0; cell < width * height; cell++)
			{
				int x = cell % width, y = cell / width;
				for (int pattern = 0; pattern < patterns.Length; pattern++) if (wave[cell, pattern])
				{
					for (int direction = 0; direction < 4; direction++)
					{
						int nx = x + Dx[direction], ny = y + Dy[direction];
						if ((uint)nx >= (uint)width || (uint)ny >= (uint)height) continue;
						int neighbor = ny * width + nx; bool supported = false;
						for (int candidate = 0; candidate < patterns.Length && !supported; candidate++)
							supported = wave[neighbor, candidate] && compatible[direction, pattern, candidate];
						if (!supported) { wave[cell, pattern] = false; changed = true; break; }
					}
				}
				if (First(wave, cell) < 0) { LastContradictionCell = (cell % width, cell / width); return false; }
			}
		} while (changed);
		return true;
	}

	private int SelectCell(bool[,] wave)
	{
		int selected = -1, minimum = int.MaxValue;
		for (int cell = 0; cell < width * height; cell++)
		{
			int count = 0; for (int pattern = 0; pattern < patterns.Length; pattern++) if (wave[cell, pattern]) count++;
			if (count > 1 && count < minimum) { minimum = count; selected = cell; }
		}
		return selected;
	}

	private int Choose(bool[,] wave, int cell, ref DeterministicRandom random)
	{
		long total = 0; for (int pattern = 0; pattern < patterns.Length; pattern++) if (wave[cell, pattern]) total += weights[pattern];
		if (total <= 0) return -1;
		long value = (long)(random.NextUInt64() % (ulong)total);
		for (int pattern = 0; pattern < patterns.Length; pattern++) if (wave[cell, pattern] && (value -= weights[pattern]) < 0) return pattern;
		return -1;
	}

	private int First(bool[,] wave, int cell)
	{
		for (int pattern = 0; pattern < patterns.Length; pattern++) if (wave[cell, pattern]) return pattern;
		return -1;
	}

	private static readonly int[] Dx = [0, 1, 0, -1];
	private static readonly int[] Dy = [-1, 0, 1, 0];

	private struct DeterministicRandom(ulong state)
	{
		private ulong state = state == 0 ? 0x9E3779B97F4A7C15UL : state;
		public ulong NextUInt64()
		{
			state += 0x9E3779B97F4A7C15UL;
			ulong value = state;
			value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
			value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
			return value ^ (value >> 31);
		}
	}
}
