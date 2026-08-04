namespace Voxelgine.WorldGeneration;

internal static class VillageLayoutPlanner
{
	private const int CellSize = VillagePrefabDescriptor.Width;
	private const int MaximumAttempts = 8;
	private const int MaximumEntryCandidates = 16;
	private const int MaximumFloors = 4;
	private const int DesiredConnectedCoveragePercent = 65;
	private const long MinimumPropagationChecksPerAttempt = 100_000;
	private const long MaximumPropagationChecksPerAttempt = 5_000_000;
	private const int PropagationBudgetScale = 8;
	private static readonly VillageSocketDirection[] HorizontalDirections =
		[VillageSocketDirection.NegativeZ, VillageSocketDirection.PositiveX, VillageSocketDirection.PositiveZ, VillageSocketDirection.NegativeX];

	public static PlannedVillageLayout[] Plan(
		WorldGenerationSettings settings,
		IReadOnlyList<PlannedVillageArea> villages,
		VillagePrefabCatalogDescriptor catalog,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(catalog);
		if (!catalog.HasUsefulConnectedChain()) return [];
		VillagePrefabVariant[] variants = ExpandVariants(catalog.Prefabs);
		if (variants.Length == 0 || !variants.Any(value => HorizontalDirections.Any(direction =>
			value.Socket(direction).Types.Contains(catalog.ExternalEntrySemantic, StringComparer.Ordinal))))
			return [];

		List<PlannedVillageLayout> result = [];
		foreach (PlannedVillageArea village in villages)
		{
			cancellationToken.ThrowIfCancellationRequested();
			PlannedVillageLayout? layout = PlanVillage(settings, village, variants, catalog.ExternalEntrySemantic, catalog.AdjacencyRules, cancellationToken);
			if (layout is not null) result.Add(layout);
		}
		return result.ToArray();
	}

	private static VillagePrefabVariant[] ExpandVariants(IEnumerable<VillagePrefabDescriptor> prefabs)
	{
		List<VillagePrefabVariant> result = [];
		foreach (VillagePrefabDescriptor prefab in prefabs)
		{
			VillagePrefabVariant[] candidates = prefab.AllowedRotations
				.Select(rotation => new VillagePrefabVariant(prefab, rotation, 0)).ToArray();
			VillagePrefabVariant[] distinct = candidates.GroupBy(VariantKey, StringComparer.Ordinal)
				.Select(static group => group.First()).ToArray();
			double weight = (double)prefab.Weight / distinct.Length;
			result.AddRange(distinct.Select(value => value with { Weight = weight }));
		}
		return result.ToArray();
	}

	private static string VariantKey(VillagePrefabVariant value)
	{
		string geometry = value.Prefab.RotationSignatures.Length == 4
			? value.Prefab.RotationSignatures[value.Rotation / 90] : value.Rotation.ToString();
		string sockets = string.Join('|', Enum.GetValues<VillageSocketDirection>().Select(direction =>
			$"{(int)direction}:{string.Join(',', value.Socket(direction).Types.Order(StringComparer.Ordinal))}"));
		string masks = $"{RotateMaskKey(value.Prefab.SupportMask, value.Rotation)}|{RotateMaskKey(value.Prefab.LoadMask, value.Rotation)}|{RotateMaskKey(value.Prefab.WalkableMask, value.Rotation)}";
		string markers = string.Join('|', value.Prefab.Markers.OrderBy(static marker => marker.Id, StringComparer.Ordinal).Select(marker =>
		{
			(int x, int z) = RotatePoint(marker.X, marker.Z, value.Rotation);
			return $"{marker.Id}:{marker.Kind}:{x}:{marker.Y}:{z}";
		}));
		return $"{geometry}|{sockets}|{masks}|{markers}";
	}

	private static string RotateMaskKey(byte[] mask, int rotation)
	{
		char[] result = new char[mask.Length];
		for (int z = 0; z < 5; z++) for (int x = 0; x < 5; x++)
		{
			(int rotatedX, int rotatedZ) = RotatePoint(x, z, rotation);
			result[rotatedZ * 5 + rotatedX] = mask[z * 5 + x] == 0 ? '0' : '1';
		}
		return new(result);
	}

	private static (int X, int Z) RotatePoint(int x, int z, int rotation) => rotation switch
	{
		0 => (x, z), 90 => (4 - z, x), 180 => (4 - x, 4 - z), 270 => (z, 4 - x),
		_ => throw new ArgumentOutOfRangeException(nameof(rotation)),
	};

	private static PlannedVillageLayout? PlanVillage(
		WorldGenerationSettings settings,
		PlannedVillageArea village,
		VillagePrefabVariant[] variants,
		string entrySemantic,
		IReadOnlyList<VillageAdjacencyRuleDescriptor> adjacencyRules,
		CancellationToken cancellationToken)
	{
		Grid grid = BuildGrid(village, variants, entrySemantic);
		if (grid.ActiveCells.Count == 0) return null;
		SolverPattern[] patterns = variants.Select(static value => SolverPattern.Authored(value)).Append(SolverPattern.Outside).ToArray();
		WfcSolver<SolverPattern> solver = CreateSolver(grid.Width, grid.Height, patterns, adjacencyRules);
		long groundPropagationBudget = ComputePropagationBudget(grid, patterns.Length);
		EntryCandidate[] entries = BuildEntryCandidates(grid, village, variants, entrySemantic);
		if (entries.Length == 0) return null;
		int nearestEntryDistance = EntryDistance(entries[0], grid, village.AccessRoadCells[0]);
		entries = entries.Where(entry => EntryDistance(entry, grid, village.AccessRoadCells[0]) == nearestEntryDistance).ToArray();
		PlannedVillageLayout? bestLayout = null;
		int bestConnectedGroundCells = 0;
		int bestAdjacencyPenalty = int.MaxValue;
		long bestCoveredQuality = long.MinValue;
		bool bestMeetsDesiredCoverage = false;

		for (int entryIndex = 0; entryIndex < entries.Length; entryIndex++)
		for (int attempt = 1; attempt <= MaximumAttempts; attempt++)
		{
			EntryCandidate entry = entries[entryIndex];
			int attemptOrdinal = entryIndex * MaximumAttempts + attempt;
			ulong seed = Seed(settings.Seed, village.Id, 0, attemptOrdinal);
			if (!solver.TryRun(seed, (x, z, value) => GroundAllowed(x, z, value, grid, entry.X, entry.Z, entry.Direction, entrySemantic),
				cancellationToken, groundPropagationBudget, out SolverPattern[] ground))
			{
				continue;
			}
			RetainEntryConnectedComponent(ground, grid, entry.X, entry.Z);
			int connectedGroundCells = ground.Count(static value => value.Variant is not null);
			int adjacencyPenalty = ComputeAdjacencyPenalty(ground, grid, adjacencyRules);
			bool meetsDesiredCoverage = (long)connectedGroundCells * 100 >=
				(long)grid.ActiveCells.Count * DesiredConnectedCoveragePercent;
			long coveredQuality = (long)connectedGroundCells * 100 - (long)adjacencyPenalty * 2;
			bool improvesBest = bestLayout is null
				|| meetsDesiredCoverage && !bestMeetsDesiredCoverage
				|| meetsDesiredCoverage && bestMeetsDesiredCoverage && coveredQuality > bestCoveredQuality
				|| !meetsDesiredCoverage && !bestMeetsDesiredCoverage && (connectedGroundCells > bestConnectedGroundCells
					|| connectedGroundCells == bestConnectedGroundCells && adjacencyPenalty < bestAdjacencyPenalty);
			if (!improvesBest) continue;

			List<SolverPattern[]> floors = [ground];
			bool failed = false;
			for (int floor = 1; floor < MaximumFloors; floor++)
			{
				SolverPattern[] lower = floors[^1];
				if (!lower.Any(static value => value.Variant is { } variant
					&& variant.Socket(VillageSocketDirection.PositiveY).Types.Length != 0)) break;
				SolverPattern[] upperPatterns = variants.Select(static value => SolverPattern.Authored(value)).Append(SolverPattern.NoModule).ToArray();
				WfcSolver<SolverPattern> upperSolver = CreateSolver(grid.Width, grid.Height, upperPatterns, adjacencyRules);
				long upperPropagationBudget = ComputePropagationBudget(grid, upperPatterns.Length);
				ulong upperSeed = Seed(settings.Seed, village.Id, floor, attemptOrdinal);
				if (!upperSolver.TryRun(upperSeed, (x, z, value) => UpperAllowed(x, z, value, lower, grid.Width, floor),
					cancellationToken, upperPropagationBudget, out SolverPattern[] upper))
				{
					failed = true;
					break;
				}
				floors.Add(upper);
			}
			if (failed || floors.Count == MaximumFloors && floors[^1].Any(static value => value.Variant is { } variant
				&& variant.Socket(VillageSocketDirection.PositiveY).Types.Length != 0)) continue;

			List<PlannedVillageModule> modules = [];
			for (int floor = 0; floor < floors.Count; floor++)
			{
				SolverPattern[] solved = floors[floor];
				long attemptSeed = (long)Seed(settings.Seed, village.Id, floor, attemptOrdinal);
				for (int z = 0; z < grid.Height; z++) for (int x = 0; x < grid.Width; x++)
					if (solved[z * grid.Width + x].Variant is { } variant)
						modules.Add(new(variant.Prefab.Id, variant.Rotation, floor,
							new(grid.OriginX + x * CellSize, village.SurfaceY + floor * CellSize,
								grid.OriginZ + z * CellSize), attemptSeed));
			}
			PlannedVillageLayout candidate = new(village.Id, modules.ToArray(), attemptOrdinal);
			bestConnectedGroundCells = connectedGroundCells;
			bestAdjacencyPenalty = adjacencyPenalty;
			bestCoveredQuality = coveredQuality;
			bestMeetsDesiredCoverage = meetsDesiredCoverage;
			bestLayout = candidate;
		}
		return bestLayout;
	}

	private static int ComputeAdjacencyPenalty(SolverPattern[] solved, Grid grid,
		IReadOnlyList<VillageAdjacencyRuleDescriptor> adjacencyRules)
	{
		int penalty = 0;
		for (int z = 0; z < grid.Height; z++) for (int x = 0; x < grid.Width; x++)
		{
			if (solved[z * grid.Width + x].Variant is not { } left) continue;
			foreach (VillageSocketDirection direction in new[] { VillageSocketDirection.PositiveX, VillageSocketDirection.PositiveZ })
			{
				(int X, int Z) adjacentCell = Offset((x, z), direction);
				if (!IsActive(adjacentCell.X, adjacentCell.Z, grid)
					|| solved[adjacentCell.Z * grid.Width + adjacentCell.X].Variant is not { } right) continue;
				bool connected = VillageSocketCompatibility.CreatesConnection(left.Socket(direction),
					right.Socket(VillageSocketCompatibility.Opposite(direction)));
				foreach (VillageAdjacencyRuleDescriptor rule in adjacencyRules)
					if (rule.WeightPercent != 0 && rule.AppliesTo(connected) && rule.Matches(left.Prefab.Id, right.Prefab.Id))
						penalty += 100 - rule.WeightPercent;
			}
		}
		return penalty;
	}

	private static EntryCandidate[] BuildEntryCandidates(Grid grid, PlannedVillageArea village,
		VillagePrefabVariant[] variants, string entrySemantic)
	{
		PlanPoint3 access = village.AccessRoadCells[0];
		return grid.Boundary
			.SelectMany(cell => OutwardDirections(cell.X, cell.Z, grid)
				.Select(direction => new EntryCandidate(cell.X, cell.Z, direction)))
			.Where(candidate => variants.Any(variant => GroundAllowed(candidate.X, candidate.Z,
				SolverPattern.Authored(variant), grid, candidate.X, candidate.Z, candidate.Direction, entrySemantic)))
			.OrderBy(candidate => EntryDistance(candidate, grid, access))
			.ThenBy(static candidate => candidate.Z)
			.ThenBy(static candidate => candidate.X)
			.ThenBy(static candidate => candidate.Direction)
			.Take(MaximumEntryCandidates)
			.ToArray();
	}

	private static int EntryDistance(EntryCandidate candidate, Grid grid, PlanPoint3 access)
	{
		(int outsideX, int outsideZ) = Offset((candidate.X, candidate.Z), candidate.Direction);
		int worldX = grid.OriginX + outsideX * CellSize + CellSize / 2;
		int worldZ = grid.OriginZ + outsideZ * CellSize + CellSize / 2;
		return Math.Abs(worldX - access.X) + Math.Abs(worldZ - access.Z);
	}

	private static long ComputePropagationBudget(Grid grid, int patternCount)
	{
		// AC-4 propagation work grows with cells and the square of the expanded pattern count.
		// Village reservations were doubled after the original fixed 50k limit was selected, so
		// that limit could be exhausted while applying the initial boundary constraints alone.
		long estimated = (long)Math.Max(1, grid.ActiveCells.Count) * patternCount * patternCount * PropagationBudgetScale;
		return Math.Clamp(estimated, MinimumPropagationChecksPerAttempt, MaximumPropagationChecksPerAttempt);
	}

	private static WfcSolver<SolverPattern> CreateSolver(int width, int height, SolverPattern[] patterns,
		IReadOnlyList<VillageAdjacencyRuleDescriptor> adjacencyRules) =>
		new(width, height, patterns, patterns.Select(static value => value.Weight).ToArray(), (left, right, direction) =>
		{
			VillageSocketDirection leftDirection = (VillageSocketDirection)direction;
			VillageSocketDirection rightDirection = VillageSocketCompatibility.Opposite(leftDirection);
			if (left.IsOutside || right.IsOutside) return true;
			if (left.IsNoModule || right.IsNoModule)
			{
				if (left.IsNoModule && right.IsNoModule) return true;
				VillagePrefabVariant authored = (left.Variant ?? right.Variant)!.Value;
				VillageSocketDirection authoredDirection = left.IsNoModule ? rightDirection : leftDirection;
				return authored.Socket(authoredDirection).Types.Length == 0;
			}
			VillageSocketDescriptor leftSocket = left.Variant!.Value.Socket(leftDirection);
			VillageSocketDescriptor rightSocket = right.Variant!.Value.Socket(rightDirection);
			if (!VillageSocketCompatibility.Matches(leftSocket, rightSocket)) return false;
			bool connected = VillageSocketCompatibility.CreatesConnection(leftSocket, rightSocket);
			return !adjacencyRules.Any(rule => rule.WeightPercent == 0 && rule.AppliesTo(connected)
				&& rule.Matches(left.Variant.Value.Prefab.Id, right.Variant.Value.Prefab.Id));
		}, (left, right, direction) =>
		{
			if (left.Variant is not { } leftVariant || right.Variant is not { } rightVariant) return 1.0;
			VillageSocketDirection leftDirection = (VillageSocketDirection)direction;
			bool connected = VillageSocketCompatibility.CreatesConnection(leftVariant.Socket(leftDirection),
				rightVariant.Socket(VillageSocketCompatibility.Opposite(leftDirection)));
			double result = 1.0;
			foreach (VillageAdjacencyRuleDescriptor rule in adjacencyRules)
				if (rule.WeightPercent != 0 && rule.AppliesTo(connected)
					&& rule.Matches(leftVariant.Prefab.Id, rightVariant.Prefab.Id)) result *= rule.WeightPercent / 100.0;
			return result;
		});

	private static bool GroundAllowed(int x, int z, SolverPattern value, Grid grid, int gateX, int gateZ,
		VillageSocketDirection entryDirection, string entrySemantic)
	{
		bool active = grid.Active[z * grid.Width + x];
		if (!active) return value.IsOutside;
		if (value.Variant is not { } variant) return false;
		foreach (VillageSocketDirection direction in OutwardDirections(x, z, grid))
		{
			string[] types = variant.Socket(direction).Types;
			if (x == gateX && z == gateZ && direction == entryDirection)
			{
				if (!types.Contains(entrySemantic, StringComparer.Ordinal)) return false;
			}
			else if (types.Length != 0) return false;
		}
		if (x == gateX && z == gateZ)
			return HorizontalDirections.Any(direction => direction != entryDirection
				&& variant.Socket(direction).Types.Contains(entrySemantic, StringComparer.Ordinal));
		return true;
	}

	private static bool UpperAllowed(int x, int z, SolverPattern value, SolverPattern[] lower, int width, int floor)
	{
		SolverPattern below = lower[z * width + x];
		if (below.Variant is not { } lowerVariant || lowerVariant.Socket(VillageSocketDirection.PositiveY).Types.Length == 0)
			return value.IsNoModule;
		if (value.Variant is not { } upperVariant) return false;
		if (!VillageSocketCompatibility.CreatesConnection(lowerVariant.Socket(VillageSocketDirection.PositiveY),
			upperVariant.Socket(VillageSocketDirection.NegativeY))) return false;
		return floor < MaximumFloors - 1 || upperVariant.Socket(VillageSocketDirection.PositiveY).Types.Length == 0;
	}

	private static void RetainEntryConnectedComponent(SolverPattern[] solved, Grid grid, int gateX, int gateZ)
	{
		HashSet<(int X, int Z)> reached = [(gateX, gateZ)];
		Queue<(int X, int Z)> queue = new(); queue.Enqueue((gateX, gateZ));
		while (queue.TryDequeue(out (int X, int Z) cell))
		{
			VillagePrefabVariant current = solved[cell.Z * grid.Width + cell.X].Variant!.Value;
			foreach (VillageSocketDirection direction in HorizontalDirections)
			{
				(int X, int Z) neighbor = Offset(cell, direction);
				if (!IsActive(neighbor.X, neighbor.Z, grid) || reached.Contains(neighbor)) continue;
				if (solved[neighbor.Z * grid.Width + neighbor.X].Variant is not { } adjacent) continue;
				if (!VillageSocketCompatibility.CreatesConnection(current.Socket(direction),
					adjacent.Socket(VillageSocketCompatibility.Opposite(direction)))) continue;
				reached.Add(neighbor); queue.Enqueue(neighbor);
			}
		}
		for (int z = 0; z < grid.Height; z++) for (int x = 0; x < grid.Width; x++)
			if (grid.Active[z * grid.Width + x] && !reached.Contains((x, z)))
				solved[z * grid.Width + x] = SolverPattern.NoModule;
	}

	private static Grid BuildGrid(PlannedVillageArea village, VillagePrefabVariant[] variants, string entrySemantic)
	{
		HashSet<PlanPoint> footprint = village.Footprint.ToHashSet();
		PlanPoint3 access = village.AccessRoadCells[0];
		List<(Grid Grid, int EntryDistance)> candidates = [];
		for (int offsetX = 0; offsetX < CellSize; offsetX++)
		for (int offsetZ = 0; offsetZ < CellSize; offsetZ++)
		{
			int originX = village.Reservation.MinimumX + offsetX;
			int originZ = village.Reservation.MinimumZ + offsetZ;
			int width = (village.Reservation.MaximumX - originX + 1) / CellSize;
			int height = (village.Reservation.MaximumZ - originZ + 1) / CellSize;
			if (width <= 0 || height <= 0) continue;
			bool[] active = new bool[width * height];
			List<(int X, int Z)> activeCells = [];
			for (int z = 0; z < height; z++) for (int x = 0; x < width; x++)
			{
				int cellOriginX = originX + x * CellSize;
				int cellOriginZ = originZ + z * CellSize;
				bool isActive = Enumerable.Range(0, CellSize).All(dx => Enumerable.Range(0, CellSize)
					.All(dz => footprint.Contains(new(cellOriginX + dx, cellOriginZ + dz))));
				active[z * width + x] = isActive;
				if (isActive) activeCells.Add((x, z));
			}
			if (activeCells.Count == 0) continue;
			Grid candidate = new(originX, originZ, width, height, active, activeCells, []);
			candidate = candidate with { Boundary = activeCells.Where(cell => OutwardDirections(cell.X, cell.Z, candidate).Any()).ToArray() };
			EntryCandidate[] viableEntries = candidate.Boundary
				.SelectMany(cell => OutwardDirections(cell.X, cell.Z, candidate)
					.Select(direction => new EntryCandidate(cell.X, cell.Z, direction)))
				.Where(entry => variants.Any(variant => GroundAllowed(entry.X, entry.Z,
					SolverPattern.Authored(variant), candidate, entry.X, entry.Z, entry.Direction, entrySemantic)))
				.ToArray();
			if (viableEntries.Length == 0) continue;
			int entryDistance = viableEntries.Min(entry => EntryDistance(entry, candidate, access));
			candidates.Add((candidate, entryDistance));
		}
		if (candidates.Count == 0)
			return new(village.Reservation.MinimumX, village.Reservation.MinimumZ, 1, 1, [false], [], []);

		return candidates
			.OrderBy(static value => value.EntryDistance)
			.ThenByDescending(static value => value.Grid.ActiveCells.Count)
			.ThenBy(static value => value.Grid.OriginZ)
			.ThenBy(static value => value.Grid.OriginX)
			.First().Grid;
	}

	private static IEnumerable<VillageSocketDirection> OutwardDirections(int x, int z, Grid grid) =>
		HorizontalDirections.Where(direction => { (int X, int Z) cell = Offset((x, z), direction); return !IsActive(cell.X, cell.Z, grid); });
	private static bool IsActive(int x, int z, Grid grid) => (uint)x < (uint)grid.Width && (uint)z < (uint)grid.Height && grid.Active[z * grid.Width + x];
	private static (int X, int Z) Offset((int X, int Z) cell, VillageSocketDirection direction) => direction switch
	{
		VillageSocketDirection.NegativeZ => (cell.X, cell.Z - 1), VillageSocketDirection.PositiveX => (cell.X + 1, cell.Z),
		VillageSocketDirection.PositiveZ => (cell.X, cell.Z + 1), VillageSocketDirection.NegativeX => (cell.X - 1, cell.Z),
		_ => cell,
	};
	private static ulong Seed(int seed, string id, int level, int attempt)
	{
		ulong value = unchecked((uint)seed) | ((ulong)(uint)level << 32);
		foreach (char character in id) value = (value ^ character) * 1099511628211UL;
		return value ^ ((ulong)(uint)attempt * 0x9E3779B97F4A7C15UL);
	}

	private sealed record Grid(int OriginX, int OriginZ, int Width, int Height, bool[] Active,
		List<(int X, int Z)> ActiveCells, (int X, int Z)[] Boundary);
	private readonly record struct EntryCandidate(int X, int Z, VillageSocketDirection Direction);
	private readonly record struct SolverPattern(VillagePrefabVariant? Variant, bool IsOutside, bool IsNoModule, double Weight)
	{
		public static SolverPattern Authored(VillagePrefabVariant value) => new(value, false, false, value.Weight);
		public static SolverPattern Outside => new(null, true, false, 1);
		public static SolverPattern NoModule => new(null, false, true, 1);
	}
}
