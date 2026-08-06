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

public enum HydrologyKind : byte
{
	Pond,
	Lake,
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

public sealed record PlannedPond(int WaterLevel, PlanPoint3[] Cells, HydrologyKind Kind = HydrologyKind.Pond);

public sealed record PlannedTree(int X, int Z, int SurfaceY, byte Variant);

public sealed record PlannedVillageArea(
	string Id,
	PlanBounds Reservation,
	byte SurfaceY,
	PlanPoint[] Footprint,
	PlanPoint3[] AccessRoadCells);

public sealed class WorldPlan
{
	public const int CurrentFormatVersion = 6;
	public const int CurrentGeneratorVersion = 14;
	public const int CurrentMaterializerVersion = 11;

	private readonly byte[] heights;
	private readonly byte[] biomes;
	private readonly byte[] treeDensity;
	private readonly byte[] islandMask;
	private readonly byte[] hillMask;

	public WorldPlan(
		WorldGenerationSettings settings,
		ReadOnlySpan<byte> heights,
		ReadOnlySpan<byte> biomes,
		ReadOnlySpan<byte> treeDensity,
		ReadOnlySpan<byte> islandMask,
		ReadOnlySpan<byte> hillMask,
		IEnumerable<PlannedPond>? ponds = null,
		IEnumerable<PlannedWorldSite>? sites = null,
		IEnumerable<PlannedWorldRoute>? routes = null,
		IEnumerable<PlannedVillageArea>? villages = null,
		string structureCatalogHash = "",
		IEnumerable<PlannedVillageLayout>? villageLayouts = null,
		string ceramicFishDefinitionHash = "",
		IEnumerable<PlannedVillageFailure>? villageFailures = null)
	{
		Settings = settings ?? throw new ArgumentNullException(nameof(settings));
		settings.Validate();
		int count = checked(settings.Width * settings.Length);
		if (heights.Length != count || biomes.Length != count || treeDensity.Length != count || islandMask.Length != count || hillMask.Length != count)
			throw new ArgumentException("Every world-plan raster must match the configured dimensions.");
		this.heights = heights.ToArray();
		this.biomes = biomes.ToArray();
		this.treeDensity = treeDensity.ToArray();
		this.islandMask = islandMask.ToArray();
		this.hillMask = hillMask.ToArray();
		Ponds = (ponds ?? []).Select(static pond => pond with { Cells = pond.Cells.ToArray() }).ToArray();
		Sites = (sites ?? []).ToArray();
		Routes = (routes ?? []).Select(static route => route with { Cells = route.Cells.ToArray() }).ToArray();
		Villages = (villages ?? []).Select(static village => village with
		{
			Footprint = village.Footprint.ToArray(),
			AccessRoadCells = village.AccessRoadCells.ToArray(),
		}).ToArray();
		VillageLayouts = (villageLayouts ?? []).Select(static layout => layout with
		{
			Placements = layout.Placements.ToArray(),
			GateRoadCells = layout.GateRoadCells.ToArray(),
		}).ToArray();
		VillageFailures = (villageFailures ?? []).ToArray();
		StructureCatalogHash = structureCatalogHash ?? string.Empty;
		CeramicFishDefinitionHash = ceramicFishDefinitionHash ?? string.Empty;
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
	public ReadOnlyMemory<byte> HillMask => hillMask;
	public IReadOnlyList<PlannedPond> Ponds { get; }
	public IReadOnlyList<PlannedWorldSite> Sites { get; }
	public IReadOnlyList<PlannedWorldRoute> Routes { get; }
	public IReadOnlyList<PlannedVillageArea> Villages { get; }
	public IReadOnlyList<PlannedVillageLayout> VillageLayouts { get; }
	public IReadOnlyList<PlannedVillageFailure> VillageFailures { get; }
	public string StructureCatalogHash { get; }
	public string CeramicFishDefinitionHash { get; }

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
	public byte GetHillHeight(int x, int z) => hillMask[Index(x, z)];

	public void Validate()
	{
		for (int index = 0; index < heights.Length; index++)
		{
			bool land = islandMask[index] != 0;
			WorldBiome biome = (WorldBiome)biomes[index];
			if (!Enum.IsDefined(biome)) throw new InvalidDataException($"Unknown biome value {biomes[index]} at raster index {index}.");
			if (!land && (biome != WorldBiome.Void || treeDensity[index] != 0 || hillMask[index] != 0))
				throw new InvalidDataException($"Void raster cell {index} contains biome or tree data.");
			if (land && (biome == WorldBiome.Void || heights[index] >= WorldHeight))
				throw new InvalidDataException($"Land raster cell {index} is invalid.");
			if (hillMask[index] > 15 || hillMask[index] > heights[index])
				throw new InvalidDataException($"Hill raster cell {index} has an invalid height contribution.");
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
		HashSet<PlanPoint> mainRoadCells = Routes.Where(route => route.Kind == WorldFeatureKind.Road)
			.SelectMany(route => route.Cells).Select(cell => new PlanPoint(cell.X, cell.Z)).ToHashSet();
		HashSet<string> villageIds = [];
		List<PlanBounds> villageBounds = [];
		foreach (PlannedVillageArea village in Villages)
		{
			HashSet<PlanPoint> footprint = village.Footprint.ToHashSet();
			if (string.IsNullOrWhiteSpace(village.Id) || !villageIds.Add(village.Id)
				|| village.Reservation.MinimumX < 0 || village.Reservation.MinimumZ < 0
				|| village.Reservation.MaximumX >= Width || village.Reservation.MaximumZ >= Length
				|| village.Reservation.MaximumX - village.Reservation.MinimumX + 1 < 16
				|| village.Reservation.MaximumZ - village.Reservation.MinimumZ + 1 < 16
				|| footprint.Count != village.Footprint.Length || footprint.Count < 192
				|| footprint.Any(point => !village.Reservation.Contains(point.X, point.Z))
				|| footprint.Min(point => point.X) != village.Reservation.MinimumX
				|| footprint.Max(point => point.X) != village.Reservation.MaximumX
				|| footprint.Min(point => point.Z) != village.Reservation.MinimumZ
				|| footprint.Max(point => point.Z) != village.Reservation.MaximumZ
				|| villageBounds.Any(bounds => bounds.Intersects(village.Reservation)))
				throw new InvalidDataException("World-plan village reservations are invalid.");
			int minimumHeight = int.MaxValue, maximumHeight = int.MinValue;
			foreach (PlanPoint point in footprint)
			{
				if (!IsLand(point.X, point.Z)) throw new InvalidDataException($"Village '{village.Id}' leaves the island.");
				int height = GetHeight(point.X, point.Z); minimumHeight = Math.Min(minimumHeight, height); maximumHeight = Math.Max(maximumHeight, height);
			}
			HashSet<PlanPoint> connected = [];
			Queue<PlanPoint> pending = new();
			pending.Enqueue(village.Footprint[0]); connected.Add(village.Footprint[0]);
			while (pending.TryDequeue(out PlanPoint point))
				foreach (PlanPoint neighbor in CardinalNeighbors(point))
					if (footprint.Contains(neighbor) && connected.Add(neighbor)) pending.Enqueue(neighbor);
			if (maximumHeight - minimumHeight > 1 || village.SurfaceY < minimumHeight || village.SurfaceY > maximumHeight
				|| connected.Count != footprint.Count
				|| village.AccessRoadCells.Length == 0
				|| !footprint.Contains(new(village.AccessRoadCells[0].X, village.AccessRoadCells[0].Z))
				|| !mainRoadCells.Contains(new(village.AccessRoadCells[^1].X, village.AccessRoadCells[^1].Z))
				|| village.AccessRoadCells.Any(cell => (uint)cell.X >= (uint)Width || (uint)cell.Z >= (uint)Length
					|| !IsLand(cell.X, cell.Z) || cell.Y != GetHeight(cell.X, cell.Z))
				|| village.AccessRoadCells.Zip(village.AccessRoadCells.Skip(1)).Any(pair => Math.Abs(pair.First.X - pair.Second.X) + Math.Abs(pair.First.Z - pair.Second.Z) != 1))
				throw new InvalidDataException($"Village '{village.Id}' is not a connected flat reservation.");
			villageBounds.Add(village.Reservation);
		}
		HashSet<PlanPoint> acceptedPondCells = [];
		foreach (PlannedPond pond in Ponds)
		{
			if (!Enum.IsDefined(pond.Kind) || pond.WaterLevel is < 0 or >= 256
				|| pond.Cells.Length < (pond.Kind == HydrologyKind.Lake ? 128 : 24)) throw new InvalidDataException("World-plan hydrology record is invalid.");
			foreach (PlanPoint3 cell in pond.Cells)
				if ((uint)cell.X >= (uint)Width || (uint)cell.Z >= (uint)Length || !IsLand(cell.X, cell.Z)
					|| cell.Y != GetHeight(cell.X, cell.Z) || pond.WaterLevel - cell.Y is < 1 or > 4
					|| !acceptedPondCells.Add(new(cell.X, cell.Z)))
					throw new InvalidDataException("World-plan pond cell is invalid.");
		}
		if (StructureCatalogHash.Length != 0 && (StructureCatalogHash.Length != 64 || !StructureCatalogHash.All(Uri.IsHexDigit)))
			throw new InvalidDataException("World-plan structure catalog hash is malformed.");
		if (CeramicFishDefinitionHash.Length != 0 && (CeramicFishDefinitionHash.Length != 64 || !CeramicFishDefinitionHash.All(Uri.IsHexDigit)))
			throw new InvalidDataException("World-plan CeramicFish definition hash is malformed.");
		ValidateVillageLayouts(villageIds);
		ValidateTreeExclusions();
	}

	private void ValidateVillageLayouts(HashSet<string> villageIds)
	{
		Dictionary<string, PlannedVillageArea> villages = Villages.ToDictionary(static village => village.Id, StringComparer.Ordinal);
		if (VillageLayouts.Select(static layout => layout.VillageId).Distinct(StringComparer.Ordinal).Count() != VillageLayouts.Count)
			throw new InvalidDataException("Village layout IDs must be unique.");
		foreach (PlannedVillageLayout layout in VillageLayouts)
		{
			if (!villageIds.Contains(layout.VillageId) || layout.Attempts is < 1 or > 64 || layout.Placements.Length == 0
				|| layout.TopologyChecks < 0 || layout.PropagationChecks < 0)
				throw new InvalidDataException($"Village layout '{layout.VillageId}' is invalid.");
			PlannedVillageArea village = villages[layout.VillageId];
			if (layout.GridOrigin.Y != village.SurfaceY)
				throw new InvalidDataException($"Village layout '{layout.VillageId}' has an invalid grid origin.");
			HashSet<CeramicCell> occupied = [];
			foreach (PlannedVillagePlacement placement in layout.Placements)
			{
				if (string.IsNullOrWhiteSpace(placement.PrefabId) || !Enum.IsDefined(placement.Rotation)
					|| !occupied.Add(placement.Cell))
					throw new InvalidDataException($"Village layout '{layout.VillageId}' contains an invalid placement.");
				int originX = layout.GridOrigin.X + placement.Cell.X * 3;
				int originZ = layout.GridOrigin.Z + placement.Cell.Z * 3;
				for (int x = originX; x < originX + 3; x++)
				for (int z = originZ; z < originZ + 3; z++)
					if (!village.Footprint.Contains(new PlanPoint(x, z)))
						throw new InvalidDataException($"Village layout '{layout.VillageId}' leaves its flattened footprint.");
			}
			if (layout.GateRoadCells.Length == 0
				|| layout.GateRoadCells[^1] != new PlanPoint(village.AccessRoadCells[0].X, village.AccessRoadCells[0].Z)
				|| layout.GateRoadCells.Any(cell => !village.Footprint.Contains(cell))
				|| layout.GateRoadCells.Zip(layout.GateRoadCells.Skip(1)).Any(pair =>
					Math.Abs(pair.First.X - pair.Second.X) + Math.Abs(pair.First.Z - pair.Second.Z) != 1))
				throw new InvalidDataException($"Village layout '{layout.VillageId}' has an invalid gate road.");
		}
		if (VillageFailures.Select(static failure => failure.VillageId).Distinct(StringComparer.Ordinal).Count() != VillageFailures.Count
			|| VillageFailures.Any(failure => !villageIds.Contains(failure.VillageId)
				|| VillageLayouts.Any(layout => layout.VillageId == failure.VillageId)
				|| string.IsNullOrWhiteSpace(failure.Code) || string.IsNullOrWhiteSpace(failure.Message)))
			throw new InvalidDataException("Village failure diagnostics must identify distinct empty village reservations.");
	}

	private void ValidateTreeExclusions()
	{
		HashSet<PlanPoint> excluded = Ponds.SelectMany(pond => pond.Cells).Select(cell => new PlanPoint(cell.X, cell.Z)).ToHashSet();
		foreach (PlannedWorldSite site in Sites)
			for (int x = site.Reservation.MinimumX; x <= site.Reservation.MaximumX; x++)
			for (int z = site.Reservation.MinimumZ; z <= site.Reservation.MaximumZ; z++) excluded.Add(new(x, z));
		foreach (PlannedWorldRoute route in Routes)
			foreach (PlanPoint3 cell in route.Cells) AddRoadWidth(excluded, cell.X, cell.Z);
		foreach (PlannedVillageArea village in Villages)
		{
			foreach (PlanPoint point in village.Footprint) excluded.Add(point);
			foreach (PlanPoint3 cell in village.AccessRoadCells) AddRoadWidth(excluded, cell.X, cell.Z);
		}
		foreach (PlannedVillageLayout layout in VillageLayouts)
			foreach (PlanPoint cell in layout.GateRoadCells) AddRoadWidth(excluded, cell.X, cell.Z);
		foreach (PlanPoint point in excluded)
			if ((uint)point.X < (uint)Width && (uint)point.Z < (uint)Length)
			{
				if (GetTreeDensity(point.X, point.Z) != 0)
					throw new InvalidDataException($"Tree density overlaps a reserved feature at ({point.X}, {point.Z}).");
				if (GetHillHeight(point.X, point.Z) != 0)
					throw new InvalidDataException($"A generated hill overlaps a reserved feature at ({point.X}, {point.Z}).");
			}
	}

	private static void AddRoadWidth(HashSet<PlanPoint> cells, int x, int z)
	{
		for (int offsetX = -1; offsetX <= 1; offsetX++)
		for (int offsetZ = -1; offsetZ <= 1; offsetZ++) cells.Add(new(x + offsetX, z + offsetZ));
	}

	private static IEnumerable<PlanPoint> CardinalNeighbors(PlanPoint point)
	{
		yield return new(point.X - 1, point.Z);
		yield return new(point.X + 1, point.Z);
		yield return new(point.X, point.Z - 1);
		yield return new(point.X, point.Z + 1);
	}
}
