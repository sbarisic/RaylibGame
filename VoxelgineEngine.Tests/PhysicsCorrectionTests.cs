using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.Graphics;

namespace VoxelgineEngine.Tests;

public sealed class CorrectedAabbAndRayTests
{
	[Fact]
	public void EmptyAndTouchingBoxesDoNotOverlap()
	{
		AABB first = new(Vector3.Zero, Vector3.One);
		AABB touching = new(Vector3.UnitX, Vector3.One);

		Assert.False(AABB.Empty.Overlaps(first));
		Assert.False(first.Overlaps(touching));
		Assert.False(new AABB(Vector3.Zero, new Vector3(1f, 0f, 1f)).Overlaps(first));
	}

	[Fact]
	public void EmptyStateTracksPublicExtentMutation()
	{
		AABB bounds = new(Vector3.Zero, Vector3.One);
		bounds.Size.Y = 0f;

		Assert.True(bounds.IsEmpty);
		Assert.False(bounds.Contains(Vector3.Zero));
	}

	[Fact]
	public void RayStartingInsideReturnsActualExitNormal()
	{
		bool hit = RayMath.RayIntersectsAABB(
			new Vector3(0.5f),
			new Vector3(5f, 0f, 0f),
			new AABB(Vector3.Zero, Vector3.One),
			10f,
			out float distance,
			out Vector3 normal);

		Assert.True(hit);
		Assert.Equal(0.5f, distance, 4);
		Assert.Equal(Vector3.UnitX, normal);
	}

	[Fact]
	public void EmptyBoxAndZeroDirectionNeverHit()
	{
		Assert.False(RayMath.RayIntersectsAABB(
			Vector3.Zero,
			Vector3.UnitX,
			AABB.Empty,
			10f,
			out _,
			out _));
		Assert.False(RayMath.RayIntersectsAABB(
			Vector3.Zero,
			Vector3.Zero,
			new AABB(Vector3.Zero, Vector3.One),
			10f,
			out _,
			out _));
	}

	[Fact]
	public void VoxelRaycastSupportsOriginVoxelNegativeCoordinatesAndScaledDirection()
	{
		ChunkMap map = new();
		map.SetBlock(0, 0, 0, BlockType.Stone);
		map.SetBlock(-3, 0, 0, BlockType.Stone);

		Assert.True(map.TryRaycast(new Vector3(0.25f), Vector3.UnitX, 10f, out VoxelRaycastHit inside));
		Assert.Equal((0, 0, 0), (inside.X, inside.Y, inside.Z));
		Assert.Equal(0f, inside.Distance);
		Assert.Equal(Vector3.Zero, inside.Normal);

		Assert.True(map.TryRaycast(new Vector3(-0.25f, 0.5f, 0.5f), new Vector3(-20f, 0f, 0f), 10f, out VoxelRaycastHit negative));
		Assert.Equal((-3, 0, 0), (negative.X, negative.Y, negative.Z));
		Assert.Equal(1.75f, negative.Distance, 4);
		Assert.Equal(Vector3.UnitX, negative.Normal);
		Assert.False(map.TryRaycast(Vector3.Zero, Vector3.Zero, 10f, out _));
	}

	[Fact]
	public void EntityRaycastUsesNormalizedDirectionForHitPoint()
	{
		TestEntity entity = new() { Position = new Vector3(3f, 0f, 0f), Size = Vector3.One };
		RaycastHit hit = Raycast.CastAgainstEntity(new Vector3(0f, 0.5f, 0f), new Vector3(10f, 0f, 0f), entity);

		Assert.True(hit.Hit);
		Assert.Equal(2.5f, hit.Distance, 4);
		Assert.Equal(new Vector3(2.5f, 0.5f, 0f), hit.HitPosition);
		Assert.Equal(-Vector3.UnitX, hit.HitNormal);
	}

	private sealed class TestEntity : VoxEntity
	{
	}
}

public sealed class SweptCollisionTests
{
	[Fact]
	public void HighSpeedSweepCannotTunnelThroughWall()
	{
		ChunkMap map = new();
		map.SetBlock(3, 0, 0, BlockType.Stone);
		PhysicsWorld world = new(map);
		AABB mover = PhysicsUtils.CreateEntityAABB(new Vector3(0.5f, 0f, 0.5f), new Vector3(0.8f));

		Assert.True(world.SweepAabb(mover, new Vector3(10f, 0f, 0f), PhysicsCollisionMask.Player, out SweepHit hit));
		Assert.Equal(0.21f, hit.Fraction, 4);
		Assert.Equal(-Vector3.UnitX, hit.Normal);
	}

	[Fact]
	public void DiagonalCornerReturnsBothSimultaneousNormals()
	{
		ChunkMap map = new();
		map.SetBlock(2, 0, 1, BlockType.Stone);
		map.SetBlock(1, 0, 2, BlockType.Stone);
		PhysicsWorld world = new(map);
		AABB mover = PhysicsUtils.CreateEntityAABB(new Vector3(0.5f, 0f, 0.5f), Vector3.One);

		Assert.True(world.SweepAabb(mover, new Vector3(2f, 0f, 2f), PhysicsCollisionMask.Player, out SweepHit hit));
		Assert.Equal(2, hit.NormalCount);
		Vector3[] normals = [hit.GetNormal(0), hit.GetNormal(1)];
		Assert.Contains(-Vector3.UnitX, normals);
		Assert.Contains(-Vector3.UnitZ, normals);
	}

	[Fact]
	public void SweepReportsDeterministicStartingPenetration()
	{
		ChunkMap map = new();
		map.SetBlock(0, 0, 0, BlockType.Stone);
		PhysicsWorld world = new(map);
		AABB mover = PhysicsUtils.CreateEntityAABB(new Vector3(0.5f, 0.1f, 0.5f), new Vector3(0.8f));

		Assert.True(world.SweepAabb(mover, Vector3.Zero, PhysicsCollisionMask.Player, out SweepHit hit));
		Assert.Equal(0f, hit.Fraction);
		Assert.True(hit.PenetrationDepth > 0f);
		Assert.Equal(1, hit.NormalCount);
	}

	[Fact]
	public void MoveAndSlideSettlesAboveFloorAfterFastFall()
	{
		ChunkMap map = new();
		map.SetBlock(0, 0, 0, BlockType.Stone);
		PhysicsMoveResult result = WorldCollision.MoveAndSlide(
			new PhysicsWorld(map),
			new Vector3(0.5f, 5f, 0.5f),
			new Vector3(0.8f, 1.7f, 0.8f),
			new Vector3(0f, -100f, 0f),
			0.1f,
			PhysicsCollisionMask.Player);

		Assert.True(result.Grounded);
		Assert.InRange(result.Position.Y, 1f, 1.01f);
		Assert.Equal(0f, result.Velocity.Y);
	}
}

public sealed class DoorPhysicsSnapshotTests
{
	[Fact]
	public void ReplicatedClosedAndOpeningDoorStatesProduceIdenticalColliders()
	{
		FishDI services = new();
		services.AddSingleton<IFishLogging>(_ => new NullLogging());
		TestEngineRunner runner = new() { DI = services };
		services.AddSingleton<IFishEngineRunner>(_ => runner);
		services.Build();
		services.CreateScope();
		GameSimulation simulation = new(runner);
		VEntSlidingDoor door = new();
		door.Initialize(new Vector3(3f, 0f, 0f), new Vector3(1f, 2f, 1f));
		simulation.Entities.Spawn(simulation, door);
		AABB player = PhysicsUtils.CreateEntityAABB(new Vector3(0f, 0f, 0f), new Vector3(0.8f, 1.7f, 0.8f));

		Assert.True(simulation.PhysicsWorld.SweepAabb(
			player,
			new Vector3(5f, 0f, 0f),
			PhysicsCollisionMask.Player,
			out SweepHit closedHit));
		Assert.Same(door, closedHit.Collider.Entity);

		byte[] opening = door.CaptureSnapshot();
		opening[29] = (byte)VEntSlidingDoor.DoorState.Opening;
		door.ApplySnapshot(opening);

		Assert.False(simulation.PhysicsWorld.SweepAabb(
			player,
			new Vector3(5f, 0f, 0f),
			PhysicsCollisionMask.Player,
			out _));
	}

	[Fact]
	public void EntitySnapshotPacketRoundTripsSerializedDoorState()
	{
		VEntSlidingDoor door = new();
		door.Initialize(new Vector3(4f, 2f, 8f), new Vector3(2f, 3f, 0.5f));
		EntitySnapshotPacket original = new()
		{
			NetworkId = 9,
			Position = door.Position,
			Velocity = door.Velocity,
			SnapshotData = door.CaptureSnapshot(),
		};

		EntitySnapshotPacket copy = Assert.IsType<EntitySnapshotPacket>(Packet.Deserialize(original.Serialize()));
		VEntSlidingDoor replica = new();
		replica.ApplySnapshot(copy.SnapshotData);

		Assert.Equal(door.Position, replica.Position);
		Assert.Equal(door.State, replica.State);
		Assert.True(replica.PhysicsProperties.BlocksPlayers);
	}

	[Fact]
	public void DoorBoundsAreCenteredAndOnlyClosedStateBlocksPlayers()
	{
		VEntSlidingDoor source = new();
		source.Initialize(new Vector3(10f, 2f, 20f), new Vector3(2f, 3f, 0.5f));

		Assert.Equal(new Vector3(9f, 2f, 19.75f), source.GetCollisionAABB().Min);
		Assert.True(source.PhysicsProperties.BlocksPlayers);
		Assert.False(source.PhysicsProperties.AffectedByGravity);

		byte[] opening = source.CaptureSnapshot();
		opening[29] = (byte)VEntSlidingDoor.DoorState.Opening;
		VEntSlidingDoor replica = new();
		replica.Initialize(source.Position, source.Size);
		replica.ApplySnapshot(opening);

		Assert.Equal(VEntSlidingDoor.DoorState.Opening, replica.State);
		Assert.False(replica.PhysicsProperties.BlocksPlayers);
		Assert.True(replica.GetCollisionAABB().IsEmpty);
	}

	private sealed class TestEngineRunner : IFishEngineRunner
	{
		public FishDI DI { get; set; } = null!;
		public int ChunkDrawCalls { get; set; }
		public bool DebugMode { get; set; }
		public float TotalTime { get; set; }
		public void Init() { }
	}

	private sealed class NullLogging : IFishLogging
	{
		public void Init(bool isServer = false) { }
		public void WriteLine(string message) { }
		public void ServerWriteLine(string message) { }
		public void ClientWriteLine(string message) { }
		public void ServerNetworkWriteLine(string message) { }
		public void ClientNetworkWriteLine(string message) { }
	}
}
