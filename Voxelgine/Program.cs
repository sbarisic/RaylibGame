using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TextCopy;
using Voxelgine.Audio;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.FishGfxClient;
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
		IFishLogging logging = services.GetRequiredService<IFishLogging>();
		logging.Init();
		logging.WriteLine("Aurora Falls - Voxelgine Engine");
		logging.WriteLine($"Running on {Utils.GetOSName()}");

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
			RunLoop(args, engine, services, window, audio, logging, initialState);
		}
		finally
		{
			try
			{
				automaticState?.Dispose();
				engine.MultiplayerGameState?.Dispose();
				engine.EffectsPreviewState?.Dispose();
				engine.NPCPreviewState?.Dispose();
				engine.MainMenuState?.Dispose();
			}
			finally
			{
				try
				{
					audio.StopAll();
					audio.Update(0);
				}
				finally
				{
					try
					{
						audio.Dispose();
					}
					finally
					{
						window?.Dispose();
					}
				}
			}
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
				Log = logging.WriteLine
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
			logging.WriteLine(audio.IsAvailable
				? "miniaudio initialized."
				: "miniaudio unavailable; continuing silently.");
		}
		catch (Exception exception)
		{
			logging.WriteLine(
				$"Audio cue bank failed to load; continuing silently: {exception.Message}"
			);
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
