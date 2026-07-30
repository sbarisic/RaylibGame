using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.Engine.Server;
using Voxelgine.States;
using Voxelgine.Audio;

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
			Window = new TestGameWindow();
			Engine = new TestEngineRunner(Window);
		}

		public TestEngineRunner Engine { get; }

		public TestGameWindow Window { get; }

		public void Dispose()
		{
			Engine.Audio.Dispose();
			Window.Dispose();
		}
	}

	private sealed class TestEngineRunner : IClientEngineRunner
	{
		public TestEngineRunner(IGameWindow window)
		{
			Window = window;
			RuntimePaths = RuntimePathResolver.ResolveRuntimePaths(
				ApplicationKind.Test,
				Path.Combine(Path.GetTempPath(), $"aurora-falls-tests-{Guid.NewGuid():N}"),
				Environment.CurrentDirectory);
			Config = new GameConfig(RuntimePaths);
			Audio = new AudioSystem(new AudioSystemOptions { NoDevice = true });
		}

		public IFishLogging Logging { get; } = new NullLogging();
		public ILerpManager LerpManager { get; } = new LerpManager();
		public GameConfig Config { get; }
		public IAudioSystem Audio { get; }
		public RuntimePaths RuntimePaths { get; }
		public IGameWindow Window { get; }
		public ServerLoop HostedServer => null;
		public bool IsMultiplayerActive => false;
		public bool IsLocalPlayerSubmerged => false;
		public int ChunkDrawCalls { get; set; }
		public bool DebugMode { get; set; }
		public float TotalTime { get; set; }

		public void RequestState(ClientStateKind state) => Window.RouteState(null);
		public void Connect(string address, int port, string playerName) { }
		public ServerApplication StartHostedServer(int port, int seed, bool forceRegenerate) => throw new NotSupportedException();
		public void StopHostedServer() { }
		public void FireWeapon(System.Numerics.Vector3 start, System.Numerics.Vector3 direction, float maximumLength) { }
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

		public GameStateImpl LastState { get; private set; }

		public void RouteState(GameStateImpl state)
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
