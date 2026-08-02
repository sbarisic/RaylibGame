#if WINDOWS
using System.Buffers.Binary;
using System.Numerics;
using System.Security.Cryptography;
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
	private readonly AtlasEditingSession editingSession;
	private readonly AtlasSaveService saveService;
	private readonly EditorSurfaceTextureLease editorTextureLease;
	private readonly AtlasReverseUsageCatalog reverseUsage;
	private BlockType selectedBlock = DefaultBlockType;
	private VoxelMaterialPaintGeometry paintGeometry;
	private AtlasPaintLayer selectedLayer = AtlasPaintLayer.BaseColor;
	private AtlasPixel selectedColor = new(0x47, 0xc8, 0xe8, 0xff);
	private VoxelPaintHit hoverHit;
	private bool hasHoverHit;
	private VoxelPaintStroke activeStroke;
	private bool paintMode = true;
	private bool previewDirty;
	private bool saveConflict;
	private IDisposable saveReloadSuppression;
	private bool awaitingSavedReload;
	private float releaseSuppressionAt = float.PositiveInfinity;
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
	private Dictionary<string, byte[]> automaticSourceHashes;
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
		AtlasAssetPaths assetPaths = AtlasAssetPaths.Resolve(
			engine.AsClient().AssetSourceRoot,
			Path.Combine(AppContext.BaseDirectory, "data"),
			Environment.CurrentDirectory,
			AppContext.BaseDirectory);
		editingSession = new AtlasEditingSession(assetPaths);
		saveService = new AtlasSaveService(assetPaths, editingSession.Documents.Values);
		editorTextureLease = voxelScene.AcquireEditorSurfaceTextureOverride();
		reverseUsage = new AtlasReverseUsageCatalog(voxelScene.GetMaterialPreviewInfo);
		paintGeometry = voxelScene.GetMaterialPaintGeometry(new BlockValue(selectedBlock));
		fishWindow.Assets.ReloadCompleted += OnAssetReloadCompleted;
		inspectorModel = new VoxelMaterialInspectorModel(selectedBlock);
		inspector = new VoxelMaterialInspector(
			inspectorModel,
			editingSession,
			SelectBlock,
			value => lightAzimuth = value,
			value => lightElevation = value,
			value => directIntensity = value,
			value => ambientIntensity = value,
			value => automaticLightRotation = value,
			SelectPaintLayer,
			SetPaintColor,
			SetPaintMode,
			() => SavePaint(),
			DiscardPaint,
			UndoPaint,
			RedoPaint,
			RequestAtlasReload,
			RequestBack);
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
		automaticSourceHashes = editingSession.Paths.SourceRoot == null
			? new Dictionary<string, byte[]>()
			: editingSession.Documents.Values
				.Select(document => Path.Combine(editingSession.Paths.SourceRoot, document.RelativePath))
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToDictionary(static path => path, static path => SHA256.HashData(File.ReadAllBytes(path)),
					StringComparer.OrdinalIgnoreCase);
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
		foreach ((string path, byte[] expected) in automaticSourceHashes)
			if (!SHA256.HashData(File.ReadAllBytes(path)).AsSpan().SequenceEqual(expected))
				throw new InvalidOperationException($"Automatic Material Lab validation modified source asset '{path}' before Save.");
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
		if (!automaticValidation && editingSession.IsDirty && fishWindow.RenderWindow.IsCloseRequested)
		{
			fishWindow.RenderWindow.IsCloseRequested = false;
			inspector.SetEditStatus("Unsaved changes: Save or Discard before closing the application");
		}
		if (Window.InMgr.IsInputPressed(InputKey.Esc))
		{
			RequestBack();
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
		ConfigureCamera();
		Vector2 mouse = Window.InMgr.GetMousePos();
		bool overControls = inspector.Contains(mouse);
		bool orbiting = !overControls && (
			Window.InMgr.IsInputDown(InputKey.Click_Middle)
			|| Window.InMgr.IsInputDown(InputKey.Click_Left) && (!paintMode || Window.InMgr.IsInputDown(InputKey.Alt)));
		if (orbiting)
		{
			Vector2 delta = mouse - lastMousePosition;
			cameraYaw = WrapDegrees(cameraYaw + delta.X * 0.35f);
			cameraElevation = Math.Clamp(cameraElevation - delta.Y * 0.25f, -85, 85);
		}

		hasHoverHit = !overControls && VoxelMaterialPicker.TryPick(
			camera,
			mouse,
			new Vector2(Window.Width, Window.Height),
			fishWindow.RenderWindow.FramebufferSize,
			paintGeometry,
			editingSession,
			selectedLayer,
			out hoverHit);
		if (automaticValidation && automaticFrame >= 80 && selectedBlock == BlockType.Test
			&& hasHoverHit && hoverHit.TextureLayer != 8)
		{
			throw new InvalidOperationException($"Test preview picked unexpected atlas tile {hoverHit.TextureLayer}.");
		}
		if (hasHoverHit)
			inspector.RefreshPaintData(hoverHit, selectedLayer, reverseUsage.Get(hoverHit.TextureLayer));
		if (hasHoverHit && paintMode && !Window.InMgr.IsInputDown(InputKey.Alt))
		{
			if (Window.InMgr.IsInputPressed(InputKey.Click_Right))
			{
				selectedColor = hoverHit.Target.Get(hoverHit.LocalX, hoverHit.LocalY);
				inspector.SetSelectedColor(selectedColor);
			}
			if (Window.InMgr.IsInputPressed(InputKey.Click_Left))
				activeStroke = new VoxelPaintStroke(editingSession);
			if (activeStroke != null && Window.InMgr.IsInputDown(InputKey.Click_Left)
				&& activeStroke.Paint(hoverHit, EncodeSelectedColor()))
			{
				previewDirty = true;
				inspector.RefreshPaintData(hoverHit, selectedLayer, reverseUsage.Get(hoverHit.TextureLayer));
			}
		}
		if (activeStroke != null && Window.InMgr.IsInputReleased(InputKey.Click_Left))
		{
			activeStroke.Commit();
			activeStroke = null;
			previewDirty = true;
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

		ConfigureLighting();
		if (previewDirty)
			RebuildEditorSurfaceTextures();
		voxelScene.Update(camera);
		gui.Update(timing.DeltaTime, timing.TotalTime);
		if (timing.TotalTime >= releaseSuppressionAt)
		{
			releaseSuppressionAt = float.PositiveInfinity;
			saveReloadSuppression?.Dispose();
			saveReloadSuppression = null;
		}
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

		if (hasHoverHit)
		{
			FishGfx.Color marker = FishGfx.ColorSpace.SrgbToLinearColor(
				hoverHit.Editable ? new FishGfx.Color(71, 200, 232) : new FishGfx.Color(255, 171, 64));
			Vector3 center = hoverHit.Position + hoverHit.Normal * 0.008f;
			const float radius = 0.035f;
			pass.DrawLine(new FishGfx.Vertex3(center - Vector3.UnitX * radius, marker), new FishGfx.Vertex3(center + Vector3.UnitX * radius, marker));
			pass.DrawLine(new FishGfx.Vertex3(center - Vector3.UnitY * radius, marker), new FishGfx.Vertex3(center + Vector3.UnitY * radius, marker));
			pass.DrawLine(new FishGfx.Vertex3(center - Vector3.UnitZ * radius, marker), new FishGfx.Vertex3(center + Vector3.UnitZ * radius, marker));
		}
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
		saveReloadSuppression?.Dispose();
		editorTextureLease.Dispose();
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
		if (activeStroke != null)
		{
			activeStroke.Commit();
			activeStroke = null;
		}
		map.SetBlock(0, 0, 0, selectedBlock);
		paintGeometry = voxelScene.GetMaterialPaintGeometry(new BlockValue(selectedBlock));
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
		if (editingSession.IsDirty)
		{
			inspector.SetReloadStatus("Unsaved changes: Save or Discard before Reload");
			return;
		}
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
		if (awaitingSavedReload)
		{
			awaitingSavedReload = false;
			if (result.Succeeded)
			{
				editorTextureLease.Clear();
				releaseSuppressionAt = Client.TotalTime + 0.2f;
			}
			else
			{
				inspector.SetReloadStatus($"Saved, but reload failed; editor preview retained: {result.Message}");
				releaseSuppressionAt = Client.TotalTime + 0.2f;
			}
		}
	}

	private void RebuildEditorSurfaceTextures()
	{
		previewDirty = false;
		try
		{
			using AtlasTextureSnapshot snapshot = editingSession.CreateTextureSnapshot();
			OwnedVoxelSurfaceTextureSet textures = voxelScene.CreateEditorSurfaceTextures(
				snapshot.BaseColor, snapshot.Normal, snapshot.Specular, snapshot.Roughness);
			editorTextureLease.QueueReplacement(textures);
			inspector.SetEditStatus(editingSession.IsDirty ? "Unsaved preview" : "Preview synchronized");
		}
		catch (Exception exception)
		{
			inspector.SetEditStatus($"Preview rebuild failed; last valid preview retained: {exception.Message}");
		}
	}

	private AtlasPixel EncodeSelectedColor()
	{
		return selectedLayer switch
		{
			AtlasPaintLayer.BaseColor => selectedColor,
			AtlasPaintLayer.Normal => AtlasPixel.Normal(
				selectedColor.R / 255f * 2 - 1,
				selectedColor.G / 255f * 2 - 1),
			AtlasPaintLayer.Specular or AtlasPaintLayer.Roughness => AtlasPixel.Scalar(selectedColor.R),
			_ => throw new ArgumentOutOfRangeException(),
		};
	}

	internal void SelectPaintLayer(AtlasPaintLayer layer)
	{
		selectedLayer = layer;
		inspector.SetSelectedLayer(layer);
	}

	internal void SetPaintColor(AtlasPixel color) => selectedColor = color;

	internal void SetPaintMode(bool enabled) => paintMode = enabled;

	internal void UndoPaint()
	{
		if (editingSession.History.Undo(editingSession.Documents))
			previewDirty = true;
	}

	internal void RedoPaint()
	{
		if (editingSession.History.Redo(editingSession.Documents))
			previewDirty = true;
	}

	internal void DiscardPaint()
	{
		try
		{
			editingSession.Discard();
			editorTextureLease.Clear();
			previewDirty = false;
			saveConflict = false;
			voxelScene.RequestSurfaceTextureReload();
			inspector.SetEditStatus("Changes discarded; authoritative assets reloaded");
		}
		catch (Exception exception)
		{
			inspector.SetEditStatus($"Discard failed; working preview retained: {exception.Message}");
		}
	}

	internal void SavePaint(bool overwriteConflicts = false)
	{
		overwriteConflicts |= saveConflict;
		saveConflict = false;
		if (editingSession.IsReadOnly)
		{
			inspector.SetEditStatus("Read-only: provide --asset-source-root <Voxelgine/data>");
			return;
		}
		saveReloadSuppression?.Dispose();
		saveReloadSuppression = fishWindow.Assets.AcquireReloadSuppression(FishGfxVoxelScene.SurfaceTextureAssetId);
		fishWindow.Assets.ClearQueuedReloads(FishGfxVoxelScene.SurfaceTextureAssetId);
		AtlasSaveResult result;
		try
		{
			result = saveService.Save(editingSession.BuildSaveDocuments(), overwriteConflicts);
		}
		catch (Exception exception)
		{
			saveReloadSuppression.Dispose();
			saveReloadSuppression = null;
			inspector.SetEditStatus($"Save failed before replacement: {exception.Message}");
			return;
		}
		if (result.Status == AtlasSaveStatus.Conflict)
		{
			saveConflict = true;
			inspector.SetEditStatus(result.Message + " Press Save again to explicitly Overwrite, or Discard to Reload External.");
		}
		else
		{
			inspector.SetEditStatus(result.Message);
		}
		if (!result.Succeeded)
		{
			saveReloadSuppression.Dispose();
			saveReloadSuppression = null;
			return;
		}
		awaitingSavedReload = voxelScene.RequestSurfaceTextureReload();
		if (!awaitingSavedReload)
		{
			inspector.SetEditStatus("Saved, but the explicit asset reload could not be queued; preview retained");
			releaseSuppressionAt = Client.TotalTime + 0.2f;
		}
	}

	private void RequestBack()
	{
		if (editingSession.IsDirty)
		{
			inspector.SetEditStatus("Unsaved changes: choose Save, Discard, or remain in Material Lab");
			return;
		}
		Client.RequestState(ClientStateKind.MainMenu);
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
		if (automaticFrame == 12)
		{
			PaintAutomaticLayerSet(BlockType.Stone, 0, 3, 5);
			SelectBlock(BlockType.Stone);
		}
		if (automaticFrame == 32)
		{
			PaintAutomaticLayerSet(BlockType.StoneStairs, 0, 12, 9);
			SelectBlock(BlockType.StoneStairs);
		}
		if (automaticFrame == 52)
		{
			AtlasPaintTarget barrel = editingSession.GetTarget(BlockType.Barrel, AtlasPaintLayer.BaseColor, -1);
			barrel.Set(4, 7, new AtlasPixel(0x47, 0xc8, 0xe8, 0xff));
			previewDirty = true;
			SelectBlock(BlockType.Barrel);
		}
		if (automaticFrame == 72)
		{
			AtlasPaintTarget test = editingSession.GetTarget(BlockType.Test, AtlasPaintLayer.BaseColor, 8);
			test.Set(20, 20, new AtlasPixel(0x47, 0xc8, 0xe8, 0xff));
			previewDirty = true;
			SelectBlock(BlockType.Test);
		}
		if (automaticFrame == 80 && paintGeometry.Model.Vertices.Any(static vertex => vertex.TextureLayer != 8))
			throw new InvalidOperationException("Test preview geometry does not consistently reference atlas tile 8.");
		if (automaticFrame == 80 && (map.GetBlock(0, 0, 0) != BlockType.Test
			|| voxelScene.World.GetVoxel(0, 0, 0).MaterialId != voxelScene.MaterialIds[BlockType.Test]))
		{
			throw new InvalidOperationException("Test preview world material identity is stale.");
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
			80 => BlockType.Test,
			_ => null,
		};
		if (blockType.HasValue)
		{
			SelectBlock(blockType.Value);
		}
	}

	private void PaintAutomaticLayerSet(BlockType block, int tile, int x, int y)
	{
		AtlasPaintTarget baseColor = editingSession.GetTarget(block, AtlasPaintLayer.BaseColor, tile);
		AtlasPaintTarget normal = editingSession.GetTarget(block, AtlasPaintLayer.Normal, tile);
		AtlasPaintTarget specular = editingSession.GetTarget(block, AtlasPaintLayer.Specular, tile);
		AtlasPaintTarget roughness = editingSession.GetTarget(block, AtlasPaintLayer.Roughness, tile);
		baseColor.Set(x, y, new AtlasPixel(0xd1, 0xa3, 0x65, 0xff));
		normal.Set(x, y, AtlasPixel.Normal(0.25f, 0.5f));
		specular.Set(x, y, AtlasPixel.Scalar(192));
		roughness.Set(x, y, AtlasPixel.Scalar(64));
		previewDirty = true;
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
