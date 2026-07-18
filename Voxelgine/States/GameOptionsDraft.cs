using Voxelgine.Engine;

namespace Voxelgine.States;

internal enum GameDisplayMode
{
	Windowed,
	BorderlessFullscreen,
	ExclusiveFullscreen,
}

internal readonly record struct GameResolution(int Width, int Height, bool UseDesktop)
{
	public static GameResolution Desktop(int width, int height)
	{
		return new GameResolution(width, height, true);
	}

	public override string ToString()
	{
		return UseDesktop ? $"Desktop ({Width} x {Height})" : $"{Width} x {Height}";
	}
}

internal sealed record GameOptionsDraft
{
	public const float MinimumMouseSensitivity = 0.05f;
	public const float MaximumMouseSensitivity = 1f;

	public int MonitorIndex { get; set; }

	public GameDisplayMode DisplayMode { get; set; }

	public GameResolution Resolution { get; set; }

	public int TargetFps { get; set; }

	public float MouseSensitivity { get; set; }

	public bool VSync { get; set; }

	public bool Msaa { get; set; }

	public int MaxChunkDrawDistance { get; set; }

	public int ChunkMeshUploadBudget { get; set; }

	public bool SetFocused { get; set; }

	public bool Resizable { get; set; }

	public static GameOptionsDraft FromConfig(GameConfig config)
	{
		ArgumentNullException.ThrowIfNull(config);

		GameDisplayMode displayMode = !config.Fullscreen
			? GameDisplayMode.Windowed
			: config.Borderless
				? GameDisplayMode.BorderlessFullscreen
				: GameDisplayMode.ExclusiveFullscreen;

		return new GameOptionsDraft
		{
			MonitorIndex = config.Monitor,
			DisplayMode = displayMode,
			Resolution = new GameResolution(
				Math.Max(1, config.WindowWidth),
				Math.Max(1, config.WindowHeight),
				displayMode != GameDisplayMode.Windowed && config.UseFSDesktopRes
			),
			TargetFps = config.TargetFPS <= 0 ? -1 : config.TargetFPS,
			MouseSensitivity = Math.Clamp(
				config.MouseSensitivity,
				MinimumMouseSensitivity,
				MaximumMouseSensitivity
			),
			VSync = config.VSync,
			Msaa = config.Msaa,
			MaxChunkDrawDistance = Math.Clamp(
				config.MaxChunkDrawDistance,
				GameConfig.MinimumMaxChunkDrawDistance,
				GameConfig.MaximumMaxChunkDrawDistance
			),
			ChunkMeshUploadBudget = Math.Clamp(
				config.ChunkMeshUploadBudget,
				GameConfig.MinimumChunkMeshUploadBudget,
				GameConfig.MaximumChunkMeshUploadBudget
			),
			SetFocused = config.SetFocused,
			Resizable = config.Resizable,
		};
	}

	public static GameOptionsDraft CreateDefaults(int primaryMonitorIndex)
	{
		return new GameOptionsDraft
		{
			MonitorIndex = primaryMonitorIndex,
			DisplayMode = GameDisplayMode.Windowed,
			Resolution = new GameResolution(1920, 1080, false),
			TargetFps = -1,
			MouseSensitivity = 0.35f,
			VSync = true,
			Msaa = true,
			MaxChunkDrawDistance = GameConfig.DefaultMaxChunkDrawDistance,
			ChunkMeshUploadBudget = GameConfig.DefaultChunkMeshUploadBudget,
			SetFocused = true,
			Resizable = true,
		};
	}

	public void ApplyTo(GameConfig config)
	{
		ArgumentNullException.ThrowIfNull(config);

		config.Monitor = MonitorIndex;
		config.TargetFPS = TargetFps <= 0 ? -1 : TargetFps;
		config.MouseSensitivity = Math.Clamp(
			MouseSensitivity,
			MinimumMouseSensitivity,
			MaximumMouseSensitivity
		);
		config.VSync = VSync;
		config.Msaa = Msaa;
		config.MaxChunkDrawDistance = Math.Clamp(
			MaxChunkDrawDistance,
			GameConfig.MinimumMaxChunkDrawDistance,
			GameConfig.MaximumMaxChunkDrawDistance
		);
		config.ChunkMeshUploadBudget = Math.Clamp(
			ChunkMeshUploadBudget,
			GameConfig.MinimumChunkMeshUploadBudget,
			GameConfig.MaximumChunkMeshUploadBudget
		);
		config.SetFocused = SetFocused;
		config.Resizable = Resizable;

		switch (DisplayMode)
		{
			case GameDisplayMode.Windowed:
				config.Fullscreen = false;
				config.Borderless = false;
				config.UseFSDesktopRes = false;
				break;

			case GameDisplayMode.BorderlessFullscreen:
				config.Fullscreen = true;
				config.Borderless = true;
				config.UseFSDesktopRes = true;
				break;

			case GameDisplayMode.ExclusiveFullscreen:
				config.Fullscreen = true;
				config.Borderless = false;
				config.UseFSDesktopRes = Resolution.UseDesktop;
				break;

			default:
				throw new ArgumentOutOfRangeException(nameof(DisplayMode));
		}

		config.WindowWidth = Math.Max(1, Resolution.Width);
		config.WindowHeight = Math.Max(1, Resolution.Height);
	}
}
