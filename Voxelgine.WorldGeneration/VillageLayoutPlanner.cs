namespace Voxelgine.WorldGeneration;

internal static class VillageLayoutPlanner
{
	private const int CellSize = VillagePrefabDescriptor.Width;

	public static PlannedVillageLayout[] Plan(
		WorldGenerationSettings settings,
		IReadOnlyList<PlannedVillageArea> villages,
		VillagePrefabCatalogDescriptor catalog,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(catalog);
		VillagePrefabVariant[] variants = catalog.Prefabs
			.SelectMany(static prefab => prefab.AllowedRotations.Select(rotation => new VillagePrefabVariant(prefab, rotation)))
			.ToArray();
		if (!variants.Any(static value => value.Kind == VillageModuleKind.Outside)
			|| !variants.Any(static value => value.Kind == VillageModuleKind.Road)
			|| !variants.Any(static value => value.Kind == VillageModuleKind.Gate)
			|| !variants.Any(static value => value.Kind == VillageModuleKind.DefenseWall)
			|| !variants.Any(static value => value.Kind is VillageModuleKind.Room or VillageModuleKind.Hallway)
			|| !variants.Any(static value => value.Kind == VillageModuleKind.Roof))
			throw new InvalidDataException("Village prefab catalog lacks required outside, road, gate, wall, room, or roof modules.");

		List<PlannedVillageLayout> result = [];
		foreach (PlannedVillageArea village in villages)
		{
			cancellationToken.ThrowIfCancellationRequested();
			result.Add(PlanVillage(settings, village, variants, cancellationToken));
		}
		return result.ToArray();
	}

	private static PlannedVillageLayout PlanVillage(
		WorldGenerationSettings settings,
		PlannedVillageArea village,
		VillagePrefabVariant[] variants,
		CancellationToken cancellationToken)
	{
		int gridWidth = Math.Max(1, (village.Reservation.MaximumX - village.Reservation.MinimumX + 1) / CellSize);
		int gridHeight = Math.Max(1, (village.Reservation.MaximumZ - village.Reservation.MinimumZ + 1) / CellSize);
		HashSet<PlanPoint> footprint = village.Footprint.ToHashSet();
		bool[] active = new bool[gridWidth * gridHeight];
		for (int gz = 0; gz < gridHeight; gz++) for (int gx = 0; gx < gridWidth; gx++)
		{
			int originX = village.Reservation.MinimumX + gx * CellSize;
			int originZ = village.Reservation.MinimumZ + gz * CellSize;
			active[gz * gridWidth + gx] = Enumerable.Range(0, CellSize).All(dx =>
				Enumerable.Range(0, CellSize).All(dz => footprint.Contains(new(originX + dx, originZ + dz))));
		}
		List<(int X, int Z)> activeCells = [];
		for (int gz = 0; gz < gridHeight; gz++) for (int gx = 0; gx < gridWidth; gx++) if (active[gz * gridWidth + gx]) activeCells.Add((gx, gz));
		if (activeCells.Count < 12) throw new InvalidDataException($"Village '{village.Id}' is too small for a 5x5 prefab grid.");

		HashSet<(int X, int Z)> boundary = activeCells.Where(cell => IsBoundary(cell.X, cell.Z, active, gridWidth, gridHeight)).ToHashSet();
		(int X, int Z)[] straightBoundary = boundary.Where(cell => CountActiveNeighbors(cell.X, cell.Z, active, gridWidth, gridHeight) == 3).ToArray();
		IEnumerable<(int X, int Z)> gateCandidates = straightBoundary.Length == 0 ? boundary : straightBoundary;
		(int gateX, int gateZ) = NearestCell(gateCandidates, village, village.AccessRoadCells[0].X, village.AccessRoadCells[0].Z);
		int centerX = (int)Math.Round(activeCells.Average(static value => value.X));
		int centerZ = (int)Math.Round(activeCells.Average(static value => value.Z));
		(centerX, centerZ) = NearestGridCell(activeCells, centerX, centerZ);
		HashSet<(int X, int Z)> road = BuildRoad(gateX, gateZ, centerX, centerZ, active, gridWidth, gridHeight);

		WfcSolver<VillagePrefabVariant> solver = CreateSolver(gridWidth, gridHeight, variants);
		int contradictions = 0, initialContradictions = 0, insufficientBuildings = 0, roofFailures = 0;
		(int X, int Y)? firstContradiction = null;
		for (int attempt = 1; attempt <= 64; attempt++)
		{
			ulong seed = Seed(settings.Seed, village.Id, 0, attempt);
			if (!solver.TryRun(seed, (x, z, value) => GroundAllowed(x, z, value, active, gridWidth, boundary, road, gateX, gateZ), cancellationToken, out VillagePrefabVariant[] solved))
			{ contradictions++; if (solver.LastFailureObservations == 0) initialContradictions++; firstContradiction ??= solver.LastContradictionCell; continue; }
			List<PlannedVillageModule> modules = BuildGroundModules(village, solved, active, gridWidth, gridHeight, seed);
			Dictionary<int, List<(int X, int Z)>> components = AssignBuildingComponents(modules, village, gridWidth);
			if (components.Count < 3) { insufficientBuildings++; continue; }
			PlanUpperFloors(settings, village, variants, modules, components, cancellationToken);
			if (!PlanRoofs(settings, village, variants, modules, components, cancellationToken)) { roofFailures++; continue; }
			PlanPoint3[] roads = ExpandRoads(village, road).ToArray();
			return new(village.Id, modules.ToArray(), roads, attempt);
		}
		string contradictionContext = firstContradiction is { } failed
			? DescribeGroundCell(failed.X, failed.Y, active, gridWidth, gridHeight, boundary, road, gateX, gateZ)
			: "unknown";
		throw new InvalidDataException($"Village '{village.Id}' could not satisfy WFC constraints after 64 attempts "
			+ $"({contradictions} contradictions including {initialContradictions} before observation, "
			+ $"{insufficientBuildings} layouts below three buildings, {roofFailures} roof failures; "
			+ $"first contradiction at grid {firstContradiction?.X},{firstContradiction?.Y}: {contradictionContext}).");
	}

	private static string DescribeGroundCell(int x, int z, bool[] active, int width, int height,
		HashSet<(int X, int Z)> boundary, HashSet<(int X, int Z)> road, int gateX, int gateZ)
	{
		string Role(int cx, int cz)
		{
			if ((uint)cx >= (uint)width || (uint)cz >= (uint)height || !active[cz * width + cx]) return "outside";
			if (cx == gateX && cz == gateZ) return "gate";
			if (boundary.Contains((cx, cz))) return "boundary";
			if (road.Contains((cx, cz))) return "road";
			if (cx % 3 == 0 || cz % 3 == 0) return "yard";
			return "building";
		}
		return $"{Role(x, z)} [N={Role(x, z - 1)}, E={Role(x + 1, z)}, S={Role(x, z + 1)}, W={Role(x - 1, z)}]";
	}

	private static WfcSolver<VillagePrefabVariant> CreateSolver(int width, int height, VillagePrefabVariant[] variants) =>
		new(width, height, variants, variants.Select(static value => value.Weight).ToArray(), static (left, right, direction) =>
		{
			// Outside is a solver sentinel for cells beyond the organic village footprint, not a physical
			// module socket. It may border a sealed face without manufacturing a connection on that face.
			if (left.Kind == VillageModuleKind.Outside || right.Kind == VillageModuleKind.Outside) return true;
			VillageSocketDirection leftDirection = (VillageSocketDirection)direction;
			VillageSocketDirection rightDirection = VillageSocketCompatibility.Opposite(leftDirection);
			return VillageSocketCompatibility.Matches(left.Socket(leftDirection), right.Socket(rightDirection));
		});

	private static bool GroundAllowed(int x, int z, VillagePrefabVariant value, bool[] active, int width,
		HashSet<(int X, int Z)> boundary, HashSet<(int X, int Z)> road, int gateX, int gateZ)
	{
		if (!active[z * width + x]) return value.Kind == VillageModuleKind.Outside;
		if (x == gateX && z == gateZ) return value.Kind == VillageModuleKind.Gate;
		if (boundary.Contains((x, z))) return value.Kind is VillageModuleKind.DefenseWall or VillageModuleKind.DefenseCorner;
		if (road.Contains((x, z))) return value.Kind is VillageModuleKind.Road or VillageModuleKind.Plaza;
		if (x % 3 == 0 || z % 3 == 0) return value.Kind == VillageModuleKind.Yard;
		// Stairs are inserted only after a building component is assigned more than one storey.
		// Allowing them in the ground solve creates one-storey buildings whose +Y stair socket cannot accept a roof.
		return value.Kind is VillageModuleKind.Room or VillageModuleKind.Hallway or VillageModuleKind.Utility;
	}

	private static List<PlannedVillageModule> BuildGroundModules(PlannedVillageArea village, VillagePrefabVariant[] solved,
		bool[] active, int width, int height, ulong seed)
	{
		List<PlannedVillageModule> modules = [];
		for (int z = 0; z < height; z++) for (int x = 0; x < width; x++)
		{
			VillagePrefabVariant variant = solved[z * width + x];
			if (!active[z * width + x] || variant.Kind == VillageModuleKind.Outside) continue;
			modules.Add(new(variant.Prefab.Id, variant.Rotation, 0, 0,
				new(village.Reservation.MinimumX + x * CellSize, village.SurfaceY, village.Reservation.MinimumZ + z * CellSize), variant.Kind, (long)seed));
		}
		return modules;
	}

	private static Dictionary<int, List<(int X, int Z)>> AssignBuildingComponents(List<PlannedVillageModule> modules, PlannedVillageArea village, int width)
	{
		Dictionary<(int X, int Z), int> moduleIndexes = [];
		for (int index = 0; index < modules.Count; index++) if (IsBuilding(modules[index].Kind))
			moduleIndexes[((modules[index].Origin.X - village.Reservation.MinimumX) / CellSize, (modules[index].Origin.Z - village.Reservation.MinimumZ) / CellSize)] = index;
		Dictionary<int, List<(int X, int Z)>> components = []; HashSet<(int X, int Z)> visited = []; int component = 0;
		foreach ((int X, int Z) start in moduleIndexes.Keys.OrderBy(static cell => cell.Z).ThenBy(static cell => cell.X))
		{
			if (!visited.Add(start)) continue;
			List<(int X, int Z)> cells = []; Queue<(int X, int Z)> queue = new(); queue.Enqueue(start);
			while (queue.TryDequeue(out (int X, int Z) cell))
			{
				cells.Add(cell); int index = moduleIndexes[cell]; modules[index] = modules[index] with { ComponentId = component + 1 };
				foreach ((int X, int Z) neighbor in Neighbors(cell)) if (moduleIndexes.ContainsKey(neighbor) && visited.Add(neighbor)) queue.Enqueue(neighbor);
			}
			components[++component] = cells;
		}
		return components;
	}

	private static void PlanUpperFloors(WorldGenerationSettings settings, PlannedVillageArea village, VillagePrefabVariant[] variants,
		List<PlannedVillageModule> modules, Dictionary<int, List<(int X, int Z)>> components, CancellationToken token)
	{
		VillagePrefabVariant[] upper = variants.Where(static value => (value.Prefab.Levels & VillageModuleLevel.Upper) != 0
			&& value.Kind is VillageModuleKind.Room or VillageModuleKind.Hallway or VillageModuleKind.Stairs or VillageModuleKind.Utility).ToArray();
		VillagePrefabVariant[] stairs = upper.Where(static value => value.Kind == VillageModuleKind.Stairs).ToArray();
		VillagePrefabVariant[] roofs = variants.Where(static value => value.Kind == VillageModuleKind.Roof
			&& (value.Prefab.Levels & VillageModuleLevel.Roof) != 0).ToArray();
		if (upper.Length == 0) return;
		foreach ((int component, List<(int X, int Z)> cells) in components)
		{
			int roll = (int)(Seed(settings.Seed, village.Id, component, 0) % 100);
			int floors = component == 1 ? 3 : component == 2 ? 2 : roll < 50 ? 1 : roll < 85 ? 2 : 3;
			if (floors > 1 && stairs.Length != 0)
			{
				(int stairX, int stairZ) = cells[0];
				int groundIndex = modules.FindIndex(module => module.Floor == 0 && module.ComponentId == component
					&& module.Origin.X == village.Reservation.MinimumX + stairX * CellSize
					&& module.Origin.Z == village.Reservation.MinimumZ + stairZ * CellSize);
				if (groundIndex >= 0)
				{
					VillagePrefabVariant stair = stairs[0];
					modules[groundIndex] = modules[groundIndex] with { PrefabId = stair.Prefab.Id, Rotation = stair.Rotation, Kind = VillageModuleKind.Stairs };
				}
			}
			for (int floor = 1; floor < floors; floor++)
			{
				ulong seed = Seed(settings.Seed, village.Id, component * 10 + floor, 1);
				bool isTopFloor = floor == floors - 1;
				List<PlannedVillageModule> pending = [];
				for (int index = 0; index < cells.Count; index++)
				{
					(int x, int z) = cells[index];
					PlannedVillageModule lowerModule = modules.First(module => module.ComponentId == component && module.Floor == floor - 1
						&& module.Origin.X == village.Reservation.MinimumX + x * CellSize && module.Origin.Z == village.Reservation.MinimumZ + z * CellSize);
					VillagePrefabVariant lower = variants.First(value => value.Prefab.Id == lowerModule.PrefabId && value.Rotation == lowerModule.Rotation);
					VillagePrefabVariant[] compatible = upper.Where(value => VillageSocketCompatibility.Matches(
						lower.Socket(VillageSocketDirection.PositiveY), value.Socket(VillageSocketDirection.NegativeY))).ToArray();
					if (isTopFloor)
						compatible = compatible.Where(value => roofs.Any(roof => VillageSocketCompatibility.Matches(
							value.Socket(VillageSocketDirection.PositiveY), roof.Socket(VillageSocketDirection.NegativeY)))).ToArray();
					VillagePrefabVariant[] preferred = index == 0 && !isTopFloor
						? compatible.Where(static value => value.Kind == VillageModuleKind.Stairs).ToArray()
						: compatible;
					if (preferred.Length == 0) { pending.Clear(); break; }
					VillagePrefabVariant variant = preferred[(int)((seed + (ulong)index) % (ulong)preferred.Length)];
					pending.Add(new(variant.Prefab.Id, variant.Rotation, floor, component,
						new(village.Reservation.MinimumX + x * CellSize, village.SurfaceY + floor * VillagePrefabDescriptor.Height,
							village.Reservation.MinimumZ + z * CellSize), variant.Kind, (long)seed));
				}
				if (pending.Count != cells.Count) break;
				modules.AddRange(pending);
				token.ThrowIfCancellationRequested();
			}
		}
	}

	private static bool PlanRoofs(WorldGenerationSettings settings, PlannedVillageArea village, VillagePrefabVariant[] variants,
		List<PlannedVillageModule> modules, Dictionary<int, List<(int X, int Z)>> components, CancellationToken token)
	{
		VillagePrefabVariant[] roofs = variants.Where(static value => value.Kind == VillageModuleKind.Roof && (value.Prefab.Levels & VillageModuleLevel.Roof) != 0).ToArray();
		if (roofs.Length == 0) return false;
		foreach ((int component, List<(int X, int Z)> cells) in components)
		{
			int topFloor = modules.Where(module => module.ComponentId == component).Max(static module => module.Floor);
			ulong seed = Seed(settings.Seed, village.Id, component, 32);
			for (int index = 0; index < cells.Count; index++)
			{
				(int x, int z) = cells[index];
				PlannedVillageModule lowerModule = modules.First(module => module.ComponentId == component && module.Floor == topFloor
					&& module.Origin.X == village.Reservation.MinimumX + x * CellSize && module.Origin.Z == village.Reservation.MinimumZ + z * CellSize);
				VillagePrefabVariant lower = variants.First(value => value.Prefab.Id == lowerModule.PrefabId && value.Rotation == lowerModule.Rotation);
				VillagePrefabVariant[] compatible = roofs.Where(value => VillageSocketCompatibility.Matches(
					lower.Socket(VillageSocketDirection.PositiveY), value.Socket(VillageSocketDirection.NegativeY))).ToArray();
				if (compatible.Length == 0) return false;
				VillagePrefabVariant roof = compatible[(int)((seed + (ulong)index) % (ulong)compatible.Length)];
				modules.Add(new(roof.Prefab.Id, roof.Rotation, 3, component,
					new(village.Reservation.MinimumX + x * CellSize, village.SurfaceY + (topFloor + 1) * VillagePrefabDescriptor.Height,
						village.Reservation.MinimumZ + z * CellSize), roof.Kind, (long)seed));
			}
			token.ThrowIfCancellationRequested();
		}
		return true;
	}

	private static IEnumerable<PlanPoint3> ExpandRoads(PlannedVillageArea village, HashSet<(int X, int Z)> cells)
	{
		HashSet<PlanPoint3> result = [];
		foreach ((int gx, int gz) in cells)
		{
			int centerX = village.Reservation.MinimumX + gx * CellSize + CellSize / 2;
			int centerZ = village.Reservation.MinimumZ + gz * CellSize + CellSize / 2;
			result.Add(new(centerX, village.SurfaceY, centerZ));
			if (cells.Contains((gx + 1, gz))) for (int dx = 1; dx <= CellSize; dx++) result.Add(new(centerX + dx, village.SurfaceY, centerZ));
			if (cells.Contains((gx, gz + 1))) for (int dz = 1; dz <= CellSize; dz++) result.Add(new(centerX, village.SurfaceY, centerZ + dz));
		}
		return result.OrderBy(static cell => cell.X).ThenBy(static cell => cell.Z);
	}

	private static HashSet<(int X, int Z)> BuildRoad(int gateX, int gateZ, int centerX, int centerZ, bool[] active, int width, int height)
	{
		(int X, int Z) gate = (gateX, gateZ), center = (centerX, centerZ);
		(int X, int Z)[] outside = Neighbors(gate).Where(cell => !IsActive(cell.X, cell.Z, active, width, height)).ToArray();
		(int X, int Z) start = gate;
		if (outside.Length == 1)
		{
			(int X, int Z) direction = (gate.X - outside[0].X, gate.Z - outside[0].Z);
			(int X, int Z) inward = (gate.X + direction.X, gate.Z + direction.Z);
			if (IsActive(inward.X, inward.Z, active, width, height)) start = inward;
		}

		Dictionary<(int X, int Z), (int X, int Z)> previous = [];
		HashSet<(int X, int Z)> visited = [start];
		Queue<(int X, int Z)> pending = new(); pending.Enqueue(start);
		while (pending.TryDequeue(out (int X, int Z) cell) && cell != center)
			foreach ((int X, int Z) neighbor in Neighbors(cell)
				.OrderBy(value => Math.Abs(value.X - center.X) + Math.Abs(value.Z - center.Z))
				.ThenBy(static value => value.Z).ThenBy(static value => value.X))
				if (IsActive(neighbor.X, neighbor.Z, active, width, height) && visited.Add(neighbor))
				{ previous[neighbor] = cell; pending.Enqueue(neighbor); }

		HashSet<(int X, int Z)> result = [gate];
		if (!visited.Contains(center)) return result;
		for ((int X, int Z) cell = center; ; cell = previous[cell])
		{
			result.Add(cell); if (cell == start) break;
		}
		return result;
	}

	private static int CountActiveNeighbors(int x, int z, bool[] active, int width, int height) =>
		Neighbors((x, z)).Count(cell => IsActive(cell.X, cell.Z, active, width, height));
	private static bool IsActive(int x, int z, bool[] active, int width, int height) =>
		(uint)x < (uint)width && (uint)z < (uint)height && active[z * width + x];

	private static bool IsBoundary(int x, int z, bool[] active, int width, int height) =>
		Neighbors((x, z)).Any(cell => (uint)cell.X >= (uint)width || (uint)cell.Z >= (uint)height || !active[cell.Z * width + cell.X]);

	private static (int X, int Z) NearestCell(IEnumerable<(int X, int Z)> cells, PlannedVillageArea village, int worldX, int worldZ) => cells
		.OrderBy(cell => Math.Abs(village.Reservation.MinimumX + cell.X * CellSize + 2 - worldX) + Math.Abs(village.Reservation.MinimumZ + cell.Z * CellSize + 2 - worldZ)).First();
	private static (int X, int Z) NearestGridCell(IEnumerable<(int X, int Z)> cells, int x, int z) => cells.OrderBy(cell => Math.Abs(cell.X - x) + Math.Abs(cell.Z - z)).First();
	private static bool IsBuilding(VillageModuleKind kind) => kind is VillageModuleKind.Room or VillageModuleKind.Hallway or VillageModuleKind.Stairs or VillageModuleKind.Utility;
	private static IEnumerable<(int X, int Z)> Neighbors((int X, int Z) cell) { yield return (cell.X - 1, cell.Z); yield return (cell.X + 1, cell.Z); yield return (cell.X, cell.Z - 1); yield return (cell.X, cell.Z + 1); }
	private static ulong Seed(int seed, string id, int level, int attempt)
	{
		ulong value = unchecked((uint)seed) | ((ulong)(uint)level << 32); foreach (char character in id) value = (value ^ character) * 1099511628211UL;
		return value ^ ((ulong)(uint)attempt * 0x9E3779B97F4A7C15UL);
	}
}
