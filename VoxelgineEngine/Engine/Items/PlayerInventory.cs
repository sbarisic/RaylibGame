using System;

namespace Voxelgine.Engine;

public enum InventoryActionKind : byte
{
	LeftClickSlot,
	RightClickSlot,
	CancelCursor,
}

public readonly record struct InventoryMutationResult(bool Accepted, bool Changed, long Revision)
{
	public static InventoryMutationResult Rejected(long revision) => new(false, false, revision);
	public static InventoryMutationResult NoChange(long revision) => new(true, false, revision);
}

public readonly record struct InventoryInsertionResult(
	bool Changed,
	ItemStack Remainder,
	long Revision);

public readonly record struct PreparedConsumption(
	bool IsValid,
	int Slot,
	long ExpectedRevision,
	ItemStack ExpectedStack,
	ItemStack ResultStack);

public sealed class PlayerInventory
{
	public const int HotbarSlotCount = 10;
	public const int StorageSlotCount = 50;
	public const int SlotCount = HotbarSlotCount + StorageSlotCount;
	public const int NoCursorOrigin = -1;

	private readonly ItemStack[] _slots = new ItemStack[SlotCount];

	public ItemStack Cursor { get; private set; } = ItemStack.Empty;
	public int CursorOriginSlot { get; private set; } = NoCursorOrigin;
	public long Revision { get; private set; } = 1;

	public ItemStack GetSlot(int index)
	{
		ValidateSlot(index);
		return _slots[index];
	}

	public ReadOnlySpan<ItemStack> GetSlots() => _slots;

	public void CopySlotsTo(Span<ItemStack> destination)
	{
		if (destination.Length < SlotCount)
			throw new ArgumentException($"Destination must contain at least {SlotCount} elements.", nameof(destination));
		_slots.AsSpan().CopyTo(destination);
	}

	public InventoryMutationResult ApplyClick(InventoryActionKind kind, int slot)
	{
		if (kind == InventoryActionKind.CancelCursor)
		{
			if (slot != NoCursorOrigin)
				return InventoryMutationResult.Rejected(Revision);
			return CancelCursor();
		}

		if ((uint)slot >= SlotCount)
			return InventoryMutationResult.Rejected(Revision);

		return kind switch
		{
			InventoryActionKind.LeftClickSlot => LeftClick(slot),
			InventoryActionKind.RightClickSlot => RightClick(slot),
			_ => InventoryMutationResult.Rejected(Revision),
		};
	}

	public InventoryInsertionResult TryInsert(ItemStack stack)
	{
		if (!ItemCatalog.IsCanonical(stack))
			throw new ArgumentException("Stack is not canonical.", nameof(stack));
		if (stack.IsEmpty)
			return new InventoryInsertionResult(false, ItemStack.Empty, Revision);

		ItemStack remainder = InsertCore(stack, NoCursorOrigin);
		bool changed = remainder.Count != stack.Count;
		if (changed)
			Revision++;
		return new InventoryInsertionResult(changed, remainder, Revision);
	}

	public PreparedConsumption TryPrepareConsumption(int slot, ItemId expectedItem, ushort count)
	{
		if ((uint)slot >= SlotCount || expectedItem.IsEmpty || count == 0)
			return default;

		ItemStack current = _slots[slot];
		if (current.Item != expectedItem || current.Count < count)
			return default;

		ushort remaining = checked((ushort)(current.Count - count));
		ItemStack result = remaining == 0 ? ItemStack.Empty : new ItemStack(current.Item, remaining);
		return new PreparedConsumption(true, slot, Revision, current, result);
	}

	public bool ApplyPreparedConsumption(in PreparedConsumption consumption)
	{
		if (!consumption.IsValid ||
			consumption.ExpectedRevision != Revision ||
			(uint)consumption.Slot >= SlotCount ||
			_slots[consumption.Slot] != consumption.ExpectedStack)
		{
			return false;
		}

		_slots[consumption.Slot] = consumption.ResultStack;
		Revision++;
		return true;
	}

	public InventoryInsertionResult Grant(ItemStack stack) => TryInsert(stack);

	public int Grant(ItemId item, int count)
	{
		if (!ItemCatalog.TryGet(item, out ItemDefinition definition))
			throw new ArgumentException("Item is not registered.", nameof(item));
		if (count <= 0)
			throw new ArgumentOutOfRangeException(nameof(count));

		int remaining = count;
		while (remaining > 0)
		{
			ushort requested = checked((ushort)Math.Min(remaining, definition.MaximumStack));
			ItemStack remainder = InsertCore(new ItemStack(item, requested), NoCursorOrigin);
			int inserted = requested - remainder.Count;
			remaining -= inserted;
			if (inserted != requested)
				break;
		}

		int granted = count - remaining;
		if (granted > 0)
			Revision++;
		return granted;
	}

	/// <summary>
	/// Inserts as much of the cursor as possible, clears it, and returns the
	/// remainder that must become a protected death drop. This is one inventory
	/// transaction regardless of how many slots receive items.
	/// </summary>
	public ItemStack ResolveCursorForDeath()
	{
		if (Cursor.IsEmpty)
			return ItemStack.Empty;

		int origin = CursorOriginSlot;
		ItemStack remainder = Cursor;
		if ((uint)origin < SlotCount)
			remainder = InsertIntoSlot(remainder, origin);
		remainder = InsertCore(remainder, origin);
		ClearCursor();
		Revision++;
		return remainder;
	}

	public void Restore(ReadOnlySpan<ItemStack> slots, ItemStack cursor, int cursorOriginSlot)
	{
		if (slots.Length != SlotCount)
			throw new ArgumentException($"Inventory must contain exactly {SlotCount} slots.", nameof(slots));
		for (int i = 0; i < slots.Length; i++)
		{
			if (!ItemCatalog.IsCanonical(slots[i]))
				throw new ArgumentException($"Slot {i} is not canonical.", nameof(slots));
		}
		ValidateCursor(cursor, cursorOriginSlot);

		slots.CopyTo(_slots);
		Cursor = cursor;
		CursorOriginSlot = cursorOriginSlot;
		Revision = 1;
	}

	private InventoryMutationResult LeftClick(int slot)
	{
		ItemStack target = _slots[slot];
		if (Cursor.IsEmpty)
		{
			if (target.IsEmpty)
				return InventoryMutationResult.NoChange(Revision);
			_slots[slot] = ItemStack.Empty;
			Cursor = target;
			CursorOriginSlot = slot;
			return Changed();
		}

		if (target.IsEmpty)
		{
			_slots[slot] = Cursor;
			ClearCursor();
			return Changed();
		}

		if (target.Item == Cursor.Item)
		{
			ushort maximum = ItemCatalog.Get(target.Item).MaximumStack;
			int capacity = maximum - target.Count;
			if (capacity <= 0)
				return InventoryMutationResult.NoChange(Revision);

			ushort moved = (ushort)Math.Min(capacity, Cursor.Count);
			_slots[slot] = new ItemStack(target.Item, checked((ushort)(target.Count + moved)));
			SetCursorCount(checked((ushort)(Cursor.Count - moved)));
			return Changed();
		}

		_slots[slot] = Cursor;
		Cursor = target;
		CursorOriginSlot = slot;
		return Changed();
	}

	private InventoryMutationResult RightClick(int slot)
	{
		ItemStack target = _slots[slot];
		if (Cursor.IsEmpty)
		{
			if (target.IsEmpty)
				return InventoryMutationResult.NoChange(Revision);

			ushort taken = checked((ushort)((target.Count + 1) / 2));
			ushort left = checked((ushort)(target.Count - taken));
			_slots[slot] = left == 0 ? ItemStack.Empty : new ItemStack(target.Item, left);
			Cursor = new ItemStack(target.Item, taken);
			CursorOriginSlot = slot;
			return Changed();
		}

		if (target.IsEmpty)
		{
			_slots[slot] = new ItemStack(Cursor.Item, 1);
			SetCursorCount(checked((ushort)(Cursor.Count - 1)));
			return Changed();
		}

		if (target.Item != Cursor.Item)
			return InventoryMutationResult.NoChange(Revision);

		ushort maximum = ItemCatalog.Get(target.Item).MaximumStack;
		if (target.Count >= maximum)
			return InventoryMutationResult.NoChange(Revision);

		_slots[slot] = new ItemStack(target.Item, checked((ushort)(target.Count + 1)));
		SetCursorCount(checked((ushort)(Cursor.Count - 1)));
		return Changed();
	}

	private InventoryMutationResult CancelCursor()
	{
		if (Cursor.IsEmpty)
			return InventoryMutationResult.NoChange(Revision);

		int originalOrigin = CursorOriginSlot;
		ItemStack originalCursor = Cursor;
		ItemStack remainder = Cursor;
		if ((uint)originalOrigin < SlotCount)
			remainder = InsertIntoSlot(remainder, originalOrigin);
		remainder = InsertCore(remainder, originalOrigin);

		Cursor = remainder;
		CursorOriginSlot = remainder.IsEmpty ? NoCursorOrigin : originalOrigin;
		if (Cursor == originalCursor)
			return InventoryMutationResult.NoChange(Revision);
		return Changed();
	}

	private ItemStack InsertCore(ItemStack stack, int excludedSlot)
	{
		ItemStack remainder = stack;
		for (int i = 0; i < SlotCount && !remainder.IsEmpty; i++)
		{
			if (i == excludedSlot || _slots[i].IsEmpty || _slots[i].Item != remainder.Item)
				continue;
			remainder = InsertIntoSlot(remainder, i);
		}

		for (int i = 0; i < SlotCount && !remainder.IsEmpty; i++)
		{
			if (i == excludedSlot || !_slots[i].IsEmpty)
				continue;
			remainder = InsertIntoSlot(remainder, i);
		}
		return remainder;
	}

	private ItemStack InsertIntoSlot(ItemStack stack, int slot)
	{
		if (stack.IsEmpty)
			return ItemStack.Empty;

		ItemStack target = _slots[slot];
		if (!target.IsEmpty && target.Item != stack.Item)
			return stack;

		ushort maximum = ItemCatalog.Get(stack.Item).MaximumStack;
		int capacity = target.IsEmpty ? maximum : maximum - target.Count;
		if (capacity <= 0)
			return stack;

		ushort moved = (ushort)Math.Min(capacity, stack.Count);
		ushort newCount = checked((ushort)((target.IsEmpty ? 0 : target.Count) + moved));
		_slots[slot] = new ItemStack(stack.Item, newCount);
		ushort remaining = checked((ushort)(stack.Count - moved));
		return remaining == 0 ? ItemStack.Empty : new ItemStack(stack.Item, remaining);
	}

	private void SetCursorCount(ushort count)
	{
		if (count == 0)
			ClearCursor();
		else
			Cursor = new ItemStack(Cursor.Item, count);
	}

	private void ClearCursor()
	{
		Cursor = ItemStack.Empty;
		CursorOriginSlot = NoCursorOrigin;
	}

	private InventoryMutationResult Changed()
	{
		Revision++;
		return new InventoryMutationResult(true, true, Revision);
	}

	private static void ValidateCursor(ItemStack cursor, int origin)
	{
		if (!ItemCatalog.IsCanonical(cursor))
			throw new ArgumentException("Cursor stack is not canonical.", nameof(cursor));
		if (cursor.IsEmpty && origin != NoCursorOrigin)
			throw new ArgumentException("An empty cursor cannot have an origin.", nameof(origin));
		if (!cursor.IsEmpty && (uint)origin >= SlotCount)
			throw new ArgumentOutOfRangeException(nameof(origin));
	}

	private static void ValidateSlot(int index)
	{
		if ((uint)index >= SlotCount)
			throw new ArgumentOutOfRangeException(nameof(index));
	}
}
