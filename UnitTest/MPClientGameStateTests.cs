using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.States;

namespace UnitTest;

public sealed unsafe class MPClientGameStateTests
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

	[Fact]
	public void GameplayUiIsEnabledWhenCreatedAfterStateActivation()
	{
		GameplayInputOwnership ownership = new();

		ownership.Activate();

		Assert.True(ownership.UiInputEnabled);
		Assert.True(ownership.CursorCaptured);
	}

	[Fact]
	public void ChatAndDebugMenuOwnInputExclusively()
	{
		GameplayInputOwnership ownership = new();
		ownership.Activate();

		Assert.True(ownership.ToggleDebugMenu());
		Assert.Equal(GameplayInputMode.DebugMenu, ownership.Mode);
		Assert.True(ownership.GameplayInputSuppressed);
		Assert.False(ownership.CursorCaptured);

		Assert.True(ownership.ToggleDebugMenu());
		Assert.Equal(GameplayInputMode.Gameplay, ownership.Mode);
		Assert.True(ownership.CursorCaptured);

		Assert.True(ownership.OpenChat());
		Assert.Equal(GameplayInputMode.Chat, ownership.Mode);
		Assert.False(ownership.ToggleDebugMenu());
		Assert.Equal(GameplayInputMode.Chat, ownership.Mode);
		Assert.True(ownership.GameplayInputSuppressed);
		Assert.False(ownership.CursorCaptured);

		ownership.CloseOverlay();
		Assert.Equal(GameplayInputMode.Gameplay, ownership.Mode);
		Assert.True(ownership.CursorCaptured);

		ownership.Deactivate();
		Assert.False(ownership.UiInputEnabled);
		Assert.False(ownership.CursorCaptured);
	}

	[Theory]
	[InlineData((int)GameplayInputMode.Chat)]
	[InlineData((int)GameplayInputMode.DebugMenu)]
	public void UiModesProduceNeutralNetworkAndPredictionInput(int modeValue)
	{
		GameplayInputMode mode = (GameplayInputMode)modeValue;
		InputState source = new()
		{
			GameTime = 42.5f,
			MousePos = new System.Numerics.Vector2(320, 180),
			MouseWheel = 2,
		};
		source.KeysDown[(int)InputKey.W] = true;
		source.KeysDown[(int)InputKey.Num3] = true;
		source.KeysDown[(int)InputKey.Click_Left] = true;

		InputState neutral = MPClientGameState.CreateSimulationInputState(source, mode);
		ClientInputBuffer buffer = new();
		InputStatePacket packet = buffer.Record(7, neutral, new System.Numerics.Vector2(15, -10));

		Assert.Equal(source.GameTime, neutral.GameTime);
		Assert.Equal(System.Numerics.Vector2.Zero, neutral.MousePos);
		Assert.Equal(0, neutral.MouseWheel);
		for (int key = 0; key < (int)InputKey.InputKeyCount; key++)
			Assert.False(neutral.KeysDown[key]);
		Assert.Equal(0UL, packet.KeysBitmask);
		Assert.Equal(0, packet.MouseWheel);
		Assert.True(buffer.TryGetInput(7, out BufferedInput buffered));
		Assert.Equal(0, buffered.State.MouseWheel);
	}

	[Fact]
	public void GameplayModePreservesRawInput()
	{
		InputState source = new()
		{
			GameTime = 3,
			MouseWheel = -1,
		};
		source.KeysDown[(int)InputKey.A] = true;

		InputState result = MPClientGameState.CreateSimulationInputState(
			source,
			GameplayInputMode.Gameplay
		);

		Assert.Equal(-1, result.MouseWheel);
		Assert.True(result.KeysDown[(int)InputKey.A]);
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
