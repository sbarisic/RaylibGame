using Voxelgine.Engine;
using Voxelgine.States;

namespace UnitTest;

public sealed class ClientInventoryModelTests
{
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
		int origin = PlayerInventory.NoCursorOrigin)
	{
		ItemStack[] slots = new ItemStack[PlayerInventory.SlotCount];
		slots[0] = slot0;
		return new InventoryStatePacket
		{
			AcknowledgedActionId = acknowledgement,
			ActionAccepted = accepted,
			Revision = revision,
			SelectedHotbarSlot = 0,
			SelectionCommandTick = 0,
			Cursor = cursor,
			CursorOriginSlot = origin == PlayerInventory.NoCursorOrigin
				? InventoryStatePacket.NoCursorOrigin
				: checked((byte)origin),
			Slots = slots,
		};
	}
}
