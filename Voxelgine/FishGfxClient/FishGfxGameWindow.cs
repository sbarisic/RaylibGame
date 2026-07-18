using FishGfx.Graphics;
using System.Diagnostics;
using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.FishGfxClient.Assets;
using Voxelgine.FishGfxClient.Input;
using Voxelgine.FishGfxClient.Rendering;

namespace Voxelgine.FishGfxClient;

public sealed class FishGfxGameWindow : IFishGfxGameWindow
{
	private const float FixedDeltaTime = 0.015f;
	private readonly GameConfig config;
	private readonly IFishLogging logging;
	private GameRenderGraph renderGraph;
	private readonly FishGfxInputSource inputSource;
	private readonly Stopwatch frameLimiter = Stopwatch.StartNew();
	private GameStateImpl state;
	private FrameTiming timing;
	private float previousTime;
	private bool configurationApplyPending;
	private bool disposed;

	public FishGfxGameWindow(IFishConfig fishConfig, IFishEngineRunner engine)
	{
		ArgumentNullException.ThrowIfNull(engine);
		config = fishConfig as GameConfig
			?? throw new ArgumentException("FishGfxGameWindow requires GameConfig.", nameof(fishConfig));
		logging = engine.DI.GetRequiredService<IFishLogging>();
		WindowMode mode = ResolveMode(config);
		IReadOnlyList<MonitorInfo> monitors = RenderWindow.GetMonitors();
		MonitorInfo initialMonitor = ResolveMonitor(config.Monitor, monitors);
		config.Monitor = initialMonitor.Index;
		MonitorVideoMode? exclusiveVideoMode = ResolveExclusiveVideoMode(config, initialMonitor);
		RenderWindow = new RenderWindow(
			new RenderWindowOptions
			{
				Width = Math.Max(1, config.WindowWidth),
				Height = Math.Max(1, config.WindowHeight),
				Title = config.Title,
				Resizable = config.Resizable,
				Mode = mode,
				MonitorIndex = initialMonitor.Index,
				ExclusiveVideoMode = exclusiveVideoMode,
				VSync = config.VSync,
				PreferredVersion = new OpenGlVersion(4, 6),
				MinimumVersion = new OpenGlVersion(4, 3),
			}
		);

		GameAssetStore assets = null;
		GameRenderGraph graph = null;
		FishGfxInputSource source = null;
		try
		{
			if (config.SetFocused)
			{
				RenderWindow.Focus();
			}

			assets = new GameAssetStore(
				RenderWindow.Graphics,
				AppContext.BaseDirectory,
				(level, message) => logging.Log(level, "Assets", message)
			);
			graph = new GameRenderGraph(RenderWindow.Graphics, assets, config.Msaa);
			source = new FishGfxInputSource(RenderWindow, config);
			Assets = assets;
			renderGraph = graph;
			inputSource = source;
			InMgr = new InputMgr(source);
		}
		catch
		{
			source?.Dispose();
			graph?.Dispose();
			assets?.Dispose();
			RenderWindow.Dispose();
			throw;
		}

		RenderWindow.Resized += OnResized;
		MonitorInfo selected = RenderWindow.Monitor;
		logging.WriteLine(
			$"Using FishGfx on monitor '{selected.Name}' ({selected.CurrentVideoMode.Width}x{selected.CurrentVideoMode.Height})"
		);
		logging.WriteLine($"Scene samples: {renderGraph.SampleCount}; VSync: {RenderWindow.VSyncEnabled}");
	}

	public RenderWindow RenderWindow { get; }

	public GameAssetStore Assets { get; }

	public IReadOnlyList<MonitorInfo> Monitors => RenderWindow.GetMonitors();

	public InputMgr InMgr { get; }

	public int Width => RenderWindow.Width;

	public int Height => RenderWindow.Height;

	public float AspectRatio => Height == 0 ? 1 : (float)Width / Height;

	public void ApplyConfiguration()
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		configurationApplyPending = true;
	}

	private void ApplyConfigurationNow()
	{
		configurationApplyPending = false;
		IReadOnlyList<MonitorInfo> monitors = RenderWindow.GetMonitors();
		MonitorInfo monitor = ResolveMonitor(config.Monitor, monitors);
		config.Monitor = monitor.Index;
		int requestedWidth = Math.Max(1, config.WindowWidth);
		int requestedHeight = Math.Max(1, config.WindowHeight);
		WindowMode mode = ResolveMode(config);
		MonitorVideoMode? exclusiveVideoMode = ResolveExclusiveVideoMode(config, monitor);
		if (mode == WindowMode.ExclusiveFullscreen
			&& !config.UseFSDesktopRes
			&& !exclusiveVideoMode.HasValue)
		{
			logging.WriteLine(
				$"Requested exclusive mode {config.WindowWidth}x{config.WindowHeight} is unavailable on '{monitor.Name}'; using its current mode."
			);
		}
		RenderWindow.SetWindowMode(mode, monitor, exclusiveVideoMode);
		if (mode == WindowMode.Windowed)
		{
			RenderWindow.SetClientSize(requestedWidth, requestedHeight);
		}
		RenderWindow.VSyncEnabled = config.VSync;
		logging.WriteLine(
			$"Window mode: {RenderWindow.Mode}; monitor: '{RenderWindow.Monitor.Name}'; VSync: {RenderWindow.VSyncEnabled}"
		);

		int requestedSamples = config.Msaa ? 4 : 1;
		if (renderGraph.SampleCount != requestedSamples)
		{
			renderGraph.SetMultisampling(config.Msaa);
			logging.WriteLine($"Scene samples: {renderGraph.SampleCount}");
		}
	}

	public void SetState(GameStateImpl nextState)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(nextState);
		logging.Log(GameLogLevel.Info, "State", $"Transition old={state?.GetType().Name ?? "None"} new={nextState.GetType().Name}");
		state?.SwapFrom();
		state = nextState;
		RenderWindow.CaptureCursor = false;
		RenderWindow.ShowCursor = true;
		state.SwapTo();
		state.OnResize(this);
	}

	public bool IsOpen()
	{
		return !disposed && !RenderWindow.IsCloseRequested;
	}

	public void Close()
	{
		if (!disposed)
		{
			RenderWindow.IsCloseRequested = true;
		}
	}

	public void Tick(float gameTime)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		state?.BeginInputFrame();
		RenderWindow.PollEvents();
		if (configurationApplyPending)
		{
			ApplyConfigurationNow();
		}
		Assets.ProcessQueuedReloads();

		float deltaTime = previousTime == 0 ? 0 : Math.Clamp(gameTime - previousTime, 0, 0.25f);
		previousTime = gameTime;
		timing = new FrameTiming(gameTime, deltaTime, FixedDeltaTime, timing.InterpolationAlpha);
		InMgr.Tick(gameTime);
		state?.Tick(gameTime);
	}

	public void UpdateLockstep(float totalTime, float deltaTime)
	{
		state?.UpdateLockstep(totalTime, deltaTime, InMgr);
	}

	public void Render(float interpolationAlpha)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		GameStateImpl renderingState = state;
		if (renderingState is null)
		{
			return;
		}

		timing = timing with { InterpolationAlpha = interpolationAlpha };
		renderingState.BeginFrame(timing);
		renderGraph.Render(renderingState, timing, RenderWindow.FramebufferSize);
		ApplyFrameLimiter();
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		RenderWindow.Resized -= OnResized;
		inputSource.Dispose();
		renderGraph.Dispose();
		Assets.Dispose();
		RenderWindow.Dispose();
		disposed = true;
	}

	private static WindowMode ResolveMode(GameConfig config)
	{
		if (!config.Fullscreen)
		{
			return WindowMode.Windowed;
		}

		return config.Borderless
			? WindowMode.BorderlessFullscreen
			: WindowMode.ExclusiveFullscreen;
	}

	private static MonitorVideoMode? ResolveExclusiveVideoMode(
		GameConfig config,
		MonitorInfo monitor)
	{
		if (!config.Fullscreen || config.Borderless || config.UseFSDesktopRes)
		{
			return null;
		}

		MonitorVideoMode? selected = null;
		foreach (MonitorVideoMode mode in monitor.VideoModes)
		{
			if (mode.Width == config.WindowWidth
				&& mode.Height == config.WindowHeight
				&& (!selected.HasValue || mode.RefreshRate > selected.Value.RefreshRate))
			{
				selected = mode;
			}
		}
		return selected;
	}

	private static MonitorInfo ResolveMonitor(
		int requestedIndex,
		IReadOnlyList<MonitorInfo> monitors)
	{
		if (monitors.Count == 0)
		{
			throw new InvalidOperationException("No monitors are available.");
		}

		if (requestedIndex >= 0 && requestedIndex < monitors.Count)
		{
			return monitors[requestedIndex];
		}

		return monitors.FirstOrDefault(monitor => monitor.IsPrimary) ?? monitors[0];
	}

	private void OnResized(object sender, WindowResizeEventArgs args)
	{
		if (RenderWindow.Mode == WindowMode.Windowed)
		{
			config.WindowWidth = args.Width;
			config.WindowHeight = args.Height;
		}
		state?.OnResize(this);
	}

	private void ApplyFrameLimiter()
	{
		if (RenderWindow.VSyncEnabled || config.TargetFPS <= 0)
		{
			frameLimiter.Restart();
			return;
		}

		double targetSeconds = 1.0 / config.TargetFPS;
		double remaining = targetSeconds - frameLimiter.Elapsed.TotalSeconds;
		if (remaining > 0.002)
		{
			Thread.Sleep(TimeSpan.FromSeconds(remaining - 0.001));
		}
		while (frameLimiter.Elapsed.TotalSeconds < targetSeconds)
		{
			Thread.SpinWait(64);
		}
		frameLimiter.Restart();
	}
}
