namespace Mxgmn.WaveFunctionCollapse;

public sealed class ConstrainedTiledModel<TPattern> : Model
{
	private readonly TPattern[] patterns;
	private readonly Func<TPattern, TPattern, int, double>? adjacencyWeight;
	private Func<int, int, TPattern, bool>? allowed;

	public ConstrainedTiledModel(
		int width,
		int height,
		IReadOnlyList<TPattern> patterns,
		IReadOnlyList<double> weights,
		Func<TPattern, TPattern, int, bool> compatible,
		Func<TPattern, TPattern, int, double>? adjacencyWeight = null,
		Heuristic heuristic = Heuristic.Entropy)
		: base(width, height, 1, periodic: false, heuristic)
	{
		ArgumentNullException.ThrowIfNull(patterns);
		ArgumentNullException.ThrowIfNull(weights);
		ArgumentNullException.ThrowIfNull(compatible);
		if (patterns.Count == 0 || patterns.Count != weights.Count || weights.Any(static value => value <= 0 || !double.IsFinite(value)))
			throw new ArgumentException("WFC patterns require matching positive finite weights.");
		this.patterns = patterns.ToArray();
		this.adjacencyWeight = adjacencyWeight;
		this.weights = weights.ToArray();
		T = patterns.Count;
		propagator = new int[4][][];
		for (int direction = 0; direction < 4; direction++)
		{
			propagator[direction] = new int[T][];
			for (int source = 0; source < T; source++)
			{
				List<int> supported = [];
				for (int neighbor = 0; neighbor < T; neighbor++)
					if (compatible(this.patterns[source], this.patterns[neighbor], direction)) supported.Add(neighbor);
				propagator[direction][source] = supported.ToArray();
			}
		}
	}

	protected override double ObservationWeight(int node, int pattern)
	{
		if (adjacencyWeight is null) return 1.0;
		int x = node % MX, y = node / MX;
		double modifier = 1.0;
		int[] dx = [-1, 0, 1, 0], dy = [0, 1, 0, -1];
		for (int direction = 0; direction < 4; direction++)
		{
			int nx = x + dx[direction], ny = y + dy[direction];
			if ((uint)nx >= (uint)MX || (uint)ny >= (uint)MY) continue;
			bool[] neighborWave = wave[nx + ny * MX];
			int observedNeighbor = -1;
			for (int candidate = 0; candidate < patterns.Length; candidate++) if (neighborWave[candidate])
			{
				if (observedNeighbor >= 0) { observedNeighbor = -2; break; }
				observedNeighbor = candidate;
			}
			if (observedNeighbor >= 0)
				modifier *= Math.Max(1e-6, adjacencyWeight(patterns[pattern], patterns[observedNeighbor], direction));
		}
		return modifier;
	}

	public bool TryRun(
		int seed,
		Func<int, int, TPattern, bool>? allowed,
		CancellationToken cancellationToken,
		long maximumPropagationOperations,
		out TPattern[] result)
	{
		this.allowed = allowed;
		try
		{
			if (!Run(seed, -1, cancellationToken, maximumPropagationOperations)) { result = []; return false; }
			result = Observed.Select(index => patterns[index]).ToArray();
			return true;
		}
		finally { this.allowed = null; }
	}

	protected override void ApplyInitialConstraints()
	{
		if (allowed is null) return;
		for (int index = 0; index < wave.Length; index++)
			for (int pattern = 0; pattern < patterns.Length; pattern++)
				if (!allowed(index % MX, index / MX, patterns[pattern])) Ban(index, pattern);
	}
}
