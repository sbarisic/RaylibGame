using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.Pathfinding;
using Voxelgine.Graphics;

namespace VoxelgineEngine.Tests;

public sealed class PathSteeringTests
{
	[Fact]
	public void ElevatedFinalWaypointDoesNotCompleteAtWrongHeight()
	{
		ChunkMap map = new();
		map.SetBlock(0, 0, 0, BlockType.Stone);
		map.SetBlock(1, 1, 0, BlockType.Stone);
		PathFollower follower = new(map);

		Assert.True(follower.SetTarget(new Vector3(0.5f, 1f, 0.5f), new Vector3(1.5f, 2f, 0.5f)));
		PathSteering approach = follower.Step(new Vector3(0.5f, 1f, 0.5f));
		Assert.True(approach.JumpRequested);
		Assert.False(approach.Completed);

		PathSteering belowTarget = follower.Step(new Vector3(1.5f, 1f, 0.5f));
		Assert.True(belowTarget.JumpRequested);
		Assert.False(belowTarget.Completed);

		PathSteering reached = follower.Step(new Vector3(1.5f, 2f, 0.5f));
		Assert.True(reached.Completed);
	}
}
