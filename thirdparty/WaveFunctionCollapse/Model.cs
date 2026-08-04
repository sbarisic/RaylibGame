// Copyright (C) 2016 Maxim Gumin, The MIT License (MIT)
// Based on mxgmn/WaveFunctionCollapse Model.cs at de7d22e705e816b62b4d613199d0463820fcaef3.

namespace Mxgmn.WaveFunctionCollapse;

public abstract class Model
{
	protected bool[][] wave = null!;
	protected int[][][] propagator = null!;
	private int[][][] compatible = null!;
	protected int[] observed = null!;

	private (int Index, int Pattern)[] stack = null!;
	private int stackSize;
	private int observedSoFar;

	protected int MX, MY, T, N;
	protected bool periodic, ground;
	protected double[] weights = null!;

	private double[] weightLogWeights = null!;
	private double[] distribution = null!;
	private int[] sumsOfOnes = null!;
	private double sumOfWeights, sumOfWeightLogWeights, startingEntropy;
	private double[] sumsOfWeights = null!, sumsOfWeightLogWeights = null!, entropies = null!;
	private CancellationToken cancellationToken;
	private long remainingPropagationOperations;

	public enum Heuristic { Entropy, MRV, Scanline }
	private readonly Heuristic heuristic;

	protected Model(int width, int height, int n, bool periodic, Heuristic heuristic)
	{
		if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
		if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
		if (n <= 0) throw new ArgumentOutOfRangeException(nameof(n));
		MX = width;
		MY = height;
		N = n;
		this.periodic = periodic;
		this.heuristic = heuristic;
	}

	public bool BudgetExceeded { get; private set; }
	public int? ContradictionIndex { get; private set; }
	public int ObservationCount { get; private set; }
	public IReadOnlyList<int> Observed => observed;

	private void Init()
	{
		wave = new bool[MX * MY][];
		compatible = new int[wave.Length][][];
		for (int i = 0; i < wave.Length; i++)
		{
			wave[i] = new bool[T];
			compatible[i] = new int[T][];
			for (int t = 0; t < T; t++) compatible[i][t] = new int[4];
		}
		distribution = new double[T];
		observed = new int[MX * MY];

		weightLogWeights = new double[T];
		sumOfWeights = 0;
		sumOfWeightLogWeights = 0;
		for (int t = 0; t < T; t++)
		{
			weightLogWeights[t] = weights[t] * Math.Log(weights[t]);
			sumOfWeights += weights[t];
			sumOfWeightLogWeights += weightLogWeights[t];
		}
		startingEntropy = Math.Log(sumOfWeights) - sumOfWeightLogWeights / sumOfWeights;

		sumsOfOnes = new int[MX * MY];
		sumsOfWeights = new double[MX * MY];
		sumsOfWeightLogWeights = new double[MX * MY];
		entropies = new double[MX * MY];
		stack = new (int, int)[wave.Length * T];
	}

	public bool Run(int seed, int limit = -1) =>
		Run(seed, limit, CancellationToken.None, long.MaxValue);

	public bool Run(int seed, int limit, CancellationToken cancellationToken, long maximumPropagationOperations)
	{
		if (maximumPropagationOperations <= 0) throw new ArgumentOutOfRangeException(nameof(maximumPropagationOperations));
		if (wave is null) Init();
		this.cancellationToken = cancellationToken;
		remainingPropagationOperations = maximumPropagationOperations;
		BudgetExceeded = false;
		ContradictionIndex = null;
		ObservationCount = 0;

		Clear();
		if (BudgetExceeded || ContradictionIndex is not null) return false;
		ApplyInitialConstraints();
		if (stackSize > 0 && !Propagate()) return false;
		Random random = new(seed);
		for (int l = 0; l < limit || limit < 0; l++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			int node = NextUnobservedNode(random);
			if (node >= 0)
			{
				Observe(node, random);
				ObservationCount++;
				if (!Propagate()) return false;
			}
			else
			{
				for (int i = 0; i < wave!.Length; i++)
				{
					if (sumsOfOnes[i] == 0) { ContradictionIndex = i; return false; }
					for (int t = 0; t < T; t++) if (wave[i][t]) { observed[i] = t; break; }
				}
				return true;
			}
		}
		return false;
	}

	protected virtual void ApplyInitialConstraints() { }

	private int NextUnobservedNode(Random random)
	{
		if (heuristic == Heuristic.Scanline)
		{
			for (int i = observedSoFar; i < wave.Length; i++)
			{
				if (!periodic && (i % MX + N > MX || i / MX + N > MY)) continue;
				if (sumsOfOnes[i] > 1) { observedSoFar = i + 1; return i; }
			}
			return -1;
		}

		double min = 1E+4;
		int argmin = -1;
		for (int i = 0; i < wave.Length; i++)
		{
			if (!periodic && (i % MX + N > MX || i / MX + N > MY)) continue;
			int remainingValues = sumsOfOnes[i];
			double entropy = heuristic == Heuristic.Entropy ? entropies[i] : remainingValues;
			if (remainingValues > 1 && entropy <= min)
			{
				double noise = 1E-6 * random.NextDouble();
				if (entropy + noise < min) { min = entropy + noise; argmin = i; }
			}
		}
		return argmin;
	}

	private void Observe(int node, Random random)
	{
		bool[] w = wave[node];
		for (int t = 0; t < T; t++) distribution[t] = w[t] ? weights[t] * ObservationWeight(node, t) : 0.0;
		int selected = WeightedRandom(distribution, random.NextDouble());
		for (int t = 0; t < T; t++) if (w[t] != (t == selected)) Ban(node, t);
	}

	protected virtual double ObservationWeight(int node, int pattern) => 1.0;

	private bool Propagate()
	{
		while (stackSize > 0)
		{
			cancellationToken.ThrowIfCancellationRequested();
			(int i1, int t1) = stack[--stackSize];
			int x1 = i1 % MX;
			int y1 = i1 / MX;
			for (int d = 0; d < 4; d++)
			{
				int x2 = x1 + Dx[d];
				int y2 = y1 + Dy[d];
				if (!periodic && (x2 < 0 || y2 < 0 || x2 + N > MX || y2 + N > MY)) continue;
				if (x2 < 0) x2 += MX; else if (x2 >= MX) x2 -= MX;
				if (y2 < 0) y2 += MY; else if (y2 >= MY) y2 -= MY;
				int i2 = x2 + y2 * MX;
				int[] candidates = propagator[d][t1];
				int[][] compat = compatible[i2];
				for (int l = 0; l < candidates.Length; l++)
				{
					if (--remainingPropagationOperations < 0) { BudgetExceeded = true; return false; }
					int t2 = candidates[l];
					int[] counts = compat[t2];
					counts[d]--;
					if (counts[d] == 0) Ban(i2, t2);
				}
			}
		}
		return ContradictionIndex is null;
	}

	protected void Ban(int index, int pattern)
	{
		if (!wave[index][pattern]) return;
		wave[index][pattern] = false;
		Array.Clear(compatible[index][pattern]);
		stack[stackSize++] = (index, pattern);
		sumsOfOnes[index]--;
		sumsOfWeights[index] -= weights[pattern];
		sumsOfWeightLogWeights[index] -= weightLogWeights[pattern];
		if (sumsOfOnes[index] == 0) { ContradictionIndex ??= index; return; }
		double sum = sumsOfWeights[index];
		entropies[index] = Math.Log(sum) - sumsOfWeightLogWeights[index] / sum;
	}

	private void Clear()
	{
		stackSize = 0;
		for (int i = 0; i < wave.Length; i++)
		{
			for (int t = 0; t < T; t++)
			{
				wave[i][t] = true;
				for (int d = 0; d < 4; d++) compatible[i][t][d] = propagator[Opposite[d]][t].Length;
			}
			sumsOfOnes[i] = weights.Length;
			sumsOfWeights[i] = sumOfWeights;
			sumsOfWeightLogWeights[i] = sumOfWeightLogWeights;
			entropies[i] = startingEntropy;
			observed[i] = -1;
		}
		observedSoFar = 0;

		for (int y = 0; y < MY; y++) for (int x = 0; x < MX; x++)
		{
			if (!periodic && (x + N > MX || y + N > MY)) continue;
			int i = x + y * MX;
			for (int t = 0; t < T; t++)
			{
				bool noNeighborsRight = (periodic || x < MX - N) && propagator[2][t].Length == 0;
				bool noNeighborsTop = (periodic || y > 0) && propagator[3][t].Length == 0;
				bool noNeighborsLeft = (periodic || x > 0) && propagator[0][t].Length == 0;
				bool noNeighborsBottom = (periodic || y < MY - N) && propagator[1][t].Length == 0;
				if (noNeighborsRight || noNeighborsTop || noNeighborsLeft || noNeighborsBottom) Ban(i, t);
			}
		}
		if (ground)
		{
			for (int x = 0; x < MX; x++)
			{
				int bottom = x + (MY - 1) * MX;
				for (int t = 0; t < T - 1; t++) if (wave[bottom][t]) Ban(bottom, t);
				for (int y = 0; y < MY - 1; y++) if (wave[x + y * MX][T - 1]) Ban(x + y * MX, T - 1);
			}
		}
		if (stackSize > 0) Propagate();
	}

	private static int WeightedRandom(double[] values, double random)
	{
		double sum = values.Sum();
		double threshold = random * sum;
		double partial = 0;
		for (int i = 0; i < values.Length; i++) { partial += values[i]; if (partial >= threshold) return i; }
		return 0;
	}

	private static readonly int[] Dx = [-1, 0, 1, 0];
	private static readonly int[] Dy = [0, 1, 0, -1];
	private static readonly int[] Opposite = [2, 3, 0, 1];
}
