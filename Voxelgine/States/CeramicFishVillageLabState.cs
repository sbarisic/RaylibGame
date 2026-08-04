#if WINDOWS
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
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

/// <summary>Edits and previews the production 3x5x3 CeramicFish village definition.</summary>
public sealed class CeramicFishVillageLabState : GameStateImpl
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() },
	};
	private readonly IFishGfxGameWindow fishWindow;
	private readonly FishUIManager gui;
	private readonly RuntimePaths runtimePaths;
	private readonly string runtimeDefinitionPath;
	private readonly string sourceDefinitionPath;
	private readonly CeramicVillageEditingSession session;
	private readonly ChunkMap previewMap = new();
	private readonly FishGfxVoxelScene voxelScene;
	private readonly RenderQueue renderQueue = new();
	private readonly Camera camera = new();
	private readonly Panel sidebar = new() { ID = "village_prefab_sidebar" };
	private readonly Panel editor = new() { ID = "village_prefab_editor", IsTransparent = true };
	private readonly ListBox prefabList = new() { ID = "village_prefab_list", CustomItemHeight = 25 };
	private readonly ListBox blockList = new() { ID = "village_prefab_block_list", CustomItemHeight = 23 };
	private readonly Label header = new() { ID = "village_prefab_header" };
	private readonly Label status = new() { ID = "village_prefab_status" };
	private readonly Label hoverLabel = new() { ID = "village_prefab_hover" };
	private readonly Textbox idInput = new() { ID = "village_prefab_id" };
	private readonly Textbox tagsInput = new() { ID = "village_prefab_tags" };
	private readonly Textbox weightInput = new() { ID = "village_prefab_weight" };
	private readonly DropDown stairRotation = new() { ID = "village_prefab_stair_rotation" };
	private readonly Dictionary<CeramicDirection, DropDown> socketInputs = [];
	private readonly Dictionary<CeramicRotationOptions, CheckBox> rotationInputs = [];
	private readonly Window policyWindow;
	private readonly MultiLineEditbox policyEditor;
	private readonly Label policyError;
	private readonly HashSet<Int3> previewCells = [];
	private CeramicPrefabDefinition selected;
	private BlockValue[] working = new BlockValue[75];
	private BlockType paintBlock = BlockType.Plank;
	private CeramicRotation paintRotation;
	private Vector4 viewport;
	private Vector2 previousMouse;
	private Vector2 middlePress;
	private float cameraYaw = 38;
	private float cameraElevation = 28;
	private float cameraDistance = 10;
	private bool middleDragged;
	private bool synchronizing;
	private bool villageMode;
	private bool automatic;
	private bool automaticComplete;
	private Task<FishUIDebugSnapshot> automaticCapture;
	private bool hasHover;
	private Int3 hoverCell;
	private Int3 occupiedCell;
	private bool occupiedHit;

	public CeramicFishVillageLabState(IGameWindow window, IFishEngineRunner engine) : base(window, engine)
	{
		fishWindow = window as IFishGfxGameWindow ?? throw new ArgumentException("CeramicFish Village Lab requires FishGfx.", nameof(window));
		runtimePaths = engine.AsClient().RuntimePaths;
		gui = new FishUIManager(window, engine.Logging, runtimePaths);
		runtimeDefinitionPath = Path.Combine(AppContext.BaseDirectory, "data", "world", "ceramic-fish", "village.json");
		sourceDefinitionPath = ResolveSourceDefinitionPath(engine.AsClient().AssetSourceRoot);
		session = new(CeramicVillageCatalog.Load(runtimeDefinitionPath));
		voxelScene = new(fishWindow.RenderWindow.Graphics, fishWindow.Assets, previewMap,
			maxChunkDrawDistance: 64, chunkMeshUploadBudget: 16, fogQuality: VolumetricFogQuality.Off);
		voxelScene.Renderer.FogSettings = VoxelFogSettings.Disabled;

		header.Text = "CeramicFish Village Lab";
		header.Position = new Vector2(16, 14); header.Size = new Vector2(342, 28); sidebar.AddChild(header);
		prefabList.Position = new Vector2(16, 50); prefabList.Size = new Vector2(342, 228);
		prefabList.OnItemSelected += (_, _, item) => { if (item?.UserData is CeramicPrefabDefinition prefab) Select(prefab.Id); };
		sidebar.AddChild(prefabList);
		AddButton(sidebar, "New", 16, 286, 72, NewPrefab);
		AddButton(sidebar, "Duplicate", 94, 286, 86, DuplicatePrefab);
		AddButton(sidebar, "Delete", 186, 286, 72, DeletePrefab);
		AddButton(sidebar, "Save", 264, 286, 94, Save);
		AddLabel(sidebar, "ID", 16, 334, 34); idInput.Position = new Vector2(54, 330); idInput.Size = new Vector2(304, 28); sidebar.AddChild(idInput);
		AddLabel(sidebar, "Tags", 16, 370, 42); tagsInput.Position = new Vector2(62, 366); tagsInput.Size = new Vector2(296, 28); sidebar.AddChild(tagsInput);
		AddLabel(sidebar, "Weight", 16, 406, 54); weightInput.Position = new Vector2(74, 402); weightInput.Size = new Vector2(74, 28); sidebar.AddChild(weightInput);
		idInput.OnTextChanged += (_, _) => CommitMetadata(); tagsInput.OnTextChanged += (_, _) => CommitMetadata(); weightInput.OnTextChanged += (_, _) => CommitMetadata();
		int rotationIndex = 0;
		foreach ((CeramicRotationOptions flag, string label) in new[]
		{
			(CeramicRotationOptions.Rot0, "0"), (CeramicRotationOptions.Rot90CW, "90"),
			(CeramicRotationOptions.Rot180CW, "180"), (CeramicRotationOptions.Rot270CW, "270"),
		})
		{
			float x = 160 + rotationIndex++ * 48;
			AddLabel(sidebar, label, x, 400, 44);
			CheckBox check = new() { Position = new Vector2(x + 7, 424), Size = new Vector2(20, 20) };
			check.OnCheckedChanged += (_, _) => CommitMetadata(); sidebar.AddChild(check); rotationInputs[flag] = check;
		}
		status.Position = new Vector2(16, 454); status.Size = new Vector2(342, 58); status.Text = "Select a prefab."; sidebar.AddChild(status);
		AddButton(sidebar, "Edit policies (JSON)...", 16, 520, 166, ShowPolicies);
		AddButton(sidebar, "Validate", 190, 520, 168, ValidateDefinition);
		AddButton(sidebar, "Prefab Preview", 16, 562, 166, ShowPrefabPreview);
		AddButton(sidebar, "Village Preview", 190, 562, 168, ShowVillagePreview);
		AddButton(sidebar, "Back", 16, 604, 342, () => Client.RequestState(ClientStateKind.MainMenu));

		AddLabel(editor, "3 x 5 x 3 voxel prefab", 18, 14, 230, true);
		hoverLabel.Position = new Vector2(18, 40); hoverLabel.Size = new Vector2(750, 24); hoverLabel.Text = "Hover the voxel volume to inspect X/Y/Z."; editor.AddChild(hoverLabel);
		AddLabel(editor, "Block palette", 18, 72, 210);
		blockList.Position = new Vector2(18, 96); blockList.Size = new Vector2(210, 205);
		blockList.AddItem(new ListBoxItem("0: Erase", BlockType.None));
		foreach (BlockType block in Enum.GetValues<BlockType>().Where(static value => value != BlockType.None))
			blockList.AddItem(new ListBoxItem($"{(int)block}: {block}", block));
		blockList.OnItemSelected += (_, _, item) => { if (item?.UserData is BlockType block) paintBlock = block; };
		editor.AddChild(blockList);
		AddLabel(editor, "Stair orientation", 18, 310, 120);
		stairRotation.Position = new Vector2(138, 306); stairRotation.Size = new Vector2(90, 28);
		foreach (CeramicRotation rotation in Enum.GetValues<CeramicRotation>()) stairRotation.AddItem(new(rotation.ToString(), rotation));
		stairRotation.OnItemSelected += (_, item) => paintRotation = (CeramicRotation)item.UserData;
		stairRotation.SelectIndex(0); editor.AddChild(stairRotation);
		AddButton(editor, "Undo", 18, 344, 100, Undo); AddButton(editor, "Redo", 128, 344, 100, Redo);
		AddLabel(editor, "Face sockets", 18, 388, 210);
		int socketIndex = 0;
		foreach (CeramicDirection direction in Enum.GetValues<CeramicDirection>())
		{
			float y = 416 + socketIndex++ * 46;
			AddLabel(editor, direction.ToString(), 18, y + 3, 62);
			DropDown input = new() { ID = $"village_prefab_socket_{direction.ToString().ToLowerInvariant()}", Position = new Vector2(82, y), Size = new Vector2(146, 28), Searchable = true };
			CeramicDirection captured = direction;
			input.OnItemSelected += (_, item) => SetSocket(captured, item.UserData as string ?? item.Text);
			editor.AddChild(input); socketInputs[direction] = input;
		}
		AddLabel(editor, "L: place   R: erase   M: pick   Alt+L/M-drag: orbit   Wheel: zoom", 250, 14, 650, true);

		policyWindow = new() { ID = "ceramic_fish_policy_dialog", Title = "CeramicFish policies", Size = new Vector2(820, 680), IsResizable = true, IsModal = true, CloseButtonEnabled = true, ShowCloseButton = true, Visible = false };
		policyEditor = new() { ID = "ceramic_fish_policy_json", Position = new Vector2(16, 48), Size = new Vector2(788, 548), ShowLineNumbers = true };
		policyWindow.AddChild(policyEditor);
		policyError = new() { Position = new Vector2(16, 602), Size = new Vector2(560, 54) }; policyWindow.AddChild(policyError);
		AddButton(policyWindow, "Apply", 590, 610, 98, ApplyPolicies); AddButton(policyWindow, "Close", 698, 610, 106, () => HideModal(policyWindow));
		gui.AddControl(sidebar); gui.AddControl(editor); gui.AddControl(policyWindow);
		RebuildPrefabList(); Select(session.Prefabs[0].Id); Layout();
	}

	internal bool IsReady => !automatic || automaticComplete;

	internal void EnableAutomaticValidation()
	{
		automatic = true;
		Paint(new(1, 1, 1), BlockType.StoneStairs, CeramicRotation.Rot90CW); Undo(); Redo();
		SetSocket(CeramicDirection.West, CeramicSocket.NoConnection);
		string path = Path.Combine(runtimePaths.Root, "prefab-lab-auto", $"village-{Guid.NewGuid():N}.json");
		new CeramicFishJsonStorage().SaveAsync(path, session.Definition).AsTask().GetAwaiter().GetResult();
		CeramicVillageCatalog loaded = CeramicVillageCatalog.Load(path);
		if (loaded.Get(selected.Id).Sockets.Single(socket => socket.Direction == CeramicDirection.West).Type != CeramicSocket.NoConnection)
			throw new InvalidOperationException("Automatic no-connection socket persistence failed.");
		status.Text = "Automatic CeramicFish edit/save/load validation passed; waiting for preview mesh.";
	}

	internal void ValidateAutomaticSnapshotBundle()
	{
		if (!automatic || automaticCapture is null) throw new InvalidOperationException("The automatic CeramicFish Village Lab snapshot was not queued.");
		FishUIDebugSnapshot snapshot = automaticCapture.WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
		gui.UI.Diagnostics.WaitForPendingExportsAsync().WaitAsync(TimeSpan.FromSeconds(10)).GetAwaiter().GetResult();
		if (snapshot.CaptureStatus != FishUIDebugCaptureStatus.Complete || snapshot.ScreenshotPng is null || snapshot.OverlayPng is null)
			throw new InvalidOperationException("Automatic CeramicFish Village Lab snapshot was incomplete.");
	}

	public override void SwapTo() { gui.InputEnabled = true; fishWindow.RenderWindow.CaptureCursor = false; fishWindow.RenderWindow.ShowCursor = true; previousMouse = Window.InMgr.GetMousePos(); }
	public override void SwapFrom() => gui.InputEnabled = false;
	public override void BeginInputFrame() => gui.BeginInputFrame();
	public override void Tick(float gameTime) { if (Window.InMgr.IsInputPressed(InputKey.Esc)) Client.RequestState(ClientStateKind.MainMenu); }
	public override void BeginFrame(in FrameTiming timing)
	{
		Layout(); ConfigureCamera(); Vector2 mouse = Window.InMgr.GetMousePos(); bool modal = policyWindow.Visible || gui.UI.ModalControl is not null;
		bool over = !modal && Contains(viewport, mouse);
		if (Window.InMgr.IsInputPressed(InputKey.Click_Middle)) { middlePress = mouse; middleDragged = false; }
		if (Window.InMgr.IsInputDown(InputKey.Click_Middle) && Vector2.DistanceSquared(mouse, middlePress) > 9) middleDragged = true;
		bool orbit = over && (Window.InMgr.IsInputDown(InputKey.Click_Middle) && middleDragged || Window.InMgr.IsInputDown(InputKey.Click_Left) && Window.InMgr.IsInputDown(InputKey.Alt));
		if (orbit) { Vector2 delta = mouse - previousMouse; cameraYaw += delta.X * .35f; cameraElevation = Math.Clamp(cameraElevation - delta.Y * .25f, -85, 85); }
		if (over && !villageMode && !orbit)
		{
			hasHover = TryPick(mouse, out hoverCell, out occupiedCell, out occupiedHit);
			if (hasHover) hoverLabel.Text = $"X={hoverCell.X} Y={hoverCell.Y} Z={hoverCell.Z}  {working[Index(hoverCell)].Type}  paint {paintBlock} R{paintRotation}";
			if (hasHover && Window.InMgr.IsInputPressed(InputKey.Click_Left)) Paint(hoverCell, paintBlock, paintRotation);
			if (occupiedHit && Window.InMgr.IsInputPressed(InputKey.Click_Right)) Paint(occupiedCell, BlockType.None, CeramicRotation.Rot0);
			if (occupiedHit && Window.InMgr.IsInputReleased(InputKey.Click_Middle) && !middleDragged) paintBlock = working[Index(occupiedCell)].Type;
		}
		cameraDistance = Math.Clamp(cameraDistance - (over ? Window.InMgr.GetMouseWheel() : 0) * (villageMode ? 4 : .5f), villageMode ? 20 : 6, villageMode ? 180 : 22);
		previousMouse = mouse; voxelScene.Update(camera); gui.Update(timing.DeltaTime, timing.TotalTime);
		if (automatic && automaticCapture is null && voxelScene.Renderer.IsIdle && voxelScene.GetPresentationState(new(0, 0, 0)) == VoxelPresentationState.Resident)
		{
			automaticCapture = FishUIDiagnostics.CaptureAsync(gui.UI, new FishUIDebugSnapshotOptions(), FishUIDebugCaptureReason.TestFailure);
			automaticComplete = true;
		}
	}

	public override GameStateRenderSettings GetRenderSettings(Vector2 framebufferSize)
	{
		ConfigureCamera(framebufferSize); GameStateRenderSettings overlay = GameStateRenderSettings.CreateOverlay(new(Window.Width, Window.Height));
		return new() { WorldView = new(camera), ViewmodelView = new(camera), OverlayView = overlay.OverlayView, ClearColor = new(20, 24, 31) };
	}
	public override void RenderWorld(RenderPass pass, in FrameTiming timing)
	{
		renderQueue.BeginFrame(); voxelScene.Enqueue(renderQueue, camera, shadows: null); pass.Execute(renderQueue, RenderQueueBucket.Opaque); pass.Execute(renderQueue, RenderQueueBucket.Transparent);
		if (!villageMode) DrawGrid(pass);
	}
	public override void RenderOverlay(RenderPass pass, in FrameTiming timing) => gui.Render(pass, timing.DeltaTime, timing.TotalTime);
	public override void OnResize(IGameWindow window) { base.OnResize(window); gui.OnResize(window.Width, window.Height); Layout(); }
	protected override void DisposeCore() { renderQueue.Clear(); voxelScene.Dispose(); gui.Dispose(); }

	private void RebuildPrefabList()
	{
		prefabList.Items.Clear(); foreach (CeramicPrefabDefinition prefab in session.Prefabs.OrderBy(static value => value.Id, StringComparer.Ordinal))
			prefabList.AddItem(new(prefab.Id, prefab));
	}

	private void Select(string id)
	{
		selected = session.Get(id); working = new BlockValue[75];
		foreach (CeramicEntity entity in selected.Entities) working[Index(new(entity.X, entity.Y, entity.Z))] = CeramicVillageCatalog.ToBlockValue(entity);
		villageMode = false; cameraDistance = 10; SynchronizePreview(); RefreshEditor();
	}

	private void RefreshEditor()
	{
		synchronizing = true;
		try
		{
			header.Text = $"CeramicFish Village Lab  •  {selected.Id}"; idInput.Text = selected.Id;
			tagsInput.Text = string.Join(", ", selected.Tags); weightInput.Text = selected.Weight.ToString();
			foreach ((CeramicRotationOptions flag, CheckBox check) in rotationInputs) check.IsChecked = selected.AllowedRotations.HasFlag(flag);
			RebuildSocketInputs();
			status.Text = $"{selected.Entities.Count} voxels  •  revision {session.Revision}{(session.IsDirty ? "  •  unsaved" : string.Empty)}";
		}
		finally { synchronizing = false; }
	}

	private void CommitMetadata()
	{
		if (synchronizing || selected is null) return;
		string id = idInput.Text?.Trim() ?? string.Empty;
		if (id != selected.Id) { status.Text = "Prefab IDs are immutable. Duplicate the prefab to create a new ID."; return; }
		if (!int.TryParse(weightInput.Text, out int weight) || weight < 1) return;
		string[] tags = (tagsInput.Text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.Ordinal).ToArray();
		CeramicRotationOptions rotations = rotationInputs.Where(static pair => pair.Value.IsChecked).Aggregate(CeramicRotationOptions.None, static (value, pair) => value | pair.Key);
		if (rotations == CeramicRotationOptions.None) return;
		CeramicPrefabDefinition updated = selected with { Tags = tags, Weight = weight, AllowedRotations = rotations };
		if (updated == selected) return; session.ReplacePrefab(updated); selected = updated;
	}

	private void SetSocket(CeramicDirection direction, string type)
	{
		if (synchronizing || selected is null || string.IsNullOrWhiteSpace(type)) return;
		CeramicSocket[] sockets = selected.Sockets.Select(socket => socket.Direction == direction ? socket with { Type = type } : socket).ToArray();
		selected = selected with { Sockets = sockets }; session.ReplacePrefab(selected); RefreshEditor();
	}

	private void RebuildSocketInputs()
	{
		string[] types = new[] { CeramicSocket.NoConnection }
			.Concat(session.Definition.ConnectionPolicies.Select(static policy => policy.SocketType))
			.Concat(session.Prefabs.SelectMany(static prefab => prefab.Sockets).Select(static socket => socket.Type))
			.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
		foreach ((CeramicDirection direction, DropDown input) in socketInputs)
		{
			input.ClearItems(); foreach (string type in types) input.AddItem(new(type, type));
			input.SelectIndex(Array.IndexOf(types, selected.Sockets.Single(socket => socket.Direction == direction).Type));
		}
	}

	private void Paint(Int3 cell, BlockType block, CeramicRotation rotation)
	{
		if (!InBounds(cell)) return; BlockValue value = new(block, BlockShapeCatalog.IsStair(block) ? (byte)((int)rotation / 90) : (byte)0);
		if (working[Index(cell)] == value) return;
		CeramicPrefabDefinition before = selected;
		List<CeramicEntity> entities = selected.Entities.Where(entity => entity.X != cell.X || entity.Y != cell.Y || entity.Z != cell.Z).ToList();
		if (block != BlockType.None) entities.Add(new((int)block, cell.X, cell.Y, cell.Z, rotation));
		selected = selected with { Entities = entities.OrderBy(static entity => entity.Y).ThenBy(static entity => entity.Z).ThenBy(static entity => entity.X).ToArray() };
		session.ReplacePrefab(selected); working[Index(cell)] = value; previewMap.SetBlock(cell.X, cell.Y, cell.Z, value); status.Text = $"Painted {block} at {cell}.";
	}

	private void Undo() { if (session.Undo()) RestoreSelection(); }
	private void Redo() { if (session.Redo()) RestoreSelection(); }
	private void RestoreSelection() { string id = session.Prefabs.Any(prefab => prefab.Id == selected?.Id) ? selected.Id : session.Prefabs[0].Id; RebuildPrefabList(); Select(id); }
	private void NewPrefab() { int index = 1; while (session.Prefabs.Any(prefab => prefab.Id == $"new.prefab.{index}")) index++; CeramicPrefabDefinition value = CeramicVillageEditingSession.EmptyPrefab($"new.prefab.{index}"); session.AddPrefab(value); RebuildPrefabList(); Select(value.Id); }
	private void DuplicatePrefab() { if (selected is null) return; int index = 1; while (session.Prefabs.Any(prefab => prefab.Id == $"{selected.Id}.copy{index}")) index++; CeramicPrefabDefinition copy = selected with { Id = $"{selected.Id}.copy{index}", Tags = selected.Tags.ToArray(), Entities = selected.Entities.ToArray(), Sockets = selected.Sockets.ToArray() }; session.AddPrefab(copy); RebuildPrefabList(); Select(copy.Id); }
	private void DeletePrefab() { if (selected is null) return; session.RemovePrefab(selected.Id); RebuildPrefabList(); Select(session.Prefabs[0].Id); }

	private void Save()
	{
		try
		{
			long revision = session.Revision; string[] targets = new[] { sourceDefinitionPath, runtimeDefinitionPath }.Where(static path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
			IReadOnlyList<CeramicVillageCatalog> saved = CeramicVillageCatalog.SaveSynchronized(targets, session.Definition); session.MarkSaved(revision);
			status.Text = $"Saved {session.Prefabs.Count} prefabs. Hash {saved[0].Hash[..12]}."; RefreshEditor();
		}
		catch (Exception exception) { status.Text = $"Save failed; previous files restored. {exception.Message}"; }
	}

	private void ValidateDefinition()
	{
		try { CeramicVillageCatalog.ValidateVoxelDefinition(session.Definition); status.Text = $"Valid schema v{session.Definition.FormatVersion}: {session.Prefabs.Count} prefabs and {session.Definition.ConnectionPolicies.Count} connection policies."; }
		catch (CeramicDefinitionException exception) { status.Text = string.Join("\n", exception.Errors.Take(2).Select(static error => $"{error.Code}: {error.Message}")); }
	}

	private void ShowPolicies()
	{
		policyEditor.Text = JsonSerializer.Serialize(PolicyDocument.From(session.Definition), JsonOptions); policyError.Text = string.Empty; CenterModal(policyWindow); policyWindow.ShowModal();
	}

	private void ApplyPolicies()
	{
		try
		{
			PolicyDocument value = JsonSerializer.Deserialize<PolicyDocument>(policyEditor.Text, JsonOptions) ?? throw new InvalidDataException("Policy JSON is empty.");
			CeramicFishDefinition definition = session.Definition with { ConnectionPolicies = value.ConnectionPolicies ?? [], ComponentAdjacencyPolicies = value.ComponentAdjacencyPolicies ?? [], ComponentTagPolicies = value.ComponentTagPolicies ?? [], ComponentEntryPolicies = value.ComponentEntryPolicies ?? [], WallFeaturePolicies = value.WallFeaturePolicies ?? [] };
			CeramicVillageCatalog.ValidateVoxelDefinition(definition); session.ReplaceDefinition(definition); selected = session.Get(selected.Id); policyError.Text = "Policies applied and validated."; RefreshEditor();
		}
		catch (Exception exception) { policyError.Text = exception.Message; }
	}

	private void ShowPrefabPreview() { if (selected is not null) Select(selected.Id); }
	private void ShowVillagePreview()
	{
		try
		{
			CeramicVillagePreviewResult result = CeramicVillagePlanner.PlanPreview(session.Definition, Environment.TickCount);
			if (result.Layout is null) { status.Text = $"Village preview failed: {result.Failure?.Code} {result.Failure?.Message}"; return; }
			ClearPreview(); villageMode = true;
			foreach (PlannedVillagePlacement placement in result.Layout.Placements)
			{
				CeramicPrefabDefinition prefab = session.Get(placement.PrefabId); int originX = placement.Cell.X * 3, originZ = placement.Cell.Z * 3;
				foreach (CeramicEntity source in prefab.Entities)
				{
					CeramicEntity entity = CeramicGeometry.RotateEntity(source, 3, 3, placement.Rotation); Int3 cell = new(originX + entity.X, entity.Y, originZ + entity.Z);
					BlockValue value = CeramicVillageCatalog.ToBlockValue(entity); previewMap.SetBlock(cell.X, cell.Y, cell.Z, value); previewCells.Add(cell);
				}
			}
			cameraDistance = 125; status.Text = $"Village preview: {result.Layout.Placements.Length} placements, {result.Layout.TopologyChecks} topology checks, {result.Layout.PropagationChecks} placement checks.";
		}
		catch (Exception exception) { status.Text = $"Village preview failed: {exception.Message}"; }
	}

	private void SynchronizePreview()
	{
		ClearPreview(); for (int y = 0; y < 5; y++) for (int z = 0; z < 3; z++) for (int x = 0; x < 3; x++) { Int3 cell = new(x, y, z); previewMap.SetBlock(x, y, z, working[Index(cell)]); if (working[Index(cell)].Type != BlockType.None) previewCells.Add(cell); }
	}
	private void ClearPreview() { foreach (Int3 cell in previewCells) previewMap.SetBlock(cell.X, cell.Y, cell.Z, BlockValue.Empty); previewCells.Clear(); }

	private bool TryPick(Vector2 mouse, out Int3 placement, out Int3 occupied, out bool foundOccupied)
	{
		Vector2 framebuffer = new(mouse.X * fishWindow.RenderWindow.FramebufferWidth / Math.Max(1, Window.Width), mouse.Y * fishWindow.RenderWindow.FramebufferHeight / Math.Max(1, Window.Height));
		PickingRay ray = camera.CreatePickingRay(framebuffer); float nearest = float.PositiveInfinity; occupied = default; Vector3 normal = default; foundOccupied = false;
		for (int y = 0; y < 5; y++) for (int z = 0; z < 3; z++) for (int x = 0; x < 3; x++) if (working[Index(new(x, y, z))].Type != BlockType.None && RayBox(ray, new(x, y, z), new(x + 1, y + 1, z + 1), out float distance, out Vector3 hitNormal) && distance < nearest) { nearest = distance; occupied = new(x, y, z); normal = hitNormal; foundOccupied = true; }
		if (foundOccupied) { Int3 adjacent = new(occupied.X + (int)normal.X, occupied.Y + (int)normal.Y, occupied.Z + (int)normal.Z); placement = InBounds(adjacent) && working[Index(adjacent)].Type == BlockType.None ? adjacent : occupied; return true; }
		if (!RayBox(ray, Vector3.Zero, new(3, 5, 3), out float volumeDistance, out _)) { placement = default; return false; }
		Vector3 point = ray.GetPoint(volumeDistance + .001f); placement = new(Math.Clamp((int)MathF.Floor(point.X), 0, 2), Math.Clamp((int)MathF.Floor(point.Y), 0, 4), Math.Clamp((int)MathF.Floor(point.Z), 0, 2)); return true;
	}

	private static bool RayBox(PickingRay ray, Vector3 minimum, Vector3 maximum, out float distance, out Vector3 normal)
	{
		float near = 0, far = float.PositiveInfinity; normal = default;
		for (int axis = 0; axis < 3; axis++) { float origin = axis == 0 ? ray.Origin.X : axis == 1 ? ray.Origin.Y : ray.Origin.Z, direction = axis == 0 ? ray.Direction.X : axis == 1 ? ray.Direction.Y : ray.Direction.Z; float min = axis == 0 ? minimum.X : axis == 1 ? minimum.Y : minimum.Z, max = axis == 0 ? maximum.X : axis == 1 ? maximum.Y : maximum.Z; if (MathF.Abs(direction) < 1e-6f) { if (origin < min || origin > max) { distance = 0; return false; } continue; } float a = (min - origin) / direction, b = (max - origin) / direction; Vector3 axisNormal = axis == 0 ? Vector3.UnitX : axis == 1 ? Vector3.UnitY : Vector3.UnitZ; Vector3 candidate = -axisNormal * MathF.Sign(direction); if (a > b) (a, b) = (b, a); if (a > near) { near = a; normal = candidate; } far = MathF.Min(far, b); if (near > far) { distance = 0; return false; } }
		distance = near; return far >= 0;
	}

	private void DrawGrid(RenderPass pass)
	{
		FishGfx.Color color = FishGfx.ColorSpace.SrgbToLinearColor(new FishGfx.Color(220, 225, 235, 170));
		for (int x = 0; x <= 3; x++) for (int y = 0; y <= 5; y++) pass.DrawLine(new FishGfx.Vertex3(new(x, y, 0), color), new FishGfx.Vertex3(new(x, y, 3), color));
		for (int z = 0; z <= 3; z++) for (int y = 0; y <= 5; y++) pass.DrawLine(new FishGfx.Vertex3(new(0, y, z), color), new FishGfx.Vertex3(new(3, y, z), color));
		for (int x = 0; x <= 3; x++) for (int z = 0; z <= 3; z++) pass.DrawLine(new FishGfx.Vertex3(new(x, 0, z), color), new FishGfx.Vertex3(new(x, 5, z), color));
	}

	private void Layout() { float left = Math.Min(382, Math.Max(300, Window.Width * .3f)); sidebar.Position = new Vector2(12, 12); sidebar.Size = new Vector2(left, Math.Max(0, Window.Height - 24)); editor.Position = new Vector2(left + 24, 12); editor.Size = new Vector2(Math.Max(0, Window.Width - left - 36), Math.Max(0, Window.Height - 24)); viewport = new(editor.Position.X + 244, editor.Position.Y + 48, Math.Max(0, editor.Size.X - 252), Math.Max(0, editor.Size.Y - 56)); CenterModal(policyWindow); }
	private void ConfigureCamera() => ConfigureCamera(fishWindow.RenderWindow.FramebufferSize);
	private void ConfigureCamera(Vector2 framebuffer) { float yaw = cameraYaw * MathF.PI / 180, elevation = cameraElevation * MathF.PI / 180, horizontal = MathF.Cos(elevation) * cameraDistance; Vector3 target = villageMode ? new(46.5f, 2.5f, 46.5f) : new(1.5f, 2.5f, 1.5f); camera.Position = target + new Vector3(MathF.Sin(yaw) * horizontal, MathF.Sin(elevation) * cameraDistance, MathF.Cos(yaw) * horizontal); camera.LookAt(target); camera.SetPerspective(framebuffer, 45 * MathF.PI / 180, .05f, 512); }
	private void HideModal(Window modal) { modal.Visible = false; if (gui.UI.ModalControl == modal) gui.UI.SetModalControl(null); }
	private void CenterModal(Window modal) => modal.Position = new Vector2(Math.Max(0, (Window.Width - modal.Size.X) / 2), Math.Max(0, (Window.Height - modal.Size.Y) / 2));
	private static int Index(Int3 cell) => (cell.Y * 3 + cell.Z) * 3 + cell.X;
	private static bool InBounds(Int3 cell) => (uint)cell.X < 3 && (uint)cell.Y < 5 && (uint)cell.Z < 3;
	private static bool Contains(Vector4 bounds, Vector2 point) => point.X >= bounds.X && point.Y >= bounds.Y && point.X < bounds.X + bounds.Z && point.Y < bounds.Y + bounds.W;
	private static void AddLabel(Panel parent, string text, float x, float y, float width, bool white = false) { Label label = new() { Text = text, Position = new Vector2(x, y), Size = new Vector2(width, 24) }; if (white) label.SetColorOverride("Text", FishColor.White); parent.AddChild(label); }
	private static Button AddButton(Panel parent, string text, float x, float y, float width, Action action) { Button button = new() { Text = text, Position = new Vector2(x, y), Size = new Vector2(width, 34) }; button.OnButtonPressed += (_, _, _) => action(); parent.AddChild(button); return button; }
	private static Button AddButton(Window parent, string text, float x, float y, float width, Action action) { Button button = new() { Text = text, Position = new Vector2(x, y), Size = new Vector2(width, 34) }; button.OnButtonPressed += (_, _, _) => action(); parent.AddChild(button); return button; }
	private static string ResolveSourceDefinitionPath(string root) { if (!string.IsNullOrWhiteSpace(root)) return Path.Combine(Path.GetFullPath(root), "world", "ceramic-fish", "village.json"); foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory }.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase)) for (DirectoryInfo current = new(start); current is not null; current = current.Parent) foreach (string project in new[] { current.FullName, Path.Combine(current.FullName, "Voxelgine") }) if (File.Exists(Path.Combine(project, "Voxelgine.csproj"))) return Path.Combine(project, "data", "world", "ceramic-fish", "village.json"); return null; }

	private readonly record struct Int3(int X, int Y, int Z);
	private sealed record PolicyDocument(
		IReadOnlyList<CeramicConnectionPolicy> ConnectionPolicies,
		IReadOnlyList<CeramicComponentAdjacencyPolicy> ComponentAdjacencyPolicies,
		IReadOnlyList<CeramicComponentTagPolicy> ComponentTagPolicies,
		IReadOnlyList<CeramicComponentEntryPolicy> ComponentEntryPolicies,
		IReadOnlyList<CeramicWallFeaturePolicy> WallFeaturePolicies)
	{
		internal static PolicyDocument From(CeramicFishDefinition definition) => new(definition.ConnectionPolicies, definition.ComponentAdjacencyPolicies, definition.ComponentTagPolicies, definition.ComponentEntryPolicies, definition.WallFeaturePolicies);
	}
}
#endif
