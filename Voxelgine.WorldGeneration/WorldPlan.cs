namespace Voxelgine.WorldGeneration;

public enum WorldBiome : byte
{
	Void,
	Grassland,
	Forest,
	Sand,
	Rocky,
	Wetland,
}

public enum WorldFeatureKind : byte
{
	Road,
	Conduit,
}

public enum WorldStructureRole : byte
{
	Shelter,
	Relay,
	GravityAnchor,
	Shaft,
	Support,
}

public readonly record struct PlanPoint(int X, int Z);
public readonly record struct PlanPoint3(int X, int Y, int Z);
public readonly record struct PlanBounds(int MinimumX, int MinimumZ, int MaximumX, int MaximumZ)
{
	public bool Contains(int x, int z) => x >= MinimumX && x <= MaximumX && z >= MinimumZ && z <= MaximumZ;
	public bool Intersects(PlanBounds other) => MinimumX <= other.MaximumX && MaximumX >= other.MinimumX
		&& MinimumZ <= other.MaximumZ && MaximumZ >= other.MinimumZ;
}

public sealed record WorldGenerationSettings(
	int Seed,
	int Width = 1024,
	int Length = 1024,
	int WorldHeight = 64,
	int ProfileVersion = WorldPlan.CurrentGeneratorVersion)
{
	public void Validate()
	{
		if (Width <= 0 || Length <= 0 || Width > 4096 || Length > 4096)
			throw new ArgumentOutOfRangeException(nameof(Width), "World dimensions must be between 1 and 4096.");
		if (WorldHeight is < 16 or > 64)
			throw new ArgumentOutOfRangeException(nameof(WorldHeight), "Generation profile 1 requires a world height between 16 and 64.");
		if (ProfileVersion != WorldPlan.CurrentGeneratorVersion)
			throw new NotSupportedException($"Generation profile {ProfileVersion} is unsupported.");
	}
}

public sealed record StructureConnectorDescriptor(string Id, WorldFeatureKind Kind, int X, int Z, int DirectionX, int DirectionZ);

public sealed record StructureTemplateDescriptor(
	string Id,
	WorldStructureRole Role,
	int Width,
	int Length,
	int AnchorX,
	int AnchorZ,
	int[] AllowedRotations,
	StructureConnectorDescriptor[] Connectors);

public sealed record PlannedWorldSite(
	string Id,
	string TemplateId,
	WorldStructureRole Role,
	PlanPoint3 Origin,
	int Rotation,
	PlanBounds Reservation,
	bool EmergencyFallback);

public sealed record PlannedWorldRoute(
	string Id,
	WorldFeatureKind Kind,
	string SourceSite,
	string DestinationSite,
	PlanPoint3[] Cells);

public sealed record PlannedPond(int WaterLevel, PlanPoint3[] Cells);

public sealed record PlannedTree(int X, int Z, int SurfaceY, byte Variant);

public sealed class WorldPlan
{
	public const int CurrentFormatVersion = 1;
	public const int CurrentGeneratorVersion = 1;
	public const int CurrentMaterializerVersion = 1;

	private readonly byte[] heights;
	private readonly byte[] biomes;
	private readonly byte[] treeDensity;
	private readonly byte[] islandMask;

	public WorldPlan(
		WorldGenerationSettings settings,
		ReadOnlySpan<byte> heights,
		ReadOnlySpan<byte> biomes,
		ReadOnlySpan<byte> treeDensity,
		ReadOnlySpan<byte> islandMask,
		IEnumerable<PlannedPond>? ponds = null,
		IEnumerable<PlannedWorldSite>? sites = null,
		IEnumerable<PlannedWorldRoute>? routes = null,
		string structureCatalogHash = "")
	{
		Settings = settings ?? throw new ArgumentNullException(nameof(settings));
		settings.Validate();
		int count = checked(settings.Width * settings.Length);
		if (heights.Length != count || biomes.Length != count || treeDensity.Length != count || islandMask.Length != count)
			throw new ArgumentException("Every world-plan raster must match the configured dimensions.");
		this.heights = heights.ToArray();
		this.biomes = biomes.ToArray();
		this.treeDensity = treeDensity.ToArray();
		this.islandMask = islandMask.ToArray();
		Ponds = (ponds ?? []).Select(static pond => pond with { Cells = pond.Cells.ToArray() }).ToArray();
		Sites = (sites ?? []).ToArray();
		Routes = (routes ?? []).Select(static route => route with { Cells = route.Cells.ToArray() }).ToArray();
		StructureCatalogHash = structureCatalogHash ?? string.Empty;
		Validate();
	}

	public WorldGenerationSettings Settings { get; }
	public int Width => Settings.Width;
	public int Length => Settings.Length;
	public int WorldHeight => Settings.WorldHeight;
	public int Seed => Settings.Seed;
	public ReadOnlyMemory<byte> Heights => heights;
	public ReadOnlyMemory<byte> Biomes => biomes;
	public ReadOnlyMemory<byte> TreeDensity => treeDensity;
	public ReadOnlyMemory<byte> IslandMask => islandMask;
	public IReadOnlyList<PlannedPond> Ponds { get; }
	public IReadOnlyList<PlannedWorldSite> Sites { get; }
	public IReadOnlyList<PlannedWorldRoute> Routes { get; }
	public string StructureCatalogHash { get; }

	public int Index(int x, int z)
	{
		if ((uint)x >= (uint)Width || (uint)z >= (uint)Length)
			throw new ArgumentOutOfRangeException(nameof(x));
		return x * Length + z;
	}

	public bool IsLand(int x, int z) => islandMask[Index(x, z)] != 0;
	public byte GetHeight(int x, int z) => heights[Index(x, z)];
	public WorldBiome GetBiome(int x, int z) => (WorldBiome)biomes[Index(x, z)];
	public byte GetTreeDensity(int x, int z) => treeDensity[Index(x, z)];

	public void Validate()
	{
		for (int index = 0; index < heights.Length; index++)
		{
			bool land = islandMask[index] != 0;
			WorldBiome biome = (WorldBiome)biomes[index];
			if (!Enum.IsDefined(biome)) throw new InvalidDataException($"Unknown biome value {biomes[index]} at raster index {index}.");
			if (!land && (biome != WorldBiome.Void || treeDensity[index] != 0))
				throw new InvalidDataException($"Void raster cell {index} contains biome or tree data.");
			if (land && (biome == WorldBiome.Void || heights[index] >= WorldHeight))
				throw new InvalidDataException($"Land raster cell {index} is invalid.");
		}
		foreach (PlannedWorldSite site in Sites)
			if (string.IsNullOrWhiteSpace(site.Id) || string.IsNullOrWhiteSpace(site.TemplateId)
				|| !Enum.IsDefined(site.Role) || site.Rotation is not (0 or 90 or 180 or 270)
				|| site.Reservation.MinimumX > site.Reservation.MaximumX || site.Reservation.MinimumZ > site.Reservation.MaximumZ
				|| site.Reservation.MinimumX < 0 || site.Reservation.MinimumZ < 0
				|| site.Reservation.MaximumX >= Width || site.Reservation.MaximumZ >= Length
				|| !site.Reservation.Contains(site.Origin.X, site.Origin.Z)
				|| !IsLand(site.Origin.X, site.Origin.Z))
				throw new InvalidDataException("World-plan sites require stable IDs and template IDs.");
		if (Sites.Select(site => site.Id).Distinct(StringComparer.Ordinal).Count() != Sites.Count)
			throw new InvalidDataException("World-plan site IDs must be unique.");
		foreach (PlannedWorldRoute route in Routes)
			if (string.IsNullOrWhiteSpace(route.Id) || !Enum.IsDefined(route.Kind) || route.Cells.Length == 0
				|| route.Cells.Any(cell => (uint)cell.X >= (uint)Width || (uint)cell.Z >= (uint)Length
					|| cell.Y != GetHeight(cell.X, cell.Z) || !IsLand(cell.X, cell.Z))
				|| route.Cells.Zip(route.Cells.Skip(1)).Any(pair => Math.Abs(pair.First.X - pair.Second.X) + Math.Abs(pair.First.Z - pair.Second.Z) != 1))
				throw new InvalidDataException($"Route '{route.Id}' leaves the world bounds.");
		HashSet<string> siteIds = Sites.Select(site => site.Id).ToHashSet(StringComparer.Ordinal);
		if (Routes.Select(route => route.Id).Distinct(StringComparer.Ordinal).Count() != Routes.Count
			|| Routes.Any(route => !siteIds.Contains(route.SourceSite) || !siteIds.Contains(route.DestinationSite)))
			throw new InvalidDataException("World-plan routes require unique IDs and resolved sites.");
		HashSet<PlanPoint> acceptedPondCells = [];
		foreach (PlannedPond pond in Ponds)
		{
			if (pond.WaterLevel is < 0 or >= 256 || pond.Cells.Length < 24) throw new InvalidDataException("World-plan pond record is invalid.");
			foreach (PlanPoint3 cell in pond.Cells)
				if ((uint)cell.X >= (uint)Width || (uint)cell.Z >= (uint)Length || !IsLand(cell.X, cell.Z)
					|| cell.Y != GetHeight(cell.X, cell.Z) || pond.WaterLevel - cell.Y is < 1 or > 4
					|| !acceptedPondCells.Add(new(cell.X, cell.Z)))
					throw new InvalidDataException("World-plan pond cell is invalid.");
		}
		if (StructureCatalogHash.Length != 0 && (StructureCatalogHash.Length != 64 || !StructureCatalogHash.All(Uri.IsHexDigit)))
			throw new InvalidDataException("World-plan structure catalog hash is malformed.");
	}
}
