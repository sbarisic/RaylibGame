using System.Diagnostics;
using Voxelgine.Audio;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.Engine.Server;
using Voxelgine.FishGfxClient;
using Voxelgine.GUI;

namespace Voxelgine;

/// <summary>Explicit composition root and lifetime owner for the Windows client.</summary>
internal sealed partial class ClientApplication : IClientEngineRunner, IDisposable
{
	private const float MaximumFrameTime = 0.25f;
	private const float FixedDeltaTime = 0.015f;
	private readonly string[] arguments;
	private readonly GameStateHost stateHost;
	private readonly EventHandler<UnobservedTaskExceptionEventArgs> unobservedTaskHandler;
	private readonly UnhandledExceptionEventHandler unhandledExceptionHandler;
	private ServerApplication hostedServer;
	private bool disposed;

	public ClientApplication(string[] args)
	{
		arguments = args ?? Array.Empty<string>();
		string dataRoot = GetOptionValue(arguments, "--data-root");
		AssetSourceRoot = GetOptionValue(arguments, "--asset-source-root");
		RuntimePaths = RuntimePathResolver.ResolveRuntimePaths(
			ApplicationKind.Client,
			dataRoot,
			Environment.CurrentDirectory,
			message => Console.Error.WriteLine($"[PROCESS][WARNING][RuntimePaths] {message}"));
		RuntimePaths.CreateDirectories();

		Config = new GameConfig(RuntimePaths);
		Config.LoadFromJson();
		if (IsAutomaticRun)
			Config.SetFocused = false;

		Logging = new FishLogging(Config);
		Logging.Init();
		FishUI.FishUIDebug.Logger = new FishUILoggingAdapter(Logging);
		FishUI.FishUIDebug.Enabled = true;
		FishUI.FishUIDebug.LogControlEvents = true;

		unhandledExceptionHandler = (_, eventArgs) => Logging.Log(
			GameLogLevel.Fatal,
			"BackgroundThread",
			"Unhandled AppDomain exception.",
			eventArgs.ExceptionObject as Exception);
		unobservedTaskHandler = (_, eventArgs) =>
		{
			Logging.Log(GameLogLevel.Error, "Task", "Unobserved task exception.", eventArgs.Exception);
			eventArgs.SetObserved();
		};
		AppDomain.CurrentDomain.UnhandledException += unhandledExceptionHandler;
		TaskScheduler.UnobservedTaskException += unobservedTaskHandler;

		LerpManager = new LerpManager();
		Audio = CreateAudio();
		Window = new FishGfxGameWindow(Config, Logging);
		stateHost = new GameStateHost(Window, Logging);
		DebugMode = Debugger.IsAttached;

		LogStartup();
		InitializeAudio();
		GameStateImpl initialState = CreateInitialState();
		stateHost.Start(() => initialState);
		if (initialState is States.MainMenuStateFishUI menu)
			menu.ShowAutomaticDialog(arguments);
	}

	public IFishLogging Logging { get; }

	public ILerpManager LerpManager { get; }

	public GameConfig Config { get; }

	public IAudioSystem Audio { get; }

	public RuntimePaths RuntimePaths { get; }

	public string AssetSourceRoot { get; }

	public IFishGfxGameWindow Window { get; }

	IGameWindow IClientEngineRunner.Window => Window;

	public ServerLoop HostedServer => hostedServer?.Server;

	public bool IsMultiplayerActive => stateHost.ActiveState is States.MPClientGameState { IsActive: true };

	public bool IsLocalPlayerSubmerged => stateHost.ActiveState is States.MPClientGameState multiplayer
		&& multiplayer.IsLocalPlayerSubmerged;

	public int ChunkDrawCalls { get; set; }

	public bool DebugMode { get; set; }

	public float TotalTime { get; set; }

	private bool IsAutomaticRun => arguments.Any(static argument =>
		argument.StartsWith("--fishgfx-auto", StringComparison.OrdinalIgnoreCase));

	public void Run()
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		float simulationTime = 0;
		float accumulator = 0;
		float previousTime = 0;
		int renderedFrames = 0;
		int automaticFrameCount = GetAutomaticFrameCount();

		try
		{
			while (Window.IsOpen())
			{
				stateHost.ProcessPending();
				float totalTime = (float)stopwatch.Elapsed.TotalSeconds;
				TotalTime = totalTime;
				float frameTime = Math.Min(totalTime - previousTime, MaximumFrameTime);
				previousTime = totalTime;
				accumulator += frameTime;

				Window.Tick(totalTime);
				Audio.Update(frameTime);
				while (accumulator >= FixedDeltaTime)
				{
					LerpManager.Update(FixedDeltaTime);
					Window.UpdateLockstep(simulationTime, FixedDeltaTime);
					simulationTime += FixedDeltaTime;
					accumulator -= FixedDeltaTime;
				}

				ChunkDrawCalls = 0;
				Window.Render(accumulator / FixedDeltaTime);
				if (IsAutomaticRun && ++renderedFrames >= automaticFrameCount)
				{
					ValidateAutomaticFrame((IFishGfxGameWindow)Window, stateHost.ActiveState);
					Logging.WriteLine("FishGfx automatic render validation passed.");
					Window.Close();
				}
			}
		}
		catch (Exception exception)
		{
			Logging.Log(GameLogLevel.Fatal, "MainLoop", "Unhandled client exception.", exception);
			throw;
		}
	}

	private IAudioSystem CreateAudio()
	{
		return new AudioSystem(new AudioSystemOptions
		{
			Log = message => Logging.Log(GameLogLevel.Debug, "Audio", message),
		});
	}

	private void InitializeAudio()
	{
		try
		{
			AudioCueBank.LoadDefault().RegisterWith(Audio);
			Audio.SetBusGain(AudioBus.Master, 0.7f);
			Logging.Log(Audio.IsAvailable ? GameLogLevel.Info : GameLogLevel.Warning, "Audio",
				Audio.IsAvailable ? "miniaudio initialized." : "miniaudio unavailable; continuing silently.");
		}
		catch (Exception exception)
		{
			Logging.Log(GameLogLevel.Error, "Audio", "Audio cue bank failed to load; continuing silently.", exception);
		}
	}

	private void LogStartup()
	{
#if DEBUG
		const string configuration = "Debug";
#else
		const string configuration = "Release";
#endif
		Logging.Log(GameLogLevel.Info, "Startup", "Aurora Falls - Voxelgine Engine");
		Logging.Log(GameLogLevel.Info, "Startup", $"Build={configuration} processId={Environment.ProcessId} logLevel={Logging.MinimumLevel}");
		Logging.Log(GameLogLevel.Info, "Startup", $"OS={Utils.GetOSName()} workingDirectory={Environment.CurrentDirectory} baseDirectory={AppContext.BaseDirectory}");
		Logging.Log(GameLogLevel.Info, "RuntimePaths", $"root={RuntimePaths.Root} config={RuntimePaths.ConfigurationFile} worlds={RuntimePaths.WorldDirectory} players={RuntimePaths.PlayerDirectory} logs={RuntimePaths.LogDirectory}");
	}

	private static string GetOptionValue(string[] args, string option)
	{
		for (int index = 0; index + 1 < args.Length; index++)
		{
			if (string.Equals(args[index], option, StringComparison.OrdinalIgnoreCase))
				return args[index + 1];
		}
		return null;
	}

	public void Dispose()
	{
		if (disposed)
			return;
		disposed = true;
		Logging.Log(GameLogLevel.Debug, "Shutdown", "Disposing game-state host.");
		stateHost.Dispose();
		StopHostedServer();
		try
		{
			Audio.StopAll();
			Audio.Update(0);
		}
		finally
		{
			Audio.Dispose();
			Window.Dispose();
			AppDomain.CurrentDomain.UnhandledException -= unhandledExceptionHandler;
			TaskScheduler.UnobservedTaskException -= unobservedTaskHandler;
			Logging.Log(GameLogLevel.Info, "Shutdown", "Client shutdown complete.");
			(Logging as IDisposable)?.Dispose();
		}
	}
}
