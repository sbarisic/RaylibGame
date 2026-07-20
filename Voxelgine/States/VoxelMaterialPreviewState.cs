#if WINDOWS
using System.Numerics;
using FishGfx.Graphics;
using FishGfx.Voxels;
using FishUI;
using FishUI.Controls;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.FishGfxClient;
using Voxelgine.FishGfxClient.Assets;
using Voxelgine.FishGfxClient.Rendering;
using Voxelgine.FishGfxClient.Voxels;
using Voxelgine.Graphics;
using Voxelgine.GUI;

namespace Voxelgine.States;

public sealed class VoxelMaterialPreviewState : GameStateImpl
{
	private const float ControlsWidth = 380;
	internal const BlockType DefaultBlockType = BlockType.Stone;
	private static readonly BlockType[] BlockTypes = Enum.GetValues<BlockType>()
		.Where(static type => type != BlockType.None)
		.OrderBy(static type => (int)type)
		.ToArray();

	private readonly IFishGfxGameWindow fishWindow;
	private readonly FishUIManager gui;
	private readonly ChunkMap map = new();
	private readonly FishGfxVoxelScene voxelScene;
	private readonly RenderQueue renderQueue = new();
	private readonly Camera camera = new();
	private readonly DropDown materialDropDown;
	private readonly Label materialInfoLabel;
	private readonly Label reloadStatusLabel;
	private readonly Slider lightAzimuthSlider;
	private readonly CheckBox automaticLightCheckBox;
	private BlockType selectedBlock = DefaultBlockType;
	private float cameraYaw = 35;
	private float cameraElevation = 20;
	private float cameraDistance = 4;
	private float lightAzimuth = 45;
	private float lightElevation = 35;
	private float directIntensity = 1;
	private float ambientIntensity = 0.15f;
	private Vector2 lastMousePosition;
	private bool automaticLightRotation;
	private bool automaticValidation;
	private bool automaticReloadSucceeded;
	private int automaticFrame;
	private bool disposed;

	public VoxelMaterialPreviewState(IGameWindow window, IFishEngineRunner engine)
		: base(window, engine)
	{
		fishWindow = window as IFishGfxGameWindow
			?? throw new ArgumentException("Voxel material preview requires FishGfx.", nameof(window));
		gui = new FishUIManager(window, engine.DI.GetRequiredService<IFishLogging>());
		map.SetBlock(0, 0, 0, selectedBlock);
		voxelScene = new FishGfxVoxelScene(
			fishWindow.RenderWindow.Graphics,
			fishWindow.Assets,
			map,
			maxChunkDrawDistance: 64,
			chunkMeshUploadBudget: 16,
			fogQuality: VolumetricFogQuality.Off
		);
		voxelScene.Renderer.FogSettings = VoxelFogSettings.Disabled;
		fishWindow.Assets.ReloadCompleted += OnAssetReloadCompleted;

		(materialDropDown, materialInfoLabel, reloadStatusLabel, lightAzimuthSlider,
			automaticLightCheckBox) = CreateUi();
		SelectDropDownBlock(selectedBlock);
		UpdateMaterialInfo();
		ConfigureCamera();
	}

	internal static IReadOnlyList<BlockType> AvailableBlockTypes => BlockTypes;

	internal BlockType SelectedBlock => selectedBlock;

	public bool IsReady =>
		voxelScene.Renderer.IsIdle
		&& voxelScene.GetPresentationState(new ChunkCoordinate(0, 0, 0))
			== VoxelPresentationState.Resident
		&& (!automaticValidation || automaticReloadSucceeded);

	internal void EnableAutomaticValidation()
	{
		automaticValidation = true;
	}

	public override void SwapTo()
	{
		gui.InputEnabled = true;
		fishWindow.RenderWindow.CaptureCursor = false;
		fishWindow.RenderWindow.ShowCursor = true;
		lastMousePosition = Window.InMgr.GetMousePos();
	}

	public override void SwapFrom()
	{
		gui.InputEnabled = false;
		materialDropDown.Close();
	}

	public override void Tick(float gameTime)
	{
		if (Window.InMgr.IsInputPressed(InputKey.Esc))
		{
			Window.SetState(Eng.AsClient().MainMenuState);
		}
	}

	public override void BeginInputFrame()
	{
		gui.BeginInputFrame();
	}

	public override void BeginFrame(in FrameTiming timing)
	{
		RunAutomaticValidationStep();
		Vector2 mouse = Window.InMgr.GetMousePos();
		bool overControls = mouse.X <= ControlsWidth + 20;
		if (!overControls && Window.InMgr.IsInputDown(InputKey.Click_Left))
		{
			Vector2 delta = mouse - lastMousePosition;
			cameraYaw = WrapDegrees(cameraYaw + delta.X * 0.35f);
			cameraElevation = Math.Clamp(cameraElevation - delta.Y * 0.25f, -85, 85);
		}

		if (!overControls)
		{
			cameraDistance = Math.Clamp(
				cameraDistance - Window.InMgr.GetMouseWheel() * 0.35f,
				1.75f,
				12
			);
		}
		lastMousePosition = mouse;

		if (automaticLightRotation)
		{
			lightAzimuth = WrapDegrees(lightAzimuth + 30 * timing.DeltaTime);
			lightAzimuthSlider.Value = lightAzimuth;
		}

		ConfigureCamera();
		ConfigureLighting();
		voxelScene.Update(camera);
		gui.Update(timing.DeltaTime, timing.TotalTime);
	}

	public override GameStateRenderSettings GetRenderSettings(Vector2 framebufferSize)
	{
		ConfigureCamera(framebufferSize);
		GameStateRenderSettings overlay = GameStateRenderSettings.CreateOverlay(
			new Vector2(Window.Width, Window.Height)
		);
		return new GameStateRenderSettings
		{
			WorldView = new RenderView(camera),
			ViewmodelView = new RenderView(camera),
			OverlayView = overlay.OverlayView,
			ClearColor = new FishGfx.Color(34, 38, 46),
		};
	}

	public override void RenderWorld(RenderPass pass, in FrameTiming timing)
	{
		renderQueue.BeginFrame();
		voxelScene.Enqueue(renderQueue, camera, shadows: null);
		pass.Execute(renderQueue, RenderQueueBucket.Opaque);
		pass.Execute(renderQueue, RenderQueueBucket.Transparent);

		for (int coordinate = -4; coordinate <= 4; coordinate++)
		{
			FishGfx.Color color = coordinate == 0
				? new FishGfx.Color(92, 102, 118)
				: new FishGfx.Color(55, 61, 72);
			pass.DrawLine(
				new FishGfx.Vertex3(new Vector3(coordinate, -0.01f, -4), color),
				new FishGfx.Vertex3(new Vector3(coordinate, -0.01f, 4), color)
			);
			pass.DrawLine(
				new FishGfx.Vertex3(new Vector3(-4, -0.01f, coordinate), color),
				new FishGfx.Vertex3(new Vector3(4, -0.01f, coordinate), color)
			);
		}

		pass.DrawLine(
			new FishGfx.Vertex3(Vector3.Zero, FishGfx.Color.Red),
			new FishGfx.Vertex3(new Vector3(1.5f, 0, 0), FishGfx.Color.Red)
		);
		pass.DrawLine(
			new FishGfx.Vertex3(Vector3.Zero, FishGfx.Color.Green),
			new FishGfx.Vertex3(new Vector3(0, 1.5f, 0), FishGfx.Color.Green)
		);
		pass.DrawLine(
			new FishGfx.Vertex3(Vector3.Zero, FishGfx.Color.Blue),
			new FishGfx.Vertex3(new Vector3(0, 0, 1.5f), FishGfx.Color.Blue)
		);
	}

	public override void RenderOverlay(RenderPass pass, in FrameTiming timing)
	{
		gui.Render(pass, timing.DeltaTime, timing.TotalTime);
	}

	public override void Dispose()
	{
		if (disposed)
		{
			return;
		}

		disposed = true;
		fishWindow.Assets.ReloadCompleted -= OnAssetReloadCompleted;
		renderQueue.Clear();
		voxelScene.Dispose();
		gui.Dispose();
	}

	internal void SelectBlock(BlockType blockType)
	{
		if (!BlockTypes.Contains(blockType))
		{
			throw new ArgumentOutOfRangeException(nameof(blockType));
		}

		if (selectedBlock == blockType)
		{
			return;
		}

		selectedBlock = blockType;
		map.SetBlock(0, 0, 0, selectedBlock);
		UpdateMaterialInfo();
	}

	private (DropDown, Label, Label, Slider, CheckBox) CreateUi()
	{
		var controlsWindow = new Window
		{
			Title = "Voxel Material Preview",
			Position = new Vector2(20, 20),
			Size = new Vector2(ControlsWidth, 750),
			IsResizable = false,
			ShowCloseButton = false,
		};
		var scroll = new ScrollablePane
		{
			Position = Vector2.Zero,
			Size = controlsWindow.GetContentSize(),
			Anchor = FishUIAnchor.All,
			AutoContentSize = true,
		};
		var stack = new StackLayout
		{
			Orientation = StackOrientation.Vertical,
			Spacing = 7,
			Position = new Vector2(10, 10),
			Size = new Vector2(ControlsWidth - 42, 920),
			IsTransparent = true,
		};
		float width = ControlsWidth - 48;

		stack.AddChild(new Label { Text = "Block material", Size = new Vector2(width, 22) });
		var dropDown = new DropDown
		{
			ID = "voxel_material_block",
			Size = new Vector2(width, 30),
			MaxVisibleItems = 12,
		};
		foreach (BlockType blockType in BlockTypes)
		{
			dropDown.AddItem(new DropDownItem($"{(int)blockType}: {blockType}", blockType));
		}
		dropDown.OnItemSelected += (_, item) =>
		{
			if (item.UserData is BlockType blockType)
			{
				SelectBlock(blockType);
			}
		};
		stack.AddChild(dropDown);

		var info = new Label
		{
			ID = "voxel_material_info",
			Size = new Vector2(width, 128),
		};
		stack.AddChild(info);

		Slider AddSlider(string id, string title, float minimum, float maximum,
			float value, float step, Action<float> changed)
		{
			stack.AddChild(new Label { Text = title, Size = new Vector2(width, 20) });
			var slider = new Slider
			{
				ID = id,
				Size = new Vector2(width, 26),
				MinValue = minimum,
				MaxValue = maximum,
				Value = value,
				Step = step,
				ShowValueLabel = true,
				ValueLabelFormat = "0.00",
			};
			slider.OnValueChanged += (_, newValue) => changed(newValue);
			stack.AddChild(slider);
			return slider;
		}

		Slider azimuth = AddSlider(
			"voxel_material_light_azimuth", "Light azimuth", 0, 360, 45, 1,
			value => lightAzimuth = value);
		AddSlider(
			"voxel_material_light_elevation", "Light elevation", 5, 85, 35, 1,
			value => lightElevation = value);
		AddSlider(
			"voxel_material_direct", "Direct intensity", 0, 2, 1, 0.05f,
			value => directIntensity = value);
		AddSlider(
			"voxel_material_ambient", "Ambient intensity", 0, 0.5f, 0.15f, 0.01f,
			value => ambientIntensity = value);

		var automatic = new CheckBox("Automatic light rotation (30 degrees/second)")
		{
			ID = "voxel_material_auto_light",
			IsChecked = false,
			Size = new Vector2(width, 28),
		};
		automatic.OnCheckedChanged += (_, value) => automaticLightRotation = value;
		stack.AddChild(automatic);

		var reloadButton = new Button
		{
			ID = "voxel_material_reload_atlases",
			Text = "Reload All Atlas Textures",
			Size = new Vector2(width, 38),
		};
		reloadButton.OnButtonPressed += (_, _, _) =>
		{
			reloadStatusLabel.Text = voxelScene.RequestSurfaceTextureReload()
				? "Queued"
				: "Failed - surface texture asset is unavailable";
		};
		stack.AddChild(reloadButton);

		var reloadStatus = new Label
		{
			ID = "voxel_material_reload_status",
			Text = "Watching deployed data/textures (200 ms debounce)",
			Size = new Vector2(width, 50),
		};
		stack.AddChild(reloadStatus);
		stack.AddChild(new Label
		{
			Text = "Drag outside this panel to orbit. Use the mouse wheel to zoom.",
			Size = new Vector2(width, 44),
		});

		var backButton = new Button
		{
			ID = "voxel_material_back",
			Text = "Back to Main Menu",
			Size = new Vector2(width, 40),
		};
		backButton.OnButtonPressed += (_, _, _) => Window.SetState(Eng.AsClient().MainMenuState);
		stack.AddChild(backButton);

		scroll.AddChild(stack);
		controlsWindow.AddChild(scroll);
		gui.AddControl(controlsWindow);
		return (dropDown, info, reloadStatus, azimuth, automatic);
	}

	private void ConfigureCamera()
	{
		ConfigureCamera(fishWindow.RenderWindow.FramebufferSize);
	}

	private void ConfigureCamera(Vector2 framebufferSize)
	{
		float yaw = cameraYaw * MathF.PI / 180;
		float elevation = cameraElevation * MathF.PI / 180;
		float horizontal = MathF.Cos(elevation) * cameraDistance;
		Vector3 target = new(0.5f, 0.5f, 0.5f);
		camera.Position = target + new Vector3(
			MathF.Sin(yaw) * horizontal,
			MathF.Sin(elevation) * cameraDistance,
			MathF.Cos(yaw) * horizontal
		);
		camera.LookAt(target);
		camera.SetPerspective(framebufferSize, 45 * MathF.PI / 180, 0.05f, 128);
	}

	private void ConfigureLighting()
	{
		float azimuth = lightAzimuth * MathF.PI / 180;
		float elevation = lightElevation * MathF.PI / 180;
		Vector3 towardLight = new(
			MathF.Cos(elevation) * MathF.Sin(azimuth),
			MathF.Sin(elevation),
			MathF.Cos(elevation) * MathF.Cos(azimuth)
		);
		voxelScene.SetEnvironmentLighting(1, (byte)Math.Clamp(
			(int)MathF.Round(ambientIntensity * VoxelEnvironmentSampling.MaximumSkyLight),
			0,
			VoxelEnvironmentSampling.MaximumSkyLight
		));
		voxelScene.Renderer.SunSettings = new VoxelSunSettings(
			-towardLight,
			FishGfx.Color.White,
			directIntensity,
			ambientIntensity
		);
	}

	private void SelectDropDownBlock(BlockType blockType)
	{
		int index = materialDropDown.Items.FindIndex(
			item => item.UserData is BlockType candidate && candidate == blockType
		);
		if (index >= 0)
		{
			materialDropDown.SelectIndex(index);
		}
	}

	private void UpdateMaterialInfo()
	{
		VoxelMaterialPreviewInfo info = voxelScene.GetMaterialPreviewInfo(selectedBlock);
		VoxelFaceTiles tiles = info.AtlasTiles;
		string geometry = info.IsCustomModel ? "Custom model" : "Cube";
		string surfaceMaps = info.SurfaceMapsEnabled
			? "Enabled"
			: "Disabled (custom-model tangents are zero)";
		materialInfoLabel.Text =
			$"Name: {info.Name}\n" +
			$"Render mode: {info.RenderMode}\n" +
			$"Geometry: {geometry}\n" +
			$"Surface maps: {surfaceMaps}\n" +
			$"Atlas +X/-X/+Y/-Y/+Z/-Z: " +
			$"{tiles.PositiveX}/{tiles.NegativeX}/{tiles.PositiveY}/" +
			$"{tiles.NegativeY}/{tiles.PositiveZ}/{tiles.NegativeZ}";
	}

	private void OnAssetReloadCompleted(AssetReloadResult result)
	{
		if (!string.Equals(
			result.AssetId,
			FishGfxVoxelScene.SurfaceTextureAssetId,
			StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		reloadStatusLabel.Text = result.Succeeded
			? "Reloaded"
			: result.Message;
		if (automaticValidation)
		{
			automaticReloadSucceeded = result.Succeeded;
		}
	}

	private void RunAutomaticValidationStep()
	{
		if (!automaticValidation)
		{
			return;
		}

		automaticFrame++;
		if (automaticFrame == 2)
		{
			reloadStatusLabel.Text = voxelScene.RequestSurfaceTextureReload()
				? "Queued"
				: "Failed - surface texture asset is unavailable";
		}

		BlockType? blockType = automaticFrame switch
		{
			10 => BlockType.Grass,
			20 => BlockType.Leaf,
			30 => BlockType.Water,
			40 => BlockType.Glowstone,
			50 => BlockType.Barrel,
			60 => BlockType.Campfire,
			70 => BlockType.Foliage,
			80 => DefaultBlockType,
			_ => null,
		};
		if (blockType.HasValue)
		{
			SelectBlock(blockType.Value);
			SelectDropDownBlock(blockType.Value);
		}
	}

	private static float WrapDegrees(float value)
	{
		value %= 360;
		return value < 0 ? value + 360 : value;
	}
}
#endif
