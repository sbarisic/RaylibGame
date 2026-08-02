namespace Voxelgine.Engine.World.Structures;

public readonly record struct GeneratedSiteId(string Value) : IComparable<GeneratedSiteId>
{
	public int CompareTo(GeneratedSiteId other) => string.Compare(Value, other.Value, StringComparison.Ordinal);
	public override string ToString() => Value;
}

public readonly record struct GeneratedMarkerId(GeneratedSiteId Site, string BlueprintMarkerId);

public readonly record struct StructureBounds(BlockCoordinate Minimum, BlockCoordinate Maximum)
{
	public bool Intersects(in StructureBounds other) =>
		Minimum.X <= other.Maximum.X && Maximum.X >= other.Minimum.X &&
		Minimum.Y <= other.Maximum.Y && Maximum.Y >= other.Minimum.Y &&
		Minimum.Z <= other.Maximum.Z && Maximum.Z >= other.Minimum.Z;

	public bool Contains(BlockCoordinate coordinate) =>
		coordinate.X >= Minimum.X && coordinate.X <= Maximum.X &&
		coordinate.Y >= Minimum.Y && coordinate.Y <= Maximum.Y &&
		coordinate.Z >= Minimum.Z && coordinate.Z <= Maximum.Z;

	public bool ContainsHorizontal(int x, int z) =>
		x >= Minimum.X && x <= Maximum.X &&
		z >= Minimum.Z && z <= Maximum.Z;
}

public readonly record struct PlannedMarker(
	GeneratedMarkerId Id,
	StructureMarkerKind Kind,
	BlockCoordinate Position,
	BlockType? ExpectedBlock,
	string Data);

public readonly record struct PlannedConnector(
	GeneratedSiteId Site,
	string Id,
	StructureConnectorKind Kind,
	BlockCoordinate Position,
	BlockCoordinate Direction);

public sealed record PlannedSite(
	GeneratedSiteId Id,
	StructureRole Role,
	string BlueprintId,
	BlockCoordinate Origin,
	int Rotation,
	StructureBounds Reservation,
	bool EmergencyFallback,
	StructureBounds ModificationBounds,
	PlannedMarker[] Markers,
	PlannedConnector[] Connectors);

public readonly record struct PlannedRoute(
	string Id,
	StructureConnectorKind Kind,
	GeneratedSiteId SourceSite,
	string SourceConnector,
	GeneratedSiteId DestinationSite,
	string DestinationConnector,
	BlockCoordinate[] Cells);

public sealed class WorldFeaturePlan
{
	public static WorldFeaturePlan Empty { get; } = new(
		Array.Empty<PlannedSite>(),
		Array.Empty<PlannedRoute>());

	public WorldFeaturePlan(PlannedSite[] sites, PlannedRoute[] routes)
	{
		Sites = sites ?? throw new ArgumentNullException(nameof(sites));
		Routes = routes ?? throw new ArgumentNullException(nameof(routes));
	}

	public IReadOnlyList<PlannedSite> Sites { get; }
	public IReadOnlyList<PlannedRoute> Routes { get; }

	public IEnumerable<PlannedMarker> Markers => Sites.SelectMany(static site => site.Markers);

	public PlannedMarker? FindFirstMarker(StructureMarkerKind kind) =>
		Markers.Select(static marker => (PlannedMarker?)marker).FirstOrDefault(marker => marker.Value.Kind == kind);
}

public sealed record StructurePlanningDiagnostic(
	GeneratedSiteId Site,
	string BlueprintId,
	bool UsedEmergencyFallback,
	string[] RejectedReasons,
	StructureBounds ModificationBounds);

public sealed class WorldFeatureGenerationResult
{
	public WorldFeatureGenerationResult(
		WorldFeaturePlan plan,
		StructurePlanningDiagnostic[] diagnostics,
		TimeSpan sitePlanningDuration,
		TimeSpan routeDuration)
	{
		Plan = plan;
		Diagnostics = diagnostics;
		SitePlanningDuration = sitePlanningDuration;
		RouteDuration = routeDuration;
	}

	public WorldFeaturePlan Plan { get; }
	public IReadOnlyList<StructurePlanningDiagnostic> Diagnostics { get; }
	public TimeSpan SitePlanningDuration { get; }
	public TimeSpan RouteDuration { get; }
}

public readonly record struct StructureGenerationTimings(
	TimeSpan SitePlanning,
	TimeSpan Routes,
	TimeSpan Stamping);
