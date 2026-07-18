using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TextCopy;
using Voxelgine.Audio;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.FishGfxClient;
using Voxelgine.GUI;
using Voxelgine.States;

namespace Voxelgine;

internal sealed class FEngineRunner : IClientEngineRunner
{
	public FishDI DI { get; set; }

	public int ChunkDrawCalls { get; set; }

	public bool DebugMode { get; set; }

	public float TotalTime { get; set; }

	public MainMenuStateFishUI MainMenuState { get; set; }

	public NPCPreviewState NPCPreviewState { get; set; }

	public EffectsPreviewState EffectsPreviewState { get; set; }

	public MPClientGameState MultiplayerGameState { get; set; }

	public void Init()
	{
	}
}

internal static class Program
{
	private const float MaximumFrameTime = 0.25f;
	private const float FixedDeltaTime = 0.015f;

	private static void Main(string[] args)
	{
		FishDI services = BuildServices();
		IClientEngineRunner engine = (IClientEngineRunner)services.GetRequiredService<IFishEngineRunner>();
		engine.DI = services;

		GameConfig config = services.GetRequiredService<GameConfig>();
		config.LoadFromJson();
		if (args.Any(static argument =>
			argument.StartsWith("--fishgfx-auto", StringComparison.OrdinalIgnoreCase)))
		{
			config.SetFocused = false;
		}
		IFishLogging logging = services.GetRequiredService<IFishLogging>();
		logging.Init();
		FishUI.FishUIDebug.Logger = new FishUILoggingAdapter(logging);
		FishUI.FishUIDebug.Enabled = true;
		FishUI.FishUIDebug.LogControlEvents = true;
		UnhandledExceptionEventHandler unhandledExceptionHandler = (_, eventArgs) =>
		{
			logging.Log(GameLogLevel.Fatal, "BackgroundThread", "Unhandled AppDomain exception.", eventArgs.ExceptionObject as Exception);
		};
		EventHandler<UnobservedTaskExceptionEventArgs> unobservedTaskHandler = (_, eventArgs) =>
		{
			logging.Log(GameLogLevel.Error, "Task", "Unobserved task exception.", eventArgs.Exception);
			eventArgs.SetObserved();
		};
		AppDomain.CurrentDomain.UnhandledException += unhandledExceptionHandler;
		TaskScheduler.UnobservedTaskException += unobservedTaskHandler;
		logging.Log(GameLogLevel.Info, "Startup", "Aurora Falls - Voxelgine Engine");
		logging.Log(GameLogLevel.Info, "Startup", $"Build={GetBuildConfiguration()} processId={Environment.ProcessId} logLevel={logging.MinimumLevel}");
		logging.Log(GameLogLevel.Info, "Startup", $"OS={Utils.GetOSName()} workingDirectory={Environment.CurrentDirectory} baseDirectory={AppContext.BaseDirectory}");

		IAudioSystem audio = services.GetRequiredService<IAudioSystem>();
		InitializeAudio(audio, logging);
		IGameWindow window = null;
		GameStateImpl automaticState = null;

		try
		{
			window = services.GetRequiredService<IGameWindow>();
			CreateStates(engine, window);
			GameStateImpl initialState = SelectInitialState(
				args,
				engine,
				(IFishGfxGameWindow)window,
				out automaticState
			);
			window.SetState(initialState);
			engine.Init();
			if (initialState is MainMenuStateFishUI menuState)
			{
				menuState.ShowAutomaticDialog(args);
			}
			RunLoop(args, engine, services, window, audio, logging, initialState);
		}
		catch (Exception exception)
		{
			logging.Log(GameLogLevel.Fatal, "MainLoop", "Unhandled client exception.", exception);
			throw;
		}
		finally
		{
			try
			{
				logging.Log(GameLogLevel.Debug, "Shutdown", "Disposing game states.");
				DisposeStates(engine, automaticState);
			}
			finally
			{
				try
				{
					logging.Log(GameLogLevel.Debug, "Shutdown", "Stopping and disposing audio.");
					DisposeAudio(audio);
				}
				finally
				{
					try
					{
						logging.Log(GameLogLevel.Debug, "Shutdown", "Disposing graphics window and resources.");
						window?.Dispose();
					}
					finally
					{
						logging.Log(GameLogLevel.Info, "Shutdown", "Client shutdown complete.");
						AppDomain.CurrentDomain.UnhandledException -= unhandledExceptionHandler;
						TaskScheduler.UnobservedTaskException -= unobservedTaskHandler;
						(logging as IDisposable)?.Dispose();
					}
				}
			}
		}
	}

	private static string GetBuildConfiguration()
	{
#if DEBUG
		return "Debug";
#else
		return "Release";
#endif
	}

	private static void DisposeStates(IClientEngineRunner engine, GameStateImpl automaticState)
	{
		automaticState?.Dispose();
		engine.MultiplayerGameState?.Dispose();
		engine.EffectsPreviewState?.Dispose();
		engine.NPCPreviewState?.Dispose();
		engine.MainMenuState?.Dispose();
	}

	private static void DisposeAudio(IAudioSystem audio)
	{
		try
		{
			audio.StopAll();
			audio.Update(0);
		}
		finally
		{
			audio.Dispose();
		}
	}

	private static GameStateImpl SelectInitialState(
		string[] args,
		IClientEngineRunner engine,
		IFishGfxGameWindow window,
		out GameStateImpl automaticState
	)
	{
		automaticState = null;
		if (args.Contains("--fishgfx-auto-gameplay", StringComparer.OrdinalIgnoreCase))
		{
			automaticState = new FishGfxGameplaySmokeState(window, engine);
			return automaticState;
		}
		if (args.Contains("--fishgfx-auto-transition", StringComparer.OrdinalIgnoreCase))
		{
			automaticState = new FishGfxStateTransitionSmokeState(
				window,
				engine,
				engine.MainMenuState
			);
			return automaticState;
		}
		if (args.Contains("--fishgfx-auto-npc", StringComparer.OrdinalIgnoreCase))
		{
			return engine.NPCPreviewState;
		}
		if (args.Contains("--fishgfx-auto-effects", StringComparer.OrdinalIgnoreCase))
		{
			return engine.EffectsPreviewState;
		}
		return engine.MainMenuState;
	}

	private static FishDI BuildServices()
	{
		FishDI services = new();
		services.AddSingleton<IFishEngineRunner, FEngineRunner>();
		services.AddSingleton<IFishConfig, GameConfig>();
		services.AddSingleton<IClipboard, Clipboard>();
		services.AddSingleton<ILerpManager, LerpManager>();
		services.AddSingleton<IGameWindow, FishGfxGameWindow>();
		services.AddSingleton<IFishDebug, Engine.Debug>();
		services.AddSingleton<IFishLogging, FishLogging>();
		services.AddSingleton<IAudioSystem>(provider =>
		{
			IFishLogging logging = provider.GetRequiredService<IFishLogging>();
			return new AudioSystem(new AudioSystemOptions
			{
				Log = message => logging.Log(GameLogLevel.Debug, "Audio", message)
			});
		});
		_ = services.Build();
		services.CreateScope();
		return services;
	}

	private static void InitializeAudio(IAudioSystem audio, IFishLogging logging)
	{
		try
		{
			AudioCueBank.LoadDefault().RegisterWith(audio);
			audio.SetBusGain(AudioBus.Master, 0.7f);
			logging.Log(audio.IsAvailable ? GameLogLevel.Info : GameLogLevel.Warning, "Audio", audio.IsAvailable
				? "miniaudio initialized."
				: "miniaudio unavailable; continuing silently.");
		}
		catch (Exception exception)
		{
			logging.Log(GameLogLevel.Error, "Audio", "Audio cue bank failed to load; continuing silently.", exception);
		}
	}

	private static void CreateStates(IClientEngineRunner engine, IGameWindow window)
	{
		engine.DebugMode = Debugger.IsAttached;
		engine.MainMenuState = new MainMenuStateFishUI(window, engine);
		engine.NPCPreviewState = new NPCPreviewState(window, engine);
		engine.EffectsPreviewState = new EffectsPreviewState(window, engine);
		engine.MultiplayerGameState = new MPClientGameState(window, engine);
	}

	private static void RunLoop(
		string[] args,
		IClientEngineRunner engine,
		FishDI services,
		IGameWindow window,
		IAudioSystem audio,
		IFishLogging logging,
		GameStateImpl initialState)
	{
		Stopwatch stopwatch = Stopwatch.StartNew();
		ILerpManager lerpManager = services.GetRequiredService<ILerpManager>();
		bool automaticRun = args.Any(static argument =>
			argument.StartsWith("--fishgfx-auto", StringComparison.OrdinalIgnoreCase)
		);
		int automaticFrameCount = args.Contains(
			"--fishgfx-auto-gameplay",
			StringComparer.OrdinalIgnoreCase
		) ? 90 : 4;
		float simulationTime = 0;
		float accumulator = 0;
		float previousTime = 0;
		int renderedFrames = 0;

		while (window.IsOpen())
		{
			float totalTime = (float)stopwatch.Elapsed.TotalSeconds;
			engine.TotalTime = totalTime;
			float frameTime = Math.Min(totalTime - previousTime, MaximumFrameTime);
			previousTime = totalTime;
			accumulator += frameTime;

			window.Tick(totalTime);
			audio.Update(frameTime);

			while (accumulator >= FixedDeltaTime)
			{
				lerpManager.Update(FixedDeltaTime);
				window.UpdateLockstep(simulationTime, FixedDeltaTime);
				simulationTime += FixedDeltaTime;
				accumulator -= FixedDeltaTime;
			}

			engine.ChunkDrawCalls = 0;
			window.Render(accumulator / FixedDeltaTime);

			if (automaticRun && ++renderedFrames >= automaticFrameCount)
			{
				ValidateAutomaticFrame(
					(IFishGfxGameWindow)window,
					initialState
				);
				logging.WriteLine("FishGfx automatic render validation passed.");
				window.Close();
			}
		}
	}

	private static void ValidateAutomaticFrame(
		IFishGfxGameWindow window,
		GameStateImpl state)
	{
		const int channelTolerance = 16;
		const int minimumForegroundPixels = 64;
		FishGfx.Color clear = state.GetRenderSettings(
			window.RenderWindow.FramebufferSize
		).ClearColor;
		int foregroundPixels = 0;

		window.RenderWindow.ReadPixels();
		foreach (FishGfx.Color pixel in window.RenderWindow.PixelData.Span)
		{
			bool differsFromPostProcessedClear =
				Math.Abs(pixel.R - clear.R) > channelTolerance
				|| Math.Abs(pixel.G - clear.G) > channelTolerance
				|| Math.Abs(pixel.B - clear.B) > channelTolerance;
			if (differsFromPostProcessedClear
				&& ++foregroundPixels >= minimumForegroundPixels)
			{
				return;
			}
		}

		throw new InvalidOperationException(
			$"FishGfx automatic run produced fewer than {minimumForegroundPixels} foreground pixels."
		);
	}
}
