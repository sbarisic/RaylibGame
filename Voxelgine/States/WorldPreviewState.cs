#if WINDOWS
using System.Diagnostics;
using System.Numerics;
using System.Collections.Concurrent;
using FishGfx.Graphics;
using FishUI;
using FishUI.Controls;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.Engine.World.Structures;
using Voxelgine.FishGfxClient;
using Voxelgine.FishGfxClient.Rendering;
using Voxelgine.GUI;
using Voxelgine.Graphics;
using Voxelgine.WorldGeneration;

namespace Voxelgine.States;

public sealed class WorldPreviewState : GameStateImpl
{
	private readonly IFishGfxGameWindow fishWindow;
	private readonly RuntimePaths runtimePaths;
	private readonly FishUIManager gui;
	private readonly Panel sidebar;
	private readonly Panel viewport;
	private readonly ImageBox image;
	private readonly Textbox seedInput;
	private readonly Textbox pathInput;
	private readonly Label status;
	private readonly Label inspection;
	private readonly Label legend;
	private readonly Dictionary<WorldPlanRendering.Layer, Button> layerButtons = [];
	private readonly StructureBlueprintCatalog structureCatalog;
	private readonly VillagePrefabCatalog villagePrefabs;
	private CancellationTokenSource generationCancellation;
	private Task<WorldPlan> generationTask;
	private WorldPlan plan;
	private WorldPlanRendering.Layer layer = WorldPlanRendering.Layer.Combined;
	private long requestGeneration;
	private long pendingGeneration;
	private Stopwatch generationTimer;
	private float zoom;
	private Vector2 pan;
	private Vector2 previousMouse;
	private bool automatic;
	private bool automaticExportStarted;
	private Task automaticExport;
	private Task manualExport;
	private string manualExportPath;
	private Task<FishUIDebugSnapshot> automaticCapture;
	private string automaticBundle;
	private int readyFrames;
	private readonly ConcurrentQueue<WorldGenerationProgress> progressUpdates = new();

	public WorldPreviewState(IGameWindow window, IFishEngineRunner engine) : base(window, engine)
	{
		fishWindow = window as IFishGfxGameWindow ?? throw new ArgumentException("World Preview requires FishGfx.", nameof(window));
		runtimePaths = engine.AsClient().RuntimePaths;
		structureCatalog = StructureBlueprintCatalog.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "data", "world", "structures"));
		villagePrefabs = VillagePrefabCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data", "world", "village-prefabs", "catalog.json"));
		gui = new FishUIManager(window, engine.Logging, runtimePaths);
		sidebar = new Panel { ID = "world_preview_sidebar" };
		viewport = new Panel { ID = "world_preview_viewport" };
		image = new ImageBox { ID = "world_preview_image", ScaleMode = ImageScaleMode.Stretch, FilterMode = ImageFilterMode.Pixelated };
		viewport.AddChild(image);

		sidebar.AddChild(new Label { Text = "World Preview", Position = new Vector2(16, 16), Size = new Vector2(340, 28) });
		seedInput = new Textbox { ID = "world_preview_seed", Text = "666", Position = new Vector2(16, 54), Size = new Vector2(238, 30), TooltipText = "32-bit deterministic world seed" };
		sidebar.AddChild(seedInput);
		Button randomize = Button("Randomize", new Vector2(264, 54), new Vector2(100, 30), () => seedInput.Text = Random.Shared.Next().ToString()); sidebar.AddChild(randomize);
		Button generate = Button("Generate", new Vector2(16, 94), new Vector2(166, 34), GenerateFromInput); sidebar.AddChild(generate);
		Button cancel = Button("Cancel", new Vector2(198, 94), new Vector2(166, 34), CancelGeneration); sidebar.AddChild(cancel);

		float y = 144;
		foreach (WorldPlanRendering.Layer choice in Enum.GetValues<WorldPlanRendering.Layer>())
		{
			WorldPlanRendering.Layer captured = choice;
			Button button = Button(DisplayName(choice), new Vector2(16 + ((int)choice % 2) * 176, y + ((int)choice / 2) * 38), new Vector2(166, 32), () => SelectLayer(captured));
			button.ID = $"world_preview_layer_{choice.ToString().ToLowerInvariant()}";
			layerButtons[choice] = button; sidebar.AddChild(button);
		}
		y += ((Enum.GetValues<WorldPlanRendering.Layer>().Length + 1) / 2) * 38 + 12;
		Button fit = Button("Fit", new Vector2(16, y), new Vector2(106, 30), () => { zoom = 0; pan = Vector2.Zero; LayoutImage(); }); sidebar.AddChild(fit);
		Button oneToOne = Button("1:1", new Vector2(130, y), new Vector2(106, 30), () => { zoom = 1; pan = Vector2.Zero; LayoutImage(); }); sidebar.AddChild(oneToOne);
		Button back = Button("Back", new Vector2(244, y), new Vector2(120, 30), () => Client.RequestState(ClientStateKind.MainMenu)); sidebar.AddChild(back);

		pathInput = new Textbox { ID = "world_preview_path", Text = string.Empty, Position = new Vector2(16, y + 48), Size = new Vector2(348, 30), TooltipText = "Bundle directory; blank exports to the runtime world-plans folder" };
		sidebar.AddChild(pathInput);
		Button load = Button("Load bundle", new Vector2(16, y + 88), new Vector2(166, 34), LoadBundle); sidebar.AddChild(load);
		Button export = Button("Export bundle", new Vector2(198, y + 88), new Vector2(166, 34), ExportBundle); sidebar.AddChild(export);
		status = new Label { ID = "world_preview_status", Text = "Enter a seed and generate a plan.", Position = new Vector2(16, y + 136), Size = new Vector2(348, 64) }; sidebar.AddChild(status);
		inspection = new Label { ID = "world_preview_inspection", Text = "X/Z: -", Position = new Vector2(16, y + 206), Size = new Vector2(348, 72) }; sidebar.AddChild(inspection);
		legend = new Label { ID = "world_preview_legend", Text = "Biomes: Grassland  Forest  Sand\nRocky  Wetland  Void\nFeatures: hills, lakes, trees\nroads, villages, conduits, structures", Position = new Vector2(16, y + 284), Size = new Vector2(348, 88) }; sidebar.AddChild(legend);
		gui.AddControl(sidebar); gui.AddControl(viewport);
		Layout(); SelectLayer(layer);
	}

	internal bool IsReady => plan is not null && (!automatic || automaticExport?.IsCompletedSuccessfully == true && automaticCapture?.IsCompletedSuccessfully == true);

	internal void EnableAutomaticValidation()
	{
		automatic = true;
		seedInput.Text = "24681357";
		WorldGenerationSettings settings = new(24681357, 1024, 1024, 64);
		generationTimer = Stopwatch.StartNew();
		plan = WorldPlanMaterializer.GeneratePlan(settings.Width, settings.Length, settings.Seed, structureCatalog, villagePrefabs: villagePrefabs);
		status.Text = $"Generated seed {plan.Seed} with {plan.Villages.Count} villages in {generationTimer.Elapsed.TotalMilliseconds:F0} ms";
		RefreshImage();
		automaticBundle = Path.Combine(runtimePaths.Root, "world-plans", $"auto-{plan.Seed}-{Guid.NewGuid():N}");
		WorldPlanBundle.SaveAsync(automaticBundle, plan).GetAwaiter().GetResult();
		automaticExport = Task.CompletedTask;
		automaticExportStarted = true;
	}

	internal void ValidateAutomaticBundle()
	{
		if (plan is null || automaticExport is null || automaticCapture is null) throw new InvalidOperationException("Automatic World Preview did not queue bundle export and capture.");
		automaticExport.GetAwaiter().GetResult();
		WorldPlan loaded = WorldPlanBundle.LoadAsync(automaticBundle, expectedVillagePrefabCatalogHash: villagePrefabs.Hash).GetAwaiter().GetResult();
		if (loaded.Seed != plan.Seed || loaded.Width != 1024 || loaded.Length != 1024) throw new InvalidOperationException("Automatic World Preview bundle metadata is invalid.");
		if (loaded.Villages.Count < 6 || loaded.Villages.Any(village => village.Footprint.Any(point => loaded.GetTreeDensity(point.X, point.Z) != 0)))
			throw new InvalidOperationException("Automatic World Preview village reservations are invalid.");
		if (!loaded.Ponds.Any(pond => pond.Kind == HydrologyKind.Lake) || !loaded.HillMask.Span.ContainsAnyExcept((byte)0))
			throw new InvalidOperationException("Automatic World Preview did not generate lakes and unused-space hills.");
		foreach (string file in new[] { "manifest.json", "height.png", "biome.png", "hill-mask.png", "tree-density.png", "features.png", "features-floor-2.png", "features-floor-3.png", "features-roof.png", "combined.png" })
			if (!File.Exists(Path.Combine(automaticBundle, file))) throw new InvalidOperationException($"Automatic World Preview bundle is missing {file}.");
		FishUIDebugSnapshot snapshot = automaticCapture.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
		gui.UI.Diagnostics.WaitForPendingExportsAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
		if (snapshot.CaptureStatus != FishUIDebugCaptureStatus.Complete || snapshot.ScreenshotPng is null || snapshot.OverlayPng is null) throw new InvalidOperationException("Automatic World Preview snapshot is incomplete.");
	}

	public override void SwapTo() { gui.InputEnabled = true; fishWindow.RenderWindow.CaptureCursor = false; fishWindow.RenderWindow.ShowCursor = true; previousMouse = Window.InMgr.GetMousePos(); }
	public override void SwapFrom() => gui.InputEnabled = false;
	public override void Tick(float gameTime) { if (Window.InMgr.IsInputPressed(InputKey.Esc)) Client.RequestState(ClientStateKind.MainMenu); }
	public override void BeginInputFrame() => gui.BeginInputFrame();

	public override void BeginFrame(in FrameTiming timing)
	{
		Layout();
		CompleteGeneration();
		while (progressUpdates.TryDequeue(out WorldGenerationProgress update))
			if (generationTask is { IsCompleted: false }) status.Text = $"{update.Stage}  {update.Fraction:P0}";
		Vector2 mouse = Window.InMgr.GetMousePos();
		Vector2 viewportOrigin = viewport.GetAbsolutePosition();
		bool overViewport = mouse.X >= viewportOrigin.X && mouse.Y >= viewportOrigin.Y && mouse.X < viewportOrigin.X + viewport.Size.X && mouse.Y < viewportOrigin.Y + viewport.Size.Y;
		if (overViewport && Window.InMgr.IsInputDown(InputKey.Click_Middle)) { pan += mouse - previousMouse; LayoutImage(); }
		float wheel = overViewport ? Window.InMgr.GetMouseWheel() : 0;
		if (wheel != 0) { zoom = Math.Clamp((zoom <= 0 ? FitScale() : zoom) * MathF.Pow(1.15f, wheel), 0.05f, 16); LayoutImage(); }
		previousMouse = mouse;
		UpdateInspection(mouse);
		if (automatic && plan is not null && !automaticExportStarted)
		{
			automaticExportStarted = true;
			automaticBundle = Path.Combine(runtimePaths.Root, "world-plans", $"auto-{plan.Seed}-{Guid.NewGuid():N}");
			automaticExport = WorldPlanBundle.SaveAsync(automaticBundle, plan);
		}
		if (manualExport is { IsCompleted: true })
		{
			status.Text = manualExport.IsCompletedSuccessfully ? $"Exported {manualExportPath}" : $"Export failed: {manualExport.Exception?.GetBaseException().Message}";
			manualExport = null;
		}
		if (automatic && automaticExport?.IsCompletedSuccessfully == true && automaticCapture is null && ++readyFrames >= 4)
			automaticCapture = FishUIDiagnostics.CaptureAsync(gui.UI, new FishUIDebugSnapshotOptions(), FishUIDebugCaptureReason.TestFailure);
		gui.Update(timing.DeltaTime, timing.TotalTime);
	}

	public override GameStateRenderSettings GetRenderSettings(Vector2 framebufferSize)
	{
		GameStateRenderSettings overlay = GameStateRenderSettings.CreateOverlay(new Vector2(Window.Width, Window.Height));
		return new GameStateRenderSettings { WorldView = overlay.WorldView, ViewmodelView = overlay.ViewmodelView, OverlayView = overlay.OverlayView, ClearColor = new FishGfx.Color(20, 24, 31) };
	}
	public override void RenderOverlay(RenderPass pass, in FrameTiming timing) => gui.Render(pass, timing.DeltaTime, timing.TotalTime);
	public override void OnResize(IGameWindow window) { base.OnResize(window); gui.OnResize(window.Width, window.Height); Layout(); }

	protected override void DisposeCore()
	{
		generationCancellation?.Cancel(); generationCancellation?.Dispose(); gui.Dispose();
	}

	private void GenerateFromInput()
	{
		if (!int.TryParse(seedInput.Text?.Trim(), out int seed)) { status.Text = "Seed must be a 32-bit integer."; return; }
		StartGeneration(() => Task.Run(() => WorldPlanMaterializer.GeneratePlan(
			1024, 1024, seed, structureCatalog, generationCancellation.Token,
			new Progress<WorldGenerationProgress>(progressUpdates.Enqueue), villagePrefabs), generationCancellation.Token));
	}

	private void LoadBundle()
	{
		string path = pathInput.Text?.Trim();
		if (string.IsNullOrWhiteSpace(path)) { status.Text = "Enter a bundle directory to load."; return; }
		StartGeneration(() => WorldPlanBundle.LoadAsync(path, cancellationToken: generationCancellation.Token, expectedVillagePrefabCatalogHash: villagePrefabs.Hash));
	}

	private void StartGeneration(Func<Task<WorldPlan>> operation)
	{
		CancelGeneration(); while (progressUpdates.TryDequeue(out _)) { } generationCancellation = new(); pendingGeneration = ++requestGeneration; generationTimer = Stopwatch.StartNew();
		try { generationTask = operation(); status.Text = "Generating plan..."; }
		catch (Exception exception) { status.Text = exception.Message; }
	}

	private void CompleteGeneration()
	{
		if (generationTask is null || !generationTask.IsCompleted) return;
		Task<WorldPlan> completed = generationTask; long completedGeneration = pendingGeneration; generationTask = null;
		if (completedGeneration != requestGeneration) return;
		if (completed.IsCanceled) { status.Text = "Generation cancelled."; return; }
		if (completed.IsFaulted) { status.Text = $"Generation failed: {completed.Exception?.GetBaseException().Message}"; return; }
		plan = completed.Result; zoom = 0; pan = Vector2.Zero; RefreshImage();
		status.Text = $"Validated seed {plan.Seed}  {plan.Width}x{plan.Length}\n{plan.Ponds.Count(pond => pond.Kind == HydrologyKind.Lake)} lakes  {plan.Villages.Count} villages  {generationTimer.Elapsed.TotalMilliseconds:F0} ms";
	}

	private void CancelGeneration()
	{
		requestGeneration++; generationCancellation?.Cancel(); generationCancellation?.Dispose(); generationCancellation = null;
		if (generationTask is { IsCompleted: false }) status.Text = "Cancelling...";
	}

	private void ExportBundle()
	{
		if (plan is null) { status.Text = "Generate or load a plan before exporting."; return; }
		string path = pathInput.Text?.Trim();
		if (string.IsNullOrWhiteSpace(path)) path = Path.Combine(runtimePaths.Root, "world-plans", $"{plan.Seed}-{DateTime.UtcNow:yyyyMMddTHHmmssfffffffZ}");
		string exportPath = path;
		status.Text = $"Exporting {exportPath}...";
		manualExportPath = exportPath;
		manualExport = WorldPlanBundle.SaveAsync(exportPath, plan);
	}

	private void SelectLayer(WorldPlanRendering.Layer selected)
	{
		layer = selected;
		foreach ((WorldPlanRendering.Layer choice, Button button) in layerButtons) button.Text = (choice == layer ? "• " : string.Empty) + DisplayName(choice);
		if (plan is not null) RefreshImage();
	}

	private void RefreshImage()
	{
		byte[] png = WorldPlanRendering.EncodePreviewPng(plan, layer);
		string directory = Path.Combine(runtimePaths.Root, "world-plans", ".preview-cache"); Directory.CreateDirectory(directory);
		string path = Path.Combine(directory, $"{plan.Seed}-{requestGeneration}-{layer}-{Guid.NewGuid():N}.png"); File.WriteAllBytes(path, png);
		image.Image = gui.UI.Graphics.LoadImage(path); LayoutImage();
	}

	private void Layout()
	{
		float width = Math.Min(396, Math.Max(280, Window.Width * 0.28f));
		sidebar.Position = new Vector2(12, 12); sidebar.Size = new Vector2(width, Math.Max(0, Window.Height - 24));
		viewport.Position = new Vector2(width + 24, 12); viewport.Size = new Vector2(Math.Max(0, Window.Width - width - 36), Math.Max(0, Window.Height - 24));
		LayoutImage();
	}

	private void LayoutImage()
	{
		if (plan is null || viewport.Size.X <= 0 || viewport.Size.Y <= 0) return;
		float scale = zoom <= 0 ? FitScale() : zoom;
		Vector2 size = new(plan.Width * scale, plan.Length * scale);
		image.Size = size; image.Position = (viewport.Size - size) * 0.5f + pan;
	}

	private float FitScale() => plan is null ? 1 : Math.Min(viewport.Size.X / plan.Width, viewport.Size.Y / plan.Length);

	private void UpdateInspection(Vector2 mouse)
	{
		if (plan is null) return;
		Vector2 origin = image.GetAbsolutePosition(), size = image.GetAbsoluteSize();
		if (mouse.X < origin.X || mouse.Y < origin.Y || mouse.X >= origin.X + size.X || mouse.Y >= origin.Y + size.Y) { inspection.Text = "X/Z: -"; return; }
		int x = Math.Clamp((int)((mouse.X - origin.X) / size.X * plan.Width), 0, plan.Width - 1);
		int z = Math.Clamp((int)((mouse.Y - origin.Y) / size.Y * plan.Length), 0, plan.Length - 1);
		PlannedPond pond = plan.Ponds.FirstOrDefault(candidate => candidate.Cells.Any(cell => cell.X == x && cell.Z == z));
		PlannedWorldSite site = plan.Sites.FirstOrDefault(candidate => candidate.Reservation.Contains(x, z));
		PlannedVillageArea village = plan.Villages.FirstOrDefault(candidate => candidate.Footprint.Contains(new PlanPoint(x, z)));
		PlannedWorldRoute route = plan.Routes.FirstOrDefault(candidate => candidate.Cells.Any(cell => Math.Abs(cell.X - x) <= (candidate.Kind == WorldFeatureKind.Road ? 1 : 0) && Math.Abs(cell.Z - z) <= (candidate.Kind == WorldFeatureKind.Road ? 1 : 0)));
		PlannedVillageModule module = plan.VillageLayouts.SelectMany(static layout => layout.Modules)
			.Where(candidate => candidate.Floor == LayerFloor(layer) && x >= candidate.Origin.X && x < candidate.Origin.X + 5 && z >= candidate.Origin.Z && z < candidate.Origin.Z + 5)
			.FirstOrDefault();
		inspection.Text = $"X {x}  Z {z}  Y {(plan.IsLand(x, z) ? plan.GetHeight(x, z) : 0)}  hill {plan.GetHillHeight(x, z)}\n{plan.GetBiome(x, z)}  trees {plan.GetTreeDensity(x, z)}  water {(pond is null ? "-" : $"{pond.Kind} {pond.WaterLevel}")}\nfeature {module?.PrefabId ?? site?.Id ?? village?.Id ?? route?.Id ?? "-"}{(module is null ? "" : $"  C{module.ComponentId} R{module.Rotation}")}";
	}

	private static int LayerFloor(WorldPlanRendering.Layer value) => value switch
	{
		WorldPlanRendering.Layer.FeaturesFloor2 => 1,
		WorldPlanRendering.Layer.FeaturesFloor3 => 2,
		WorldPlanRendering.Layer.FeaturesRoof => 3,
		_ => 0,
	};

	private static Button Button(string text, Vector2 position, Vector2 size, Action pressed)
	{
		Button button = new() { Text = text, Position = position, Size = size }; button.OnButtonPressed += (_, _, _) => pressed(); return button;
	}
	private static string DisplayName(WorldPlanRendering.Layer value) => value switch
	{
		WorldPlanRendering.Layer.TreeDensity => "Tree Density",
		WorldPlanRendering.Layer.FeaturesFloor2 => "Second Floor",
		WorldPlanRendering.Layer.FeaturesFloor3 => "Third Floor",
		WorldPlanRendering.Layer.FeaturesRoof => "Roofs",
		WorldPlanRendering.Layer.Features => "First Floor",
		_ => value.ToString(),
	};
}
#endif
