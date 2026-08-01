using Voxelgine.Engine;

namespace Voxelgine.States;

internal sealed class GameplayInventoryController : IDisposable
{
	private readonly ClientInventoryModel _model;
	private readonly Action<InventoryActionRequestPacket> _send;

	public GameplayInventoryController(
		ClientInventoryModel model,
		Action<InventoryActionRequestPacket> send)
	{
		_model = model ?? throw new ArgumentNullException(nameof(model));
		_send = send ?? throw new ArgumentNullException(nameof(send));
		_model.PacketReady += Send;
	}

	public bool LeftClick(int slot) => _model.QueueAction(InventoryActionKind.LeftClickSlot, slot);
	public bool RightClick(int slot) => _model.QueueAction(InventoryActionKind.RightClickSlot, slot);
	public bool CancelCursor() => _model.QueueAction(InventoryActionKind.CancelCursor, PlayerInventory.NoCursorOrigin);

	public void Dispose()
	{
		_model.PacketReady -= Send;
	}

	private void Send(InventoryActionRequestPacket packet) => _send(packet);
}
