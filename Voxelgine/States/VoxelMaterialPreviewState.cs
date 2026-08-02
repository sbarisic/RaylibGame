#if WINDOWS
using System.Buffers.Binary;
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
	internal const BlockType DefaultBlockType = BlockType.Stone;
	private static readonly BlockType[] BlockTypes = Enum.GetValues<BlockType>()
		.Where(static type => type != BlockType.None)
		.OrderBy(static type => (int)type)
		.ToArray();

	private readonly IFishGfxGameWindow fishWindow;
	private readonly RuntimePaths runtimePaths;
	private readonly FishUIManager gui;
	private readonly ChunkMap map = new();
	private readonly FishGfxVoxelScene voxelScene;
	private readonly RenderQueue renderQueue = new();
	private readonly Camera camera = new();
	private readonly VoxelMaterialInspectorModel inspectorModel;
	private readonly VoxelMaterialInspector inspector;
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
	private Task<FishUIDebugSnapshot> automaticCapture;
	private int automaticFrame;
	private bool disposed;

	public VoxelMaterialPreviewState(IGameWindow window, IFishEngineRunner engine)
		: base(window, engine)
	{
		fishWindow = window as IFishGfxGameWindow
			?? throw new ArgumentException("Voxel material preview requires FishGfx.", nameof(window));
		runtimePaths = engine.AsClient().RuntimePaths;
		gui = new FishUIManager(window, engine.Logging, runtimePaths);
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
		inspectorModel = new VoxelMaterialInspectorModel(selectedBlock);
		inspector = new VoxelMaterialInspector(
			inspectorModel,
			SelectBlock,
			value => lightAzimuth = value,
			value => lightElevation = value,
			value => directIntensity = value,
			value => ambientIntensity = value,
			value => automaticLightRotation = value,
			RequestAtlasReload,
			() => Client.RequestState(ClientStateKind.MainMenu));
		inspector.UpdateLayout(window.Width, window.Height);
		gui.AddControl(inspector.Root);
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

	internal void ValidateAutomaticSnapshotBundle()
	{
		if (!automaticValidation || automaticCapture == null)
			throw new InvalidOperationException("The automatic snapshot request was not queued.");
		FishUIDebugSnapshot snapshot = automaticCapture.WaitAsync(TimeSpan.FromSeconds(10))
			.GetAwaiter().GetResult();
		gui.UI.Diagnostics.WaitForPendingExportsAsync().WaitAsync(TimeSpan.FromSeconds(10))
			.GetAwaiter().GetResult();
		if (snapshot.CaptureStatus != FishUIDebugCaptureStatus.Complete
			|| snapshot.ScreenshotPng == null || snapshot.OverlayPng == null)
			throw new InvalidOperationException("The automatic FishUI snapshot did not produce both image artifacts.");
		if (snapshot.FramebufferWidthPixels != fishWindow.RenderWindow.FramebufferWidth
			|| snapshot.FramebufferHeightPixels != fishWindow.RenderWindow.FramebufferHeight)
			throw new InvalidOperationException("FishUI snapshot framebuffer metadata does not match the rendered target.");
		(int screenshotWidth, int screenshotHeight) = ReadPngSize(snapshot.ScreenshotPng);
		(int overlayWidth, int overlayHeight) = ReadPngSize(snapshot.OverlayPng);
		if (screenshotWidth != overlayWidth || screenshotHeight != overlayHeight
			|| screenshotWidth != snapshot.FramebufferWidthPixels
			|| screenshotHeight != snapshot.FramebufferHeightPixels)
			throw new InvalidOperationException("FishUI screenshot and annotated overlay dimensions are not aligned.");

		string root = Path.Combine(runtimePaths.Root, "diagnostics", "fishui");
		string directory = Directory.GetDirectories(root, snapshot.DefaultExportName + "*")
			.OrderByDescending(Directory.GetCreationTimeUtc).FirstOrDefault()
			?? throw new InvalidOperationException("The automatic FishUI snapshot bundle was not exported.");
		foreach (string artifact in new[] { "snapshot.json", "recent-events.json", "interaction-summary.txt", "screenshot.png", "overlay.png" })
			if (!File.Exists(Path.Combine(directory, artifact)))
				throw new InvalidOperationException($"FishUI snapshot bundle is missing '{artifact}'.");
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
	}

	public override void Tick(float gameTime)
	{
		if (Window.InMgr.IsInputPressed(InputKey.Esc))
		{
			Client.RequestState(ClientStateKind.MainMenu);
		}
	}

	public override void BeginInputFrame()
	{
		gui.BeginInputFrame();
	}

	public override void BeginFrame(in FrameTiming timing)
	{
		RunAutomaticValidationStep();
		inspector.UpdateLayout(Window.Width, Window.Height);
		Vector2 mouse = Window.InMgr.GetMousePos();
		bool overControls = inspector.Contains(mouse);
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
			inspector.LightAzimuthSlider.Value = lightAzimuth;
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
			color = FishGfx.ColorSpace.SrgbToLinearColor(color);
			pass.DrawLine(
				new FishGfx.Vertex3(new Vector3(coordinate, -0.01f, -4), color),
				new FishGfx.Vertex3(new Vector3(coordinate, -0.01f, 4), color)
			);
			pass.DrawLine(
				new FishGfx.Vertex3(new Vector3(-4, -0.01f, coordinate), color),
				new FishGfx.Vertex3(new Vector3(4, -0.01f, coordinate), color)
			);
		}

		FishGfx.Color red = FishGfx.ColorSpace.SrgbToLinearColor(FishGfx.Color.Red);
		FishGfx.Color green = FishGfx.ColorSpace.SrgbToLinearColor(FishGfx.Color.Green);
		FishGfx.Color blue = FishGfx.ColorSpace.SrgbToLinearColor(FishGfx.Color.Blue);
		pass.DrawLine(
			new FishGfx.Vertex3(Vector3.Zero, red),
			new FishGfx.Vertex3(new Vector3(1.5f, 0, 0), red)
		);
		pass.DrawLine(
			new FishGfx.Vertex3(Vector3.Zero, green),
			new FishGfx.Vertex3(new Vector3(0, 1.5f, 0), green)
		);
		pass.DrawLine(
			new FishGfx.Vertex3(Vector3.Zero, blue),
			new FishGfx.Vertex3(new Vector3(0, 0, 1.5f), blue)
		);
	}

	public override void RenderOverlay(RenderPass pass, in FrameTiming timing)
	{
		gui.Render(pass, timing.DeltaTime, timing.TotalTime);
	}

	protected override void DisposeCore()
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
		inspector.Select(blockType);
		UpdateMaterialInfo();
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

	private void UpdateMaterialInfo()
	{
		VoxelMaterialPreviewInfo info = voxelScene.GetMaterialPreviewInfo(selectedBlock);
		inspector.UpdateMaterialInfo(info);
	}

	private void RequestAtlasReload()
	{
		inspector.SetReloadStatus(voxelScene.RequestSurfaceTextureReload()
			? "Queued"
			: "Failed - surface texture asset is unavailable");
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

		inspector.SetReloadStatus(result.Succeeded
			? "Reloaded"
			: result.Message);
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
			RequestAtlasReload();
		}
		if (automaticFrame == 90)
			automaticCapture = FishUIDiagnostics.CaptureAsync(gui.UI,
				new FishUIDebugSnapshotOptions(), FishUIDebugCaptureReason.TestFailure);

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
		}
	}

	private static float WrapDegrees(float value)
	{
		value %= 360;
		return value < 0 ? value + 360 : value;
	}

	private static (int Width, int Height) ReadPngSize(byte[] png)
	{
		if (png.Length < 24 || !png.AsSpan(1, 3).SequenceEqual("PNG"u8))
			throw new InvalidOperationException("FishUI image artifact is not a PNG file.");
		return (
			BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)),
			BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
	}
}
#endif
