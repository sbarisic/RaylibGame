using Mxgmn.WaveFunctionCollapse;
using Voxelgine.WorldGeneration;

namespace VoxelgineEngine.Tests;

public sealed class WaveFunctionCollapseLibraryTests
{
	[Fact]
	public void OriginalAc4ModelHonorsPerCellConstraintsAndAdjacency()
	{
		string[] patterns = ["A", "B"];
		ConstrainedTiledModel<string> model = new(3, 1, patterns, [1.0, 1.0],
			(left, right, direction) => direction is 0 or 2 ? left != right : true);

		Assert.True(model.TryRun(1234, (x, _, value) => x != 0 || value == "A",
			CancellationToken.None, 10_000, out string[] result));
		Assert.Equal(["A", "B", "A"], result);
		Assert.False(model.BudgetExceeded);
		Assert.Null(model.ContradictionIndex);
	}

	[Fact]
	public void OriginalAc4ModelIsDeterministicAndUsesWeights()
	{
		string[] patterns = ["rare", "common"];
		int common = 0;
		for (int seed = 1; seed <= 200; seed++)
		{
			ConstrainedTiledModel<string> first = new(1, 1, patterns, [1.0, 9.0], static (_, _, _) => true);
			ConstrainedTiledModel<string> second = new(1, 1, patterns, [1.0, 9.0], static (_, _, _) => true);
			Assert.True(first.TryRun(seed, null, CancellationToken.None, 100, out string[] firstResult));
			Assert.True(second.TryRun(seed, null, CancellationToken.None, 100, out string[] secondResult));
			Assert.Equal(firstResult, secondResult);
			if (firstResult[0] == "common") common++;
		}
		Assert.InRange(common, 150, 198);
	}

	[Fact]
	public void AdjacencyWeightDiscouragesButDoesNotForbidMatchingNeighbors()
	{
		string[] patterns = ["road", "house"];
		int adjacentRoads = 0;
		for (int seed = 1; seed <= 200; seed++)
		{
			ConstrainedTiledModel<string> model = new(2, 1, patterns, [1.0, 1.0], static (_, _, _) => true,
				static (left, right, _) => left == "road" && right == "road" ? .05 : 1.0);
			Assert.True(model.TryRun(seed, null, CancellationToken.None, 1_000, out string[] result));
			if (result.SequenceEqual(["road", "road"])) adjacentRoads++;
		}
		Assert.InRange(adjacentRoads, 1, 20);
	}

	[Fact]
	public void IncompleteVillageCatalogLeavesReservationsUnmaterialized()
	{
		VillagePrefabDescriptor sealedOnly = new(
			"sealed-only", 1, [0, 90, 180, 270],
			Enum.GetValues<VillageSocketDirection>()
				.Select(static direction => new VillageSocketDescriptor(direction, [], new byte[25])).ToArray(),
			new byte[25], new byte[25], new byte[25], []);
		VillagePrefabCatalogDescriptor incomplete = new([sealedOnly], socketSemantics: ["road"]);
		WorldGenerationSettings settings = new(1776, 128, 128, 64);

		WorldPlan plan = WorldPlanGenerator.Generate(settings, [], "", villagePrefabs: incomplete);

		Assert.Empty(plan.VillageLayouts);
	}
}
