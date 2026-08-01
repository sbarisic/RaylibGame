using System.Numerics;
using Voxelgine.Graphics;
using Voxelgine.GUI;

namespace Voxelgine.Engine;

public unsafe partial class ClientPlayer
{
	private FishUIItemBox _healthBox;
	private FishUIInfoLabel _infoLabel;
	private FishUIInventory _hotbar;
	private ClientInventoryModel _inventoryModel;

	public event Action<ItemUseChannel> ItemUseRequested;

	public ItemStack GetSelectedStack() =>
		_inventoryModel == null
			? ItemStack.Empty
			: _inventoryModel.GetSlot(GetSelectedInventoryIndex());

	public override int GetSelectedInventoryIndex() =>
		_hotbar?.GetSelectedIndex() ?? base.GetSelectedInventoryIndex();

	public override void SetSelectedInventoryIndex(int index)
	{
		if ((uint)index >= PlayerInventory.HotbarSlotCount)
			return;
		base.SetSelectedInventoryIndex(index);
		_hotbar?.SetSelectedIndex(index);
		UpdateSelectedPresentation();
	}

	public void BindInventoryModel(ClientInventoryModel model)
	{
		if (_inventoryModel != null)
			_inventoryModel.Changed -= RefreshInventoryPresentation;
		_inventoryModel = model;
		if (_inventoryModel != null)
		{
			_inventoryModel.Changed += RefreshInventoryPresentation;
			SetSelectedInventoryIndex(_inventoryModel.SelectedHotbarSlot);
		}
		RefreshInventoryPresentation();
	}

	public void RecalcGUI(IGameWindow window)
	{
		if (_healthBox != null)
			_healthBox.Position = new Vector2(100, window.Height - 100);
		if (_hotbar != null)
			_hotbar.Position = new Vector2((window.Width - _hotbar.Size.X) / 2, window.Height - 80);
	}

	public void InitGUI(IGameWindow window, FishUIManager gui)
	{
		_healthBox = new FishUIItemBox
		{
			Position = new Vector2(100, window.Height - 100),
			Size = new Vector2(64),
		};
		_healthBox.LoadTextures(gui.UI);
		_healthBox.SetIcon(gui.UI, "data/textures/items/heart_full.png", 3);
		_healthBox.Text = "100";
		gui.AddControl(_healthBox);

		_infoLabel = new FishUIInfoLabel
		{
			Position = new Vector2(20, 140),
			Size = new Vector2(300, 250),
			Visible = Eng.DebugMode,
		};
		gui.AddControl(_infoLabel);

		_hotbar = new FishUIInventory(gui.UI, PlayerInventory.HotbarSlotCount)
		{
			Position = new Vector2((window.Width - 676) / 2f, window.Height - 80),
		};
		_hotbar.OnActiveSelectionChanged = _ => UpdateSelectedPresentation();
		gui.AddControl(_hotbar);
		RefreshInventoryPresentation();
		UpdateSelectedPresentation();
	}

	public void UpdateGUI()
	{
		if (_infoLabel == null)
			return;
		_infoLabel.Visible = Eng.DebugMode;
		if (!Eng.DebugMode)
			return;

		_infoLabel.Clear();
		_infoLabel.WriteLine("Pos: {0:0.00}, {1:0.00}, {2:0.00}", Position.X, Position.Y, Position.Z);
		_infoLabel.WriteLine("Vel: {0:0.000}", GetVelocity().Length());
		_infoLabel.WriteLine("NoClip (C): {0}", NoClip ? "ON" : "OFF");
		_infoLabel.WriteLine("OnGround: {0}", GetWasLastLegsOnFloor() ? "YES" : "NO");
		_infoLabel.WriteLine("ChunkDraws: {0}", Eng.ChunkDrawCalls);
		_infoLabel.WriteLine(ViewMdl.GetDebugInfo());
	}

	public void TickGUI(InputMgr input, ChunkMap map)
	{
		float wheel = input.GetMouseWheel();
		if (wheel >= 1)
			_hotbar?.SelectNext();
		else if (wheel <= -1)
			_hotbar?.SelectPrevious();

		if (!CursorDisabled)
			return;

		ItemStack selected = GetSelectedStack();
		bool secondaryDown = input.IsInputDown(InputKey.Click_Right);
		if (selected.Item == ItemIds.Gun)
		{
			ViewMdl.SetRotationMode(secondaryDown
				? ViewModelRotationMode.GunIronsight
				: ViewModelRotationMode.Gun);
		}

		if (input.IsInputPressed(InputKey.Click_Left))
		{
			if (selected.Item == ItemIds.Hammer)
				ViewMdl.ApplySwing();
			else if (selected.Item == ItemIds.Gun && secondaryDown)
				ViewMdl.ApplyKickback();
			else
				ViewMdl.ApplyJiggle();
			ItemUseRequested?.Invoke(ItemUseChannel.Primary);
		}
		if (input.IsInputPressed(InputKey.Click_Right))
			ItemUseRequested?.Invoke(ItemUseChannel.Secondary);
	}

	private void RefreshInventoryPresentation()
	{
		if (_hotbar == null)
			return;
		for (int slot = 0; slot < PlayerInventory.HotbarSlotCount; slot++)
		{
			FishUIItemBox box = _hotbar.GetItem(slot);
			ItemStack stack = _inventoryModel?.GetSlot(slot) ?? ItemStack.Empty;
			box.SetStack(stack);
			ClientItemPresentationCatalog.ApplyIcon(GUI.UI, box, stack);
		}
		if (_inventoryModel != null)
			SetSelectedInventoryIndex(_inventoryModel.SelectedHotbarSlot);
		UpdateSelectedPresentation();
	}

	private void UpdateSelectedPresentation()
	{
		ItemStack selected = GetSelectedStack();
		if (selected.IsEmpty)
		{
			ViewMdl.IsActive = false;
			ViewMdl.SetPresentationAsset(ViewModelAssetKind.None);
			return;
		}

		ClientItemPresentation presentation = ClientItemPresentationCatalog.Get(selected.Item);
		ViewMdl.IsActive = true;
		ViewMdl.SetPresentationAsset(presentation.ViewModel);
		ViewMdl.SetRotationMode(selected.Item == ItemIds.Hammer
			? ViewModelRotationMode.Tool
			: selected.Item == ItemIds.Gun
				? ViewModelRotationMode.Gun
				: ViewModelRotationMode.Block);
	}
}
