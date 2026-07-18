using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.States;

namespace UnitTest;

public sealed class MPClientGameStateTests
{
	[Fact]
	public void ClientClockUsesApplicationTimeBeforeFirstStateTick()
	{
		using TestContext context = new();
		context.Engine.TotalTime = 23.5f;
		MPClientGameState state = new(context.Window, context.Engine);

		Assert.Equal(23.5f, state.GetClientTime());
	}

	[Fact]
	public void EscapeReturnsToMenuAfterConnectionFailsDuringLoading()
	{
		using TestContext context = new();
		MPClientGameState state = new(context.Window, context.Engine);
		state.OnDisconnected("Connection attempt timed out");

		context.Window.InputSource.EscapeDown = true;
		context.Window.InMgr.Tick(1);
		state.Tick(1);

		Assert.Equal(1, context.Window.StateChangeCount);
		Assert.Null(context.Window.LastState);
	}

	private sealed class TestContext : IDisposable
	{
		public TestContext()
		{
			FishDI services = new();
			services.AddSingleton<IFishLogging>(_ => new NullLogging());
			services.Build();
			services.CreateScope();
			Engine = new TestEngineRunner { DI = services };
			Window = new TestGameWindow();
		}

		public TestEngineRunner Engine { get; }

		public TestGameWindow Window { get; }

		public void Dispose()
		{
			Window.Dispose();
		}
	}

	private sealed class TestEngineRunner : IClientEngineRunner
	{
		public FishDI DI { get; set; } = null!;
		public int ChunkDrawCalls { get; set; }
		public bool DebugMode { get; set; }
		public float TotalTime { get; set; }
		public MainMenuStateFishUI MainMenuState { get; set; } = null!;
		public NPCPreviewState NPCPreviewState { get; set; } = null!;
		public EffectsPreviewState EffectsPreviewState { get; set; } = null!;
		public MPClientGameState MultiplayerGameState { get; set; } = null!;

		public void Init()
		{
		}
	}

	private sealed class TestGameWindow : IGameWindow
	{
		public TestGameWindow()
		{
			InputSource = new TestInputSource();
			InMgr = new InputMgr(InputSource);
			InMgr.Tick(0);
		}

		public TestInputSource InputSource { get; }

		public InputMgr InMgr { get; }

		public int Width => 1280;

		public int Height => 720;

		public float AspectRatio => (float)Width / Height;

		public int StateChangeCount { get; private set; }

		public GameStateImpl? LastState { get; private set; }

		public void SetState(GameStateImpl state)
		{
			StateChangeCount++;
			LastState = state;
		}

		public void UpdateLockstep(float totalTime, float deltaTime)
		{
		}

		public void Tick(float gameTime)
		{
		}

		public void Render(float interpolationAlpha)
		{
		}

		public void Close()
		{
		}

		public bool IsOpen()
		{
			return true;
		}

		public void Dispose()
		{
		}
	}

	private sealed class TestInputSource : IInputSource
	{
		public bool EscapeDown { get; set; }

		public unsafe InputState Poll(float gameTime)
		{
			InputState state = new() { GameTime = gameTime };
			state.KeysDown[(int)InputKey.Esc] = EscapeDown;
			return state;
		}
	}

	private sealed class NullLogging : IFishLogging
	{
		public void Init(bool isServer = false)
		{
		}

		public void WriteLine(string message)
		{
		}

		public void ServerWriteLine(string message)
		{
		}

		public void ClientWriteLine(string message)
		{
		}

		public void ServerNetworkWriteLine(string message)
		{
		}

		public void ClientNetworkWriteLine(string message)
		{
		}
	}
}
