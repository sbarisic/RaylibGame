using Voxelgine.WorldGeneration;

namespace CeramicFish.TestHarness;

internal static class CeramicTestCatalog
{
	internal const int GridSize = 85;
	internal const int ImageSize = 256;
	internal const int PrefabSize = 3;

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
	};

	internal static CeramicGenerationRequest CreateRequest(int seed)
	{
		CeramicCell gate = new(GridSize / 2, GridSize - 1);
		List<CeramicCell> region = new(GridSize * GridSize);
		List<CeramicCellConstraint> constraints = new(GridSize * GridSize);
		for (int z = 0; z < GridSize; z++)
		for (int x = 0; x < GridSize; x++)
		{
			CeramicCell cell = new(x, z);
			region.Add(cell);
			bool boundary = x == 0 || z == 0 || x == GridSize - 1 || z == GridSize - 1;
			if (cell == gate)
			{
				constraints.Add(new(cell, ["defense-wall", "gate"], []));
			}
			else if (boundary)
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
				new("road", MinimumCells: 434, MaximumCells: 867),
				new("house-wall", MinimumCells: 723, MaximumCells: 1_589),
			],
		};
	}

	internal static void VerifyRoundTrip(
		CeramicFishDefinition expected,
		CeramicFishDefinition actual)
	{
		if (actual.FormatVersion != expected.FormatVersion || actual.Id != expected.Id
			|| !actual.ConnectionPolicies.SequenceEqual(expected.ConnectionPolicies)
			|| !actual.ComponentAdjacencyPolicies.SequenceEqual(expected.ComponentAdjacencyPolicies)
			|| !actual.ComponentTagPolicies.SequenceEqual(expected.ComponentTagPolicies)
			|| !actual.ComponentEntryPolicies.SequenceEqual(expected.ComponentEntryPolicies)
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
		_ => throw new InvalidDataException($"Unknown CeramicFish test-palette symbol '{symbol}'."),
	};
}
