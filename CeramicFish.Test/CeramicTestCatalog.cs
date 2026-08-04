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
		"ceramic-fish-test-v1",
		[
			CreatePrefab("tile-00", ["house-wall"], ".H.", "#HH", ".#."),
			CreatePrefab("tile-01", ["house-wall"], "g.g", "HHH", "g.g"),
			CreatePrefab("tile-02", ["house-wall"], ".#.", "H.H", ".#."),
			CreatePrefab("tile-03", ["neutral"], "g.g", ".g.", "g.g"),
			CreatePrefab("tile-04", ["road"], "g.g", ".r.", "grg"),
			CreatePrefab("tile-05", ["road"], ".r.", "#r#", ".r."),
			CreatePrefab("tile-06", ["road"], "grg", ".rr", "g.g"),
			CreatePrefab("tile-07", ["road"], ".r.", "#rr", ".r."),
			CreatePrefab("tile-08", ["defense-wall"], ".#.", "WWW", "WWW"),
			CreatePrefab("tile-09", ["defense-wall"], "gWW", "WWW", "WWW"),
			CreatePrefab("tile-10", ["defense-wall", "gate"], ".#.", "W.W", "W#W"),
			CreatePrefab("tile-11", ["neutral"], "g.g", ".g.", "g.g"),
			CreatePrefab("empty", ["empty"], "___", "___", "___"),
		],
		[
			new("defense-wall", RequiredDegree: 2, RequiredComponentCount: 1),
			new("house-wall", RequiredDegree: 2),
		]);

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
			BoundarySocket = CeramicSocket.Closed,
			Entrances = [new(gate, CeramicDirection.South, "road")],
			CellConstraints = constraints,
		};
	}

	internal static void VerifyRoundTrip(
		CeramicFishDefinition expected,
		CeramicFishDefinition actual)
	{
		if (actual.FormatVersion != expected.FormatVersion || actual.Id != expected.Id
			|| !actual.ConnectionPolicies.SequenceEqual(expected.ConnectionPolicies)
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
		params string[] rows)
	{
		if (rows.Length != PrefabSize || rows.Any(row => row.Length != PrefabSize))
			throw new ArgumentException("CeramicFish test prefabs must be 3x3.", nameof(rows));
		List<CeramicEntity> entities = [];
		for (int z = 0; z < PrefabSize; z++)
		for (int x = 0; x < PrefabSize; x++)
		{
			int value = Value(rows[z][x]);
			if (value != 0) entities.Add(new(value, x, 0, z));
		}
		CeramicSocket[] sockets = Enum.GetValues<CeramicDirection>()
			.Select(direction => new CeramicSocket(direction, CeramicSocket.Closed))
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
