using System.Numerics;
using Voxelgine;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.Graphics;

namespace VoxelgineEngine.Tests;

public sealed class CorrectnessRegressionTests
{
	[Fact]
	public void SpatialHashGrid_DistantCoordinatesDoNotAlias()
	{
		SpatialHashGrid<string> grid = new();
		Vector3 origin = Vector3.Zero;
		Vector3 formerlyAliased = new(0, 0, 1 << 21);

		grid.Add(origin, "origin");
		grid.Add(formerlyAliased, "distant");

		Assert.Equal(2, grid.Count);
		Assert.True(grid.TryGetValue(origin, out string first));
		Assert.True(grid.TryGetValue(formerlyAliased, out string second));
		Assert.Equal("origin", first);
		Assert.Equal("distant", second);
	}

	[Theory]
	[InlineData(int.MinValue, 16)]
	[InlineData(int.MaxValue, 16)]
	[InlineData(-17, 16)]
	[InlineData(-16, 16)]
	[InlineData(-1, 16)]
	public void FloorDivisionAndMod_ReconstructOriginalValue(int value, int divisor)
	{
		int quotient = Utils.FloorDiv(value, divisor);
		int remainder = Utils.Mod(value, divisor);

		Assert.InRange(remainder, 0, divisor - 1);
		Assert.Equal((long)value, (long)quotient * divisor + remainder);
	}

	[Fact]
	public void VectorBlockLookup_FloorsNegativeCoordinates()
	{
		ChunkMap map = new();
		map.SetBlock(-1, 0, 0, BlockType.Stone);

		Assert.Equal(BlockType.Stone, map.GetBlock(new Vector3(-0.5f, 0.5f, 0.5f)));
		Assert.Equal(BlockType.None, map.GetBlock(new Vector3(0.5f, 0.5f, 0.5f)));
	}

	[Fact]
	public void ApplyColumn_InvalidReplacementPreservesExistingColumn()
	{
		ChunkMap map = new();
		map.SetBlock(0, 0, 0, BlockType.Stone);
		BlockType[] blocks = new BlockType[ChunkSnapshot.BlockCount];
		ChunkSnapshot invalid = new(1, 0, 0, blocks);
		ChunkColumnSnapshot replacement = new(0, 0, 2, new[] { invalid });

		Assert.Throws<InvalidDataException>(() => map.ApplyColumn(replacement));
		Assert.Equal(BlockType.Stone, map.GetBlock(0, 0, 0));
		Assert.True(map.IsColumnResident(0, 0));
	}

	[Fact]
	public void AxisAlignedVoxelRay_DoesNotProduceNanSteps()
	{
		List<Vector3> visited = new();
		Utils.RaycastVoxel(
			new Vector3(0.5f),
			Vector3.UnitX,
			3,
			(x, y, z, _, _) =>
			{
				visited.Add(new Vector3(x, y, z));
				return x == 3;
			});

		Assert.Equal(
			new[]
			{
				new Vector3(0, 0, 0),
				new Vector3(1, 0, 0),
				new Vector3(2, 0, 0),
				new Vector3(3, 0, 0),
			},
			visited);
	}

	[Fact]
	public void FishDi_ResolvesConcreteClassWithoutGuessingItsInterfaces()
	{
		using FishDI services = new();
		services.AddSingleton<IConcreteService, ConcreteService>();
		services.Build();
		services.CreateScope();

		ConcreteService concrete = services.GetRequiredService<ConcreteService>();
		Assert.Same(concrete, services.GetRequiredService<IConcreteService>());
	}

	private interface IConcreteService
	{
	}

	private sealed class ConcreteService : IConcreteService
	{
	}
}
