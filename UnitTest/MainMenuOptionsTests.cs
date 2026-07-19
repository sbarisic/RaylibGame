using FishUI.Controls;
using FishGfx.Graphics.Shadows;
using Newtonsoft.Json;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.Engine.Input;
using Voxelgine.States;

namespace UnitTest;

public sealed class MainMenuOptionsTests
{
	[Theory]
	[InlineData(0, false, false, false)]
	[InlineData(1, true, true, true)]
	[InlineData(2, true, false, false)]
	public void ApplyToMapsDisplayModeToLegacyConfiguration(
		int modeValue,
		bool fullscreen,
		bool borderless,
		bool useDesktop)
	{
		GameDisplayMode mode = (GameDisplayMode)modeValue;
		GameConfig config = CreateConfig();
		var draft = GameOptionsDraft.FromConfig(config) with
		{
			DisplayMode = mode,
			Resolution = new GameResolution(1600, 900, useDesktop),
		};

		draft.ApplyTo(config);

		Assert.Equal(fullscreen, config.Fullscreen);
		Assert.Equal(borderless, config.Borderless);
		Assert.Equal(useDesktop && mode != GameDisplayMode.Windowed, config.UseFSDesktopRes);
		Assert.Equal(1600, config.WindowWidth);
		Assert.Equal(900, config.WindowHeight);
	}

	[Fact]
	public void FromConfigRecognizesExclusiveDesktopMode()
	{
		GameConfig config = CreateConfig();
		config.Fullscreen = true;
		config.Borderless = false;
		config.UseFSDesktopRes = true;
		config.WindowWidth = 2560;
		config.WindowHeight = 1440;

		GameOptionsDraft draft = GameOptionsDraft.FromConfig(config);

		Assert.Equal(GameDisplayMode.ExclusiveFullscreen, draft.DisplayMode);
		Assert.Equal(GameResolution.Desktop(2560, 1440), draft.Resolution);
	}

	[Fact]
	public void CustomFrameRateAndResolutionSurviveDraftRoundTrip()
	{
		GameConfig config = CreateConfig();
		config.TargetFPS = 137;
		config.WindowWidth = 1366;
		config.WindowHeight = 768;

		GameOptionsDraft draft = GameOptionsDraft.FromConfig(config);
		GameConfig destination = CreateConfig();
		draft.ApplyTo(destination);

		Assert.Equal(137, destination.TargetFPS);
		Assert.Equal(1366, destination.WindowWidth);
		Assert.Equal(768, destination.WindowHeight);
	}

	[Fact]
	public void CustomOptimizationSettingsSurviveDraftRoundTrip()
	{
		GameConfig config = CreateConfig();
		config.MaxChunkDrawDistance = 173;
		config.ChunkMeshUploadBudget = 37;
		config.SunShadowQuality = SunShadowQuality.High;
		config.VolumetricFogQuality = VolumetricFogQuality.Low;

		GameOptionsDraft draft = GameOptionsDraft.FromConfig(config);
		GameConfig destination = CreateConfig();
		draft.ApplyTo(destination);

		Assert.Equal(173, destination.MaxChunkDrawDistance);
		Assert.Equal(37, destination.ChunkMeshUploadBudget);
		Assert.Equal(SunShadowQuality.High, destination.SunShadowQuality);
		Assert.Equal(VolumetricFogQuality.Low, destination.VolumetricFogQuality);
	}

	[Fact]
	public void ShadowQualityDefaultsAndInvalidValuesUseMedium()
	{
		GameConfig config = CreateConfig();

		Assert.Equal(SunShadowQuality.Medium, config.SunShadowQuality);
		Assert.Equal(
			SunShadowQuality.Medium,
			GameOptionsDraft.CreateDefaults(0).SunShadowQuality
		);

		config.SunShadowQuality = (SunShadowQuality)999;
		GameOptionsDraft draft = GameOptionsDraft.FromConfig(config);

		Assert.Equal(SunShadowQuality.Medium, draft.SunShadowQuality);
	}

	[Fact]
	public void VolumetricFogQualityDefaultsAndInvalidValuesUseMedium()
	{
		GameConfig config = CreateConfig();
		Assert.Equal(VolumetricFogQuality.Medium, config.VolumetricFogQuality);
		Assert.Equal(
			VolumetricFogQuality.Medium,
			GameOptionsDraft.CreateDefaults(0).VolumetricFogQuality
		);

		config.VolumetricFogQuality = (VolumetricFogQuality)999;
		GameOptionsDraft draft = GameOptionsDraft.FromConfig(config);
		Assert.Equal(VolumetricFogQuality.Medium, draft.VolumetricFogQuality);
	}

	[Theory]
	[InlineData(SunShadowQuality.Off, 0, 1, 0, DirectionalShadowFilter.Pcf3x3, new int[] { })]
	[InlineData(SunShadowQuality.Low, 2, 1024, 64, DirectionalShadowFilter.Pcf3x3, new[] { 1, 4 })]
	[InlineData(SunShadowQuality.Medium, 3, 2048, 128, DirectionalShadowFilter.Pcf3x3, new[] { 1, 2, 4 })]
	[InlineData(SunShadowQuality.High, 4, 2048, 256, DirectionalShadowFilter.Pcf5x5, new[] { 1, 1, 2, 4 })]
	public void ShadowQualityMapsToExpectedRendererPreset(
		SunShadowQuality quality,
		int cascades,
		int resolution,
		int maximumDistance,
		DirectionalShadowFilter filter,
		int[] updateIntervals)
	{
		DirectionalShadowOptions options = MPClientGameState.CreateShadowOptions(
			quality,
			GameConfig.MaximumMaxChunkDrawDistance
		);

		Assert.Equal(cascades, options.CascadeCount);
		Assert.Equal(resolution, options.Resolution);
		Assert.Equal(maximumDistance, options.MaximumDistance);
		Assert.Equal(filter, options.Filter);
		Assert.Equal(updateIntervals, options.UpdateIntervals);
		Assert.Equal(0.75f, options.RasterSlopeBias);
		Assert.Equal(1f, options.RasterConstantBias);
	}

	[Fact]
	public void ShadowDistanceIsCappedByChunkDrawDistance()
	{
		DirectionalShadowOptions options = MPClientGameState.CreateShadowOptions(
			SunShadowQuality.High,
			96
		);

		Assert.Equal(96, options.MaximumDistance);
	}

	[Theory]
	[InlineData(1, 0, GameConfig.MinimumMaxChunkDrawDistance, GameConfig.MinimumChunkMeshUploadBudget)]
	[InlineData(173, 37, 173, 37)]
	[InlineData(4096, 999, GameConfig.MaximumMaxChunkDrawDistance, GameConfig.MaximumChunkMeshUploadBudget)]
	public void OptimizationSettingsAreClamped(
		int configuredDistance,
		int configuredUploadBudget,
		int expectedDistance,
		int expectedUploadBudget)
	{
		GameConfig config = CreateConfig();
		config.MaxChunkDrawDistance = configuredDistance;
		config.ChunkMeshUploadBudget = configuredUploadBudget;

		GameOptionsDraft draft = GameOptionsDraft.FromConfig(config);

		Assert.Equal(expectedDistance, draft.MaxChunkDrawDistance);
		Assert.Equal(expectedUploadBudget, draft.ChunkMeshUploadBudget);
	}

	[Theory]
	[InlineData(-10f, GameOptionsDraft.MinimumMouseSensitivity)]
	[InlineData(0.2f, 0.2f)]
	[InlineData(4f, GameOptionsDraft.MaximumMouseSensitivity)]
	public void MouseSensitivityIsClamped(float configured, float expected)
	{
		GameConfig config = CreateConfig();
		config.MouseSensitivity = configured;

		GameOptionsDraft draft = GameOptionsDraft.FromConfig(config);

		Assert.Equal(expected, draft.MouseSensitivity);
	}

	[Fact]
	public void ApplyingOptionsPreservesHiddenAndUnrelatedConfiguration()
	{
		GameConfig config = CreateConfig();
		config.Title = "Custom title";
		config.LogFolder = "custom/logs";
		config.HighDpiWindow = false;
		config.LastOptWnd_X = 321;
		config.MouseButtonDown = new[]
		{
			new KeyValuePair<InputKey, PhysicalMouseButton>(InputKey.Click_Left, PhysicalMouseButton.Right),
		};
		config.KeyDown = new[]
		{
			new KeyValuePair<InputKey, PhysicalKey>(InputKey.W, PhysicalKey.Up),
		};
		var mouseBindings = config.MouseButtonDown;
		var keyBindings = config.KeyDown;

		GameOptionsDraft draft = GameOptionsDraft.CreateDefaults(2);
		draft.ApplyTo(config);

		Assert.Equal("Custom title", config.Title);
		Assert.Equal("custom/logs", config.LogFolder);
		Assert.False(config.HighDpiWindow);
		Assert.Equal(321, config.LastOptWnd_X);
		Assert.Same(mouseBindings, config.MouseButtonDown);
		Assert.Same(keyBindings, config.KeyDown);
	}

	[Fact]
	public void DraftCloneProvidesApplyCancelDirtySemantics()
	{
		GameOptionsDraft baseline = GameOptionsDraft.CreateDefaults(0);
		GameOptionsDraft draft = baseline with { };

		Assert.Equal(baseline, draft);

		draft.VSync = false;
		Assert.NotEqual(baseline, draft);

		draft = baseline with { };
		Assert.Equal(baseline, draft);
	}

	[Fact]
	public void LegacyJsonLoadsAndRoundTripsWithoutChangingFieldNames()
	{
		const string legacyJson = """
		{
		  "Monitor": 1,
		  "WindowWidth": 1280,
		  "WindowHeight": 720,
		  "Fullscreen": true,
		  "UseFSDesktopRes": false,
		  "Borderless": false,
		  "SetFocused": false,
		  "TargetFPS": 90,
		  "MouseSensitivity": 0.2,
		  "VSync": false,
		  "Msaa": true
		}
		""";
		GameConfig config = CreateConfig();

		JsonConvert.PopulateObject(legacyJson, config);
		JsonSerializerSettings settings = new();
		settings.Converters.Add(new Newtonsoft.Json.Converters.StringEnumConverter());
		string roundTrip = JsonConvert.SerializeObject(config, settings);

		Assert.Equal(1, config.Monitor);
		Assert.Equal(1280, config.WindowWidth);
		Assert.Equal(720, config.WindowHeight);
		Assert.Equal(GameLogLevel.Debug, config.LogLevel);
		Assert.Contains("\"UseFSDesktopRes\"", roundTrip, StringComparison.Ordinal);
		Assert.Contains("\"LogLevel\":\"Debug\"", roundTrip.Replace(" ", string.Empty), StringComparison.Ordinal);
		Assert.Contains("\"MouseSensitivity\"", roundTrip, StringComparison.Ordinal);
		Assert.Equal(GameConfig.DefaultMaxChunkDrawDistance, config.MaxChunkDrawDistance);
		Assert.Equal(GameConfig.DefaultChunkMeshUploadBudget, config.ChunkMeshUploadBudget);
		Assert.Equal(SunShadowQuality.Medium, config.SunShadowQuality);
		Assert.Equal(VolumetricFogQuality.Medium, config.VolumetricFogQuality);
		Assert.Contains(
			"\"SunShadowQuality\":\"Medium\"",
			roundTrip.Replace(" ", string.Empty),
			StringComparison.Ordinal
		);
		Assert.Contains(
			"\"VolumetricFogQuality\":\"Medium\"",
			roundTrip.Replace(" ", string.Empty),
			StringComparison.Ordinal
		);
	}

	[Fact]
	public void MenuEntriesExposeDeveloperToolsOnlyInDebugMode()
	{
		IReadOnlyList<string> releaseEntries = MainMenuStateFishUI.GetMainMenuEntries(false);
		IReadOnlyList<string> debugEntries = MainMenuStateFishUI.GetMainMenuEntries(true);

		Assert.DoesNotContain("Developer Tools", releaseEntries);
		Assert.Contains("Developer Tools", debugEntries);
		Assert.Contains("Join Game", releaseEntries);
		Assert.DoesNotContain("Multiplayer", releaseEntries);
	}

	[Fact]
	public void BooleanAndPortFactoriesUseTypedControls()
	{
		ToggleSwitch toggle = MainMenuStateFishUI.CreateToggle("typed_toggle");
		NumericUpDown port = MainMenuStateFishUI.CreatePortInput("typed_port");

		Assert.True(toggle.ShowLabels);
		Assert.Equal("typed_toggle", toggle.ID);
		Assert.Equal(1, port.MinValue);
		Assert.Equal(65535, port.MaxValue);
		Assert.Equal(1, port.Step);
		Assert.Equal("typed_port", port.ID);
		Assert.Null(typeof(GameConfig).GetMethod("GetVariables"));
	}

	[Fact]
	public void WindowedResolutionChoicesIncludeStandard720pMode()
	{
		IReadOnlyList<GameResolution> choices = MainMenuStateFishUI.BuildResolutionChoices(
			GameDisplayMode.Windowed,
			Array.Empty<GameResolution>(),
			new GameResolution(1920, 1080, false),
			GameResolution.Desktop(5120, 1440)
		);

		Assert.Contains(new GameResolution(1280, 720, false), choices);
		Assert.Contains(new GameResolution(1920, 1080, false), choices);
	}

	[Fact]
	public void ExclusiveResolutionChoicesDoNotInventUnsupportedModes()
	{
		IReadOnlyList<GameResolution> choices = MainMenuStateFishUI.BuildResolutionChoices(
			GameDisplayMode.ExclusiveFullscreen,
			new[] { new GameResolution(1920, 1080, false) },
			new GameResolution(1920, 1080, false),
			GameResolution.Desktop(5120, 1440)
		);

		Assert.DoesNotContain(new GameResolution(1280, 720, false), choices);
		Assert.Contains(GameResolution.Desktop(5120, 1440), choices);
	}

	private static GameConfig CreateConfig()
	{
		return new GameConfig(null);
	}
}
