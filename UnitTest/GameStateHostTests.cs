using Voxelgine.Engine;
using Voxelgine.Engine.DI;

namespace UnitTest;

public sealed class GameStateHostTests
{
	[Fact]
	public void PreparationFailurePreservesCurrentStateAndRouting()
	{
		TestWindow window = new();
		using GameStateHost host = new(window, new NullLogging());
		TestState current = new(window);
		host.Start(() => current);
		TestState candidate = new(window) { ThrowDuringPrepare = true };

		host.Request(() => candidate);
		Assert.Throws<InvalidOperationException>(() => host.ProcessPending());

		Assert.Same(current, host.ActiveState);
		Assert.Same(current, window.RoutedState);
		Assert.Equal(0, current.DeactivateCount);
		Assert.Equal(1, candidate.DisposeCount);
	}

	[Fact]
	public void ActivationFailureRestoresPreviousRoutingAndCleansCandidate()
	{
		TestWindow window = new();
		using GameStateHost host = new(window, new NullLogging());
		TestState current = new(window);
		host.Start(() => current);
		TestState candidate = new(window) { ThrowDuringActivate = true };

		host.Request(() => candidate);
		Assert.Throws<InvalidOperationException>(() => host.ProcessPending());

		Assert.Same(current, host.ActiveState);
		Assert.Same(current, window.RoutedState);
		Assert.Equal(1, candidate.DeactivateCount);
		Assert.Equal(1, candidate.DisposeCount);
		Assert.Equal(0, current.DeactivateCount);
	}

	[Fact]
	public void RoutingFailureRestoresPreviousStateAndCleansCandidate()
	{
		TestWindow window = new();
		using GameStateHost host = new(window, new NullLogging());
		TestState current = new(window);
		host.Start(() => current);
		TestState candidate = new(window);
		window.RejectedState = candidate;

		host.Request(() => candidate);
		Assert.Throws<InvalidOperationException>(() => host.ProcessPending());

		Assert.Same(current, host.ActiveState);
		Assert.Same(current, window.RoutedState);
		Assert.Equal(1, candidate.DeactivateCount);
		Assert.Equal(1, candidate.DisposeCount);
	}

	[Fact]
	public void RollbackRoutingFailureStillCleansCandidateAndPreservesOwnership()
	{
		TestWindow window = new();
		using GameStateHost host = new(window, new NullLogging());
		TestState current = new(window);
		TestState candidate = new(window);
		host.Start(() => current);
		window.RejectAllRoutes = true;

		host.Request(() => candidate);
		Assert.Throws<InvalidOperationException>(() => host.ProcessPending());

		Assert.Same(current, host.ActiveState);
		Assert.Same(current, window.RoutedState);
		Assert.Equal(1, candidate.DeactivateCount);
		Assert.Equal(1, candidate.DisposeCount);
		window.RejectAllRoutes = false;
	}

	[Fact]
	public void SuccessfulTransitionRetiresPreviousStateExactlyOnce()
	{
		TestWindow window = new();
		using GameStateHost host = new(window, new NullLogging());
		TestState previous = new(window);
		TestState next = new(window);
		host.Start(() => previous);

		host.Request(() => next);
		host.ProcessPending();

		Assert.Same(next, host.ActiveState);
		Assert.Equal(1, previous.DeactivateCount);
		Assert.Equal(1, previous.DisposeCount);
		previous.Dispose();
		Assert.Equal(1, previous.DisposeCount);
	}

	[Fact]
	public void NestedTransitionWaitsForAnotherFrameBoundary()
	{
		TestWindow window = new();
		using GameStateHost host = new(window, new NullLogging());
		TestState nested = new(window);
		TestState first = new(window)
		{
			OnActivate = () => host.Request(() => nested),
		};

		host.Start(() => first);
		Assert.Same(first, host.ActiveState);

		host.ProcessPending();
		Assert.Same(nested, host.ActiveState);
	}

	private sealed class TestState : GameStateImpl
	{
		public TestState(IGameWindow window)
			: base(window, new TestEngineRunner())
		{
		}

		public bool ThrowDuringPrepare { get; init; }
		public bool ThrowDuringActivate { get; init; }
		public Action OnActivate { get; init; }
		public int DeactivateCount { get; private set; }
		public int DisposeCount { get; private set; }

		public override void Prepare()
		{
			if (ThrowDuringPrepare)
				throw new InvalidOperationException("prepare");
		}

		public override void Activate()
		{
			if (ThrowDuringActivate)
				throw new InvalidOperationException("activate");
			OnActivate?.Invoke();
		}

		public override void Deactivate()
		{
			DeactivateCount++;
		}

		protected override void DisposeCore()
		{
			DisposeCount++;
		}
	}

	private sealed class TestWindow : IGameWindow
	{
		public TestWindow()
		{
			InMgr = new InputMgr(new NullInputSource());
		}

		public GameStateImpl RoutedState { get; private set; }
		public GameStateImpl RejectedState { get; set; }
		public bool RejectAllRoutes { get; set; }
		public InputMgr InMgr { get; }
		public int Width => 1280;
		public int Height => 720;
		public float AspectRatio => (float)Width / Height;
		public void RouteState(GameStateImpl state)
		{
			RoutedState = state;
			if (RejectAllRoutes || (RejectedState != null && ReferenceEquals(state, RejectedState)))
				throw new InvalidOperationException("route");
		}
		public void UpdateLockstep(float totalTime, float deltaTime) { }
		public void Tick(float gameTime) { }
		public void Render(float interpolationAlpha) { }
		public void Close() { }
		public bool IsOpen() => true;
		public void Dispose() { }
	}

	private sealed class NullInputSource : IInputSource
	{
		public unsafe InputState Poll(float gameTime) => new() { GameTime = gameTime };
	}

	private sealed class TestEngineRunner : IFishEngineRunner
	{
		public IFishLogging Logging { get; } = new NullLogging();
		public ILerpManager LerpManager { get; } = new LerpManager();
		public int ChunkDrawCalls { get; set; }
		public bool DebugMode { get; set; }
		public float TotalTime { get; set; }
	}

	private sealed class NullLogging : IFishLogging
	{
		public void Init(bool IsServer = false) { }
		public void WriteLine(string message) { }
		public void ServerWriteLine(string message) { }
		public void ClientWriteLine(string message) { }
		public void ServerNetworkWriteLine(string message) { }
		public void ClientNetworkWriteLine(string message) { }
	}
}
