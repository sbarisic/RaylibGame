using Voxelgine.Engine;
using Voxelgine.States;

namespace UnitTest;

public sealed class ClientInventoryModelTests
{
	[Fact]
	public void RemovingUnsentPredictionDoesNotSkipWireActionIds()
	{
		ClientInventoryModel model = new();
		ItemStack[] slots = new ItemStack[PlayerInventory.SlotCount];
		slots[0] = new(ItemIds.Wheat, 10); slots[1] = new(ItemIds.Wheat, 10); slots[3] = new(ItemIds.Wheat, 10);
		model.Apply(new InventoryStatePacket { Revision=1, Slots=slots, CursorOriginSlot=InventoryStatePacket.NoCursorOrigin });
		List<InventoryActionRequestPacket> sent = new(); model.PacketReady += sent.Add;
		Assert.True(model.QueueAction(InventoryActionKind.LeftClickSlot,0));
		Assert.True(model.QueueAction(InventoryActionKind.LeftClickSlot,1));
		Assert.True(model.QueueAction(InventoryActionKind.LeftClickSlot,3));
		slots = (ItemStack[])slots.Clone(); slots[0]=default; slots[1]=new(ItemIds.Wheat,ItemCatalog.Get(ItemIds.Wheat).MaximumStack);
		model.Apply(new InventoryStatePacket { Revision=3,AcknowledgedActionId=1,ActionAccepted=true,Slots=slots,Cursor=new(ItemIds.Wheat,10),CursorOriginSlot=0 });
		Assert.Equal(2,sent.Count); Assert.Equal(2u,sent[1].ActionId); Assert.Equal(3,sent[1].Slot);
	}

	[Fact]
	public void PredictionSendsOneActionAtATimeAndReplaysAfterAcceptance()
	{
		ClientInventoryModel model = CreateModel(ItemStack.Create(ItemIds.FromBlock(BlockType.Dirt), 8));
		List<InventoryActionRequestPacket> sent = new();
		model.PacketReady += sent.Add;

		Assert.True(model.QueueAction(InventoryActionKind.RightClickSlot, 0));
		Assert.True(model.QueueAction(InventoryActionKind.RightClickSlot, 1));
		Assert.Single(sent);
		Assert.Equal((ushort)3, model.Cursor.Count);
		Assert.Equal((ushort)1, model.GetSlot(1).Count);

		model.Apply(CreateState(
			revision: 2,
			acknowledgement: sent[0].ActionId,
			accepted: true,
			slot0: ItemStack.Create(ItemIds.FromBlock(BlockType.Dirt), 4),
			cursor: ItemStack.Create(ItemIds.FromBlock(BlockType.Dirt), 4),
			origin: 0));

		Assert.Equal(2, sent.Count);
		Assert.Equal((ushort)1, model.GetSlot(1).Count);
	}

	[Fact]
	public void SameRevisionRejectionStillRollsBackCurrentPrediction()
	{
		ClientInventoryModel model = CreateModel(ItemStack.Create(ItemIds.FromBlock(BlockType.Stone), 2));
		InventoryActionRequestPacket request = null;
		model.PacketReady += packet => request = packet;

		Assert.True(model.QueueAction(InventoryActionKind.LeftClickSlot, 0));
		Assert.False(model.Cursor.IsEmpty);
		model.Apply(CreateState(
			revision: 1,
			acknowledgement: request.ActionId,
			accepted: false,
			slot0: ItemStack.Create(ItemIds.FromBlock(BlockType.Stone), 2)));

		Assert.True(model.Cursor.IsEmpty);
		Assert.Equal((ushort)2, model.GetSlot(0).Count);
		Assert.Equal(0, model.PendingActionCount);
	}

	[Fact]
	public void InventoryModeOwnsInputAndBlocksDebugToggle()
	{
		GameplayInputOwnership ownership = new();
		ownership.Activate();

		Assert.True(ownership.OpenInventory());
		Assert.Equal(GameplayInputMode.Inventory, ownership.Mode);
		Assert.True(ownership.GameplayInputSuppressed);
		Assert.False(ownership.CursorCaptured);
		Assert.False(ownership.ToggleDebugMenu());

		ownership.CloseOverlay();
		Assert.Equal(GameplayInputMode.Gameplay, ownership.Mode);
	}

	[Fact]
	public void UnsolicitedUpdateDoesNotLetLaterActionOvertakeInflightAction()
	{
		ClientInventoryModel model = CreateModel(ItemStack.Create(ItemIds.FromBlock(BlockType.Dirt), 2));
		List<InventoryActionRequestPacket> sent = new();
		model.PacketReady += sent.Add;
		Assert.True(model.QueueAction(InventoryActionKind.LeftClickSlot, 0));
		Assert.True(model.QueueAction(InventoryActionKind.LeftClickSlot, 1));

		model.Apply(CreateState(
			revision: 2,
			acknowledgement: 0,
			accepted: true,
			slot0: ItemStack.Empty));

		Assert.Single(sent);
		Assert.Equal(1, model.PendingActionCount);
	}

	[Fact]
	public void PredictedHotbarSelectionIgnoresOlderServerSelection()
	{
		ClientInventoryModel model = CreateModel(ItemStack.Empty);
		int changed = 0;
		model.Changed += () => changed++;

		model.ApplySelection(1, 101);
		model.Apply(CreateState(
			revision: 2,
			acknowledgement: 0,
			accepted: true,
			slot0: ItemStack.Empty,
			selectedHotbarSlot: 0,
			selectionCommandTick: 100));

		Assert.Equal(1, model.SelectedHotbarSlot);
		Assert.Equal(101, model.SelectionCommandTick);
		Assert.Equal(2, changed);
	}

	[Fact]
	public void RepeatingPredictedSelectionAdvancesTickWithoutRefreshingPresentation()
	{
		ClientInventoryModel model = CreateModel(ItemStack.Empty);
		int changed = 0;
		model.Changed += () => changed++;

		model.ApplySelection(1, 101);
		model.ApplySelection(1, 102);

		Assert.Equal(1, model.SelectedHotbarSlot);
		Assert.Equal(102, model.SelectionCommandTick);
		Assert.Equal(1, changed);
	}

	private static ClientInventoryModel CreateModel(ItemStack slot0)
	{
		ClientInventoryModel model = new();
		model.Apply(CreateState(1, 0, true, slot0));
		return model;
	}

	private static InventoryStatePacket CreateState(
		long revision,
		uint acknowledgement,
		bool accepted,
		ItemStack slot0,
		ItemStack cursor = default,
		int origin = PlayerInventory.NoCursorOrigin,
		byte selectedHotbarSlot = 0,
		int selectionCommandTick = 0)
	{
		ItemStack[] slots = new ItemStack[PlayerInventory.SlotCount];
		slots[0] = slot0;
		return new InventoryStatePacket
		{
			AcknowledgedActionId = acknowledgement,
			ActionAccepted = accepted,
			Revision = revision,
			SelectedHotbarSlot = selectedHotbarSlot,
			SelectionCommandTick = selectionCommandTick,
			Cursor = cursor,
			CursorOriginSlot = origin == PlayerInventory.NoCursorOrigin
				? InventoryStatePacket.NoCursorOrigin
				: checked((byte)origin),
			Slots = slots,
		};
	}
}
