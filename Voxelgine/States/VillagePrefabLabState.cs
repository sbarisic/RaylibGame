#if WINDOWS
using System.Numerics;
using FishGfx.Graphics;
using FishGfx.Voxels;
using FishUI;
using FishUI.Controls;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.Engine.World.Structures;
using Voxelgine.FishGfxClient;
using Voxelgine.FishGfxClient.Rendering;
using Voxelgine.FishGfxClient.Voxels;
using Voxelgine.GUI;
using Voxelgine.Graphics;
using Voxelgine.WorldGeneration;

namespace Voxelgine.States;

public sealed class VillagePrefabLabState : GameStateImpl
{
	private readonly IFishGfxGameWindow fishWindow;
	private readonly FishUIManager gui;
	private readonly RuntimePaths runtimePaths;
	private readonly string runtimeCatalogPath;
	private readonly string sourceCatalogPath;
	private readonly VillagePrefabEditingSession session;
	private readonly ChunkMap previewMap = new();
	private readonly FishGfxVoxelScene voxelScene;
	private readonly RenderQueue renderQueue = new();
	private readonly Camera camera = new();
	private readonly Panel sidebar;
	private readonly Panel editor;
	private readonly Panel editorHeaderPanel;
	private readonly Panel editorToolsPanel;
	private readonly Textbox search;
	private readonly ListBox prefabList;
	private readonly ListBox blockList;
	private readonly Label header;
	private readonly Label status;
	private readonly Label metadata;
	private readonly Textbox weightInput;
	private readonly Dictionary<int, CheckBox> rotationChecks = [];
	private readonly Label hoverLabel;
	private readonly Label[] axisLabels = new Label[6];
	private readonly Window createWindow;
	private readonly Textbox createId;
	private readonly Textbox createDisplayName;
	private readonly Label createError;
	private readonly Window semanticsWindow;
	private readonly ListBox semanticsList;
	private readonly Textbox semanticName;
	private readonly Label semanticError;
	private readonly DropDown externalEntryDropDown;
	private readonly Window rulesWindow;
	private readonly ListBox rulesList;
	private readonly Textbox ruleId;
	private readonly Textbox ruleFirstPattern;
	private readonly Textbox ruleSecondPattern;
	private readonly Textbox ruleWeightPercent;
	private readonly DropDown ruleRelation;
	private readonly Label ruleError;
	private readonly Dictionary<VillageSocketDirection, DropDown> socketDropDowns = [];
	private readonly Stack<BlockValue[]> undo = [];
	private readonly Stack<BlockValue[]> redo = [];
	private VillagePrefab selected;
	private BlockValue[] working;
	private BlockType paintBlock = BlockType.Plank;
	private Vector4 viewport;
	private Vector2 previousMouse;
	private float cameraYaw = 38;
	private float cameraElevation = 28;
	private float cameraDistance = 10;
	private bool synchronizingSockets;
	private bool automatic;
	private bool automaticComplete;
	private Task<FishUIDebugSnapshot> automaticCapture;
	private bool hasHover;
	private Int3 hoverCell;
	private Int3 occupiedCell;
	private bool hasOccupiedHit;
	private Vector2 middlePressPosition;
	private bool middleDragged;

	public VillagePrefabLabState(IGameWindow window, IFishEngineRunner engine) : base(window, engine)
	{
		fishWindow = window as IFishGfxGameWindow ?? throw new ArgumentException("Village Prefab Lab requires FishGfx.", nameof(window));
		IClientEngineRunner client = engine.AsClient();
		runtimePaths = client.RuntimePaths;
		gui = new FishUIManager(window, engine.Logging, runtimePaths);
		runtimeCatalogPath = RuntimeCatalogPath();
		sourceCatalogPath = ResolveSourceCatalogPath(client.AssetSourceRoot);
		VillagePrefabCatalog catalog = VillagePrefabCatalog.Load(runtimeCatalogPath);
		session = new VillagePrefabEditingSession(catalog);
		voxelScene = new FishGfxVoxelScene(fishWindow.RenderWindow.Graphics, fishWindow.Assets, previewMap,
			maxChunkDrawDistance: 64, chunkMeshUploadBudget: 16, fogQuality: VolumetricFogQuality.Off);
		voxelScene.Renderer.FogSettings = VoxelFogSettings.Disabled;

		sidebar = new Panel { ID = "village_prefab_sidebar" };
		editor = new Panel { ID = "village_prefab_editor", IsTransparent = true };
		editorHeaderPanel = new Panel
		{
			ID = "village_prefab_editor_header_panel", Position = new Vector2(8, 8), Size = new Vector2(850, 50),
			Variant = PanelVariant.Dark, BorderStyle = BorderStyle.Solid, BorderColor = new FishColor(70, 78, 92, 255)
		};
		editorToolsPanel = new Panel
		{
			ID = "village_prefab_editor_tools_panel", Position = new Vector2(8, 64), Size = new Vector2(230, 620),
			Variant = PanelVariant.Normal, BorderStyle = BorderStyle.Solid, BorderColor = new FishColor(135, 142, 154, 255)
		};
		editor.AddChild(editorHeaderPanel);
		editor.AddChild(editorToolsPanel);
		header = new Label { ID = "village_prefab_header", Text = "Village Prefab Lab", Position = new Vector2(16, 16), Size = new Vector2(328, 26) }; sidebar.AddChild(header);
		search = new Textbox { ID = "village_prefab_search", Placeholder = "Search prefab", Position = new Vector2(16, 50), Size = new Vector2(328, 30) };
		search.OnTextChanged += (_, _) => RebuildPrefabList(); sidebar.AddChild(search);
		prefabList = new ListBox { ID = "village_prefab_list", Position = new Vector2(16, 90), Size = new Vector2(328, 220), CustomItemHeight = 26 };
		prefabList.OnItemSelected += (_, _, item) => { if (item?.UserData is VillagePrefab prefab) Select(prefab); }; sidebar.AddChild(prefabList);
		AddButton(sidebar, "New", 16, 320, 76, ShowCreateDialog);
		AddButton(sidebar, "Duplicate", 98, 320, 94, DuplicatePrefab);
		AddButton(sidebar, "Delete", 198, 320, 70, DeletePrefab);
		AddButton(sidebar, "Save", 274, 320, 70, Save);
		metadata = new Label { ID = "village_prefab_metadata", Position = new Vector2(16, 366), Size = new Vector2(328, 52) }; sidebar.AddChild(metadata);
		sidebar.AddChild(new Label { Text = "Weight", Position = new Vector2(16, 424), Size = new Vector2(54, 26) });
		weightInput = new Textbox { ID = "village_prefab_weight", Position = new Vector2(72, 422), Size = new Vector2(76, 28) };
		weightInput.OnTextChanged += (_, value) => UpdateWeight(value); sidebar.AddChild(weightInput);
		sidebar.AddChild(new Label { Text = "Rotations", Position = new Vector2(158, 424), Size = new Vector2(70, 22) });
		int rotationIndex = 0;
		foreach (int rotation in new[] { 0, 90, 180, 270 })
		{
			float x = 166 + rotationIndex * 44;
			sidebar.AddChild(new Label { Text = $"{rotation}°", Position = new Vector2(x, 444), Size = new Vector2(38, 18) });
			CheckBox check = new() { ID = $"village_prefab_rotation_{rotation}", Position = new Vector2(x, 462), Size = new Vector2(20, 20), TooltipText = $"Allow {rotation} degree rotation" };
			int captured = rotation; check.OnCheckedChanged += (_, _) => UpdateRotations(captured); sidebar.AddChild(check); rotationChecks[rotation] = check;
			rotationIndex++;
		}
		status = new Label { ID = "village_prefab_status", Position = new Vector2(16, 490), Size = new Vector2(328, 42), Text = "Select a module to edit." }; sidebar.AddChild(status);
		AddButton(sidebar, "Adjacency rules...", 16, 538, 328, ShowRulesDialog);
		AddButton(sidebar, "Validate / Test WFC", 16, 576, 160, ValidateCatalog);
		AddButton(sidebar, "Back", 184, 576, 160, () => Client.RequestState(ClientStateKind.MainMenu));

		Label editorTitle = new() { Text = "3D 5 x 5 x 5 voxel editor", Position = new Vector2(20, 14), Size = new Vector2(220, 24) };
		editorTitle.SetColorOverride("Text", FishColor.White); editor.AddChild(editorTitle);
		hoverLabel = new Label { ID = "village_prefab_hover", Text = "Hover the white grid to inspect X/Y/Z.", Position = new Vector2(20, 40), Size = new Vector2(650, 24) }; editor.AddChild(hoverLabel);
		hoverLabel.SetColorOverride("Text", FishColor.White);
		editor.AddChild(new Label { Text = "Block palette", Position = new Vector2(20, 72), Size = new Vector2(210, 22) });
		blockList = new ListBox { ID = "village_prefab_block_list", Position = new Vector2(20, 98), Size = new Vector2(210, 205), CustomItemHeight = 24 };
		blockList.AddItem(new ListBoxItem("0: Erase", BlockType.None));
		foreach (BlockType block in Enum.GetValues<BlockType>().Where(static value => value != BlockType.None)) blockList.AddItem(new ListBoxItem($"{(int)block}: {block}", block));
		blockList.OnItemSelected += (_, _, item) => { if (item?.UserData is BlockType block) paintBlock = block; }; editor.AddChild(blockList);
		AddButton(editor, "Undo", 20, 312, 100, Undo);
		AddButton(editor, "Redo", 130, 312, 100, Redo);
		editor.AddChild(new Label { Text = "Semantic sockets", Position = new Vector2(20, 360), Size = new Vector2(210, 22) });
		int socketIndex = 0;
		foreach (VillageSocketDirection direction in Enum.GetValues<VillageSocketDirection>())
		{
			float y = 382 + socketIndex * 43;
			editor.AddChild(new Label { Text = VillageSocketCompatibility.Label(direction), Position = new Vector2(20, y), Size = new Vector2(42, 28) });
			DropDown dropDown = new()
			{
				ID = $"village_prefab_socket_{direction.ToString().ToLowerInvariant()}", Position = new Vector2(64, y), Size = new Vector2(166, 28),
				MaxVisibleItems = 12, MultiSelect = true, Searchable = true,
				TooltipText = $"Compatibility semantics for the {VillageSocketCompatibility.Label(direction)} face"
			};
			VillageSocketDirection captured = direction;
			dropDown.OnMultiSelectionChanged += (_, _) => SetSocket(captured, dropDown.GetSelectedItems()
				.Select(static item => item.UserData as string ?? item.Text).ToArray());
			editor.AddChild(dropDown); socketDropDowns[direction] = dropDown; socketIndex++;
		}
		AddButton(editor, "Edit semantic list...", 20, 646, 210, ShowSemanticsDialog);
		Label editorHelp = new() { Text = "L: place  R: erase  M: pick  Alt+L/M-drag: orbit  Wheel: zoom", Position = new Vector2(250, 14), Size = new Vector2(620, 24) };
		editorHelp.SetColorOverride("Text", FishColor.White); editor.AddChild(editorHelp);

		string[] axisText = ["+X", "-X", "+Y", "-Y", "+Z", "-Z"];
		for (int i = 0; i < axisLabels.Length; i++)
		{
			axisLabels[i] = new ScaledAxisLabel { Text = axisText[i], Size = new Vector2(52, 30) };
			editor.AddChild(axisLabels[i]);
		}

		createWindow = new Window { ID = "village_prefab_create_dialog", Title = "Create Village Prefab", Size = new Vector2(460, 286), IsResizable = false, IsModal = true, CloseButtonEnabled = true, ShowCloseButton = true, Visible = false };
		createWindow.AddChild(new Label { Text = "Prefab ID", Position = new Vector2(20, 48), Size = new Vector2(400, 22) });
		createId = new Textbox { ID = "village_prefab_create_id", Placeholder = "example: house.kitchen", Position = new Vector2(20, 74), Size = new Vector2(420, 30) }; createWindow.AddChild(createId);
		createWindow.AddChild(new Label { Text = "Display name", Position = new Vector2(20, 112), Size = new Vector2(400, 22) });
		createDisplayName = new Textbox { ID = "village_prefab_create_display_name", Placeholder = "example: Kitchen", Position = new Vector2(20, 138), Size = new Vector2(420, 30) }; createWindow.AddChild(createDisplayName);
		createError = new Label { ID = "village_prefab_create_error", Position = new Vector2(20, 176), Size = new Vector2(420, 34) }; createWindow.AddChild(createError);
		AddButton(createWindow, "Create", 20, 224, 200, ConfirmCreatePrefab); AddButton(createWindow, "Cancel", 240, 224, 200, () => HideModal(createWindow)); createWindow.OnClosed += HideModal;

		semanticsWindow = new Window { ID = "village_prefab_semantics_dialog", Title = "Socket Semantics", Size = new Vector2(480, 540), IsResizable = false, IsModal = true, CloseButtonEnabled = true, ShowCloseButton = true, Visible = false };
		semanticsWindow.AddChild(new Label { Text = "These names are shared by every prefab. Faces match when at least one checked name overlaps.\nAn empty selection is a sealed face.", Position = new Vector2(20, 46), Size = new Vector2(440, 48) });
		semanticsList = new ListBox { ID = "village_prefab_semantics_list", Position = new Vector2(20, 100), Size = new Vector2(440, 220), CustomItemHeight = 26 }; semanticsWindow.AddChild(semanticsList);
		semanticsWindow.AddChild(new Label { Text = "External entry semantic", Position = new Vector2(20, 328), Size = new Vector2(180, 24) });
		externalEntryDropDown = new DropDown { ID = "village_prefab_external_entry", Position = new Vector2(210, 326), Size = new Vector2(250, 28) };
		externalEntryDropDown.OnItemSelected += (_, item) => { if (!synchronizingSockets && item.UserData is string value) { session.SetExternalEntrySemantic(value); Refresh(); } }; semanticsWindow.AddChild(externalEntryDropDown);
		semanticName = new Textbox { ID = "village_prefab_semantic_name", Placeholder = "new semantic name", Position = new Vector2(20, 370), Size = new Vector2(300, 30) }; semanticsWindow.AddChild(semanticName);
		AddButton(semanticsWindow, "Add", 330, 368, 130, AddSocketSemantic);
		semanticError = new Label { ID = "village_prefab_semantic_error", Position = new Vector2(20, 408), Size = new Vector2(440, 34) }; semanticsWindow.AddChild(semanticError);
		AddButton(semanticsWindow, "Remove selected", 20, 468, 210, RemoveSocketSemantic); AddButton(semanticsWindow, "Done", 250, 468, 210, FinishEditingSemantics); semanticsWindow.OnClosed += _ => FinishEditingSemantics();

		rulesWindow = new Window { ID = "village_prefab_adjacency_rules", Title = "Adjacency Rules", Size = new Vector2(520, 500), IsResizable = false, IsModal = true, CloseButtonEnabled = true, ShowCloseButton = true, Visible = false };
		rulesWindow.AddChild(new Label { Text = "Rules multiply selection weight for neighboring prefab IDs. Use an exact ID or a trailing wildcard such as road.*. 0% is a hard exclusion.", Position = new Vector2(20, 44), Size = new Vector2(480, 50) });
		rulesList = new ListBox { ID = "village_prefab_adjacency_rule_list", Position = new Vector2(20, 98), Size = new Vector2(480, 130), CustomItemHeight = 27 }; rulesWindow.AddChild(rulesList);
		rulesWindow.AddChild(new Label { Text = "Rule ID", Position = new Vector2(20, 238), Size = new Vector2(110, 22) });
		ruleId = new Textbox { ID = "village_prefab_rule_id", Placeholder = "discourage-road-clusters", Position = new Vector2(140, 236), Size = new Vector2(360, 28) }; rulesWindow.AddChild(ruleId);
		rulesWindow.AddChild(new Label { Text = "First pattern", Position = new Vector2(20, 274), Size = new Vector2(110, 22) });
		ruleFirstPattern = new Textbox { ID = "village_prefab_rule_first", Placeholder = "road.*", Position = new Vector2(140, 272), Size = new Vector2(360, 28) }; rulesWindow.AddChild(ruleFirstPattern);
		rulesWindow.AddChild(new Label { Text = "Second pattern", Position = new Vector2(20, 310), Size = new Vector2(110, 22) });
		ruleSecondPattern = new Textbox { ID = "village_prefab_rule_second", Placeholder = "road.*", Position = new Vector2(140, 308), Size = new Vector2(360, 28) }; rulesWindow.AddChild(ruleSecondPattern);
		rulesWindow.AddChild(new Label { Text = "Weight percent", Position = new Vector2(20, 346), Size = new Vector2(110, 22) });
		ruleWeightPercent = new Textbox { ID = "village_prefab_rule_weight", Text = "25", Position = new Vector2(140, 344), Size = new Vector2(90, 28) }; rulesWindow.AddChild(ruleWeightPercent);
		ruleRelation = new DropDown { ID = "village_prefab_rule_relation", Position = new Vector2(240, 344), Size = new Vector2(130, 28) };
		foreach (VillageAdjacencyRelation relation in Enum.GetValues<VillageAdjacencyRelation>()) ruleRelation.AddItem(new DropDownItem(relation.ToString(), relation));
		ruleRelation.SelectIndex((int)VillageAdjacencyRelation.Disconnected); rulesWindow.AddChild(ruleRelation);
		AddButton(rulesWindow, "Add rule", 380, 342, 120, AddAdjacencyRule);
		ruleError = new Label { ID = "village_prefab_rule_error", Position = new Vector2(20, 384), Size = new Vector2(480, 42) }; rulesWindow.AddChild(ruleError);
		AddButton(rulesWindow, "Remove selected", 20, 442, 230, RemoveAdjacencyRule); AddButton(rulesWindow, "Done", 270, 442, 230, () => HideModal(rulesWindow)); rulesWindow.OnClosed += HideModal;

		gui.AddControl(sidebar); gui.AddControl(editor); gui.AddControl(createWindow); gui.AddControl(semanticsWindow); gui.AddControl(rulesWindow);
		RebuildSocketDropDowns(); RebuildPrefabList(); Select(session.Prefabs[0]); Layout(); ConfigureCamera();
	}

	internal bool IsReady => (!automatic || automaticComplete) && voxelScene.Renderer.IsIdle
		&& voxelScene.GetPresentationState(new ChunkCoordinate(0, 0, 0)) == VoxelPresentationState.Resident;
	internal void EnableAutomaticValidation()
	{
		automatic = true;
		Select(session.Prefabs.First(static prefab => prefab.Descriptor.HasVoxels));
		string semantic = "auto_socket";
		if (!session.SocketSemantics.Contains(semantic, StringComparer.Ordinal)) session.AddSemantic(semantic);
		RebuildSocketDropDowns(); SetSocket(VillageSocketDirection.PositiveX, [semantic]); SetSocket(VillageSocketDirection.NegativeX, []);
		Paint(new(2, 0, 0), BlockType.None); Paint(new(2, 1, 0), BlockType.None); Undo(); Redo();
		string path = Path.Combine(runtimePaths.Root, "prefab-lab-auto", $"catalog-{Guid.NewGuid():N}.json");
		(VillagePrefab[] modules, string[] semantics, string externalEntry, VillageAdjacencyRuleDescriptor[] rules, _) = session.Snapshot(); VillagePrefabCatalog.Save(path, modules, semantics, externalEntry, rules);
		VillagePrefabCatalog loaded = VillagePrefabCatalog.Load(path);
		if (!loaded.SocketSemantics.Contains(semantic, StringComparer.Ordinal)) throw new InvalidOperationException("Automatic semantic persistence failed.");
		if (!loaded.AdjacencyRules.SequenceEqual(rules)) throw new InvalidOperationException("Automatic adjacency-rule persistence failed.");
		if (loaded.Get(selected.Descriptor.Id).Descriptor.Socket(VillageSocketDirection.NegativeX).Types.Length != 0)
			throw new InvalidOperationException("Automatic disabled-face socket persistence failed.");
		status.Text = "Automatic 3D prefab edit/save/load validation passed; waiting for preview mesh.";
	}

	internal void ValidateAutomaticSnapshotBundle()
	{
		if (!automatic || automaticCapture is null) throw new InvalidOperationException("The automatic Village Prefab Lab snapshot was not queued.");
		FishUIDebugSnapshot snapshot = automaticCapture.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
		gui.UI.Diagnostics.WaitForPendingExportsAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
		if (snapshot.CaptureStatus != FishUIDebugCaptureStatus.Complete || snapshot.ScreenshotPng is null || snapshot.OverlayPng is null)
			throw new InvalidOperationException("Automatic Village Prefab Lab snapshot was incomplete.");
	}

	public override void SwapTo() { gui.InputEnabled = true; fishWindow.RenderWindow.CaptureCursor = false; fishWindow.RenderWindow.ShowCursor = true; previousMouse = Window.InMgr.GetMousePos(); }
	public override void SwapFrom() => gui.InputEnabled = false;
	public override void BeginInputFrame() => gui.BeginInputFrame();
	public override void Tick(float gameTime) { if (Window.InMgr.IsInputPressed(InputKey.Esc)) Client.RequestState(ClientStateKind.MainMenu); }
	public override void BeginFrame(in FrameTiming timing)
	{
		Layout(); ConfigureCamera(); Vector2 mouse = Window.InMgr.GetMousePos();
		bool modalOpen = IsModalOpen();
		bool overViewport = !modalOpen && Contains(viewport, mouse);
		if (!modalOpen && Window.InMgr.IsInputPressed(InputKey.Click_Middle)) { middlePressPosition = mouse; middleDragged = false; }
		if (!modalOpen && Window.InMgr.IsInputDown(InputKey.Click_Middle) && Vector2.DistanceSquared(mouse, middlePressPosition) > 9) middleDragged = true;
		bool orbit = !modalOpen && overViewport && (Window.InMgr.IsInputDown(InputKey.Click_Middle) && middleDragged || Window.InMgr.IsInputDown(InputKey.Click_Left) && Window.InMgr.IsInputDown(InputKey.Alt));
		if (orbit) { Vector2 delta = mouse - previousMouse; cameraYaw += delta.X * .35f; cameraElevation = Math.Clamp(cameraElevation - delta.Y * .25f, -85, 85); }
		if (overViewport && !orbit)
		{
			hasHover = TryPick(mouse, out hoverCell, out occupiedCell, out hasOccupiedHit);
			if (hasHover) hoverLabel.Text = $"X={hoverCell.X}  Y={hoverCell.Y}  Z={hoverCell.Z}  •  {working[Index(hoverCell)].Type}  •  paint {paintBlock}";
			if (Window.InMgr.IsInputPressed(InputKey.Click_Left) && hasHover) Paint(hoverCell, paintBlock);
			if (Window.InMgr.IsInputPressed(InputKey.Click_Right) && hasOccupiedHit) Paint(occupiedCell, BlockType.None);
			if (Window.InMgr.IsInputReleased(InputKey.Click_Middle) && !middleDragged && hasOccupiedHit) { paintBlock = working[Index(occupiedCell)].Type; SelectBlockList(paintBlock); }
			cameraDistance = Math.Clamp(cameraDistance - Window.InMgr.GetMouseWheel() * .5f, 6, 22);
		}
		else if (!overViewport) { hasHover = false; hoverLabel.Text = modalOpen ? "Close the dialog to edit the 3D prefab." : "Hover the white grid to inspect X/Y/Z."; }
		previousMouse = mouse; UpdateAxisLabels(); voxelScene.Update(camera); gui.Update(timing.DeltaTime, timing.TotalTime);
		if (automatic && automaticCapture is null && voxelScene.Renderer.IsIdle
			&& voxelScene.GetPresentationState(new ChunkCoordinate(0, 0, 0)) == VoxelPresentationState.Resident)
		{
			automaticCapture = FishUIDiagnostics.CaptureAsync(gui.UI, new FishUIDebugSnapshotOptions(), FishUIDebugCaptureReason.TestFailure);
			automaticComplete = true; status.Text = "Automatic 3D prefab edit/save/load validation passed; capturing UI.";
		}
	}

	public override GameStateRenderSettings GetRenderSettings(Vector2 framebufferSize)
	{
		ConfigureCamera(framebufferSize); GameStateRenderSettings overlay = GameStateRenderSettings.CreateOverlay(new(Window.Width, Window.Height));
		return new() { WorldView = new RenderView(camera), ViewmodelView = new RenderView(camera), OverlayView = overlay.OverlayView, ClearColor = new(20, 24, 31) };
	}

	public override void RenderWorld(RenderPass pass, in FrameTiming timing)
	{
		renderQueue.BeginFrame(); voxelScene.Enqueue(renderQueue, camera, shadows: null); pass.Execute(renderQueue, RenderQueueBucket.Opaque); pass.Execute(renderQueue, RenderQueueBucket.Transparent);
		FishGfx.Color inner = FishGfx.ColorSpace.SrgbToLinearColor(new FishGfx.Color(180, 190, 205, 80));
		FishGfx.Color outer = FishGfx.ColorSpace.SrgbToLinearColor(new FishGfx.Color(255, 255, 255, 210));
		for (int a = 0; a <= 5; a++) for (int b = 0; b <= 5; b++)
		{
			FishGfx.Color color = a is 0 or 5 || b is 0 or 5 ? outer : inner;
			pass.DrawLine(new FishGfx.Vertex3(new(a, b, 0), color), new FishGfx.Vertex3(new(a, b, 5), color));
			pass.DrawLine(new FishGfx.Vertex3(new(a, 0, b), color), new FishGfx.Vertex3(new(a, 5, b), color));
			pass.DrawLine(new FishGfx.Vertex3(new(0, a, b), color), new FishGfx.Vertex3(new(5, a, b), color));
		}
		if (hasHover) DrawCell(pass, hoverCell, new FishGfx.Color(71, 200, 232));
		DrawAxes(pass);
	}

	public override void RenderOverlay(RenderPass pass, in FrameTiming timing) => gui.Render(pass, timing.DeltaTime, timing.TotalTime);
	public override void OnResize(IGameWindow window) { base.OnResize(window); gui.OnResize(window.Width, window.Height); Layout(); }
	protected override void DisposeCore() { renderQueue.Clear(); voxelScene.Dispose(); gui.Dispose(); }

	private void Layout()
	{
		float left = Math.Min(376, Math.Max(300, Window.Width * .3f)); sidebar.Position = new Vector2(12, 12); sidebar.Size = new Vector2(left, Math.Max(0, Window.Height - 24));
		editor.Position = new Vector2(left + 24, 12); editor.Size = new Vector2(Math.Max(0, Window.Width - left - 36), Math.Max(0, Window.Height - 24));
		editorHeaderPanel.Size = new Vector2(Math.Max(0, editor.Size.X - 16), 50);
		editorToolsPanel.Size = new Vector2(230, Math.Max(0, Math.Min(620, editor.Size.Y - 72)));
		viewport = new(editor.Position.X + 246, editor.Position.Y + 42, Math.Max(0, editor.Size.X - 258), Math.Max(0, editor.Size.Y - 54));
		CenterModal(createWindow); CenterModal(semanticsWindow); CenterModal(rulesWindow);
	}

	private void ConfigureCamera() => ConfigureCamera(fishWindow.RenderWindow.FramebufferSize);
	private void ConfigureCamera(Vector2 framebufferSize)
	{
		float yaw = cameraYaw * MathF.PI / 180, elevation = cameraElevation * MathF.PI / 180, horizontal = MathF.Cos(elevation) * cameraDistance;
		Vector3 target = new(2.5f); camera.Position = target + new Vector3(MathF.Sin(yaw) * horizontal, MathF.Sin(elevation) * cameraDistance, MathF.Cos(yaw) * horizontal);
		camera.LookAt(target); camera.SetPerspective(framebufferSize, 45 * MathF.PI / 180, .05f, 128);
	}

	private void RebuildPrefabList()
	{
		string query = search.Text?.Trim() ?? string.Empty; prefabList.Items.Clear();
		foreach (VillagePrefab prefab in session.Prefabs.Where(prefab => query.Length == 0 || prefab.Descriptor.Id.Contains(query, StringComparison.OrdinalIgnoreCase) || prefab.Descriptor.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)))
			prefabList.AddItem(new ListBoxItem($"{prefab.Descriptor.DisplayName} ({prefab.Descriptor.Id})", prefab));
	}

	private void Select(VillagePrefab prefab)
	{
		CommitWorking(); selected = session.Prefabs.FirstOrDefault(value => value.Descriptor.Id == prefab.Descriptor.Id) ?? prefab;
		working = Enumerable.Range(0, 125).Select(index => selected.GetCell(index % 5, index / 25, index / 5 % 5)).ToArray(); undo.Clear(); redo.Clear(); SynchronizePreview(); Refresh();
	}

	private void CommitWorking()
	{
		if (selected is null || working is null) return;
		VillagePrefab updated = new(selected.Descriptor, working);
		if (!CellsEqual(updated, selected) || !DescriptorEqual(updated.Descriptor, selected.Descriptor)) session.Replace(updated);
		selected = updated;
	}

	private void Paint(Int3 cell, BlockType block)
	{
		if (working is null || !InBounds(cell) || working[Index(cell)].Type == block) return;
		PushUndo(); working[Index(cell)] = new(block); previewMap.SetBlock(cell.X, cell.Y, cell.Z, new BlockValue(block)); CommitWorking(); Refresh();
	}

	private void PushUndo() { undo.Push(working.ToArray()); while (undo.Count > 100) { BlockValue[][] keep = undo.Reverse().TakeLast(100).ToArray(); undo.Clear(); foreach (BlockValue[] value in keep) undo.Push(value); } redo.Clear(); }
	private void Undo() { if (undo.TryPop(out BlockValue[] value)) { redo.Push(working.ToArray()); working = value; SynchronizePreview(); CommitWorking(); Refresh(); } }
	private void Redo() { if (redo.TryPop(out BlockValue[] value)) { undo.Push(working.ToArray()); working = value; SynchronizePreview(); CommitWorking(); Refresh(); } }

	private void SynchronizePreview() { for (int y = 0; y < 5; y++) for (int z = 0; z < 5; z++) for (int x = 0; x < 5; x++) previewMap.SetBlock(x, y, z, working[(y * 5 + z) * 5 + x]); }

	private void Refresh()
	{
		if (selected is null) return; header.Text = $"Village Prefab Lab  •  {selected.Descriptor.DisplayName}";
		metadata.Text = $"ID: {selected.Descriptor.Id}\nEntry semantic: {session.ExternalEntrySemantic}\nRevision: {session.Revision}{(session.IsDirty ? "  •  unsaved" : string.Empty)}";
		synchronizingSockets = true;
		try
		{
			weightInput.Text = selected.Descriptor.Weight.ToString();
			foreach ((int rotation, CheckBox check) in rotationChecks) check.IsChecked = selected.Descriptor.AllowedRotations.Contains(rotation);
			foreach ((VillageSocketDirection direction, DropDown dropDown) in socketDropDowns)
			{
				dropDown.ClearSelection(); string[] selectedTypes = selected.Descriptor.Socket(direction).Types;
				for (int index = 0; index < dropDown.Items.Count; index++) if (selectedTypes.Contains(dropDown.Items[index].UserData as string ?? dropDown.Items[index].Text, StringComparer.Ordinal)) dropDown.ToggleItemSelection(index);
			}
		}
		finally { synchronizingSockets = false; }
	}

	private void UpdateWeight(string value)
	{
		if (synchronizingSockets || selected is null || !int.TryParse(value, out int weight) || weight is < 1 or > 1_000_000) return;
		if (selected.Descriptor.Weight == weight) return;
		selected = new VillagePrefab(selected.Descriptor with { Weight = weight }, working); session.Replace(selected); Refresh();
	}

	private void UpdateRotations(int _)
	{
		if (synchronizingSockets || selected is null) return;
		int[] rotations = rotationChecks.Where(static pair => pair.Value.IsChecked).Select(static pair => pair.Key).Order().ToArray();
		if (rotations.Length == 0) { synchronizingSockets = true; rotationChecks[selected.Descriptor.AllowedRotations[0]].IsChecked = true; synchronizingSockets = false; status.Text = "At least one rotation is required."; return; }
		if (selected.Descriptor.AllowedRotations.SequenceEqual(rotations)) return;
		selected = new VillagePrefab(selected.Descriptor with { AllowedRotations = rotations }, working); session.Replace(selected); Refresh();
	}

	private void SetSocket(VillageSocketDirection direction, string[] values)
	{
		if (synchronizingSockets || selected is null) return;
		try
		{
			values = values.Distinct(StringComparer.Ordinal).ToArray();
			string[] current = selected.Descriptor.Socket(direction).Types;
			if (current.SequenceEqual(values)) { Refresh(); return; }
			VillageSocketDescriptor[] sockets = selected.Descriptor.Sockets.Select(socket => socket.Direction == direction ? socket with { Types = values } : socket).ToArray();
			selected = new VillagePrefab(selected.Descriptor with { Sockets = sockets }, working); session.Replace(selected);
			status.Text = values.Length == 0
				? $"Disabled all connections on {VillageSocketCompatibility.Label(direction)}."
				: $"Updated {VillageSocketCompatibility.Label(direction)} semantics.";
		}
		catch (Exception exception) { status.Text = $"Socket change rejected: {exception.Message}"; }
		Refresh();
	}

	private void ShowCreateDialog() { createId.Text = string.Empty; createDisplayName.Text = string.Empty; createError.Text = string.Empty; ShowModal(createWindow); }
	private void ConfirmCreatePrefab()
	{
		try
		{
			string id = createId.Text?.Trim() ?? string.Empty, display = createDisplayName.Text?.Trim() ?? string.Empty;
			if (id.Length is < 1 or > 64) throw new InvalidDataException("Enter a prefab ID containing 1-64 characters.");
			if (display.Length is < 1 or > 96) throw new InvalidDataException("Enter a display name containing 1-96 characters.");
			VillagePrefabDescriptor template = session.Prefabs[0].Descriptor;
			VillageSocketDescriptor[] sockets = Enum.GetValues<VillageSocketDirection>().Select(static direction => new VillageSocketDescriptor(direction, [], new byte[25])).ToArray();
			VillagePrefab prefab = new(template with { Id = id, DisplayName = display, Weight = 1, AllowedRotations = [0, 90, 180, 270], Sockets = sockets, SupportMask = new byte[25], LoadMask = new byte[25], WalkableMask = new byte[25], Markers = [] }, new BlockValue[125]);
			session.Add(prefab); RebuildPrefabList(); Select(prefab); HideModal(createWindow);
		}
		catch (Exception exception) { createError.Text = exception.Message; }
	}

	private void DuplicatePrefab() { if (selected is null) return; int suffix = 1; while (session.Prefabs.Any(prefab => prefab.Descriptor.Id == $"{selected.Descriptor.Id}.copy{suffix}")) suffix++; VillagePrefab copy = new(selected.Descriptor with { Id = $"{selected.Descriptor.Id}.copy{suffix}", DisplayName = $"{selected.Descriptor.DisplayName} Copy {suffix}" }, working); session.Add(copy); RebuildPrefabList(); Select(copy); }
	private void DeletePrefab() { if (selected is null) return; int index = session.Prefabs.ToList().FindIndex(value => value.Descriptor.Id == selected.Descriptor.Id); session.Remove(selected.Descriptor.Id); selected = null; working = null; RebuildPrefabList(); Select(session.Prefabs[Math.Min(index, session.Prefabs.Count - 1)]); }
	private void Save()
	{
		try
		{
			CommitWorking(); (VillagePrefab[] prefabs, string[] semantics, string externalEntry, VillageAdjacencyRuleDescriptor[] rules, long revision) = session.Snapshot();
			string[] targets = new[] { sourceCatalogPath, runtimeCatalogPath }.Where(static path => !string.IsNullOrWhiteSpace(path)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
			IReadOnlyList<VillagePrefabCatalog> saved = VillagePrefabCatalog.SaveSynchronized(targets, prefabs, semantics, externalEntry, rules); session.MarkSaved(revision);
			status.Text = $"Saved and verified {prefabs.Length} modules, {semantics.Length} semantics.\nHash {saved[0].Hash[..12]}\n{string.Join("\n", targets)}"; Refresh();
		}
		catch (Exception exception) { status.Text = $"Save failed; previous files restored.\n{exception.Message}"; }
	}

	private void ValidateCatalog() { try { CommitWorking(); (VillagePrefab[] prefabs, string[] semantics, string externalEntry, VillageAdjacencyRuleDescriptor[] rules, _) = session.Snapshot(); VillagePrefabCatalogDescriptor descriptor = new(prefabs.Select(static prefab => prefab.Descriptor), socketSemantics: semantics, externalEntrySemantic: externalEntry, adjacencyRules: rules); status.Text = descriptor.HasUsefulConnectedChain() ? $"Valid: {descriptor.Prefabs.Count} modules, {descriptor.Prefabs.Sum(static prefab => prefab.AllowedRotations.Length)} configured rotations, {descriptor.SocketSemantics.Count} semantics, {descriptor.AdjacencyRules.Count} adjacency rules." : $"Catalog is valid, but no connected socket chain leads from '{externalEntry}' to a terminating prefab. Villages will remain empty."; } catch (Exception exception) { status.Text = $"Validation failed: {exception.Message}"; } }
	private void ShowSemanticsDialog() { CloseSocketDropDowns(); RebuildSemanticsList(); RebuildExternalEntryDropDown(); semanticName.Text = string.Empty; semanticError.Text = string.Empty; ShowModal(semanticsWindow); }
	private void FinishEditingSemantics() { CloseSocketDropDowns(); RebuildSocketDropDowns(); Refresh(); HideModal(semanticsWindow); }
	private void AddSocketSemantic() { try { session.AddSemantic(semanticName.Text); semanticName.Text = string.Empty; semanticError.Text = string.Empty; RebuildSemanticsList(); RebuildSocketDropDowns(); RebuildExternalEntryDropDown(); Refresh(); } catch (Exception exception) { semanticError.Text = exception.Message; } }
	private void RemoveSocketSemantic() { try { if (semanticsList.GetSelectedItems().FirstOrDefault()?.UserData is not string value) throw new InvalidDataException("Select a socket semantic to remove."); session.RemoveSemantic(value); semanticError.Text = string.Empty; RebuildSemanticsList(); RebuildSocketDropDowns(); Refresh(); } catch (Exception exception) { semanticError.Text = exception.Message; } }
	private void RebuildSemanticsList() { semanticsList.Items.Clear(); foreach (string value in session.SocketSemantics) semanticsList.AddItem(new ListBoxItem(value, value)); }
	private void RebuildExternalEntryDropDown()
	{
		synchronizingSockets = true;
		try
		{
			externalEntryDropDown.ClearItems();
			foreach (string value in session.SocketSemantics) externalEntryDropDown.AddItem(new DropDownItem(value, value));
			int index = session.SocketSemantics.ToList().FindIndex(value => value == session.ExternalEntrySemantic);
			if (index >= 0) externalEntryDropDown.SelectIndex(index);
		}
		finally { synchronizingSockets = false; }
	}
	private void RebuildSocketDropDowns()
	{
		synchronizingSockets = true;
		try
		{
			foreach (DropDown dropDown in socketDropDowns.Values)
			{
				dropDown.Close(); dropDown.ClearItems();
				foreach (string value in session.SocketSemantics) dropDown.AddItem(new DropDownItem(value, value));
			}
		}
		finally { synchronizingSockets = false; }
	}
	private void CloseSocketDropDowns() { foreach (DropDown dropDown in socketDropDowns.Values) dropDown.Close(); }
	private void ShowRulesDialog()
	{
		RebuildRulesList(); ruleId.Text = string.Empty; ruleFirstPattern.Text = string.Empty;
		ruleSecondPattern.Text = string.Empty; ruleWeightPercent.Text = "25"; ruleRelation.SelectIndex((int)VillageAdjacencyRelation.Disconnected); ruleError.Text = string.Empty; ShowModal(rulesWindow);
	}
	private void AddAdjacencyRule()
	{
		try
		{
			if (!int.TryParse(ruleWeightPercent.Text, out int weight)) throw new InvalidDataException("Weight percent must be an integer from 0 through 100.");
			VillageAdjacencyRelation relation = ruleRelation.GetSelectedItem()?.UserData is VillageAdjacencyRelation value ? value : VillageAdjacencyRelation.Disconnected;
			VillageAdjacencyRuleDescriptor rule = new(ruleId.Text?.Trim() ?? string.Empty,
				ruleFirstPattern.Text?.Trim() ?? string.Empty, ruleSecondPattern.Text?.Trim() ?? string.Empty, weight, relation);
			session.AddAdjacencyRule(rule); ruleError.Text = string.Empty; RebuildRulesList(); Refresh();
		}
		catch (Exception exception) { ruleError.Text = exception.Message; }
	}
	private void RemoveAdjacencyRule()
	{
		try
		{
			if (rulesList.GetSelectedItems().FirstOrDefault()?.UserData is not VillageAdjacencyRuleDescriptor rule)
				throw new InvalidDataException("Select an adjacency rule to remove.");
			session.RemoveAdjacencyRule(rule.Id); ruleError.Text = string.Empty; RebuildRulesList(); Refresh();
		}
		catch (Exception exception) { ruleError.Text = exception.Message; }
	}
	private void RebuildRulesList()
	{
		rulesList.Items.Clear();
		foreach (VillageAdjacencyRuleDescriptor rule in session.AdjacencyRules)
			rulesList.AddItem(new ListBoxItem($"{rule.Id}: {rule.FirstPattern} ↔ {rule.SecondPattern}, {rule.Relation}, {rule.WeightPercent}%", rule));
	}

	private bool TryPick(Vector2 logicalMouse, out Int3 placement, out Int3 occupied, out bool occupiedHit)
	{
		Vector2 framebufferMouse = new(logicalMouse.X * fishWindow.RenderWindow.FramebufferWidth / Math.Max(1, Window.Width), logicalMouse.Y * fishWindow.RenderWindow.FramebufferHeight / Math.Max(1, Window.Height));
		PickingRay ray = camera.CreatePickingRay(framebufferMouse); float nearest = float.PositiveInfinity; occupied = default; Vector3 normal = default; occupiedHit = false;
		for (int y = 0; y < 5; y++) for (int z = 0; z < 5; z++) for (int x = 0; x < 5; x++) if (working[(y * 5 + z) * 5 + x].Type != BlockType.None && RayBox(ray, new(x, y, z), new(x + 1, y + 1, z + 1), out float distance, out Vector3 hitNormal) && distance < nearest)
		{ nearest = distance; occupied = new(x, y, z); normal = hitNormal; occupiedHit = true; }
		if (occupiedHit)
		{
			Int3 adjacent = new(occupied.X + (int)normal.X, occupied.Y + (int)normal.Y, occupied.Z + (int)normal.Z);
			placement = InBounds(adjacent) && working[Index(adjacent)].Type == BlockType.None ? adjacent : occupied; return true;
		}
		if (!RayBox(ray, Vector3.Zero, new(5), out float volumeDistance, out _)) { placement = default; return false; }
		Vector3 point = ray.GetPoint(volumeDistance + .001f); placement = new(Math.Clamp((int)MathF.Floor(point.X), 0, 4), Math.Clamp((int)MathF.Floor(point.Y), 0, 4), Math.Clamp((int)MathF.Floor(point.Z), 0, 4)); return true;
	}

	private static bool RayBox(PickingRay ray, Vector3 minimum, Vector3 maximum, out float distance, out Vector3 normal)
	{
		float near = 0, far = float.PositiveInfinity; normal = default;
		for (int axis = 0; axis < 3; axis++)
		{
			float origin = axis == 0 ? ray.Origin.X : axis == 1 ? ray.Origin.Y : ray.Origin.Z, direction = axis == 0 ? ray.Direction.X : axis == 1 ? ray.Direction.Y : ray.Direction.Z;
			float min = axis == 0 ? minimum.X : axis == 1 ? minimum.Y : minimum.Z, max = axis == 0 ? maximum.X : axis == 1 ? maximum.Y : maximum.Z;
			if (MathF.Abs(direction) < 1e-6f) { if (origin < min || origin > max) { distance = 0; return false; } continue; }
			float a = (min - origin) / direction, b = (max - origin) / direction; Vector3 axisNormal = axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ;
			Vector3 candidate = -axisNormal * MathF.Sign(direction); if (a > b) (a, b) = (b, a);
			if (a > near) { near = a; normal = candidate; } far = MathF.Min(far, b); if (near > far) { distance = 0; return false; }
		}
		distance = near; return far >= 0;
	}

	private void DrawAxes(RenderPass pass)
	{
		Vector3 center = new(2.5f); float length = 1.15f;
		DrawAxis(pass, center, Vector3.UnitX * length, new(235, 75, 75), new(120, 45, 45)); DrawAxis(pass, center, Vector3.UnitY * length, new(75, 220, 105), new(40, 110, 55)); DrawAxis(pass, center, Vector3.UnitZ * length, new(75, 130, 240), new(40, 65, 120));
	}
	private static void DrawAxis(RenderPass pass, Vector3 center, Vector3 axis, FishGfx.Color positive, FishGfx.Color negative) { positive = FishGfx.ColorSpace.SrgbToLinearColor(positive); negative = FishGfx.ColorSpace.SrgbToLinearColor(negative); pass.DrawLine(new FishGfx.Vertex3(center, positive), new FishGfx.Vertex3(center + axis, positive)); pass.DrawLine(new FishGfx.Vertex3(center, negative), new FishGfx.Vertex3(center - axis, negative)); }
	private void UpdateAxisLabels()
	{
		Vector3 center = new(2.5f); Vector3[] points = [center + Vector3.UnitX * 1.25f, center - Vector3.UnitX * 1.25f, center + Vector3.UnitY * 1.25f, center - Vector3.UnitY * 1.25f, center + Vector3.UnitZ * 1.25f, center - Vector3.UnitZ * 1.25f];
		for (int index = 0; index < points.Length; index++)
		{
			Vector3 screen = camera.WorldToScreen(points[index]); float logicalX = screen.X * Window.Width / Math.Max(1, fishWindow.RenderWindow.FramebufferWidth), logicalY = screen.Y * Window.Height / Math.Max(1, fishWindow.RenderWindow.FramebufferHeight);
			axisLabels[index].Position = new Vector2(Math.Clamp(logicalX, viewport.X, viewport.X + viewport.Z - 52) - editor.Position.X, Math.Clamp(logicalY, viewport.Y, viewport.Y + viewport.W - 30) - editor.Position.Y);
		}
	}
	private static void DrawCell(RenderPass pass, Int3 cell, FishGfx.Color color) { color = FishGfx.ColorSpace.SrgbToLinearColor(color); Vector3 min = new(cell.X, cell.Y, cell.Z), max = min + Vector3.One; Vector3[] p = [min, new(max.X,min.Y,min.Z),new(max.X,max.Y,min.Z),new(min.X,max.Y,min.Z),new(min.X,min.Y,max.Z),new(max.X,min.Y,max.Z),max,new(min.X,max.Y,max.Z)]; int[] e=[0,1,1,2,2,3,3,0,4,5,5,6,6,7,7,4,0,4,1,5,2,6,3,7]; for(int i=0;i<e.Length;i+=2) pass.DrawLine(new FishGfx.Vertex3(p[e[i]],color),new FishGfx.Vertex3(p[e[i+1]],color)); }

	private void SelectBlockList(BlockType type) { int index = type == BlockType.None ? 0 : Enum.GetValues<BlockType>().Where(static value => value != BlockType.None).OrderBy(static value => (int)value).ToList().IndexOf(type) + 1; blockList.SelectIndex(index); }
	private static int Index(Int3 cell) => (cell.Y * 5 + cell.Z) * 5 + cell.X;
	private static bool InBounds(Int3 cell) => (uint)cell.X < 5 && (uint)cell.Y < 5 && (uint)cell.Z < 5;
	private static bool Contains(Vector4 rectangle, Vector2 point) => point.X >= rectangle.X && point.Y >= rectangle.Y && point.X < rectangle.X + rectangle.Z && point.Y < rectangle.Y + rectangle.W;
	private static bool DescriptorEqual(VillagePrefabDescriptor left, VillagePrefabDescriptor right) => left.Id == right.Id && left.DisplayName == right.DisplayName && left.Sockets.All(socket => right.Sockets.Any(other => socket.Direction == other.Direction && socket.Types.SequenceEqual(other.Types) && socket.Openings.SequenceEqual(other.Openings)));
	private static bool CellsEqual(VillagePrefab left, VillagePrefab right) { for (int y = 0; y < 5; y++) for (int z = 0; z < 5; z++) for (int x = 0; x < 5; x++) if (left.GetCell(x, y, z) != right.GetCell(x, y, z)) return false; return true; }
	private readonly record struct Int3(int X, int Y, int Z);
	private bool IsModalOpen() => createWindow.Visible || semanticsWindow.Visible || rulesWindow.Visible || gui.UI.ModalControl is not null;
	private void ShowModal(Window modal) { CenterModal(modal); modal.ShowModal(); }
	private void HideModal(Window modal) { modal.Visible = false; if (gui.UI.ModalControl == modal) gui.UI.SetModalControl(null); }
	private void CenterModal(Window modal) => modal.Position = new Vector2(Math.Max(0, (Window.Width - modal.Size.X) / 2), Math.Max(0, (Window.Height - modal.Size.Y) / 2));
	private static Button AddButton(Panel parent, string text, float x, float y, float width, Action action) { Button button = new() { Text = text, Position = new Vector2(x, y), Size = new Vector2(width, 34) }; button.OnButtonPressed += (_, _, _) => action(); parent.AddChild(button); return button; }
	private static Button AddButton(Window parent, string text, float x, float y, float width, Action action) { Button button = new() { Text = text, Position = new Vector2(x, y), Size = new Vector2(width, 34) }; button.OnButtonPressed += (_, _, _) => action(); parent.AddChild(button); return button; }
	private static string RuntimeCatalogPath() => Path.Combine(AppContext.BaseDirectory, "data", "world", "village-prefabs", "catalog.json");
	private static string ResolveSourceCatalogPath(string explicitDataRoot)
	{
		if (!string.IsNullOrWhiteSpace(explicitDataRoot)) return Path.Combine(Path.GetFullPath(explicitDataRoot), "world", "village-prefabs", "catalog.json");
		foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory }.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase))
			for (DirectoryInfo current = new(start); current is not null; current = current.Parent)
				foreach (string projectRoot in new[] { current.FullName, Path.Combine(current.FullName, "Voxelgine") }) if (File.Exists(Path.Combine(projectRoot, "Voxelgine.csproj"))) return Path.Combine(projectRoot, "data", "world", "village-prefabs", "catalog.json");
		return null;
	}

	private sealed class ScaledAxisLabel : Label
	{
		public override void DrawControl(FishUI.FishUI ui, float deltaTime, float totalTime)
		{
			if (string.IsNullOrEmpty(Text)) return;
			Vector2 position = GetAbsolutePosition();
			ui.Graphics.DrawTextColorScale(ui.Settings.FontDefaultBold, Text, position + Vector2.One, new FishColor(0, 0, 0, 220), 1.5f);
			ui.Graphics.DrawTextColorScale(ui.Settings.FontDefaultBold, Text, position, FishColor.White, 1.5f);
		}
	}
}
#endif

