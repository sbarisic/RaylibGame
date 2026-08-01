namespace Voxelgine.Engine;

public sealed class ClientInventoryModel
{
	public const int MaximumPendingActions = 32;

	private readonly ItemStack[] _authoritativeSlots = new ItemStack[PlayerInventory.SlotCount];
	private readonly List<PendingInventoryAction> _pending = new(MaximumPendingActions);
	private readonly PlayerInventory _view = new();
	private ItemStack _authoritativeCursor;
	private int _authoritativeCursorOrigin = PlayerInventory.NoCursorOrigin;
	private long _authoritativeRevision;
	private uint _nextActionId = 1;
	private bool _actionInFlight;

	public long Revision => _view.Revision;
	public ItemStack Cursor => _view.Cursor;
	public int CursorOriginSlot => _view.CursorOriginSlot;
	public byte SelectedHotbarSlot { get; private set; }
	public int SelectionCommandTick { get; private set; }
	public int PendingActionCount => _pending.Count;
	public bool IsQueueFull => _pending.Count >= MaximumPendingActions;

	public event Action Changed;
	public event Action<InventoryActionRequestPacket> PacketReady;

	public ItemStack GetSlot(int slot) => _view.GetSlot(slot);
	public ReadOnlySpan<ItemStack> GetSlots() => _view.GetSlots();

	public bool QueueAction(InventoryActionKind kind, int slot)
	{
		if (IsQueueFull)
			return false;

		InventoryMutationResult predicted = _view.ApplyClick(kind, slot);
		if (!predicted.Accepted || !predicted.Changed)
			return false;

		_pending.Add(new PendingInventoryAction(_nextActionId++, kind, slot));
		Changed?.Invoke();
		TrySendNext();
		return true;
	}

	public bool Apply(InventoryStatePacket packet)
	{
		packet.Validate();
		bool isCurrentAcknowledgement = packet.AcknowledgedActionId != 0 &&
			_pending.Count > 0 &&
			_pending[0].ActionId == packet.AcknowledgedActionId;
		if (packet.Revision < _authoritativeRevision && !isCurrentAcknowledgement)
			return false;

		if (packet.Revision >= _authoritativeRevision)
		{
			packet.Slots.AsSpan().CopyTo(_authoritativeSlots);
			_authoritativeCursor = packet.Cursor;
			_authoritativeCursorOrigin = packet.CursorOriginSlot == InventoryStatePacket.NoCursorOrigin
				? PlayerInventory.NoCursorOrigin
				: packet.CursorOriginSlot;
			_authoritativeRevision = packet.Revision;
		}

		if (packet.SelectionCommandTick >= SelectionCommandTick)
		{
			SelectedHotbarSlot = packet.SelectedHotbarSlot;
			SelectionCommandTick = packet.SelectionCommandTick;
		}

		if (isCurrentAcknowledgement)
		{
			_pending.RemoveAt(0);
			_actionInFlight = false;
		}

		RebuildPredictedView();
		Changed?.Invoke();
		TrySendNext();
		return true;
	}

	public void ApplySelection(byte slot, int commandTick)
	{
		if (slot >= PlayerInventory.HotbarSlotCount || commandTick < SelectionCommandTick)
			return;
		SelectedHotbarSlot = slot;
		SelectionCommandTick = commandTick;
		Changed?.Invoke();
	}

	public void Clear()
	{
		Array.Clear(_authoritativeSlots);
		_authoritativeCursor = ItemStack.Empty;
		_authoritativeCursorOrigin = PlayerInventory.NoCursorOrigin;
		_authoritativeRevision = 0;
		_pending.Clear();
		_nextActionId = 1;
		_actionInFlight = false;
		SelectedHotbarSlot = 0;
		SelectionCommandTick = 0;
		RebuildPredictedView();
	}

	private void RebuildPredictedView()
	{
		_view.Restore(_authoritativeSlots, _authoritativeCursor, _authoritativeCursorOrigin);
		for (int i = 0; i < _pending.Count;)
		{
			PendingInventoryAction action = _pending[i];
			InventoryMutationResult result = _view.ApplyClick(action.Kind, action.Slot);
			if (!result.Accepted || !result.Changed)
			{
				// A request already handed to the reliable transport remains the
				// queue head until its authoritative acknowledgement arrives. An
				// unsolicited pickup or grant may make its prediction temporarily
				// inapplicable, but dropping it here would allow a later action to
				// overtake it with a stale expected revision.
				if (i == 0 && _actionInFlight)
				{
					i++;
					continue;
				}
				_pending.RemoveAt(i);
				continue;
			}
			i++;
		}
	}

	private void TrySendNext()
	{
		if (_actionInFlight || _pending.Count == 0)
			return;

		PendingInventoryAction action = _pending[0];
		_actionInFlight = true;
		PacketReady?.Invoke(new InventoryActionRequestPacket
		{
			ActionId = action.ActionId,
			ExpectedRevision = _authoritativeRevision,
			Kind = action.Kind,
			Slot = action.Kind == InventoryActionKind.CancelCursor
				? InventoryActionRequestPacket.NoSlot
				: checked((byte)action.Slot),
		});
	}

	private readonly record struct PendingInventoryAction(
		uint ActionId,
		InventoryActionKind Kind,
		int Slot);
}
