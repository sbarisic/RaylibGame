using Voxelgine.WorldGeneration;

namespace CeramicFish.TestHarness;

internal static class CeramicTestCatalog
{
	internal const int GridSize = 85;
	internal const int ImageSize = 256;
	internal const int PrefabSize = 3;
	private const int VillageRadius = 36;

	internal static IReadOnlyDictionary<int, RgbaColor> Palette { get; } =
		new Dictionary<int, RgbaColor>
		{
			[0] = new(0, 0, 0, 0),
			[1] = new(0x28, 0x28, 0x28, 0xff),
			[2] = new(0xa5, 0x00, 0x00, 0xff),
			[3] = new(0x63, 0x63, 0x63, 0xff),
			[4] = new(0x00, 0x00, 0x00, 0xff),
			[5] = new(0xbf, 0x6a, 0x31, 0xff),
			[6] = new(0xff, 0x00, 0xdc, 0xff),
			[7] = new(0xff, 0x00, 0x00, 0xff),
		};

	internal static CeramicFishDefinition CreateDefinition() => new(
		"ceramic-fish-test-v4",
		[
			CreatePrefab("tile-00", ["house-wall"], [".H.", "#HH", ".#."],
				(CeramicDirection.North, "house-wall"), (CeramicDirection.East, "house-wall")),
			CreatePrefab("tile-01", ["house-wall"], ["g.g", "HHH", "g.g"],
				(CeramicDirection.East, "house-wall"), (CeramicDirection.West, "house-wall")),
			CreatePrefab("tile-02", ["house-wall", "house-door"], [".#.", "H.H", ".#."],
				(CeramicDirection.East, "house-wall"), (CeramicDirection.West, "house-wall")),
			CreatePrefab("tile-03", ["neutral"], ["g.g", ".g.", "g.g"]),
			CreatePrefab("tile-04", ["road"], ["g.g", ".r.", "grg"],
				(CeramicDirection.South, "road")),
			CreatePrefab("tile-05", ["road"], [".r.", "#r#", ".r."],
				(CeramicDirection.North, "road"), (CeramicDirection.South, "road")),
			CreatePrefab("tile-06", ["road"], ["grg", ".rr", "g.g"],
				(CeramicDirection.North, "road"), (CeramicDirection.East, "road")),
			CreatePrefab("tile-07", ["road"], [".r.", "#rr", ".r."],
				(CeramicDirection.North, "road"), (CeramicDirection.East, "road"),
				(CeramicDirection.South, "road")),
			CreatePrefab("tile-08", ["defense-wall"], [".#.", "WWW", "WWW"],
				(CeramicDirection.East, "defense-wall"), (CeramicDirection.West, "defense-wall")),
			CreatePrefab("tile-09", ["defense-wall"], ["gWW", "WWW", "WWW"],
				(CeramicDirection.North, "defense-wall"), (CeramicDirection.East, "defense-wall")),
			CreatePrefab("tile-10", ["defense-wall", "gate"], [".#.", "W.W", "W#W"],
				(CeramicDirection.East, "defense-wall"), (CeramicDirection.West, "defense-wall"),
				(CeramicDirection.North, "road"), (CeramicDirection.South, "road")),
			CreatePrefab("tile-11", ["neutral"], ["g.g", ".g.", "g.g"]),
			CreatePrefab("tile-12", ["house-wall", "next-room-door", "room-door"],
				[".#.", "H.H", ".#."],
				(CeramicDirection.East, "house-wall"), (CeramicDirection.West, "house-wall")),
			CreatePrefab("tile-14", ["house-wall"], [".H.", "HHH", "..."],
				(CeramicDirection.North, "house-wall"), (CeramicDirection.East, "house-wall"),
				(CeramicDirection.West, "house-wall")),
			CreatePrefab("tile-15", ["house-wall", "house-window"], ["g.g", "VVV", "g.g"],
				(CeramicDirection.East, "house-wall"), (CeramicDirection.West, "house-wall")),
			CreatePrefab("empty", ["empty"], ["___", "___", "___"]),
		],
		[
			new("defense-wall", new CeramicCountRange(2, 2),
				new CeramicCountRange(1, 1), new CeramicCountRange(0, 0)),
			new("house-wall", new CeramicCountRange(2, 3),
				new CeramicCountRange(1, null), new CeramicCountRange(0, 0)),
			new("road", new CeramicCountRange(1, 3),
				new CeramicCountRange(1, 1), new CeramicCountRange(1, 1),
				requireEntranceReachability: true),
		])
	{
		ComponentEntryPolicies =
		[
			new("house-wall", "house-door", "road", "next-room-door", "room-door",
				new CeramicCountRange(0, 2)),
		],
		WallFeaturePolicies =
		[
			new("house-wall", "house-window", new CeramicCountRange(1, 6),
				OuterWallsOnly: true, CellsPerFeature: 12),
		],
	};

	internal static CeramicGenerationRequest CreateRequest(int seed)
	{
		List<CeramicCell> region = CreateCircularRegion(out HashSet<CeramicCell> wall);
		CeramicCell gate = new(GridSize / 2, GridSize / 2 + VillageRadius);
		List<CeramicCellConstraint> constraints = new(region.Count);
		foreach (CeramicCell cell in region)
		{
			if (cell == gate)
			{
				constraints.Add(new(cell, ["defense-wall", "gate"], []));
			}
			else if (wall.Contains(cell))
			{
				constraints.Add(new(cell, ["defense-wall"], ["gate"]));
			}
			else
			{
				constraints.Add(new(cell, [], ["defense-wall", "gate"]));
			}
		}

		return new(region, new(gate, ["defense-wall", "gate"], "road", CeramicDirection.North), seed)
		{
			BoundarySocket = CeramicSocket.NoConnection,
			Entrances = [new(gate, CeramicDirection.South, "road")],
			CellConstraints = constraints,
			TagQuotas =
			[
				new("road", MinimumCells: PercentageCeiling(region.Count, 6),
					MaximumCells: PercentageFloor(region.Count, 12)),
				new("house-wall", MinimumCells: PercentageCeiling(region.Count, 10),
					MaximumCells: PercentageFloor(region.Count, 22)),
			],
		};
	}

	private static List<CeramicCell> CreateCircularRegion(out HashSet<CeramicCell> wall)
	{
		CeramicCell center = new(GridSize / 2, GridSize / 2);
		List<CeramicDirection> quadrantSteps = CreateCircleQuadrantSteps(VillageRadius);
		HashSet<CeramicCell> wallCells = [];
		CeramicCell cursor = new(center.X, center.Z - VillageRadius);
		for (int quadrant = 0; quadrant < 4; quadrant++)
		{
			foreach (CeramicDirection step in quadrantSteps)
			{
				if (!wallCells.Add(cursor))
					throw new InvalidDataException("The CeramicFish village wall crossed itself.");
				cursor = CeramicGeometry.Offset(cursor, RotateClockwise(step, quadrant));
			}
		}
		if (cursor != new CeramicCell(center.X, center.Z - VillageRadius))
			throw new InvalidDataException("The CeramicFish village wall did not close.");

		List<CeramicCell> region = [];
		for (int z = center.Z - VillageRadius; z <= center.Z + VillageRadius; z++)
		{
			int minimumX = wallCells.Where(cell => cell.Z == z).Min(cell => cell.X);
			int maximumX = wallCells.Where(cell => cell.Z == z).Max(cell => cell.X);
			for (int x = minimumX; x <= maximumX; x++)
				region.Add(new CeramicCell(x, z));
		}

		foreach (CeramicCell cell in wallCells)
		{
			int wallNeighbors = Enum.GetValues<CeramicDirection>()
				.Count(direction => wallCells.Contains(CeramicGeometry.Offset(cell, direction)));
			if (wallNeighbors != 2)
				throw new InvalidDataException("The CeramicFish village wall is not a degree-two cycle.");
		}
		wall = wallCells;
		return region;
	}

	private static List<CeramicDirection> CreateCircleQuadrantSteps(int radius)
	{
		List<CeramicDirection> steps = new(radius * 2);
		int x = 0;
		int z = -radius;
		int radiusSquared = radius * radius;
		while (x < radius || z < 0)
		{
			CeramicDirection step;
			if (x == radius)
				step = CeramicDirection.South;
			else if (z == 0)
				step = CeramicDirection.East;
			else
			{
				int eastError = Math.Abs(((x + 1) * (x + 1)) + (z * z) - radiusSquared);
				int southError = Math.Abs((x * x) + ((z + 1) * (z + 1)) - radiusSquared);
				step = eastError <= southError ? CeramicDirection.East : CeramicDirection.South;
			}

			steps.Add(step);
			if (step == CeramicDirection.East)
				x++;
			else
				z++;
		}
		return steps;
	}

	private static CeramicDirection RotateClockwise(CeramicDirection direction, int quarterTurns) =>
		(CeramicDirection)(((int)direction + quarterTurns) % 4);

	private static int PercentageCeiling(int value, int percentage) =>
		(value * percentage + 99) / 100;

	private static int PercentageFloor(int value, int percentage) =>
		value * percentage / 100;

	internal static void VerifyRoundTrip(
		CeramicFishDefinition expected,
		CeramicFishDefinition actual)
	{
		if (actual.FormatVersion != expected.FormatVersion || actual.Id != expected.Id
			|| !actual.ConnectionPolicies.SequenceEqual(expected.ConnectionPolicies)
			|| !actual.ComponentAdjacencyPolicies.SequenceEqual(expected.ComponentAdjacencyPolicies)
			|| !actual.ComponentTagPolicies.SequenceEqual(expected.ComponentTagPolicies)
			|| !actual.ComponentEntryPolicies.SequenceEqual(expected.ComponentEntryPolicies)
			|| !actual.WallFeaturePolicies.SequenceEqual(expected.WallFeaturePolicies)
			|| !actual.InteriorFeaturePolicies.SequenceEqual(expected.InteriorFeaturePolicies)
			|| actual.Prefabs.Count != expected.Prefabs.Count)
			throw new InvalidDataException("The CeramicFish JSON root did not round trip correctly.");

		for (int index = 0; index < expected.Prefabs.Count; index++)
		{
			CeramicPrefabDefinition left = expected.Prefabs[index];
			CeramicPrefabDefinition right = actual.Prefabs[index];
			if (left.Id != right.Id || left.SizeX != right.SizeX || left.SizeY != right.SizeY
				|| left.SizeZ != right.SizeZ || left.AllowedRotations != right.AllowedRotations
				|| left.Weight != right.Weight || !left.Tags.SequenceEqual(right.Tags)
				|| !left.Entities.SequenceEqual(right.Entities) || !left.Sockets.SequenceEqual(right.Sockets))
				throw new InvalidDataException($"CeramicFish prefab '{left.Id}' did not round trip correctly.");
		}
	}

	private static CeramicPrefabDefinition CreatePrefab(
		string id,
		IReadOnlyList<string> tags,
		IReadOnlyList<string> rows,
		params (CeramicDirection Direction, string Type)[] connections)
	{
		if (rows.Count != PrefabSize || rows.Any(row => row.Length != PrefabSize))
			throw new ArgumentException("CeramicFish test prefabs must be 3x3.", nameof(rows));
		List<CeramicEntity> entities = [];
		for (int z = 0; z < PrefabSize; z++)
		for (int x = 0; x < PrefabSize; x++)
		{
			int value = Value(rows[z][x]);
			if (value != 0) entities.Add(new(value, x, 0, z));
		}
		Dictionary<CeramicDirection, string> authored = connections.ToDictionary(
			connection => connection.Direction, connection => connection.Type);
		CeramicSocket[] sockets = Enum.GetValues<CeramicDirection>()
			.Select(direction => new CeramicSocket(direction,
				authored.GetValueOrDefault(direction, CeramicSocket.NoConnection)))
			.ToArray();
		return new(id, tags, PrefabSize, 1, PrefabSize, entities, sockets,
			CeramicRotationOptions.All, Weight: 1);
	}

	private static int Value(char symbol) => symbol switch
	{
		'_' => 0,
		'.' => 1,
		'H' => 2,
		'g' => 3,
		'#' => 4,
		'r' => 5,
		'W' => 6,
		'V' => 7,
		_ => throw new InvalidDataException($"Unknown CeramicFish test-palette symbol '{symbol}'."),
	};
}
