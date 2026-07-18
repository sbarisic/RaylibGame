using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.Graphics;

namespace VoxelgineEngine.Tests;

public sealed class PlayerPhysicsCorrectionTests
{
	[Fact]
	public void LeavingLedgeAppliesGravityImmediatelyWhileKeepingCoyoteJumpGrace()
	{
		Player player = CreatePlayer();
		player.ApplyPhysicsState(new PlayerPhysicsState(
			new Vector3(2.5f, 2.6f, 0.5f),
			Vector3.Zero,
			0.1f,
			0f,
			0f,
			0f,
			Vector3.Zero,
			true,
			false));
		ChunkMap map = new();
		NetworkInputSource source = new();
		InputMgr input = new(source);
		source.SetState(new InputState());
		input.Tick(0f);

		player.UpdatePhysics(new PhysicsWorld(map), new PhysData(), 0.015f, input);
		PlayerPhysicsState state = player.CapturePhysicsState();

		Assert.True(state.Velocity.Y < 0f);
		Assert.InRange(state.GroundGraceRemaining, 0.084f, 0.086f);
		Assert.False(state.WasGrounded);
	}

	[Fact]
	public unsafe void CoyoteGraceAllowsJumpWithoutGroundHovering()
	{
		Player player = CreatePlayer();
		player.ApplyPhysicsState(new PlayerPhysicsState(
			new Vector3(2.5f, 2.6f, 0.5f),
			Vector3.Zero,
			0.05f,
			0f,
			0f,
			0f,
			Vector3.Zero,
			false,
			false));
		NetworkInputSource source = new();
		InputMgr input = new(source);
		InputState state = new();
		state.KeysDown[(int)InputKey.Space] = true;
		source.SetState(state);
		input.Tick(0f);

		player.UpdatePhysics(new PhysicsWorld(new ChunkMap()), new PhysData(), 0.015f, input);
		PlayerPhysicsState result = player.CapturePhysicsState();

		Assert.True(result.Velocity.Y > 5f);
		Assert.Equal(0f, result.GroundGraceRemaining);
		Assert.False(result.WasGrounded);
	}

	[Fact]
	public unsafe void JumpLeavesGroundAndReceivesGravityOnTheSameTick()
	{
		ChunkMap map = new();
		map.SetBlock(0, 0, 0, BlockType.Stone);
		Player player = CreatePlayer();
		player.ApplyPhysicsState(new PlayerPhysicsState(
			new Vector3(0.5f, 2.6f, 0.5f),
			Vector3.Zero,
			0.1f,
			0f,
			0f,
			0f,
			Vector3.Zero,
			true,
			false));
		NetworkInputSource source = new();
		InputMgr input = new(source);
		InputState state = new();
		state.KeysDown[(int)InputKey.Space] = true;
		source.SetState(state);
		input.Tick(0f);

		player.UpdatePhysics(new PhysicsWorld(map), new PhysData(), 0.015f, input);
		PlayerPhysicsState result = player.CapturePhysicsState();

		Assert.InRange(result.Velocity.Y, 5.7f, 5.9f);
		Assert.True(result.Position.Y > 2.6f);
		Assert.False(result.WasGrounded);
	}

	[Fact]
	public void ApplyPhysicsStateRejectsNonFiniteValues()
	{
		Player player = CreatePlayer();
		Vector3 original = player.Position;
		PlayerPhysicsState invalid = new(
			new Vector3(float.PositiveInfinity, 0f, 0f),
			Vector3.Zero,
			0f,
			0f,
			0f,
			0f,
			Vector3.Zero,
			false,
			false);

		Assert.False(player.ApplyPhysicsState(invalid));
		Assert.Equal(original, player.Position);
	}

	[Fact]
	public void WaterDragAffectsEveryVelocityAxisOnce()
	{
		Vector3 velocity = new(10f, -5f, 2f);
		PhysicsUtils.ApplyDrag(ref velocity, 4f, 0.1f);

		Assert.Equal(new Vector3(6f, -3f, 1.2f), velocity);
	}

	[Theory]
	[InlineData(89.9f)]
	[InlineData(-89.9f)]
	public unsafe void ForwardInputHasNoArbitraryHorizontalDirectionAtVerticalPitch(float pitch)
	{
		Player player = CreatePlayer();
		player.Camera.CamAngle = new Vector3(0f, pitch, 0f);
		player.UpdateDirectionVectors();
		player.ApplyPhysicsState(new PlayerPhysicsState(
			new Vector3(0.5f, 10f, 0.5f),
			Vector3.Zero,
			0f,
			0f,
			0f,
			0f,
			Vector3.Zero,
			false,
			false));
		NetworkInputSource source = new();
		InputMgr input = new(source);
		InputState state = new();
		state.KeysDown[(int)InputKey.W] = true;
		source.SetState(state);
		input.Tick(0f);

		player.UpdatePhysics(new PhysicsWorld(new ChunkMap()), new PhysData(), 0.015f, input);
		PlayerPhysicsState result = player.CapturePhysicsState();

		Assert.Equal(0f, result.Velocity.X);
		Assert.Equal(0f, result.Velocity.Z);
	}

	[Fact]
	public unsafe void NoclipMovesThroughSolidVoxelsAndIsCapturedInPhysicsState()
	{
		ChunkMap map = new();
		map.SetBlock(0, 0, 1, BlockType.Stone);
		map.SetBlock(0, 1, 1, BlockType.Stone);
		Player player = CreatePlayer();
		player.SetPosition(new Vector3(0.5f, 1.6f, 0.5f));
		player.Camera.CamAngle = Vector3.Zero;
		player.UpdateDirectionVectors();
		player.NoClip = true;
		NetworkInputSource source = new();
		InputMgr input = new(source);
		InputState inputState = new();
		inputState.KeysDown[(int)InputKey.W] = true;
		source.SetState(inputState);
		input.Tick(0f);

		player.UpdatePhysics(new PhysicsWorld(map), new PhysData(), 0.1f, input);
		PlayerPhysicsState result = player.CapturePhysicsState();

		Assert.Equal(new Vector3(0.5f, 1.6f, 2f), result.Position);
		Assert.Equal(Vector3.Zero, result.Velocity);
		Assert.True(result.NoClip);
	}

	[Theory]
	[InlineData(75f)]
	[InlineData(-75f)]
	public unsafe void NoclipVerticalControlsUseWorldUp(float pitch)
	{
		Player player = CreatePlayer();
		player.SetPosition(new Vector3(4f, 5f, 6f));
		player.Camera.CamAngle = new Vector3(35f, pitch, 0f);
		player.UpdateDirectionVectors();
		player.NoClip = true;
		NetworkInputSource source = new();
		InputMgr input = new(source);
		InputState inputState = new();
		inputState.KeysDown[(int)InputKey.Space] = true;
		source.SetState(inputState);
		input.Tick(0f);

		player.UpdatePhysics(new PhysicsWorld(new ChunkMap()), new PhysData(), 0.1f, input);

		Assert.Equal(new Vector3(4f, 6.5f, 6f), player.Position);
	}

	[Fact]
	public void NoclipClearsCollisionAndWaterTransients()
	{
		Player player = CreatePlayer();
		player.ApplyPhysicsState(new PlayerPhysicsState(
			new Vector3(1f, 2f, 3f),
			new Vector3(4f, 5f, 6f),
			0.1f,
			0.05f,
			0.6f,
			0.5f,
			Vector3.UnitX,
			true,
			true,
			true));
		NetworkInputSource source = new();
		InputMgr input = new(source);
		source.SetState(new InputState());
		input.Tick(0f);

		player.UpdatePhysics(new PhysicsWorld(new ChunkMap()), new PhysData(), 0.015f, input);
		PlayerPhysicsState result = player.CapturePhysicsState();

		Assert.Equal(Vector3.Zero, result.Velocity);
		Assert.Equal(0f, result.GroundGraceRemaining);
		Assert.Equal(0f, result.JumpCooldownRemaining);
		Assert.Equal(0f, result.RecentJumpRemaining);
		Assert.Equal(0f, result.HeadBumpCooldownRemaining);
		Assert.Equal(Vector3.Zero, result.LastWallNormal);
		Assert.False(result.WasGrounded);
		Assert.False(result.WasInWater);
		Assert.True(result.NoClip);
	}

	[Fact]
	public unsafe void RepeatedCorrectionsRemainExactForTenThousandTicks()
	{
		ChunkMap map = new();
		map.SetBlock(0, 0, 0, BlockType.Stone);
		map.SetBlock(0, 4, 0, BlockType.Stone);
		PhysicsWorld world = new(map);
		PhysData physics = new();
		Player authoritative = CreatePlayer();
		Player predicted = CreatePlayer();
		authoritative.SetPosition(new Vector3(0.5f, 2.6f, 0.5f));
		predicted.SetPosition(authoritative.Position);

		NetworkInputSource authoritativeSource = new();
		NetworkInputSource predictedSource = new();
		InputMgr authoritativeInput = new(authoritativeSource);
		InputMgr predictedInput = new(predictedSource);
		ClientInputBuffer inputBuffer = new();
		ClientPrediction prediction = new();
		PredictionReconciler reconciler = new();

		for (int tick = 1; tick <= 10_000; tick++)
		{
			InputState inputState = new();
			inputState.KeysDown[(int)InputKey.Space] = tick % 120 < 2;
			authoritativeSource.SetState(inputState);
			predictedSource.SetState(inputState);
			authoritativeInput.Tick(tick * 0.015f);
			predictedInput.Tick(tick * 0.015f);
			inputBuffer.Record(tick, inputState, Vector2.Zero);

			authoritative.UpdatePhysics(world, physics, 0.015f, authoritativeInput);
			predicted.UpdatePhysics(world, physics, 0.015f, predictedInput);
			if (tick % 127 == 0)
			{
				PlayerPhysicsState perturbed = predicted.CapturePhysicsState();
				predicted.ApplyPhysicsState(perturbed with { Position = perturbed.Position + new Vector3(0.25f, 0f, 0f) });
			}

			prediction.RecordPrediction(tick, predicted.CapturePhysicsState());
			PlayerPhysicsState serverState = authoritative.CapturePhysicsState();
			if (prediction.ProcessServerSnapshot(tick, serverState))
			{
				reconciler.Reconcile(
					predicted,
					serverState,
					tick,
					tick,
					inputBuffer,
					prediction,
					world,
					physics,
					0.015f);
			}

			Assert.Equal(serverState, predicted.CapturePhysicsState());
		}

		Assert.True(prediction.ReconciliationCount >= 78);
	}

	private static Player CreatePlayer()
	{
		FishDI services = new();
		services.AddSingleton<IFishLogging>(_ => new NullLogging());
		services.Build();
		services.CreateScope();
		return new Player(new TestEngineRunner { DI = services }, 1);
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
