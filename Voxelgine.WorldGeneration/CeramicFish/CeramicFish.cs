namespace Voxelgine.WorldGeneration;

/// <summary>
/// Generates a two-dimensional village layout from prefabs whose contents are
/// three-dimensional. The caller remains responsible for spawning the entities.
/// </summary>
public interface ICeramicFish
{
	/// <summary>
	/// Version of the generation algorithm. Increment this whenever the same canonical
	/// definition, request, and seed may intentionally produce different output.
	/// </summary>
	int GeneratorVersion => 1;

	/// <summary>Checks one complete CeramicFish definition before generation starts.</summary>
	CeramicValidationResult ValidateDefinition(CeramicFishDefinition definition);

	/// <summary>Checks a generation request against its CeramicFish definition.</summary>
	CeramicValidationResult ValidateRequest(
		CeramicGenerationRequest request,
		CeramicFishDefinition definition);

	/// <summary>
	/// Generates a deterministic village layout. Cancellation throws
	/// OperationCanceledException. The facade owns retries and aggregates all attempt
	/// counters into the returned result.
	/// </summary>
	CeramicGenerationResult Generate(
		CeramicGenerationRequest request,
		CeramicFishDefinition definition,
		CancellationToken cancellationToken = default);
}

/// <summary>
/// Constructs the connection topology that a later placement solver must realize
/// with concrete rotated prefabs.
/// </summary>
public interface ICeramicTopologyPlanner
{
	/// <summary>
	/// Builds one wall, road, and other policy-owned topology attempt. Cancellation
	/// throws OperationCanceledException.
	/// </summary>
	CeramicTopologyAttemptResult Plan(
		CeramicGenerationRequest request,
		CeramicFishDefinition definition,
		int attemptOrdinal,
		CancellationToken cancellationToken = default);
}

/// <summary>
/// Selects concrete rotated prefabs for a topology that has already satisfied its
/// global graph requirements.
/// </summary>
public interface ICeramicPlacementSolver
{
	/// <summary>
	/// Performs one attempt to realize a valid topology as exactly one placement per
	/// active cell. Cancellation throws OperationCanceledException.
	/// </summary>
	CeramicPlacementAttemptResult Solve(
		CeramicGenerationRequest request,
		CeramicFishDefinition definition,
		IReadOnlyList<CeramicTopologyCell> topology,
		int attemptOrdinal,
		CancellationToken cancellationToken = default);
}

// Reproducibility is guaranteed only for the same canonical definition, request, and
// ICeramicFish.GeneratorVersion.
// TODO(CeramicFish integration): Integrate the result with the production world
// generator. Keep the 85x85 acceptance scenario out of the normal unit-test suite so
// it does not restore the removed slow world-generation coverage.

/// <summary>
/// Loads and saves one complete CeramicFish definition per JSON file.
/// Implementations must structurally validate documents after loading and before
/// replacing a file. Generator-specific validation remains the responsibility of
/// ICeramicFish.ValidateDefinition.
/// </summary>
public interface ICeramicFishJsonStorage
{
	/// <summary>
	/// Loads and validates one CeramicFish definition from a JSON file. Invalid
	/// definitions throw CeramicDefinitionException and cancellation throws
	/// OperationCanceledException.
	/// </summary>
	ValueTask<CeramicFishDefinition> LoadAsync(
		string path,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Validates and atomically saves one complete CeramicFish definition to a single
	/// JSON file. Cancellation throws OperationCanceledException.
	/// </summary>
	ValueTask SaveAsync(
		string path,
		CeramicFishDefinition definition,
		CancellationToken cancellationToken = default);
}

/// <summary>
/// The root object stored in one CeramicFish JSON file. It contains all prefabs
/// and generator-owned metadata required to reconstruct one definition.
/// </summary>
public sealed record CeramicFishDefinition
{
	public const int CurrentFormatVersion = 3;

	/// <summary>
	/// JSON constructor. A missing formatVersion is supplied as zero and must be
	/// rejected by structural validation.
	/// </summary>
	[System.Text.Json.Serialization.JsonConstructor]
	public CeramicFishDefinition(
		string id,
		IReadOnlyList<CeramicPrefabDefinition> prefabs,
		IReadOnlyList<CeramicConnectionPolicy> connectionPolicies,
		int formatVersion)
	{
		Id = id;
		Prefabs = prefabs;
		ConnectionPolicies = connectionPolicies;
		FormatVersion = formatVersion;
	}

	/// <summary>Source-compatible constructor for a new current-format definition.</summary>
	public CeramicFishDefinition(
		string id,
		IReadOnlyList<CeramicPrefabDefinition> prefabs,
		IReadOnlyList<CeramicConnectionPolicy> connectionPolicies)
		: this(id, prefabs, connectionPolicies, CurrentFormatVersion)
	{
	}

	public string Id { get; init; }
	public IReadOnlyList<CeramicPrefabDefinition> Prefabs { get; init; }
	public IReadOnlyList<CeramicConnectionPolicy> ConnectionPolicies { get; init; }

	/// <summary>JSON schema version used to reject incompatible files.</summary>
	public int FormatVersion { get; init; }

	/// <summary>
	/// Cross-network requirements, such as requiring each house-wall component to
	/// share an edge with a road-tagged cell.
	/// </summary>
	public IReadOnlyList<CeramicComponentAdjacencyPolicy> ComponentAdjacencyPolicies { get; init; } = [];

	/// <summary>
	/// Per-component topology tag requirements, such as exactly one house-door on each
	/// closed house-wall component.
	/// </summary>
	public IReadOnlyList<CeramicComponentTagPolicy> ComponentTagPolicies { get; init; } = [];

	/// <summary>
	/// Entry rules for buildings made from closed room loops that share wall segments.
	/// Shared partitions join the loops into one wall-network component. A building has
	/// one exterior entry; every additional room contributes one shared doorway and one
	/// additional independent wall cycle.
	/// </summary>
	public IReadOnlyList<CeramicComponentEntryPolicy> ComponentEntryPolicies { get; init; } = [];

	/// <summary>
	/// Bounded feature counts on socket-network walls. Outer-wall-only features are
	/// forbidden on the shared partitions identified by the component entry policy.
	/// </summary>
	public IReadOnlyList<CeramicWallFeaturePolicy> WallFeaturePolicies { get; init; } = [];
}

/// <summary>Topology requirements for one connection-bearing socket type.</summary>
public sealed record CeramicConnectionPolicy
{
	[System.Text.Json.Serialization.JsonConstructor]
	public CeramicConnectionPolicy(
		string socketType,
		CeramicCountRange degree,
		CeramicCountRange componentCount,
		CeramicCountRange externalConnectionCount,
		bool requireEntranceReachability = false)
	{
		SocketType = socketType;
		Degree = degree;
		ComponentCount = componentCount;
		ExternalConnectionCount = externalConnectionCount;
		RequireEntranceReachability = requireEntranceReachability;
	}

	/// <summary>Source-compatible constructor that converts the legacy exact fields.</summary>
	public CeramicConnectionPolicy(
		string SocketType,
		int RequiredDegree,
		int? RequiredComponentCount = null,
		bool AllowExternalConnections = false)
		: this(
			SocketType,
			new CeramicCountRange(RequiredDegree, RequiredDegree),
			RequiredComponentCount.HasValue
				? new CeramicCountRange(RequiredComponentCount.Value, RequiredComponentCount.Value)
				: new CeramicCountRange(0, null),
			AllowExternalConnections
				? new CeramicCountRange(0, null)
				: new CeramicCountRange(0, 0))
	{
	}

	public string SocketType { get; init; }
	public CeramicCountRange Degree { get; init; }
	public CeramicCountRange ComponentCount { get; init; }
	public CeramicCountRange ExternalConnectionCount { get; init; }
	public bool RequireEntranceReachability { get; init; }

	/// <summary>Legacy compatibility value used by existing source-level validators.</summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public int RequiredDegree => Degree.Minimum;

	/// <summary>Legacy compatibility value; null indicates a non-exact component range.</summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public int? RequiredComponentCount => ComponentCount.Maximum == ComponentCount.Minimum
		? ComponentCount.Minimum
		: null;

	/// <summary>Legacy compatibility value derived from the canonical external range.</summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public bool AllowExternalConnections => ExternalConnectionCount.Maximum is null
		|| ExternalConnectionCount.Maximum.Value > 0;
}

/// <summary>An inclusive non-negative count range. A null maximum means unbounded.</summary>
public readonly record struct CeramicCountRange(
	int Minimum,
	int? Maximum)
{
	public bool Contains(int value) =>
		value >= Minimum && (!Maximum.HasValue || value <= Maximum.Value);
}

/// <summary>
/// Inclusive cell-count limits for one topology-declared tag in a generated region.
/// A cell declaring multiple tags contributes once to every matching quota. Additional
/// tags on the selected prefab do not retroactively satisfy topology quotas.
/// </summary>
public sealed record CeramicTagQuota(
	string Tag,
	int MinimumCells = 0,
	int? MaximumCells = null);

/// <summary>
/// Requires every component of one socket network to border cells carrying another
/// topology-declared tag. Each shared cell edge counts separately, including multiple
/// edges that touch the same adjacent cell. Additional prefab tags do not satisfy this
/// policy unless the topology declared them first.
/// </summary>
public sealed record CeramicComponentAdjacencyPolicy(
	string ComponentSocketType,
	string RequiredAdjacentTag,
	int MinimumAdjacentEdgesPerComponent = 1);

/// <summary>
/// Requires every component of one socket network to declare a bounded number of cells
/// carrying a tag. Selected-prefab tags that were not declared by topology do not count.
/// </summary>
public sealed record CeramicComponentTagPolicy(
	string ComponentSocketType,
	string RequiredTag,
	CeramicCountRange TagCountPerComponent);

/// <summary>
/// Describes entrances for a wall-network component made from wall-sharing room loops.
/// Each component has exactly one RootEntryTag bordering RootAdjacentTag. A shared
/// partition door carries both ParentDoorTag and ChildEntryTag on the same topology
/// cell. Every such door requires another independent graph cycle, proving that the
/// adjoining closed room exists.
/// </summary>
public sealed record CeramicComponentEntryPolicy(
	string ComponentSocketType,
	string RootEntryTag,
	string RootAdjacentTag,
	string ParentDoorTag,
	string ChildEntryTag,
	CeramicCountRange AdditionalRoomsPerRoot);

/// <summary>
/// Places a bounded number of tagged wall features on each component. When
/// OuterWallsOnly is true, the feature may appear on the building envelope but never
/// on a shared partition between rooms. When CellsPerFeature is set, the required
/// count is the ceiling of eligible wall cells divided by that density, clamped to
/// CountPerComponent. A null density retains bounded random selection.
/// </summary>
public sealed record CeramicWallFeaturePolicy(
	string ComponentSocketType,
	string FeatureTag,
	CeramicCountRange CountPerComponent,
	bool OuterWallsOnly = false,
	int? CellsPerFeature = null);

/// <summary>
/// An authored village module such as a road, field, wall, house corner, or door.
/// Every prefab in one catalog must use the same square X/Z footprint. Validation must
/// reject SizeX != SizeZ so every allowed rotation remains inside one grid cell.
/// </summary>
public interface ICeramicPrefab
{
	/// <summary>Stable, unique identifier used in saved generation plans.</summary>
	string Id { get; }

	/// <summary>
	/// Logical roles used by generation constraints. A prefab may have multiple tags,
	/// such as "gate", "road", and "defense-wall".
	/// </summary>
	IReadOnlyList<string> Tags { get; }

	/// <summary>Prefab dimensions in world blocks or other caller-defined units.</summary>
	int SizeX { get; }
	int SizeY { get; }
	int SizeZ { get; }

	/// <summary>Entities stored relative to the prefab's minimum corner.</summary>
	IReadOnlyList<CeramicEntity> Entities { get; }

	/// <summary>Exactly one socket for each horizontal direction.</summary>
	IReadOnlyList<CeramicSocket> Sockets { get; }

	/// <summary>Rotations from which the generator may create prefab variants.</summary>
	CeramicRotationOptions AllowedRotations { get; }

	/// <summary>Relative selection probability after all socket rules are satisfied.</summary>
	int Weight { get; }
}

/// <summary>
/// JSON-serializable prefab definition. Tags are stored as an array and must be
/// unique using ordinal comparison.
/// </summary>
public sealed record CeramicPrefabDefinition(
	string Id,
	IReadOnlyList<string> Tags,
	int SizeX,
	int SizeY,
	int SizeZ,
	IReadOnlyList<CeramicEntity> Entities,
	IReadOnlyList<CeramicSocket> Sockets,
	CeramicRotationOptions AllowedRotations,
	int Weight) : ICeramicPrefab;

/// <summary>
/// A caller-owned entity value and its local transform inside a prefab. A clockwise
/// 90-degree rotation uses newX = SizeZ - 1 - oldZ and newZ = oldX; Y is unchanged
/// and Rotation is composed with the prefab rotation.
/// </summary>
public sealed record CeramicEntity(
	int Value,
	int X,
	int Y,
	int Z,
	CeramicRotation Rotation = CeramicRotation.Rot0);

/// <summary>
/// A label on one horizontal prefab face. Equal types may be adjacent. Two
/// "no-connection" sockets may touch, but do not create a traversable connection.
/// </summary>
public sealed record CeramicSocket(CeramicDirection Direction, string Type)
{
	/// <summary>A face that may touch another no-connection face but never forms a connection.</summary>
	public const string NoConnection = "no-connection";

	/// <summary>Compatibility alias for call sites that have not migrated to NoConnection.</summary>
	public const string Closed = NoConnection;
}

// TODO(CeramicFish compatibility): Migrate call sites outside this file from Closed to
// NoConnection, then remove the Closed alias in a later format-breaking change.

/// <summary>Horizontal directions in clockwise order around the Y axis.</summary>
public enum CeramicDirection : byte
{
	North,
	East,
	South,
	West,
}

/// <summary>A single concrete rotation applied to a placed prefab.</summary>
public enum CeramicRotation : short
{
	Rot0 = 0,
	Rot90CW = 90,
	Rot180CW = 180,
	Rot270CW = 270,
}

/// <summary>The rotations permitted by a prefab definition.</summary>
[Flags]
public enum CeramicRotationOptions : byte
{
	None = 0,
	Rot0 = 1 << 0,
	Rot90CW = 1 << 1,
	Rot180CW = 1 << 2,
	Rot270CW = 1 << 3,
	All = Rot0 | Rot90CW | Rot180CW | Rot270CW,
}

/// <summary>
/// One active cell in the caller-defined generation grid. Coordinates may be
/// negative and do not need to start at zero.
/// </summary>
public readonly record struct CeramicCell(int X, int Z);

/// <summary>
/// Compatibility form of the original single mandatory anchor. New callers should
/// use CeramicAnchor, CeramicSocketConstraint, and TopologyRoot separately.
/// </summary>
public sealed record CeramicStart(
	CeramicCell Cell,
	IReadOnlyList<string> RequiredTags,
	string RequiredSocketType,
	CeramicDirection ConnectionDirection);

/// <summary>A hard requirement for topology-declared tags at one active cell.</summary>
public sealed record CeramicAnchor(
	CeramicCell Cell,
	IReadOnlyList<string> RequiredTags);

/// <summary>A hard directional socket requirement at one active cell.</summary>
public sealed record CeramicSocketConstraint(
	CeramicCell Cell,
	CeramicDirection Direction,
	string SocketType);

/// <summary>Restricts the prefab variant selected at one generation cell.</summary>
public sealed record CeramicCellConstraint(
	CeramicCell Cell,
	IReadOnlyList<string> RequiredTags,
	IReadOnlyList<string> ForbiddenTags,
	string? RequiredPrefabId = null,
	CeramicRotation? RequiredRotation = null);

/// <summary>A complete deterministic request for one CeramicFish generation run.</summary>
public sealed record CeramicGenerationRequest
{
	/// <summary>Creates a request without mandatory anchors or socket constraints.</summary>
	public CeramicGenerationRequest(IReadOnlyCollection<CeramicCell> region, int seed)
	{
		Region = region;
		Seed = seed;
	}

	/// <summary>
	/// Source-compatible constructor that converts the original start into one hard
	/// anchor, one hard socket constraint, and the topology search root.
	/// </summary>
	public CeramicGenerationRequest(
		IReadOnlyCollection<CeramicCell> region,
		CeramicStart start,
		int seed)
		: this(region, seed)
	{
		ArgumentNullException.ThrowIfNull(start);
		Anchors = [new(start.Cell, start.RequiredTags)];
		SocketConstraints = [new(start.Cell, start.ConnectionDirection, start.RequiredSocketType)];
		TopologyRoot = start.Cell;
	}

	/// <summary>
	/// Unique active grid cells to fill. The region may be irregular, concave, and may
	/// contain enclosed holes, but must form one four-directionally connected component.
	/// Every edge surrounding a hole follows the same boundary and entrance rules as
	/// the outer boundary.
	/// </summary>
	public IReadOnlyCollection<CeramicCell> Region { get; init; }

	/// <summary>Deterministic seed interpreted together with the generator version.</summary>
	public int Seed { get; init; }

	/// <summary>Hard topology-tag requirements. Multiple anchors may share a cell.</summary>
	public IReadOnlyList<CeramicAnchor> Anchors { get; init; } = [];

	/// <summary>Hard directional socket requirements at arbitrary active cells.</summary>
	public IReadOnlyList<CeramicSocketConstraint> SocketConstraints { get; init; } = [];

	/// <summary>
	/// Optional topology-search root. Changing only this value may change search order
	/// and performance, but must never change which layouts are valid.
	/// </summary>
	public CeramicCell? TopologyRoot { get; init; }

	/// <summary>
	/// Socket required on every edge from a region cell to a cell outside the region,
	/// except for edges explicitly listed in Entrances.
	/// </summary>
	public string BoundarySocket { get; init; } = CeramicSocket.NoConnection;

	/// <summary>Required connections from the generated village to the outside world.</summary>
	public IReadOnlyList<CeramicEntrance> Entrances { get; init; } = [];

	/// <summary>Optional prefab and tag restrictions for individual region cells.</summary>
	public IReadOnlyList<CeramicCellConstraint> CellConstraints { get; init; } = [];

	/// <summary>Optional inclusive cell-count requirements for topology-declared tags.</summary>
	public IReadOnlyList<CeramicTagQuota> TagQuotas { get; init; } = [];

	/// <summary>
	/// Maximum complete topology-plus-placement attempts before generation reports
	/// failure. Attempt ordinals start at zero and derive deterministic retry seeds.
	/// </summary>
	public int MaxAttempts { get; init; } = 8;

	/// <summary>Maximum topology checks permitted during each complete solve attempt.</summary>
	public long MaxTopologyChecks { get; init; } = 1_000_000;

	/// <summary>Maximum placement propagation checks permitted during each complete solve attempt.</summary>
	public long MaxPropagationChecks { get; init; } = 1_000_000;
}

/// <summary>
/// A required outward-facing socket on a boundary cell, typically the access road.
/// </summary>
public sealed record CeramicEntrance(
	CeramicCell Cell,
	CeramicDirection Direction,
	string SocketType);

/// <summary>A selected prefab variant at one village-grid cell.</summary>
public sealed record CeramicPlacement(
	string PrefabId,
	CeramicCell Cell,
	CeramicRotation Rotation);

/// <summary>
/// One required socket on a cell in a topology-first generation plan. IsExternal may
/// be true only when this cell, direction, and socket type exactly match a declared
/// entrance.
/// </summary>
public sealed record CeramicTopologySocket(
	CeramicDirection Direction,
	string SocketType,
	bool IsExternal = false);

/// <summary>
/// The minimum required topology tags and complete directional socket assignment that
/// a later placement solver must realize at one active cell. Topology policies inspect
/// only these declared tags. A selected prefab may contain additional tags unless the
/// generation request forbids them, but those additional tags never retroactively
/// satisfy topology policies or quotas. Sockets must contain exactly one entry for
/// each horizontal direction, including explicit no-connection entries.
/// </summary>
public sealed record CeramicTopologyCell(
	CeramicCell Cell,
	IReadOnlyList<string> Tags,
	IReadOnlyList<CeramicTopologySocket> Sockets);

/// <summary>Terminal state of one topology attempt before prefab selection.</summary>
public enum CeramicTopologyAttemptStatus : byte
{
	Success,
	Contradiction,
	Unsatisfiable,
	BudgetExceeded,
}

/// <summary>
/// The serializable output of one topology attempt. Successful results contain every
/// active request-region cell exactly once. Checks covers only this attempt.
/// </summary>
public sealed record CeramicTopologyAttemptResult(
	CeramicTopologyAttemptStatus Status,
	IReadOnlyList<CeramicTopologyCell> Cells,
	long Checks,
	CeramicGenerationFailure? Failure = null)
{
	public bool Success => Status == CeramicTopologyAttemptStatus.Success;
}

/// <summary>Terminal state of one concrete prefab-placement attempt.</summary>
public enum CeramicPlacementAttemptStatus : byte
{
	Success,
	Contradiction,
	Unsatisfiable,
	BudgetExceeded,
}

/// <summary>
/// The serializable output of one placement attempt. Checks covers only this attempt.
/// </summary>
public sealed record CeramicPlacementAttemptResult(
	CeramicPlacementAttemptStatus Status,
	IReadOnlyList<CeramicPlacement> Placements,
	long Checks,
	CeramicGenerationFailure? Failure = null)
{
	public bool Success => Status == CeramicPlacementAttemptStatus.Success;
}

/// <summary>Terminal state of a generation request.</summary>
public enum CeramicGenerationStatus : byte
{
	Success,
	InvalidRequest,
	InvalidCatalog,
	Unsatisfiable,
	AttemptsExhausted,
	BudgetExceeded,
}

/// <summary>The generation phase responsible for a structured failure.</summary>
public enum CeramicGenerationStage : byte
{
	DefinitionValidation,
	RequestValidation,
	Topology,
	Placement,
}

/// <summary>Structured information about a failed generation request.</summary>
public sealed record CeramicGenerationFailure(
	string Code,
	string Message,
	CeramicCell? Cell = null,
	CeramicGenerationStage? Stage = null,
	string? SocketType = null,
	CeramicDirection? Direction = null,
	int? Attempt = null,
	IReadOnlyDictionary<string, string>? Data = null);

/// <summary>Identity required to reproduce or diagnose one generated result.</summary>
public sealed record CeramicGenerationMetadata(
	string DefinitionId,
	int DefinitionFormatVersion,
	int GeneratorVersion,
	int Seed);

/// <summary>
/// The aggregate serializable output of village generation. Attempts is the number of
/// complete topology-plus-placement cycles started. TopologyChecks and
/// PropagationChecks are totals across every attempt. Contradiction and per-attempt
/// budget exhaustion are retryable; proven Unsatisfiable and cancellation are terminal.
/// AttemptsExhausted means retryable attempts ended without a global proof.
/// </summary>
public sealed record CeramicGenerationResult(
	CeramicGenerationStatus Status,
	IReadOnlyList<CeramicPlacement> Placements,
	int Attempts,
	long TopologyChecks,
	long PropagationChecks,
	CeramicGenerationMetadata Metadata,
	CeramicGenerationFailure? Failure = null)
{
	public bool Success => Status == CeramicGenerationStatus.Success;
}

/// <summary>One machine-readable definition or request validation problem.</summary>
public sealed record CeramicValidationError(
	string Code,
	string Message,
	string? Path = null,
	CeramicCell? Cell = null);

/// <summary>Problems found while checking a definition or generation request.</summary>
public sealed record CeramicValidationResult(
	IReadOnlyList<CeramicValidationError> Errors)
{
	public bool IsValid => Errors.Count == 0;
}

/// <summary>Thrown when a serialized CeramicFish definition fails structural validation.</summary>
public sealed class CeramicDefinitionException : Exception
{
	public CeramicDefinitionException(
		string message,
		IReadOnlyList<CeramicValidationError> errors,
		Exception? innerException = null)
		: base(message, innerException)
	{
		Errors = errors;
	}

	public IReadOnlyList<CeramicValidationError> Errors { get; }
}
