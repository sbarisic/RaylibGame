namespace Voxelgine.WorldGeneration;

/// <summary>The default topology-first CeramicFish generation facade.</summary>
public sealed class CeramicFish : ICeramicFish
{
	public const int CurrentGeneratorVersion = 9;

	private readonly ICeramicTopologyPlanner topologyPlanner;
	private readonly ICeramicPlacementSolver placementSolver;

	public CeramicFish()
		: this(new CeramicTopologyPlanner(), new CeramicPlacementSolver())
	{
	}

	public CeramicFish(
		ICeramicTopologyPlanner topologyPlanner,
		ICeramicPlacementSolver placementSolver)
	{
		this.topologyPlanner = topologyPlanner
			?? throw new ArgumentNullException(nameof(topologyPlanner));
		this.placementSolver = placementSolver
			?? throw new ArgumentNullException(nameof(placementSolver));
	}

	public int GeneratorVersion => CurrentGeneratorVersion;

	public CeramicValidationResult ValidateDefinition(CeramicFishDefinition definition) =>
		CeramicFishValidation.ValidateDefinition(definition);

	public CeramicValidationResult ValidateRequest(
		CeramicGenerationRequest request,
		CeramicFishDefinition definition) =>
		CeramicFishValidation.ValidateRequest(request, definition);

	public CeramicGenerationResult Generate(
		CeramicGenerationRequest request,
		CeramicFishDefinition definition,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(definition);
		cancellationToken.ThrowIfCancellationRequested();

		CeramicGenerationMetadata metadata = new(
			definition.Id ?? string.Empty,
			definition.FormatVersion,
			GeneratorVersion,
			request.Seed);
		CeramicValidationResult definitionValidation = ValidateDefinition(definition);
		if (!definitionValidation.IsValid)
			return InvalidResult(CeramicGenerationStatus.InvalidCatalog,
				CeramicGenerationStage.DefinitionValidation, definitionValidation, metadata);
		CeramicValidationResult requestValidation = ValidateRequest(request, definition);
		if (!requestValidation.IsValid)
			return InvalidResult(CeramicGenerationStatus.InvalidRequest,
				CeramicGenerationStage.RequestValidation, requestValidation, metadata);

		long topologyChecks = 0;
		long propagationChecks = 0;
		CeramicGenerationFailure? lastFailure = null;
		for (int attempt = 0; attempt < request.MaxAttempts; attempt++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			CeramicTopologyAttemptResult topology = topologyPlanner.Plan(
				request, definition, attempt, cancellationToken);
			topologyChecks = SaturatingAdd(topologyChecks, topology.Checks);
			lastFailure = WithAttempt(topology.Failure, attempt, CeramicGenerationStage.Topology);
			if (topology.Status == CeramicTopologyAttemptStatus.Unsatisfiable)
				return new(CeramicGenerationStatus.Unsatisfiable, [], attempt + 1,
					topologyChecks, propagationChecks, metadata, lastFailure);
			if (!topology.Success) continue;

			CeramicPlacementAttemptResult placement = placementSolver.Solve(
				request, definition, topology.Cells, attempt, cancellationToken);
			propagationChecks = SaturatingAdd(propagationChecks, placement.Checks);
			lastFailure = WithAttempt(placement.Failure, attempt, CeramicGenerationStage.Placement);
			if (placement.Success)
				return new(CeramicGenerationStatus.Success, placement.Placements, attempt + 1,
					topologyChecks, propagationChecks, metadata);
			if (placement.Status == CeramicPlacementAttemptStatus.Unsatisfiable)
				return new(CeramicGenerationStatus.Unsatisfiable, [], attempt + 1,
					topologyChecks, propagationChecks, metadata, lastFailure);
		}

		lastFailure ??= new("attempts-exhausted",
			"All retryable CeramicFish generation attempts were consumed.",
			Stage: CeramicGenerationStage.Topology,
			Attempt: request.MaxAttempts - 1);
		return new(CeramicGenerationStatus.AttemptsExhausted, [], request.MaxAttempts,
			topologyChecks, propagationChecks, metadata, lastFailure);
	}

	private static CeramicGenerationResult InvalidResult(
		CeramicGenerationStatus status,
		CeramicGenerationStage stage,
		CeramicValidationResult validation,
		CeramicGenerationMetadata metadata)
	{
		CeramicValidationError first = validation.Errors[0];
		return new(status, [], 0, 0, 0, metadata,
			new(first.Code, first.Message, first.Cell, stage,
				Data: new Dictionary<string, string>
				{
					["errorCount"] = validation.Errors.Count.ToString(
						System.Globalization.CultureInfo.InvariantCulture),
					["path"] = first.Path ?? string.Empty,
				}));
	}

	private static CeramicGenerationFailure? WithAttempt(
		CeramicGenerationFailure? failure,
		int attempt,
		CeramicGenerationStage stage) => failure is null
		? null
		: failure with { Attempt = attempt, Stage = failure.Stage ?? stage };

	private static long SaturatingAdd(long left, long right) =>
		left > long.MaxValue - right ? long.MaxValue : left + right;
}

/// <summary>The default bounded hybrid topology planner.</summary>
public sealed class CeramicTopologyPlanner : ICeramicTopologyPlanner
{
	private const ulong TopologyPhase = 0xC6BC279692B5CC83UL;

	public CeramicTopologyAttemptResult Plan(
		CeramicGenerationRequest request,
		CeramicFishDefinition definition,
		int attemptOrdinal,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(definition);
		if (attemptOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(attemptOrdinal));
		cancellationToken.ThrowIfCancellationRequested();

		CeramicCompiledCatalog catalog = CeramicCompiledCatalog.Create(definition, request);
		CeramicTopologySearch search = new(request, definition, catalog,
			new CeramicDeterministicRandom(
				CeramicDeterminism.DeriveAttemptSeed(request.Seed, attemptOrdinal,
					CeramicFish.CurrentGeneratorVersion) ^ TopologyPhase),
			cancellationToken);
		return search.Run();
	}
}

/// <summary>The default deterministic weighted prefab-placement solver.</summary>
public sealed class CeramicPlacementSolver : ICeramicPlacementSolver
{
	private const ulong PlacementPhase = 0xD1B54A32D192ED03UL;

	public CeramicPlacementAttemptResult Solve(
		CeramicGenerationRequest request,
		CeramicFishDefinition definition,
		IReadOnlyList<CeramicTopologyCell> topology,
		int attemptOrdinal,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(definition);
		ArgumentNullException.ThrowIfNull(topology);
		if (attemptOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(attemptOrdinal));
		CeramicCompiledCatalog catalog = CeramicCompiledCatalog.Create(definition, request);
		CeramicDeterministicRandom random = new(
			CeramicDeterminism.DeriveAttemptSeed(request.Seed, attemptOrdinal,
				CeramicFish.CurrentGeneratorVersion) ^ PlacementPhase);
		Dictionary<CeramicCell, List<CeramicCellConstraint>> constraints = request.CellConstraints
			.GroupBy(item => item.Cell).ToDictionary(group => group.Key, group => group.ToList());
		HashSet<string> componentTags = definition.ComponentTagPolicies
			.Select(policy => policy.RequiredTag).ToHashSet(StringComparer.Ordinal);
		foreach (CeramicComponentEntryPolicy policy in definition.ComponentEntryPolicies)
		{
			componentTags.Add(policy.RootEntryTag);
			componentTags.Add(policy.ParentDoorTag);
			componentTags.Add(policy.ChildEntryTag);
		}
		foreach (CeramicWallFeaturePolicy policy in definition.WallFeaturePolicies)
			componentTags.Add(policy.FeatureTag);
		foreach (CeramicInteriorFeaturePolicy policy in definition.InteriorFeaturePolicies)
			componentTags.Add(policy.FeatureTag);
		long checks = 0;
		List<CeramicPlacement> placements = new(topology.Count);
		foreach (CeramicTopologyCell cell in topology.OrderBy(item => item.Cell.Z).ThenBy(item => item.Cell.X))
		{
			cancellationToken.ThrowIfCancellationRequested();
			string[] sockets = CeramicSolverUtilities.GetTopologySockets(cell);
			List<CeramicCompiledVariant> candidates = [];
			foreach (CeramicCompiledVariant variant in catalog.Variants)
			{
				if (checks >= request.MaxPropagationChecks)
					return new(CeramicPlacementAttemptStatus.BudgetExceeded, [], checks,
						new("placement-budget-exceeded",
							"The placement propagation budget was exhausted.", cell.Cell,
							CeramicGenerationStage.Placement));
				checks++;
				if (!variant.Sockets.SequenceEqual(sockets, StringComparer.Ordinal)
					|| !cell.Tags.All(variant.TagSet.Contains)
					|| componentTags.Any(tag => variant.TagSet.Contains(tag)
						!= cell.Tags.Contains(tag, StringComparer.Ordinal))) continue;
				if (constraints.TryGetValue(cell.Cell, out List<CeramicCellConstraint>? local)
					&& local.Any(constraint => !CeramicSolverUtilities.Allows(constraint, variant)))
					continue;
				candidates.Add(variant);
			}
			if (candidates.Count == 0)
				return new(CeramicPlacementAttemptStatus.Unsatisfiable, [], checks,
					new("placement-domain-empty",
						"No rotated prefab realizes the successful topology cell.", cell.Cell,
						CeramicGenerationStage.Placement));

			ulong totalWeight = 0;
			foreach (CeramicCompiledVariant candidate in candidates)
				totalWeight = checked(totalWeight + (uint)candidate.Weight);
			ulong selected = random.NextBelow(totalWeight);
			CeramicCompiledVariant chosen = candidates[0];
			foreach (CeramicCompiledVariant candidate in candidates)
			{
				if (selected < (uint)candidate.Weight)
				{
					chosen = candidate;
					break;
				}
				selected -= (uint)candidate.Weight;
			}
			placements.Add(new(chosen.Prefab.Id, cell.Cell, chosen.Rotation));
		}
		return new(CeramicPlacementAttemptStatus.Success, placements, checks);
	}
}
