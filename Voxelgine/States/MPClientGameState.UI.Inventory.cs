using System.Numerics;
using FishUI;
using FishUI.Controls;
using Voxelgine.Engine;
using Voxelgine.GUI;

namespace Voxelgine.States;

public unsafe partial class MPClientGameState
{
	private const float InventorySlotSize = 48;
	private const float InventorySlotSpacing = 4;

	private void CreateInventoryUI(int screenWidth, int screenHeight)
	{
		_inventoryWindow = new Window
		{
			Title = "Inventory",
			Size = new Vector2(566, 448),
			Position = new Vector2(screenWidth / 2f - 283, screenHeight / 2f - 224),
			IsResizable = false,
			ShowCloseButton = true,
			Visible = false,
		};
		_inventoryWindow.OnClosed += _ => CloseInventory();

		Label storageLabel = new()
		{
			Text = "Storage",
			Position = new Vector2(18, 14),
			Size = new Vector2(200, 22),
		};
		_inventoryWindow.AddChild(storageLabel);

		_inventorySlotBoxes = new FishUIItemBox[PlayerInventory.SlotCount];
		for (int storageIndex = 0; storageIndex < PlayerInventory.StorageSlotCount; storageIndex++)
		{
			int slot = PlayerInventory.HotbarSlotCount + storageIndex;
			int row = storageIndex / 10;
			int column = storageIndex % 10;
			_inventoryWindow.AddChild(CreateInventorySlot(
				slot,
				new Vector2(
					18 + column * (InventorySlotSize + InventorySlotSpacing),
					40 + row * (InventorySlotSize + InventorySlotSpacing))));
		}

		Label hotbarLabel = new()
		{
			Text = "Hotbar",
			Position = new Vector2(18, 308),
			Size = new Vector2(200, 22),
		};
		_inventoryWindow.AddChild(hotbarLabel);

		for (int slot = 0; slot < PlayerInventory.HotbarSlotCount; slot++)
		{
			_inventoryWindow.AddChild(CreateInventorySlot(
				slot,
				new Vector2(18 + slot * (InventorySlotSize + InventorySlotSpacing), 332)));
		}

		var concreteButton = new Button
		{
			ID = "inventory_craft_concrete",
			Text = "Craft Concrete (1 Sand + 1 Gravel, nearby Water)",
			Position = new Vector2(18, 384),
			Size = new Vector2(530, 30),
		};
		concreteButton.OnButtonPressed += (_, _, _) => RequestConcreteCraft();
		_inventoryWindow.AddChild(concreteButton);

		_inventoryStatusLabel = new Label
		{
			Text = "Left click: move stack   Right click: split/place one",
			Position = new Vector2(18, 418),
			Size = new Vector2(530, 20),
			Alignment = Align.Center,
		};
		_inventoryWindow.AddChild(_inventoryStatusLabel);
		_gui.AddControl(_inventoryWindow);

		_inventoryCursorGhost = new FishUIItemBox
		{
			Size = new Vector2(InventorySlotSize, InventorySlotSize),
			Visible = false,
			Disabled = true,
			AlwaysOnTop = true,
			ZDepth = 10000,
		};
		_inventoryCursorGhost.LoadTextures(_gui.UI);
		_gui.AddControl(_inventoryCursorGhost);

		if (_inventoryModel is not null)
		{
			_inventoryModel.Changed += RefreshInventoryUI;
			RefreshInventoryUI();
		}
	}

	private FishUIItemBox CreateInventorySlot(int slot, Vector2 position)
	{
		FishUIItemBox box = new()
		{
			SlotIndex = slot,
			Position = position,
			Size = new Vector2(InventorySlotSize, InventorySlotSize),
		};
		box.LoadTextures(_gui.UI);
		box.OnItemMouseClicked += HandleInventorySlotClick;
		_inventorySlotBoxes[slot] = box;
		return box;
	}

	private void HandleInventorySlotClick(FishUIItemBox box, FishMouseButton button)
	{
		if (_inputOwnership.Mode != GameplayInputMode.Inventory || _inventoryController is null)
			return;

		if (button == FishMouseButton.Left)
			_inventoryController.LeftClick(box.SlotIndex);
		else if (button == FishMouseButton.Right)
			_inventoryController.RightClick(box.SlotIndex);
	}

	private void RefreshInventoryUI()
	{
		if (_inventoryModel is null || _inventorySlotBoxes is null || _gui is null)
			return;

		for (int slot = 0; slot < _inventorySlotBoxes.Length; slot++)
		{
			FishUIItemBox box = _inventorySlotBoxes[slot];
			if (box is null)
				continue;

			ItemStack stack = _inventoryModel.GetSlot(slot);
			if (box.Stack.Item != stack.Item)
				ClientItemPresentationCatalog.ApplyIcon(_gui.UI, box, stack);
			box.SetStack(stack);
			box.IsSelected = slot == _inventoryModel.SelectedHotbarSlot;
			box.TooltipText = stack.IsEmpty
				? null
				: $"{ItemCatalog.Get(stack.Item).DisplayName} x{stack.Count}";
		}

		ItemStack cursor = _inventoryModel.Cursor;
		if (_inventoryCursorGhost is not null)
		{
			if (_inventoryCursorGhost.Stack.Item != cursor.Item)
				ClientItemPresentationCatalog.ApplyIcon(_gui.UI, _inventoryCursorGhost, cursor);
			_inventoryCursorGhost.SetStack(cursor);
			_inventoryCursorGhost.Visible = (_inventoryWindow?.Visible == true || _containerWindow?.Visible == true) && !cursor.IsEmpty;
		}

		if (_inventoryStatusLabel is not null)
		{
			string inventoryStatus = _inventoryModel.IsQueueFull
				? "Waiting for server — action queue is full"
				: $"Revision {_inventoryModel.Revision}   Pending {_inventoryModel.PendingActionCount}/32";
			_inventoryStatusLabel.Text = string.IsNullOrEmpty(_craftStatusText)
				? inventoryStatus
				: $"{inventoryStatus}   {_craftStatusText}";
		}
	}

	private void RequestConcreteCraft()
	{
		if (_client?.IsConnected != true)
			return;
		uint actionId = _nextCraftActionId++;
		if (_nextCraftActionId == 0)
			_nextCraftActionId = 1;
		_craftStatusText = "Crafting...";
		_client.Send(new CraftRequestPacket { ActionId = actionId, RecipeId = 1 }, true, GetClientTime());
		RefreshInventoryUI();
	}

	private void HandleCraftResult(CraftResultPacket packet)
	{
		_craftStatusText = packet.Accepted ? "Concrete crafted" : $"Craft rejected ({packet.Reason})";
		RefreshInventoryUI();
	}

	private void UpdateInventoryUI()
	{
		if (_inventoryCursorGhost?.Visible != true || _gui is null)
			return;

		Vector2 mouse = _gui.UI.Input.GetMousePosition();
		_inventoryCursorGhost.Position = mouse + new Vector2(14, 14);
	}

	private void OpenInventory()
	{
		if (!_inputOwnership.OpenInventory() || _inventoryWindow is null)
			return;

		_inventoryWindow.Visible = true;
		_inventoryWindow.BringToFront();
		RefreshInventoryUI();
		ApplyInputOwnership();
	}

	private void CloseInventory()
	{
		if (_inputOwnership.Mode != GameplayInputMode.Inventory)
			return;

		if (_inventoryModel?.Cursor.IsEmpty == false)
			_inventoryController?.CancelCursor();

		if (_inventoryWindow is not null)
			_inventoryWindow.Visible = false;
		if (_inventoryCursorGhost is not null)
			_inventoryCursorGhost.Visible = false;
		_inputOwnership.CloseOverlay();
		ApplyInputOwnership();
	}
}
