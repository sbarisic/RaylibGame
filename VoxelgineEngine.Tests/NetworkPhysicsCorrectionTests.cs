using System.Numerics;
using Voxelgine.Engine;

namespace VoxelgineEngine.Tests;

public sealed class InputCommandRecoveryTests
{
	[Fact]
	public void FourCommandPacketRoundTripsInOrder()
	{
		InputStatePacket original = Packet(10, 9, 8, 7);
		InputStatePacket copy = Assert.IsType<InputStatePacket>(Voxelgine.Engine.Packet.Deserialize(original.Serialize()));

		Assert.Equal([10, 9, 8, 7], copy.Commands.Select(command => command.TickNumber));
		Assert.Equal(original.Commands.Select(command => command.CameraAngle), copy.Commands.Select(command => command.CameraAngle));
	}

	[Fact]
	public unsafe void ClientPacketContainsNewestAndPreviousThreeCommands()
	{
		ClientInputBuffer buffer = new();
		InputState state = new();
		for (int tick = 1; tick <= 4; tick++)
		{
			state.MouseWheel = tick;
			buffer.Record(tick, state, new Vector2(tick, -tick));
		}

		InputStatePacket packet = buffer.Record(5, state, new Vector2(5f, -5f));

		Assert.Equal([5, 4, 3, 2], packet.Commands.Select(command => command.TickNumber));
	}

	[Fact]
	public void FirstSessionCommandDoesNotIncludeAnUnrecordedTickZero()
	{
		ClientInputBuffer buffer = new();
		InputStatePacket packet = buffer.Record(1, new InputState(), Vector2.Zero);

		Assert.Equal(1, Assert.Single(packet.Commands).TickNumber);
		Assert.False(buffer.TryGetInput(0, out _));
	}

	[Fact]
	public void QueueOrdersCommandsAndIgnoresDuplicates()
	{
		ServerCommandQueue queue = new();
		queue.Enqueue(Packet(3, 1, 2, 2));
		queue.BeginFrame();

		Assert.True(queue.TryDequeue(out InputCommand one));
		Assert.True(queue.TryDequeue(out InputCommand two));
		Assert.True(queue.TryDequeue(out InputCommand three));
		Assert.False(queue.TryDequeue(out _));
		Assert.Equal(1, one.TickNumber);
		Assert.Equal(2, two.TickNumber);
		Assert.Equal(3, three.TickNumber);
		Assert.Equal(3, queue.LastSimulatedCommandTick);
	}

	[Fact]
	public void QueueWaitsTwoFramesThenSynthesizesMissingCommand()
	{
		ServerCommandQueue queue = new();
		queue.Enqueue(Packet(2));

		queue.BeginFrame();
		Assert.False(queue.TryDequeue(out _));
		queue.BeginFrame();
		Assert.True(queue.TryDequeue(out InputCommand synthesized));
		Assert.Equal(1, synthesized.TickNumber);
		Assert.Equal(0f, synthesized.MouseWheel);
		Assert.True(queue.TryDequeue(out InputCommand recovered));
		Assert.Equal(2, recovered.TickNumber);
	}

	[Fact]
	public void QueueRejectsExcessiveLeadAndAlreadySimulatedCommands()
	{
		ServerCommandQueue queue = new();
		Assert.Equal(0, queue.Enqueue(Packet(ServerCommandQueue.MaximumAhead + 1)));
		Assert.Equal(1, queue.Enqueue(Packet(1)));
		queue.BeginFrame();
		Assert.True(queue.TryDequeue(out _));
		Assert.Equal(0, queue.Enqueue(Packet(1)));
	}

	private static InputStatePacket Packet(params int[] ticks)
	{
		return new InputStatePacket
		{
			Commands = ticks.Select(tick => new InputCommand
			{
				TickNumber = tick,
				CameraAngle = new Vector2(tick, 0f),
				MouseWheel = tick,
			}).ToArray(),
		};
	}
}

public sealed class CompletePredictionStateTests
{
	[Fact]
	public void WorldSnapshotRoundTripsCompletePhysicsState()
	{
		PlayerPhysicsState state = State(groundGrace: 0.1f, grounded: true);
		WorldSnapshotPacket original = new()
		{
			TickNumber = 44,
			Players =
			[
				new WorldSnapshotPacket.PlayerEntry
				{
					PlayerId = 3,
					Position = state.Position,
					Velocity = state.Velocity,
					LastInputTick = 19,
					PhysicsState = state,
				},
			],
		};

		WorldSnapshotPacket copy = Assert.IsType<WorldSnapshotPacket>(Packet.Deserialize(original.Serialize()));

		Assert.Equal(19, Assert.Single(copy.Players).LastInputTick);
		Assert.Equal(state, copy.Players[0].PhysicsState);
	}

	[Fact]
	public void TimerOrContactDifferenceRequiresReconciliation()
	{
		ClientPrediction prediction = new();
		PlayerPhysicsState predicted = State(groundGrace: 0.1f, grounded: true);
		prediction.RecordPrediction(10, predicted);
		PlayerPhysicsState server = State(groundGrace: 0.05f, grounded: true);

		Assert.True(prediction.ProcessServerSnapshot(10, server));
	}

	[Fact]
	public void IdenticalCompleteStateDoesNotRequireReconciliation()
	{
		ClientPrediction prediction = new();
		PlayerPhysicsState state = State(groundGrace: 0.1f, grounded: true);
		prediction.RecordPrediction(10, state);

		Assert.False(prediction.ProcessServerSnapshot(10, state));
	}

	private static PlayerPhysicsState State(float groundGrace, bool grounded) => new(
		new Vector3(1f, 2f, 3f),
		new Vector3(4f, 5f, 6f),
		groundGrace,
		0.03f,
		0.4f,
		0.2f,
		-Vector3.UnitX,
		grounded,
		false);
}
