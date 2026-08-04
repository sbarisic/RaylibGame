using Mxgmn.WaveFunctionCollapse;

namespace Voxelgine.WorldGeneration;

/// <summary>World-generation adapter over Maxim Gumin's original WFC model.</summary>
internal sealed class WfcSolver<T>
{
	private static readonly int[] OriginalDirectionToWorldDirection = [3, 2, 1, 0];
	private readonly ConstrainedTiledModel<T> model;
	private readonly int width;

	public WfcSolver(int width, int height, IReadOnlyList<T> patterns, IReadOnlyList<double> weights,
		Func<T, T, int, bool> compatible, Func<T, T, int, double>? adjacencyWeight = null)
	{
		ArgumentNullException.ThrowIfNull(compatible);
		this.width = width;
		model = new(width, height, patterns, weights.ToArray(),
			(left, right, originalDirection) => compatible(left, right, OriginalDirectionToWorldDirection[originalDirection]),
			adjacencyWeight is null ? null : (left, right, originalDirection) => adjacencyWeight(left, right, OriginalDirectionToWorldDirection[originalDirection]));
	}

	internal int LastFailureObservations => model.ObservationCount;
	internal (int X, int Y)? LastContradictionCell => model.ContradictionIndex is int index
		? (index % width, index / width) : null;
	internal bool LastBudgetExceeded => model.BudgetExceeded;

	public bool TryRun(ulong seed, Func<int, int, T, bool>? allowed, CancellationToken cancellationToken,
		long maximumPropagationChecks, out T[] result) =>
		model.TryRun(unchecked((int)(seed ^ (seed >> 32))), allowed, cancellationToken, maximumPropagationChecks, out result);
}
