namespace Voxelgine.Engine.Server;

public partial class ServerLoop
{
	private void HandleInventoryAction(
		NetConnection connection,
		InventoryActionRequestPacket packet)
	{
		if (!_sessions.TryGetValue(connection.PlayerId, out ServerClientSession session))
			return;

		if (session.InventoryActionHistory.TryGet(packet.ActionId, out ProcessedActionOutcome prior))
		{
			SendInventoryState(session, packet.ActionId, prior.Accepted);
			return;
		}

		bool accepted = false;
		if (packet.ActionId == session.NextExpectedInventoryActionId &&
			packet.ExpectedRevision == session.Inventory.Revision &&
			IsValidInventoryAction(packet))
		{
			int slot = packet.Kind == InventoryActionKind.CancelCursor
				? PlayerInventory.NoCursorOrigin
				: packet.Slot;
			InventoryMutationResult result = session.Inventory.ApplyClick(packet.Kind, slot);
			accepted = result.Accepted;
		}

		if (packet.ActionId == session.NextExpectedInventoryActionId)
			session.NextExpectedInventoryActionId++;

		session.InventoryActionHistory.Record(new ProcessedActionOutcome(
			packet.ActionId,
			accepted,
			0));
		SendInventoryState(session, packet.ActionId, accepted);
	}

	private static bool IsValidInventoryAction(InventoryActionRequestPacket packet)
	{
		return packet.Kind switch
		{
			InventoryActionKind.CancelCursor => packet.Slot == InventoryActionRequestPacket.NoSlot,
			InventoryActionKind.LeftClickSlot or InventoryActionKind.RightClickSlot =>
				packet.Slot < PlayerInventory.SlotCount,
			_ => false,
		};
	}

	private void SendInventoryState(
		ServerClientSession session,
		uint acknowledgedActionId,
		bool accepted)
	{
		var slots = new ItemStack[PlayerInventory.SlotCount];
		session.Inventory.CopySlotsTo(slots);
		_server.SendTo(session.Player.PlayerId, new InventoryStatePacket
		{
			AcknowledgedActionId = acknowledgedActionId,
			ActionAccepted = accepted,
			Revision = session.Inventory.Revision,
			SelectedHotbarSlot = session.SelectedHotbarSlot,
			SelectionCommandTick = session.SelectionCommandTick,
			Cursor = session.Inventory.Cursor,
			CursorOriginSlot = session.Inventory.CursorOriginSlot == PlayerInventory.NoCursorOrigin
				? InventoryStatePacket.NoCursorOrigin
				: checked((byte)session.Inventory.CursorOriginSlot),
			Slots = slots,
		}, true, CurrentTime);
	}
}
