namespace Voxelgine.WorldGeneration;

public sealed record PlannedVillagePlacement(
	string PrefabId,
	CeramicCell Cell,
	CeramicRotation Rotation);

public sealed record PlannedVillageLayout(
	string VillageId,
	PlanPoint3 GridOrigin,
	PlannedVillagePlacement[] Placements,
	PlanPoint[] GateRoadCells,
	int GenerationSeed,
	int Attempts,
	long TopologyChecks,
	long PropagationChecks);

public sealed record PlannedVillageFailure(
	string VillageId,
	string Code,
	string Message);

public sealed record CeramicVillagePreviewResult(
	PlannedVillageLayout? Layout,
	PlannedVillageFailure? Failure);

internal sealed record CeramicVillagePlanningResult(
	PlannedVillageLayout[] Layouts,
	PlannedVillageFailure[] Failures);

public static class CeramicVillagePlanner
{
	internal const int CellSize = 3;
	private const int CornerRun = 3;
	private const int MinimumWallSpan = 12;
	private const int CandidateCount = 3;
	private const int MinimumHouseWallPercentage = 15;
	private const int MaximumHouseWallPercentage = 33;

	public static CeramicVillagePreviewResult PlanPreview(
		CeramicFishDefinition definition,
		int seed,
		CancellationToken cancellationToken = default)
	{
		const int cells = 31;
		const int blocks = cells * CellSize;
		List<PlanPoint> footprint = [];
		double center = (blocks - 1) * 0.5;
		double radius = blocks * 0.47;
		for (int x = 0; x < blocks; x++)
		for (int z = 0; z < blocks; z++)
			if (Math.Pow(x - center, 2) + Math.Pow(z - center, 2) <= radius * radius)
				footprint.Add(new(x, z));
		PlanPoint access = footprint.OrderByDescending(static point => point.Z)
			.ThenBy(point => Math.Abs(point.X - center)).First();
		PlannedVillageArea village = new("preview", new(footprint.Min(static point => point.X),
			footprint.Min(static point => point.Z), footprint.Max(static point => point.X),
			footprint.Max(static point => point.Z)), 0, footprint.ToArray(), [new(access.X, 0, access.Z)]);
		CeramicVillagePlanningResult result = Plan(new(seed, blocks, blocks, 16), [village], definition, cancellationToken);
		return new(result.Layouts.FirstOrDefault(), result.Failures.FirstOrDefault());
	}

	internal static CeramicVillagePlanningResult Plan(
		WorldGenerationSettings settings,
		IReadOnlyList<PlannedVillageArea> villages,
		CeramicFishDefinition definition,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(definition);
		CeramicFish generator = new();
		CeramicValidationResult validation = generator.ValidateDefinition(definition);
		if (!validation.IsValid)
			throw new CeramicDefinitionException("The production CeramicFish definition is invalid.",
				validation.Errors);

		List<PlannedVillageLayout> layouts = [];
		List<PlannedVillageFailure> failures = [];
		foreach (PlannedVillageArea village in villages)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Grid grid = BuildGrid(village);
			CeramicGenerationFailure? finalFailure = null;
			bool generated = false;
			for (int candidateOrdinal = 0; candidateOrdinal < CandidateCount && !generated;
				candidateOrdinal++)
			{
				if (!TryBuildRequest(settings, village, grid, candidateOrdinal,
					out CeramicGenerationRequest? request, out PlanPoint3 gridOrigin,
					out PlanPoint[] gateRoadCells))
					continue;
				CeramicGenerationResult result = generator.Generate(request, definition,
					cancellationToken);
				if (!result.Success)
				{
					finalFailure = result.Failure;
					continue;
				}
				layouts.Add(new(village.Id, gridOrigin,
					result.Placements.Select(static placement => new PlannedVillagePlacement(
						placement.PrefabId, placement.Cell, placement.Rotation)).ToArray(),
					gateRoadCells, request.Seed, result.Attempts, result.TopologyChecks,
					result.PropagationChecks));
				generated = true;
			}
			if (!generated)
				failures.Add(new(village.Id, finalFailure?.Code ?? "village-region-unavailable",
					finalFailure?.Message
						?? "No valid 3x3 CeramicFish region and gate fit inside the village footprint."));
		}
		return new(layouts.ToArray(), failures.ToArray());
	}

	private static bool TryBuildRequest(
		WorldGenerationSettings settings,
		PlannedVillageArea village,
		Grid grid,
		int candidateOrdinal,
		out CeramicGenerationRequest request,
		out PlanPoint3 gridOrigin,
		out PlanPoint[] gateRoadCells)
	{
		request = null!;
		gridOrigin = default;
		gateRoadCells = [];
		if (grid.Active.Count == 0 || !TryBuildOuterWall(grid, candidateOrdinal,
			out List<CeramicCell> wall))
			return false;
		HashSet<CeramicCell> wallSet = wall.ToHashSet();
		if (wallSet.Count != wall.Count || !IsDegreeTwoCycle(wallSet)) return false;
		HashSet<CeramicCell> region = FillInside(wallSet);
		if (region.Count == 0 || region.Any(cell => !grid.Active.Contains(cell))) return false;
		if (!TrySelectGate(wall, wallSet, region, grid, village.AccessRoadCells[0],
			out CeramicCell gate, out CeramicDirection outward)) return false;
		CeramicDirection inward = CeramicGeometry.Opposite(outward);
		if (!TryBuildGateRoad(grid, region, gate, outward, village,
			out gateRoadCells)) return false;

		List<CeramicCellConstraint> constraints = new(region.Count);
		foreach (CeramicCell cell in region.OrderBy(static cell => cell.Z)
			.ThenBy(static cell => cell.X))
		{
			if (cell == gate)
				constraints.Add(new(cell, ["defense-wall", "gate"], []));
			else if (wallSet.Contains(cell))
				constraints.Add(new(cell, ["defense-wall"], ["gate"]));
			else
				constraints.Add(new(cell, [], ["defense-wall", "gate"]));
		}

		int seed = DeriveSeed(settings.Seed, village.Id, candidateOrdinal);
		request = new(region, new CeramicStart(gate, ["defense-wall", "gate"],
			"road", inward), seed)
		{
			BoundarySocket = CeramicSocket.NoConnection,
			Entrances = [new(gate, outward, "road")],
			CellConstraints = constraints,
			TagQuotas =
			[
				new("road", PercentageCeiling(region.Count, 6), PercentageFloor(region.Count, 12)),
				new("house-wall", PercentageCeiling(region.Count, MinimumHouseWallPercentage),
					PercentageFloor(region.Count, MaximumHouseWallPercentage)),
			],
		};
		gridOrigin = new(grid.OriginX, village.SurfaceY, grid.OriginZ);
		return true;
	}

	private static Grid BuildGrid(PlannedVillageArea village)
	{
		HashSet<PlanPoint> footprint = village.Footprint.ToHashSet();
		PlanPoint3 access = village.AccessRoadCells[0];
		List<Grid> candidates = [];
		for (int offsetX = 0; offsetX < CellSize; offsetX++)
		for (int offsetZ = 0; offsetZ < CellSize; offsetZ++)
		{
			int originX = village.Reservation.MinimumX + offsetX;
			int originZ = village.Reservation.MinimumZ + offsetZ;
			int width = (village.Reservation.MaximumX - originX + 1) / CellSize;
			int height = (village.Reservation.MaximumZ - originZ + 1) / CellSize;
			if (width <= 0 || height <= 0) continue;
			HashSet<CeramicCell> all = [];
			for (int z = 0; z < height; z++)
			for (int x = 0; x < width; x++)
			{
				int worldX = originX + x * CellSize;
				int worldZ = originZ + z * CellSize;
				bool contained = true;
				for (int dz = 0; dz < CellSize && contained; dz++)
				for (int dx = 0; dx < CellSize; dx++)
					if (!footprint.Contains(new(worldX + dx, worldZ + dz)))
					{
						contained = false;
						break;
					}
				if (contained) all.Add(new(x, z));
			}
			foreach (HashSet<CeramicCell> component in Components(all))
			{
				if (component.Count < MinimumWallSpan * MinimumWallSpan / 2) continue;
				long entryDistance = component.Min(cell =>
				{
					int centerX = originX + cell.X * CellSize + CellSize / 2;
					int centerZ = originZ + cell.Z * CellSize + CellSize / 2;
					long dx = centerX - access.X;
					long dz = centerZ - access.Z;
					return dx * dx + dz * dz;
				});
				candidates.Add(new(originX, originZ, width, height, component, entryDistance));
			}
		}
		return candidates.OrderByDescending(static candidate => candidate.Active.Count)
			.ThenBy(static candidate => candidate.EntryDistance)
			.ThenBy(static candidate => candidate.OriginZ)
			.ThenBy(static candidate => candidate.OriginX)
			.FirstOrDefault() ?? new(village.Reservation.MinimumX,
				village.Reservation.MinimumZ, 0, 0, [], long.MaxValue);
	}

	private static IEnumerable<HashSet<CeramicCell>> Components(HashSet<CeramicCell> cells)
	{
		HashSet<CeramicCell> remaining = cells.ToHashSet();
		while (remaining.Count != 0)
		{
			CeramicCell start = remaining.OrderBy(static cell => cell.Z)
				.ThenBy(static cell => cell.X).First();
			HashSet<CeramicCell> component = [start];
			Queue<CeramicCell> pending = new();
			pending.Enqueue(start);
			remaining.Remove(start);
			while (pending.TryDequeue(out CeramicCell cell))
				foreach (CeramicDirection direction in Enum.GetValues<CeramicDirection>())
				{
					CeramicCell neighbor = CeramicGeometry.Offset(cell, direction);
					if (remaining.Remove(neighbor))
					{
						component.Add(neighbor);
						pending.Enqueue(neighbor);
					}
				}
			yield return component;
		}
	}

	private static bool TryBuildOuterWall(
		Grid grid,
		int candidateOrdinal,
		out List<CeramicCell> wall)
	{
		wall = [];
		int centerX = (int)Math.Round(grid.Active.Average(static cell => cell.X));
		Dictionary<int, RowRun> rows = [];
		foreach (IGrouping<int, CeramicCell> row in grid.Active.GroupBy(static cell => cell.Z))
		{
			List<RowRun> runs = [];
			int[] values = row.Select(static cell => cell.X).Order().ToArray();
			int start = values[0], previous = values[0];
			for (int index = 1; index <= values.Length; index++)
			{
				if (index < values.Length && values[index] == previous + 1)
				{
					previous = values[index];
					continue;
				}
				runs.Add(new(start, previous));
				if (index < values.Length) start = previous = values[index];
			}
			RowRun[] centeredRuns = runs.Where(run => run.Left <= centerX && run.Right >= centerX)
				.OrderByDescending(static run => run.Width).ToArray();
			RowRun selected = centeredRuns.Length != 0
				? centeredRuns[0]
				: runs.OrderByDescending(static run => run.Width)
					.ThenBy(run => Math.Abs((run.Left + run.Right) / 2 - centerX)).First();
			rows[row.Key] = selected;
		}

		List<(int Top, int Bottom)> spans = [];
		int spanStart = -1, previousZ = int.MinValue;
		foreach ((int z, RowRun run) in rows.OrderBy(static pair => pair.Key))
		{
			bool eligible = run.Width >= MinimumWallSpan;
			if (!eligible || spanStart >= 0 && z != previousZ + 1)
			{
				if (spanStart >= 0) spans.Add((spanStart, previousZ));
				spanStart = -1;
			}
			if (eligible && spanStart < 0) spanStart = z;
			previousZ = z;
		}
		if (spanStart >= 0) spans.Add((spanStart, previousZ));
		int inset = candidateOrdinal * 2;
		var usableSpans = spans.Select(value => (Top: value.Top + inset, Bottom: value.Bottom - inset))
			.Where(value => value.Bottom - value.Top + 1 >= MinimumWallSpan)
			.OrderByDescending(value => Enumerable.Range(value.Top, value.Bottom - value.Top + 1)
				.Sum(z => rows[z].Width))
			.ThenBy(static value => value.Top).ToArray();
		if (usableSpans.Length == 0) return false;
		var span = usableSpans[0];

		List<WallBand> bands = [];
		for (int top = span.Top; top < span.Bottom;)
		{
			int bottom = Math.Min(span.Bottom, top + CornerRun);
			int left = Enumerable.Range(top, bottom - top + 1).Max(z => rows[z].Left) + inset;
			int right = Enumerable.Range(top, bottom - top + 1).Min(z => rows[z].Right) - inset;
			if (right - left + 1 < MinimumWallSpan) return false;
			bands.Add(new(top, bottom, left, right));
			top = bottom;
		}
		if (bands.Count < 2) return false;

		List<CeramicCell> vertices = [new(bands[0].Left, bands[0].Top),
			new(bands[0].Right, bands[0].Top)];
		for (int index = 0; index < bands.Count; index++)
		{
			WallBand band = bands[index];
			vertices.Add(new(band.Right, band.Bottom));
			if (index + 1 < bands.Count)
				vertices.Add(new(bands[index + 1].Right, band.Bottom));
		}
		vertices.Add(new(bands[^1].Left, bands[^1].Bottom));
		for (int index = bands.Count - 1; index >= 0; index--)
		{
			WallBand band = bands[index];
			vertices.Add(new(band.Left, band.Top));
			if (index > 0) vertices.Add(new(bands[index - 1].Left, band.Top));
		}

		CeramicCell first = vertices[0];
		wall.Add(first);
		CeramicCell current = first;
		foreach (CeramicCell target in vertices.Skip(1).Append(first))
		{
			if (target == current) continue;
			CeramicDirection direction = DirectionBetween(current, target);
			while (current != target)
			{
				current = CeramicGeometry.Offset(current, direction);
				if (current != first) wall.Add(current);
			}
		}
		if (wall.Any(cell => !grid.Active.Contains(cell))) return false;
		return true;
	}

	private static HashSet<CeramicCell> FillInside(HashSet<CeramicCell> wall)
	{
		int minimumX = wall.Min(static cell => cell.X) - 1;
		int maximumX = wall.Max(static cell => cell.X) + 1;
		int minimumZ = wall.Min(static cell => cell.Z) - 1;
		int maximumZ = wall.Max(static cell => cell.Z) + 1;
		HashSet<CeramicCell> exterior = [];
		Queue<CeramicCell> pending = new();
		CeramicCell start = new(minimumX, minimumZ);
		exterior.Add(start);
		pending.Enqueue(start);
		while (pending.TryDequeue(out CeramicCell cell))
			foreach (CeramicDirection direction in Enum.GetValues<CeramicDirection>())
			{
				CeramicCell neighbor = CeramicGeometry.Offset(cell, direction);
				if (neighbor.X < minimumX || neighbor.X > maximumX
					|| neighbor.Z < minimumZ || neighbor.Z > maximumZ
					|| wall.Contains(neighbor) || !exterior.Add(neighbor)) continue;
				pending.Enqueue(neighbor);
			}
		HashSet<CeramicCell> region = wall.ToHashSet();
		for (int z = minimumZ + 1; z < maximumZ; z++)
		for (int x = minimumX + 1; x < maximumX; x++)
		{
			CeramicCell cell = new(x, z);
			if (!exterior.Contains(cell)) region.Add(cell);
		}
		return region;
	}

	private static bool TrySelectGate(
		IReadOnlyList<CeramicCell> orderedWall,
		HashSet<CeramicCell> wall,
		HashSet<CeramicCell> region,
		Grid grid,
		PlanPoint3 access,
		out CeramicCell gate,
		out CeramicDirection outward)
	{
		List<(CeramicCell Cell, CeramicDirection Outward, long Distance)> candidates = [];
		foreach (CeramicCell cell in orderedWall)
		{
			CeramicDirection[] wallDirections = Enum.GetValues<CeramicDirection>()
				.Where(direction => wall.Contains(CeramicGeometry.Offset(cell, direction))).ToArray();
			if (wallDirections.Length != 2
				|| CeramicGeometry.Opposite(wallDirections[0]) != wallDirections[1]) continue;
			foreach (CeramicDirection direction in Enum.GetValues<CeramicDirection>())
			{
				if (region.Contains(CeramicGeometry.Offset(cell, direction))
					|| !region.Contains(CeramicGeometry.Offset(cell,
						CeramicGeometry.Opposite(direction)))) continue;
				int worldX = grid.OriginX + cell.X * CellSize + CellSize / 2;
				int worldZ = grid.OriginZ + cell.Z * CellSize + CellSize / 2;
				long dx = worldX - access.X;
				long dz = worldZ - access.Z;
				candidates.Add((cell, direction, dx * dx + dz * dz));
			}
		}
		if (candidates.Count == 0)
		{
			gate = default;
			outward = default;
			return false;
		}
		var selected = candidates.OrderBy(static candidate => candidate.Distance)
			.ThenBy(static candidate => candidate.Cell.Z)
			.ThenBy(static candidate => candidate.Cell.X)
			.ThenBy(static candidate => candidate.Outward).First();
		gate = selected.Cell;
		outward = selected.Outward;
		return true;
	}

	private static bool TryBuildGateRoad(
		Grid grid,
		HashSet<CeramicCell> region,
		CeramicCell gate,
		CeramicDirection outward,
		PlannedVillageArea village,
		out PlanPoint[] road)
	{
		HashSet<PlanPoint> footprint = village.Footprint.ToHashSet();
		HashSet<PlanPoint> blocked = [];
		foreach (CeramicCell cell in region)
		{
			int originX = grid.OriginX + cell.X * CellSize;
			int originZ = grid.OriginZ + cell.Z * CellSize;
			for (int dz = 0; dz < CellSize; dz++)
			for (int dx = 0; dx < CellSize; dx++) blocked.Add(new(originX + dx, originZ + dz));
		}
		int gateCenterX = grid.OriginX + gate.X * CellSize + CellSize / 2;
		int gateCenterZ = grid.OriginZ + gate.Z * CellSize + CellSize / 2;
		(int outwardX, int outwardZ) = DirectionOffset(outward);
		PlanPoint start = new(gateCenterX + outwardX, gateCenterZ + outwardZ);
		PlanPoint target = new(village.AccessRoadCells[0].X, village.AccessRoadCells[0].Z);
		blocked.Remove(start);
		blocked.Remove(target);
		if (!footprint.Contains(start) || !footprint.Contains(target))
		{
			road = [];
			return false;
		}

		Dictionary<PlanPoint, PlanPoint> parents = [];
		HashSet<PlanPoint> reached = [start];
		Queue<PlanPoint> pending = new();
		pending.Enqueue(start);
		while (pending.TryDequeue(out PlanPoint point) && !reached.Contains(target))
			foreach ((int dx, int dz) in new[] { (0, -1), (1, 0), (0, 1), (-1, 0) })
			{
				PlanPoint neighbor = new(point.X + dx, point.Z + dz);
				if (!footprint.Contains(neighbor) || blocked.Contains(neighbor)
					|| !reached.Add(neighbor)) continue;
				parents[neighbor] = point;
				pending.Enqueue(neighbor);
			}
		if (!reached.Contains(target))
		{
			road = [];
			return false;
		}
		List<PlanPoint> reversed = [target];
		for (PlanPoint current = target; current != start;)
		{
			current = parents[current];
			reversed.Add(current);
		}
		reversed.Reverse();
		road = reversed.ToArray();
		return true;
	}

	private static bool IsDegreeTwoCycle(HashSet<CeramicCell> wall) =>
		wall.Count >= 4 && wall.All(cell => Enum.GetValues<CeramicDirection>()
			.Count(direction => wall.Contains(CeramicGeometry.Offset(cell, direction))) == 2);

	private static CeramicDirection DirectionBetween(CeramicCell first, CeramicCell second)
	{
		if (first.X != second.X && first.Z != second.Z)
			throw new InvalidDataException("CeramicFish wall vertices must be axis aligned.");
		if (second.X > first.X) return CeramicDirection.East;
		if (second.X < first.X) return CeramicDirection.West;
		if (second.Z > first.Z) return CeramicDirection.South;
		if (second.Z < first.Z) return CeramicDirection.North;
		throw new InvalidDataException("CeramicFish wall vertices must be distinct.");
	}

	private static (int X, int Z) DirectionOffset(CeramicDirection direction) => direction switch
	{
		CeramicDirection.North => (0, -1),
		CeramicDirection.East => (1, 0),
		CeramicDirection.South => (0, 1),
		CeramicDirection.West => (-1, 0),
		_ => throw new ArgumentOutOfRangeException(nameof(direction)),
	};

	private static int DeriveSeed(int worldSeed, string villageId, int candidateOrdinal)
	{
		unchecked
		{
			ulong value = (uint)worldSeed;
			foreach (char character in villageId)
				value = (value ^ character) * 1099511628211UL;
			value ^= (ulong)(uint)candidateOrdinal * 0x9E3779B97F4A7C15UL;
			value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
			value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
			return (int)(value ^ (value >> 31));
		}
	}

	private static int PercentageCeiling(int value, int percentage) =>
		(value * percentage + 99) / 100;

	private static int PercentageFloor(int value, int percentage) =>
		value * percentage / 100;

	private sealed record Grid(
		int OriginX,
		int OriginZ,
		int Width,
		int Height,
		HashSet<CeramicCell> Active,
		long EntryDistance);

	private readonly record struct RowRun(int Left, int Right)
	{
		internal int Width => Right - Left + 1;
	}

	private readonly record struct WallBand(int Top, int Bottom, int Left, int Right);
}
