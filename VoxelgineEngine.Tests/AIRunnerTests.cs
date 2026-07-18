using Voxelgine.Engine;
using Voxelgine.Engine.AI;
using Voxelgine.Engine.DI;

namespace VoxelgineEngine.Tests;

public sealed class AIRunnerTests
{
	[Fact]
	public void PresenceEventFiresOncePerEnterLeaveCycle()
	{
		bool wasPresent = false;

		Assert.False(AIRunner.EnteredPresence(ref wasPresent, false));
		Assert.True(AIRunner.EnteredPresence(ref wasPresent, true));
		Assert.False(AIRunner.EnteredPresence(ref wasPresent, true));
		Assert.False(AIRunner.EnteredPresence(ref wasPresent, false));
		Assert.True(AIRunner.EnteredPresence(ref wasPresent, true));
	}

	[Fact]
	public void ActiveHandlerCannotInterruptItselfAfterCooldownExpires()
	{
		TestLogging logging = new();
		AIRunner runner = new(CreateInterruptProgram(), logging);
		VEntNPC npc = new();

		runner.RaiseEvent(AIEvent.OnPlayerInRange, 17);
		runner.Tick(npc, 1.1f);
		Assert.Equal(2, runner.ProgramCounter);

		runner.RaiseEvent(AIEvent.OnPlayerInRange, 17);

		Assert.Equal(2, runner.ProgramCounter);
		Assert.Single(logging.Messages, message =>
			message.Contains("EVENT OnPlayerInRange", StringComparison.Ordinal));
	}

	[Fact]
	public void DifferentEventCanInterruptActiveHandler()
	{
		TestLogging logging = new();
		AIRunner runner = new(CreateInterruptProgram(), logging);
		VEntNPC npc = new();

		runner.RaiseEvent(AIEvent.OnPlayerInRange, 17);
		runner.Tick(npc, 0.1f);
		runner.RaiseEvent(AIEvent.OnEnemyInRange, 17);

		Assert.Equal(4, runner.ProgramCounter);
		Assert.Equal(2, logging.Messages.Count(message =>
			message.Contains(" EVENT ", StringComparison.Ordinal)));
	}

	[Fact]
	public void ChatMatchConsumesOnlyTheFirstMatchingMessage()
	{
		AIRunner runner = new([new AIStep(AIInstruction.Wait, 1)], new TestLogging());
		runner.PushChatMessage("hello there");
		runner.PushChatMessage("something else");

		Assert.True(runner.ConsumeFirstMatchingChatMessage("HELLO"));
		Assert.False(runner.ConsumeFirstMatchingChatMessage("hello"));
		Assert.True(runner.ConsumeFirstMatchingChatMessage("else"));
		Assert.False(runner.ConsumeFirstMatchingChatMessage("else"));
	}

	private static AIStep[] CreateInterruptProgram()
	{
		return
		[
			new AIStep(AIInstruction.Wait, 10),
			AIStep.Handler(AIEvent.OnPlayerInRange),
			new AIStep(AIInstruction.Wait, 10),
			new AIStep(AIInstruction.Goto, 0),
			AIStep.Handler(AIEvent.OnEnemyInRange),
			new AIStep(AIInstruction.Wait, 10),
			new AIStep(AIInstruction.Goto, 0),
		];
	}

	private sealed class TestLogging : IFishLogging
	{
		public List<string> Messages { get; } = new();

		public void Init(bool isServer = false)
		{
		}

		public void WriteLine(string message) => Messages.Add(message);

		public void ServerWriteLine(string message) => Messages.Add(message);

		public void ClientWriteLine(string message) => Messages.Add(message);

		public void ServerNetworkWriteLine(string message) => Messages.Add(message);

		public void ClientNetworkWriteLine(string message) => Messages.Add(message);
	}
}
