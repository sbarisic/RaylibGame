using Voxelgine.Engine;
using Voxelgine.Graphics;

namespace VoxelgineEngine.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WorldGenerationCollection
{
	public const string Name = "World generation";
}

[Collection(WorldGenerationCollection.Name)]
public sealed class WorldGenerationTests
{
	[Theory]
	[InlineData(0.0f, 1.0f)]
	[InlineData(0.7f, 1.0f)]
	[InlineData(0.8f, 1.0f)]
	[InlineData(0.9f, 0.5f)]
	[InlineData(1.0f, 0.0f)]
	[InlineData(1.1f, 0.0f)]
	public void HeightFalloffInterpolatesAndClamps(float height, float expected)
	{
		Assert.Equal(expected, WorldGenerationPlanning.CalculateHeightFalloff(height), 5);
	}

	[Fact]
	public void TreeSelectionEnforcesStrictMinimumSpacingAcrossTheCompleteCandidateSet()
	{
		List<TreeGenerationCandidate> candidates =
		[
			new(0, 0, 12),
			new(100, 100, 12),
			new(120, 100, 12),
			new(140, 100, 12),
			new(160, 100, 12),
			new(180, 100, 12),
			new(1, 1, 12),
		];

		TreeGenerationCandidate[] selected = WorldGenerationPlanning.SelectTreePositions(
			candidates,
			minimumSpacing: 10,
			selectionSeed: 12345);

		Assert.Equal(6, selected.Length);
		AssertTreeSpacing(selected, 10);
		Assert.Single(selected, candidate => candidate.X <= 1 && candidate.Z <= 1);
	}

	[Fact]
	public void TreeSelectionAcceptsExactMinimumDistanceAndIsDeterministic()
	{
		TreeGenerationCandidate[] candidates =
		[
			new(0, 0, 12),
			new(10, 0, 12),
			new(20, 0, 12),
			new(20, 10, 12),
		];

		TreeGenerationCandidate[] first = WorldGenerationPlanning.SelectTreePositions(candidates, 10, 9981);
		TreeGenerationCandidate[] second = WorldGenerationPlanning.SelectTreePositions(candidates, 10, 9981);

		Assert.Equal(candidates.Length, first.Length);
		Assert.Equal(first, second);
		AssertTreeSpacing(first, 10);
	}

	[Fact]
	public void NaturalPondPlannerAcceptsAnEnclosedBasinWithoutMutatingTerrain()
	{
		const int size = 15;
		int[] heights = CreateHeightMap(size, size, 10);
		FillHeight(heights, size, 4, 4, 10, 10, 8);
		int[] original = heights.ToArray();

		bool accepted = WorldGenerationPlanning.TryPlanNaturalPond(
			heights,
			size,
			size,
			7,
			7,
			6,
			out NaturalPondPlan plan);

		Assert.True(accepted);
		Assert.Equal(9, plan.WaterLevel);
		Assert.Equal(49, plan.Cells.Length);
		Assert.All(plan.Cells, cell => Assert.InRange(plan.WaterLevel - cell.SurfaceY, 1, 4));
		Assert.Equal(original, heights);
	}

	[Fact]
	public void NaturalPondPlannerRejectsARegionThatLeaksThroughItsSearchBoundary()
	{
		const int size = 15;
		int[] heights = CreateHeightMap(size, size, 10);
		FillHeight(heights, size, 4, 4, 10, 10, 8);
		for (int x = 11; x <= 13; x++)
			heights[x * size + 7] = 8;

		Assert.False(WorldGenerationPlanning.TryPlanNaturalPond(
			heights,
			size,
			size,
			7,
			7,
			6,
			out _));
	}

	[Fact]
	public void NaturalPondPlannerRejectsUndersizedAndExcessivelyDeepBasins()
	{
		const int size = 15;
		int[] undersized = CreateHeightMap(size, size, 10);
		FillHeight(undersized, size, 6, 6, 8, 8, 8);
		Assert.False(WorldGenerationPlanning.TryPlanNaturalPond(
			undersized,
			size,
			size,
			7,
			7,
			6,
			out _));

		int[] tooDeep = CreateHeightMap(size, size, 10);
		FillHeight(tooDeep, size, 4, 4, 10, 10, 8);
		tooDeep[8 * size + 7] = 4;
		Assert.False(WorldGenerationPlanning.TryPlanNaturalPond(
			tooDeep,
			size,
			size,
			7,
			7,
			6,
			out _));
	}

	[Fact]
	public void NaturalPondPlannerAllowsAWorldWithNoValidPond()
	{
		const int size = 15;
		int[] flatTerrain = CreateHeightMap(size, size, 8);

		Assert.False(WorldGenerationPlanning.TryPlanNaturalPond(
			flatTerrain,
			size,
			size,
			7,
			7,
			6,
			out _));
	}

	[Fact]
	public void SameSeedProducesIdenticalWorldBlocksAndDifferentSeedChangesThem()
	{
		BlockType[] first = GenerateBlockTypes(64, 64, 12345);
		BlockType[] repeated = GenerateBlockTypes(64, 64, 12345);
		BlockType[] different = GenerateBlockTypes(64, 64, 54321);

		Assert.Contains(BlockType.Wood, first);
		Assert.Contains(BlockType.Foliage, first);
		Assert.Equal(first, repeated);
		Assert.False(first.SequenceEqual(different));
	}

	private static void AssertTreeSpacing(
		IReadOnlyList<TreeGenerationCandidate> candidates,
		int minimumSpacing)
	{
		int minimumSquared = minimumSpacing * minimumSpacing;
		for (int leftIndex = 0; leftIndex < candidates.Count; leftIndex++)
		{
			for (int rightIndex = leftIndex + 1; rightIndex < candidates.Count; rightIndex++)
			{
				int deltaX = candidates[leftIndex].X - candidates[rightIndex].X;
				int deltaZ = candidates[leftIndex].Z - candidates[rightIndex].Z;
				Assert.True(
					deltaX * deltaX + deltaZ * deltaZ >= minimumSquared,
					$"Trees {candidates[leftIndex]} and {candidates[rightIndex]} are too close.");
			}
		}
	}

	private static int[] CreateHeightMap(int width, int length, int height)
	{
		int[] result = new int[width * length];
		Array.Fill(result, height);
		return result;
	}

	private static void FillHeight(
		int[] heights,
		int length,
		int minimumX,
		int minimumZ,
		int maximumX,
		int maximumZ,
		int height)
	{
		for (int x = minimumX; x <= maximumX; x++)
			for (int z = minimumZ; z <= maximumZ; z++)
				heights[x * length + z] = height;
	}

	private static BlockType[] GenerateBlockTypes(int width, int length, int seed)
	{
		ChunkMap map = new();
		map.GenerateFloatingIsland(width, length, seed);
		return map.CaptureChunks()
			.SelectMany(static snapshot => snapshot.Blocks)
			.ToArray();
	}
}
