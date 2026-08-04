using System.Text.Json;
using Voxelgine.WorldGeneration;

namespace VoxelgineEngine.Tests;

public sealed class CeramicFishTests
{
	[Fact]
	public void GeometryAndSocketCompatibilityAreDeterministic()
	{
		Assert.Equal(CeramicDirection.West,
			CeramicGeometry.Rotate(CeramicDirection.North, CeramicRotation.Rot270CW));
		Assert.Equal(new CeramicCell(4, 6),
			CeramicGeometry.Offset(new CeramicCell(4, 5), CeramicDirection.South));
		CeramicSocket east = new(CeramicDirection.East, "road");
		CeramicSocket west = new(CeramicDirection.West, "road");
		Assert.True(CeramicSocketCompatibility.AreFacingSocketsCompatible(east, west));
		Assert.True(CeramicSocketCompatibility.CreatesConnection(east, west));
		Assert.False(CeramicSocketCompatibility.CreatesConnection(
			new(CeramicDirection.East, CeramicSocket.NoConnection),
			new(CeramicDirection.West, CeramicSocket.NoConnection)));
		Assert.Equal(CeramicDeterminism.DeriveAttemptSeed(12, 3, 1),
			CeramicDeterminism.DeriveAttemptSeed(12, 3, 1));
		Assert.NotEqual(CeramicDeterminism.DeriveAttemptSeed(12, 3, 1),
			CeramicDeterminism.DeriveAttemptSeed(12, 4, 1));
	}

	[Fact]
	public void ValidationReportsStructuredDefinitionAndRequestErrors()
	{
		CeramicFish generator = new();
		CeramicFishDefinition invalid = EmptyDefinition() with { FormatVersion = 2 };
		CeramicValidationResult definition = generator.ValidateDefinition(invalid);
		Assert.False(definition.IsValid);
		Assert.Contains(definition.Errors, error => error.Code == "definition-format-version");

		CeramicGenerationRequest request = new([new(0, 0), new(2, 0)], 1);
		CeramicValidationResult requestResult = generator.ValidateRequest(request, EmptyDefinition());
		Assert.False(requestResult.IsValid);
		Assert.Contains(requestResult.Errors, error => error.Code == "request-region-disconnected");
	}

	[Fact]
	public void ConcaveRegionWithHoleGeneratesExactlyOncePerCell()
	{
		CeramicCell[] region =
		[
			new(0, 0), new(1, 0), new(2, 0),
			new(0, 1),            new(2, 1),
			new(0, 2), new(1, 2), new(2, 2),
		];
		CeramicGenerationRequest request = new(region, 42);
		CeramicFish generator = new();
		CeramicGenerationResult first = generator.Generate(request, EmptyDefinition(twoVariants: true));
		CeramicGenerationResult second = generator.Generate(request, EmptyDefinition(twoVariants: true));
		Assert.True(first.Success, first.Failure?.Message);
		Assert.Equal(region.Length, first.Placements.Count);
		Assert.Equal(region.Length, first.Placements.Select(item => item.Cell).Distinct().Count());
		Assert.Equal(first.Placements, second.Placements);
	}

	[Fact]
	public void SmallVillageSatisfiesClosedWallsRoadReachabilityAndQuotas()
	{
		const int size = 12;
		CeramicCell gate = new(size / 2, size - 1);
		List<CeramicCell> region = [];
		List<CeramicCellConstraint> constraints = [];
		for (int z = 0; z < size; z++)
		for (int x = 0; x < size; x++)
		{
			CeramicCell cell = new(x, z);
			region.Add(cell);
			bool boundary = x == 0 || z == 0 || x == size - 1 || z == size - 1;
			constraints.Add(cell == gate
				? new(cell, ["defense-wall", "gate"], [])
				: boundary ? new(cell, ["defense-wall"], ["gate"])
				: new(cell, [], ["defense-wall", "gate"]));
		}
		CeramicGenerationRequest request = new(region,
			new CeramicStart(gate, ["defense-wall", "gate"], "road", CeramicDirection.North), 91)
		{
			Entrances = [new(gate, CeramicDirection.South, "road")],
			CellConstraints = constraints,
			TagQuotas = [new("road", 18, 30), new("house-wall", 12, 40)],
		};
		CeramicGenerationResult result = new CeramicFish().Generate(request, NetworkDefinition());
		Assert.True(result.Success, result.Failure?.Message);
		Assert.Equal(size * size, result.Placements.Count);
		Assert.InRange(result.TopologyChecks, 1, request.MaxTopologyChecks);
	}

	[Fact]
	public void RetryableBudgetsBecomeAttemptsExhausted()
	{
		CeramicFish generator = new(new BudgetPlanner(), new UnexpectedPlacementSolver());
		CeramicGenerationRequest request = new([new CeramicCell(0, 0)], 7) { MaxAttempts = 3 };
		CeramicGenerationResult result = generator.Generate(request, EmptyDefinition());
		Assert.Equal(CeramicGenerationStatus.AttemptsExhausted, result.Status);
		Assert.Equal(3, result.Attempts);
		Assert.Equal(9, result.TopologyChecks);
	}

	[Fact]
	public async Task JsonStorageUsesStrictCanonicalFormatThree()
	{
		string directory = Path.Combine(Path.GetTempPath(), $"ceramic-fish-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		try
		{
			string path = Path.Combine(directory, "definition.json");
			CeramicFishJsonStorage storage = new();
			await storage.SaveAsync(path, NetworkDefinition());
			string json = await File.ReadAllTextAsync(path);
			Assert.Contains("\"formatVersion\": 3", json, StringComparison.Ordinal);
			Assert.Contains("\"degree\"", json, StringComparison.Ordinal);
			Assert.Contains("no-connection", json, StringComparison.Ordinal);
			Assert.DoesNotContain("requiredDegree", json, StringComparison.OrdinalIgnoreCase);
			Assert.DoesNotContain("allowExternalConnections", json, StringComparison.OrdinalIgnoreCase);
			CeramicFishDefinition loaded = await storage.LoadAsync(path);
			CeramicFishDefinition expected = NetworkDefinition();
			Assert.Equal(expected.Id, loaded.Id);
			Assert.Equal(expected.FormatVersion, loaded.FormatVersion);
			Assert.Equal(expected.ConnectionPolicies, loaded.ConnectionPolicies);
			Assert.Equal(expected.ComponentAdjacencyPolicies, loaded.ComponentAdjacencyPolicies);
			Assert.Equal(expected.Prefabs.Select(prefab => prefab.Id),
				loaded.Prefabs.Select(prefab => prefab.Id));

			string malformed = json.Insert(json.LastIndexOf('}'), ",\"unexpected\":true");
			await File.WriteAllTextAsync(path, malformed);
			await Assert.ThrowsAsync<CeramicDefinitionException>(async () => await storage.LoadAsync(path));
			await Assert.ThrowsAsync<CeramicDefinitionException>(async () =>
				await storage.SaveAsync(path, NetworkDefinition() with { FormatVersion = 2 }));
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Fact]
	public void CancellationThrowsOperationCanceledException()
	{
		using CancellationTokenSource source = new();
		source.Cancel();
		Assert.Throws<OperationCanceledException>(() => new CeramicFish().Generate(
			new([new CeramicCell(0, 0)], 1), EmptyDefinition(), source.Token));
	}

	private static CeramicFishDefinition EmptyDefinition(bool twoVariants = false)
	{
		List<CeramicPrefabDefinition> prefabs = [Prefab("empty-a", ["empty"])];
		if (twoVariants) prefabs.Add(Prefab("empty-b", ["empty"]));
		return new("empty", prefabs, []);
	}

	private static CeramicFishDefinition NetworkDefinition() => new(
		"network",
		[
			Prefab("empty", ["empty"]),
			Prefab("defense-straight", ["defense-wall"],
				(CeramicDirection.East, "defense-wall"), (CeramicDirection.West, "defense-wall")),
			Prefab("defense-corner", ["defense-wall"],
				(CeramicDirection.North, "defense-wall"), (CeramicDirection.East, "defense-wall")),
			Prefab("gate", ["defense-wall", "gate"],
				(CeramicDirection.East, "defense-wall"), (CeramicDirection.West, "defense-wall"),
				(CeramicDirection.North, "road"), (CeramicDirection.South, "road")),
			Prefab("road-end", ["road"], (CeramicDirection.South, "road")),
			Prefab("road-straight", ["road"],
				(CeramicDirection.North, "road"), (CeramicDirection.South, "road")),
			Prefab("road-corner", ["road"],
				(CeramicDirection.North, "road"), (CeramicDirection.East, "road")),
			Prefab("road-junction", ["road"],
				(CeramicDirection.North, "road"), (CeramicDirection.East, "road"),
				(CeramicDirection.South, "road")),
			Prefab("house-straight", ["house-wall"],
				(CeramicDirection.East, "house-wall"), (CeramicDirection.West, "house-wall")),
			Prefab("house-corner", ["house-wall"],
				(CeramicDirection.North, "house-wall"), (CeramicDirection.East, "house-wall")),
		],
		[
			new("defense-wall", new CeramicCountRange(2, 2),
				new CeramicCountRange(1, 1), new CeramicCountRange(0, 0)),
			new("house-wall", new CeramicCountRange(2, 2),
				new CeramicCountRange(1, null), new CeramicCountRange(0, 0)),
			new("road", new CeramicCountRange(1, 3),
				new CeramicCountRange(1, 1), new CeramicCountRange(1, 1), true),
		])
	{
		ComponentAdjacencyPolicies = [new("house-wall", "road")],
	};

	private static CeramicPrefabDefinition Prefab(
		string id,
		IReadOnlyList<string> tags,
		params (CeramicDirection Direction, string Type)[] connections)
	{
		Dictionary<CeramicDirection, string> authored = connections.ToDictionary(
			item => item.Direction, item => item.Type);
		return new(id, tags, 1, 1, 1, [], Enum.GetValues<CeramicDirection>()
			.Select(direction => new CeramicSocket(direction,
				authored.GetValueOrDefault(direction, CeramicSocket.NoConnection))).ToArray(),
			CeramicRotationOptions.All, 1);
	}

	private sealed class BudgetPlanner : ICeramicTopologyPlanner
	{
		public CeramicTopologyAttemptResult Plan(
			CeramicGenerationRequest request,
			CeramicFishDefinition definition,
			int attemptOrdinal,
			CancellationToken cancellationToken = default) =>
			new(CeramicTopologyAttemptStatus.BudgetExceeded, [], 3,
				new("budget", "budget", Stage: CeramicGenerationStage.Topology));
	}

	private sealed class UnexpectedPlacementSolver : ICeramicPlacementSolver
	{
		public CeramicPlacementAttemptResult Solve(
			CeramicGenerationRequest request,
			CeramicFishDefinition definition,
			IReadOnlyList<CeramicTopologyCell> topology,
			int attemptOrdinal,
			CancellationToken cancellationToken = default) =>
			throw new InvalidOperationException("Placement must not run after a topology budget failure.");
	}
}
