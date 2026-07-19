using FishGfx.Graphics;
using FishUI.Controls;
using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.FishGfxClient;

namespace Voxelgine.States;

public partial class MainMenuStateFishUI
{
	private static readonly int[] FrameRatePresets =
	{
		-1,
		30,
		60,
		90,
		120,
		144,
		165,
		240,
	};
	private static readonly int[] ChunkDrawDistancePresets =
	{
		64,
		96,
		GameConfig.DefaultMaxChunkDrawDistance,
		128,
		160,
		192,
		256,
		320,
	};
	private static readonly int[] ChunkMeshUploadBudgetPresets =
	{
		4,
		8,
		12,
		16,
		GameConfig.DefaultChunkMeshUploadBudget,
		32,
		48,
		64,
	};
	private static readonly GameResolution[] StandardWindowedResolutions =
	{
		new GameResolution(640, 480, false),
		new GameResolution(800, 600, false),
		new GameResolution(1024, 768, false),
		new GameResolution(1280, 720, false),
		new GameResolution(1366, 768, false),
		new GameResolution(1600, 900, false),
		new GameResolution(1920, 1080, false),
		new GameResolution(2560, 1440, false),
		new GameResolution(3840, 2160, false),
	};

	private readonly List<MonitorInfo> optionMonitors = new();
	private readonly List<GameResolution> resolutionOptions = new();
	private GameOptionsDraft optionsBaseline;
	private GameOptionsDraft optionsDraft;
	private DropDown monitorDropDown;
	private DropDown displayModeDropDown;
	private DropDown resolutionDropDown;
	private DropDown frameRateDropDown;
	private DropDown chunkDrawDistanceDropDown;
	private DropDown chunkMeshUploadBudgetDropDown;
	private DropDown sunShadowQualityDropDown;
	private DropDown volumetricFogQualityDropDown;
	private ToggleSwitch vsyncToggle;
	private ToggleSwitch msaaToggle;
	private Slider sensitivitySlider;
	private Label sensitivityValueLabel;
	private ToggleSwitch focusToggle;
	private ToggleSwitch resizableToggle;
	private Label optionsStatusLabel;
	private Button optionsApplyButton;
	private bool synchronizingOptions;

	private void CreateOptionsWindow()
	{
		optionsWindow = CreateModalWindow("Options", new Vector2(650, 540));

		var tabs = new TabControl
		{
			ID = "options_tabs",
			Position = new Vector2(10, 10),
			Size = new Vector2(622, 420),
			MinTabWidth = 120,
			MaxTabWidth = 180,
		};
		optionsWindow.AddChild(tabs);

		CreateDisplayOptions(tabs.AddTab("Display").Content);
		CreateControlOptions(tabs.AddTab("Controls").Content);
		CreatePerformanceOptions(tabs.AddTab("Performance").Content);
		CreateAdvancedOptions(tabs.AddTab("Advanced").Content);

		var defaultsButton = new Button
		{
			ID = "options_restore_defaults",
			Text = "Restore Defaults",
			Position = new Vector2(14, 448),
			Size = new Vector2(150, 40),
		};
		defaultsButton.OnButtonPressed += (_, _, _) => RestoreOptionDefaults();
		optionsWindow.AddChild(defaultsButton);

		optionsStatusLabel = new Label
		{
			ID = "options_status",
			Position = new Vector2(174, 448),
			Size = new Vector2(190, 40),
			Alignment = Align.Left,
		};
		optionsWindow.AddChild(optionsStatusLabel);

		var cancelButton = new Button
		{
			ID = "options_cancel",
			Text = "Cancel",
			Position = new Vector2(374, 448),
			Size = new Vector2(120, 40),
		};
		cancelButton.OnButtonPressed += (_, _, _) => HideModal(optionsWindow);
		optionsWindow.AddChild(cancelButton);

		optionsApplyButton = new Button
		{
			ID = "options_apply",
			Text = "Apply",
			Position = new Vector2(504, 448),
			Size = new Vector2(120, 40),
			Disabled = true,
		};
		optionsApplyButton.OnButtonPressed += (_, _, _) => ApplyOptions();
		optionsWindow.AddChild(optionsApplyButton);
	}

	private void CreateDisplayOptions(Panel content)
	{
		monitorDropDown = CreateDropDown("options_monitor");
		AddOptionRow(content, 14, "Monitor", "Choose which display the game uses.", monitorDropDown);
		monitorDropDown.OnItemSelected += (_, item) =>
		{
			if (synchronizingOptions || item.UserData is not MonitorInfo monitor)
			{
				return;
			}

			optionsDraft.MonitorIndex = monitor.Index;
			NormalizeResolutionForMonitor(monitor);
			RefreshResolutionChoices();
			UpdateOptionsDirtyState();
		};

		displayModeDropDown = CreateDropDown("options_display_mode");
		displayModeDropDown.AddItem(new DropDownItem("Windowed", GameDisplayMode.Windowed));
		displayModeDropDown.AddItem(new DropDownItem("Borderless Fullscreen", GameDisplayMode.BorderlessFullscreen));
		displayModeDropDown.AddItem(new DropDownItem("Exclusive Fullscreen", GameDisplayMode.ExclusiveFullscreen));
		AddOptionRow(content, 76, "Display Mode", "Select windowed or fullscreen presentation.", displayModeDropDown);
		displayModeDropDown.OnItemSelected += (_, item) =>
		{
			if (synchronizingOptions || item.UserData is not GameDisplayMode mode)
			{
				return;
			}

			optionsDraft.DisplayMode = mode;
			NormalizeResolutionForMonitor(GetSelectedMonitor());
			RefreshResolutionChoices();
			UpdateOptionsDirtyState();
		};

		resolutionDropDown = CreateDropDown("options_resolution");
		AddOptionRow(content, 138, "Resolution", "Refresh rate is selected automatically.", resolutionDropDown);
		resolutionDropDown.OnItemSelected += (_, item) =>
		{
			if (synchronizingOptions || item.UserData is not GameResolution resolution)
			{
				return;
			}

			optionsDraft.Resolution = resolution;
			UpdateOptionsDirtyState();
		};

		vsyncToggle = CreateToggle("options_vsync");
		AddOptionRow(content, 200, "VSync", "Synchronize presentation with the monitor.", vsyncToggle);
		vsyncToggle.OnToggleChanged += (_, value) => SetDraftValue(() => optionsDraft.VSync = value);

		msaaToggle = CreateToggle("options_msaa");
		AddOptionRow(content, 262, "4x MSAA", "Smooth geometry edges using multisampling.", msaaToggle);
		msaaToggle.OnToggleChanged += (_, value) => SetDraftValue(() => optionsDraft.Msaa = value);

		frameRateDropDown = CreateDropDown("options_frame_rate");
		AddOptionRow(content, 324, "Frame Rate Limit", "Unlimited disables the software frame limiter.", frameRateDropDown);
		frameRateDropDown.OnItemSelected += (_, item) =>
		{
			if (synchronizingOptions || item.UserData is not int frameRate)
			{
				return;
			}

			optionsDraft.TargetFps = frameRate;
			UpdateOptionsDirtyState();
		};
	}

	private void CreateControlOptions(Panel content)
	{
		var label = new Label
		{
			Text = "Mouse Sensitivity",
			Position = new Vector2(18, 24),
			Size = new Vector2(220, 24),
		};
		content.AddChild(label);

		var description = new Label
		{
			Text = "Controls how quickly the first-person camera turns.",
			Position = new Vector2(18, 50),
			Size = new Vector2(420, 22),
		};
		content.AddChild(description);

		sensitivitySlider = new Slider
		{
			ID = "options_mouse_sensitivity",
			Position = new Vector2(18, 88),
			Size = new Vector2(500, 24),
			MinValue = GameOptionsDraft.MinimumMouseSensitivity,
			MaxValue = GameOptionsDraft.MaximumMouseSensitivity,
			Step = 0.01f,
		};
		sensitivitySlider.OnValueChanged += (_, value) =>
		{
			if (synchronizingOptions)
			{
				return;
			}

			optionsDraft.MouseSensitivity = MathF.Round(value, 2);
			RefreshSensitivityLabel();
			UpdateOptionsDirtyState();
		};
		content.AddChild(sensitivitySlider);

		sensitivityValueLabel = new Label
		{
			ID = "options_mouse_sensitivity_value",
			Position = new Vector2(532, 88),
			Size = new Vector2(64, 24),
			Alignment = Align.Center,
		};
		content.AddChild(sensitivityValueLabel);
	}

	private void CreateAdvancedOptions(Panel content)
	{
		var restartNotice = new Label
		{
			Text = "These settings take effect after restarting the game.",
			Position = new Vector2(18, 18),
			Size = new Vector2(520, 28),
		};
		content.AddChild(restartNotice);

		focusToggle = CreateToggle("options_focus_on_startup");
		AddOptionRow(content, 68, "Focus Window on Startup", "Give the game keyboard focus when it opens.", focusToggle);
		focusToggle.OnToggleChanged += (_, value) => SetDraftValue(() => optionsDraft.SetFocused = value);

		resizableToggle = CreateToggle("options_resizable");
		AddOptionRow(content, 140, "Resizable Window", "Allow resizing the window by dragging its borders.", resizableToggle);
		resizableToggle.OnToggleChanged += (_, value) => SetDraftValue(() => optionsDraft.Resizable = value);
	}

	private void CreatePerformanceOptions(Panel content)
	{
		chunkDrawDistanceDropDown = CreateDropDown("options_chunk_draw_distance");
		AddOptionRow(
			content,
			24,
			"Chunk Draw Distance",
			"Maximum chunk-rendering distance in world blocks.",
			chunkDrawDistanceDropDown
		);
		chunkDrawDistanceDropDown.OnItemSelected += (_, item) =>
		{
			if (synchronizingOptions || item.UserData is not int drawDistance)
			{
				return;
			}

			optionsDraft.MaxChunkDrawDistance = drawDistance;
			UpdateOptionsDirtyState();
		};

		chunkMeshUploadBudgetDropDown = CreateDropDown("options_chunk_mesh_upload_budget");
		AddOptionRow(
			content,
			100,
			"Chunk Mesh Uploads",
			"Maximum completed chunk meshes uploaded per frame.",
			chunkMeshUploadBudgetDropDown
		);
		chunkMeshUploadBudgetDropDown.OnItemSelected += (_, item) =>
		{
			if (synchronizingOptions || item.UserData is not int meshUploadBudget)
			{
				return;
			}

			optionsDraft.ChunkMeshUploadBudget = meshUploadBudget;
			UpdateOptionsDirtyState();
		};

		sunShadowQualityDropDown = CreateDropDown("options_sun_shadow_quality");
		foreach (SunShadowQuality quality in Enum.GetValues<SunShadowQuality>())
		{
			sunShadowQualityDropDown.AddItem(new DropDownItem(quality.ToString(), quality));
		}
		AddOptionRow(
			content,
			176,
			"Sun Shadows",
			"Cascaded sunlight shadow quality and distance.",
			sunShadowQualityDropDown
		);
		sunShadowQualityDropDown.OnItemSelected += (_, item) =>
		{
			if (synchronizingOptions || item.UserData is not SunShadowQuality quality)
			{
				return;
			}

			optionsDraft.SunShadowQuality = quality;
			UpdateOptionsDirtyState();
		};

		volumetricFogQualityDropDown = CreateDropDown(
			"options_volumetric_fog_quality"
		);
		foreach (VolumetricFogQuality quality in Enum.GetValues<VolumetricFogQuality>())
		{
			volumetricFogQualityDropDown.AddItem(new DropDownItem(
				quality.ToString(),
				quality
			));
		}
		AddOptionRow(
			content,
			252,
			"Local Volumetric Fog",
			"Depth-aware authored fog quality and raymarch step size.",
			volumetricFogQualityDropDown
		);
		volumetricFogQualityDropDown.OnItemSelected += (_, item) =>
		{
			if (synchronizingOptions
				|| item.UserData is not VolumetricFogQuality quality)
			{
				return;
			}

			optionsDraft.VolumetricFogQuality = quality;
			UpdateOptionsDirtyState();
		};

		content.AddChild(new Label
		{
			Text = "Higher values load distant scenery faster, but increase CPU, GPU, and upload work.",
			Position = new Vector2(18, 328),
			Size = new Vector2(580, 44),
		});
	}

	private static DropDown CreateDropDown(string id)
	{
		return new DropDown
		{
			ID = id,
			Size = new Vector2(220, 28),
			MaxVisibleItems = 10,
		};
	}

	internal static ToggleSwitch CreateToggle(string id)
	{
		return new ToggleSwitch
		{
			ID = id,
			Size = new Vector2(72, 28),
			ShowLabels = true,
		};
	}

	private static void AddOptionRow(
		Panel content,
		float y,
		string title,
		string description,
		Control control)
	{
		content.AddChild(new Label
		{
			Text = title,
			Position = new Vector2(18, y),
			Size = new Vector2(340, 24),
		});
		content.AddChild(new Label
		{
			Text = description,
			Position = new Vector2(18, y + 24),
			Size = new Vector2(350, 22),
		});
		control.Position = new Vector2(378, y + 8);
		content.AddChild(control);
	}

	private void ShowOptionsWindow()
	{
		GameConfig config = Eng.DI.GetRequiredService<GameConfig>();
		optionsBaseline = GameOptionsDraft.FromConfig(config);
		optionsDraft = optionsBaseline with { };
		optionMonitors.Clear();
		optionMonitors.AddRange(((IFishGfxGameWindow)Window).Monitors);

		if (optionMonitors.Count > 0 && optionMonitors.All(monitor => monitor.Index != optionsDraft.MonitorIndex))
		{
			optionsDraft.MonitorIndex = optionMonitors.FirstOrDefault(monitor => monitor.IsPrimary)?.Index
				?? optionMonitors[0].Index;
		}

		optionsBaseline = optionsDraft with { };
		RefreshAllOptionControls();
		optionsStatusLabel.Text = string.Empty;
		ShowModal(optionsWindow);
	}

	private void RefreshAllOptionControls()
	{
		synchronizingOptions = true;
		try
		{
			monitorDropDown.ClearItems();
			foreach (MonitorInfo monitor in optionMonitors)
			{
				string suffix = monitor.IsPrimary ? " (Primary)" : string.Empty;
				monitorDropDown.AddItem(new DropDownItem(monitor.Name + suffix, monitor));
			}
			SelectItem(monitorDropDown, item => item.UserData is MonitorInfo monitor && monitor.Index == optionsDraft.MonitorIndex);

			SelectItem(displayModeDropDown, item => item.UserData is GameDisplayMode mode && mode == optionsDraft.DisplayMode);
			RefreshResolutionChoicesCore();
			vsyncToggle.IsOn = optionsDraft.VSync;
			msaaToggle.IsOn = optionsDraft.Msaa;
			RefreshFrameRateChoices();
			RefreshOptimizationChoices();
			SelectItem(
				sunShadowQualityDropDown,
				item => item.UserData is SunShadowQuality quality
					&& quality == optionsDraft.SunShadowQuality
			);
			SelectItem(
				volumetricFogQualityDropDown,
				item => item.UserData is VolumetricFogQuality quality
					&& quality == optionsDraft.VolumetricFogQuality
			);
			sensitivitySlider.Value = optionsDraft.MouseSensitivity;
			RefreshSensitivityLabel();
			focusToggle.IsOn = optionsDraft.SetFocused;
			resizableToggle.IsOn = optionsDraft.Resizable;
		}
		finally
		{
			synchronizingOptions = false;
		}

		UpdateOptionsDirtyState();
	}

	private void RefreshResolutionChoices()
	{
		bool previous = synchronizingOptions;
		synchronizingOptions = true;
		try
		{
			RefreshResolutionChoicesCore();
		}
		finally
		{
			synchronizingOptions = previous;
		}
	}

	private void RefreshResolutionChoicesCore()
	{
		resolutionDropDown.ClearItems();
		MonitorInfo monitor = GetSelectedMonitor();
		if (monitor == null)
		{
			resolutionDropDown.Disabled = true;
			return;
		}

		GameResolution desktop = GameResolution.Desktop(
			monitor.CurrentVideoMode.Width,
			monitor.CurrentVideoMode.Height
		);
		resolutionOptions.Clear();
		resolutionOptions.AddRange(BuildResolutionChoices(
			optionsDraft.DisplayMode,
			monitor.VideoModes.Select(mode => new GameResolution(mode.Width, mode.Height, false)),
			optionsDraft.Resolution,
			desktop
		));

		foreach (GameResolution resolution in resolutionOptions)
		{
			resolutionDropDown.AddItem(new DropDownItem(resolution.ToString(), resolution));
		}

		resolutionDropDown.Disabled = optionsDraft.DisplayMode == GameDisplayMode.BorderlessFullscreen;
		SelectItem(resolutionDropDown, item => item.UserData is GameResolution resolution && resolution == optionsDraft.Resolution);
	}

	internal static IReadOnlyList<GameResolution> BuildResolutionChoices(
		GameDisplayMode displayMode,
		IEnumerable<GameResolution> monitorModes,
		GameResolution configuredResolution,
		GameResolution desktopResolution)
	{
		if (displayMode == GameDisplayMode.BorderlessFullscreen)
		{
			return new[] { desktopResolution };
		}

		var choices = new List<GameResolution>();
		if (displayMode == GameDisplayMode.ExclusiveFullscreen)
		{
			choices.Add(desktopResolution);
		}

		choices.AddRange(monitorModes.Where(IsValidResolution));
		if (displayMode == GameDisplayMode.Windowed)
		{
			choices.AddRange(StandardWindowedResolutions.Where(resolution =>
				resolution.Width <= desktopResolution.Width
				&& resolution.Height <= desktopResolution.Height
			));
		}

		if (!configuredResolution.UseDesktop)
		{
			choices.Add(configuredResolution);
		}

		return choices
			.Distinct()
			.OrderByDescending(resolution => resolution.UseDesktop)
			.ThenByDescending(resolution => (long)resolution.Width * resolution.Height)
			.ThenByDescending(resolution => resolution.Width)
			.ToArray();
	}

	private static bool IsValidResolution(GameResolution resolution)
	{
		return resolution.Width > 0 && resolution.Height > 0;
	}

	private void RefreshFrameRateChoices()
	{
		frameRateDropDown.ClearItems();
		foreach (int frameRate in FrameRatePresets)
		{
			frameRateDropDown.AddItem(new DropDownItem(
				frameRate <= 0 ? "Unlimited" : frameRate.ToString(),
				frameRate
			));
		}

		if (!FrameRatePresets.Contains(optionsDraft.TargetFps))
		{
			frameRateDropDown.AddItem(new DropDownItem($"{optionsDraft.TargetFps} (Current)", optionsDraft.TargetFps));
		}

		SelectItem(frameRateDropDown, item => item.UserData is int value && value == optionsDraft.TargetFps);
	}

	private void RefreshOptimizationChoices()
	{
		RefreshIntegerChoices(
			chunkDrawDistanceDropDown,
			ChunkDrawDistancePresets,
			optionsDraft.MaxChunkDrawDistance,
			value => $"{value} blocks"
		);
		RefreshIntegerChoices(
			chunkMeshUploadBudgetDropDown,
			ChunkMeshUploadBudgetPresets,
			optionsDraft.ChunkMeshUploadBudget,
			value => $"{value} per frame"
		);
	}

	private static void RefreshIntegerChoices(
		DropDown dropDown,
		IEnumerable<int> presets,
		int selectedValue,
		Func<int, string> format)
	{
		dropDown.ClearItems();
		foreach (int value in presets.Distinct())
		{
			dropDown.AddItem(new DropDownItem(format(value), value));
		}

		if (!dropDown.Items.Any(item => item.UserData is int value && value == selectedValue))
		{
			dropDown.AddItem(new DropDownItem($"{format(selectedValue)} (Current)", selectedValue));
		}

		SelectItem(dropDown, item => item.UserData is int value && value == selectedValue);
	}

	private static void SelectItem(DropDown dropDown, Func<DropDownItem, bool> predicate)
	{
		int index = dropDown.Items.FindIndex(item => predicate(item));
		if (index >= 0)
		{
			dropDown.SelectIndex(index);
		}
	}

	private MonitorInfo GetSelectedMonitor()
	{
		return optionMonitors.FirstOrDefault(monitor => monitor.Index == optionsDraft.MonitorIndex)
			?? optionMonitors.FirstOrDefault(monitor => monitor.IsPrimary)
			?? optionMonitors.FirstOrDefault();
	}

	private void NormalizeResolutionForMonitor(MonitorInfo monitor)
	{
		if (monitor == null)
		{
			return;
		}

		GameResolution desktop = GameResolution.Desktop(
			monitor.CurrentVideoMode.Width,
			monitor.CurrentVideoMode.Height
		);
		if (optionsDraft.DisplayMode == GameDisplayMode.BorderlessFullscreen)
		{
			optionsDraft.Resolution = desktop;
			return;
		}
		if (optionsDraft.DisplayMode == GameDisplayMode.Windowed && optionsDraft.Resolution.UseDesktop)
		{
			optionsDraft.Resolution = new GameResolution(desktop.Width, desktop.Height, false);
			return;
		}
		if (optionsDraft.Resolution.UseDesktop)
		{
			optionsDraft.Resolution = desktop;
			return;
		}

		bool supported = monitor.VideoModes.Any(mode =>
			mode.Width == optionsDraft.Resolution.Width
			&& mode.Height == optionsDraft.Resolution.Height
		);
		if (optionsDraft.DisplayMode == GameDisplayMode.ExclusiveFullscreen && !supported)
		{
			optionsDraft.Resolution = desktop;
		}
	}

	private void RestoreOptionDefaults()
	{
		MonitorInfo primary = optionMonitors.FirstOrDefault(monitor => monitor.IsPrimary)
			?? optionMonitors.FirstOrDefault();
		optionsDraft = GameOptionsDraft.CreateDefaults(primary?.Index ?? -1);
		RefreshAllOptionControls();
		optionsStatusLabel.Text = "Defaults staged";
	}

	private void ApplyOptions()
	{
		if (optionsDraft == null || optionsBaseline == null || optionsDraft == optionsBaseline)
		{
			return;
		}

		GameConfig config = Eng.DI.GetRequiredService<GameConfig>();
		GameOptionsDraft previous = GameOptionsDraft.FromConfig(config);
		try
		{
			optionsDraft.ApplyTo(config);
			config.SaveToJson();
			((IFishGfxGameWindow)Window).ApplyConfiguration();
			optionsBaseline = optionsDraft with { };
			HideModal(optionsWindow);
		}
		catch (Exception exception)
		{
			previous.ApplyTo(config);
			optionsStatusLabel.Text = "Could not save settings";
			logging.Log(GameLogLevel.Error, "Configuration", "Options apply/save failed; restored previous configuration.", exception);
			UpdateOptionsDirtyState();
		}
	}

	private void SetDraftValue(Action setter)
	{
		if (synchronizingOptions)
		{
			return;
		}

		setter();
		UpdateOptionsDirtyState();
	}

	private void RefreshSensitivityLabel()
	{
		sensitivityValueLabel.Text = optionsDraft.MouseSensitivity.ToString("0.00");
	}

	private void UpdateOptionsDirtyState()
	{
		optionsApplyButton.Disabled = optionsDraft == null || optionsBaseline == null || optionsDraft == optionsBaseline;
		if (!optionsApplyButton.Disabled && optionsStatusLabel.Text == "Defaults staged")
		{
			return;
		}
		optionsStatusLabel.Text = string.Empty;
	}

	private void CloseOptionDropDowns()
	{
		monitorDropDown?.Close();
		displayModeDropDown?.Close();
		resolutionDropDown?.Close();
		frameRateDropDown?.Close();
		chunkDrawDistanceDropDown?.Close();
		chunkMeshUploadBudgetDropDown?.Close();
	}
}
