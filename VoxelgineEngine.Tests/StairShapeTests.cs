using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.Pathfinding;
using Voxelgine.Graphics;

namespace VoxelgineEngine.Tests;

public sealed class StairShapeTests
{
	[Theory]
	[InlineData(StairFacing.North, 0f, 0f, 1f, 0.5f)]
	[InlineData(StairFacing.East, 0.5f, 0f, 1f, 1f)]
	[InlineData(StairFacing.South, 0f, 0.5f, 1f, 1f)]
	[InlineData(StairFacing.West, 0f, 0f, 0.5f, 1f)]
	public void StairFacingControlsUpperHalf(
		StairFacing facing, float minimumX, float minimumZ, float maximumX, float maximumZ)
	{
		BlockValue value = new(BlockType.StoneStairs, (byte)facing);
		IReadOnlyList<AABB> boxes = BlockShapeCatalog.GetCollisionBoxes(value);
		Assert.Equal(2, boxes.Count);
		Assert.Equal(new Vector3(0, 0, 0), boxes[0].Min);
		Assert.Equal(new Vector3(1, 0.5f, 1), boxes[0].Max);
		Assert.Equal(new Vector3(minimumX, 0.5f, minimumZ), boxes[1].Min);
		Assert.Equal(new Vector3(maximumX, 1, maximumZ), boxes[1].Max);
	}

	[Fact]
	public void UpsideDownMirrorsVerticalHalves()
	{
		IReadOnlyList<AABB> boxes = BlockShapeCatalog.GetCollisionBoxes(
			new BlockValue(BlockType.WoodStairs, 0b100));
		Assert.Equal(0.5f, boxes[0].Min.Y);
		Assert.Equal(1f, boxes[0].Max.Y);
		Assert.Equal(0f, boxes[1].Min.Y);
		Assert.Equal(0.5f, boxes[1].Max.Y);
	}

	[Fact]
	public void RaycastIntersectsTheActualRiserInsteadOfTheVoxelBoundary()
	{
		ChunkMap map = new();
		map.SetBlock(0, 0, 0, new BlockValue(BlockType.StoneStairs, (byte)StairFacing.North));
		Assert.True(map.TryRaycast(new Vector3(0.5f, 0.75f, 2), -Vector3.UnitZ, 4, out VoxelRaycastHit hit));
		Assert.Equal(0.5f, hit.Point.Z, 4);
		Assert.Equal(Vector3.UnitZ, hit.Normal);
	}

	[Fact]
	public void PathfinderUsesTheCanonicalWalkSurfaceQuery()
	{
		ChunkMap map = new();
		map.SetBlock(0, 0, 0, new BlockValue(BlockType.ConcreteStairs, (byte)StairFacing.East));
		VoxelPathfinder pathfinder = map.CreatePathfinder();
		Assert.True(pathfinder.IsWalkable(new Vector3Int(0, 1, 0)));
		Assert.True(BlockShapeCatalog.TryGetHighestWalkSurface(
			new BlockValue(BlockType.ConcreteStairs, (byte)StairFacing.East),
			LocalAgentFootprint.Centered(0.5f),
			1,
			out float height));
		Assert.Equal(1f, height);
	}

	[Fact]
	public void StatefulStairSurvivesColumnAndArchiveRoundTrips()
	{
		ChunkMap map = new();
		BlockValue expected = new(BlockType.ConcreteStairs, 0b110);
		map.SetBlock(-1, 2, 3, expected);
		ChunkColumnSnapshot decodedColumn = WorldColumnCodec.Decode(-1, 0, 9, WorldColumnCodec.Encode(map.CaptureColumn(-1, 0)));
		Assert.Equal(expected, decodedColumn.Chunks.Single().GetBlockValue(15, 2, 3));

		using MemoryStream archive = new();
		WorldArchive.Write(archive, map, default);
		archive.Position = 0;
		ChunkMap restored = new();
		restored.ReplaceAllColumns(WorldArchive.Read(archive).Columns);
		Assert.Equal(expected, restored.GetBlockValue(-1, 2, 3));
	}
}
