namespace Voxelgine.Engine;

public enum InventoryStoreKind : byte { Player, Container }

public readonly record struct SlotAddress
{
	public SlotAddress(InventoryStoreKind storeKind, string ownerKey, int slot)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(ownerKey);
		if (slot < 0) throw new ArgumentOutOfRangeException(nameof(slot));
		StoreKind = storeKind; OwnerKey = ownerKey; Slot = slot;
	}
	public InventoryStoreKind StoreKind { get; }
	public string OwnerKey { get; }
	public int Slot { get; }
}

public sealed class ContainerInventory
{
	private readonly ItemStack[] slots;
	public ContainerInventory(int slotCount)
	{
		if (slotCount <= 0 || slotCount > 256) throw new ArgumentOutOfRangeException(nameof(slotCount));
		slots = new ItemStack[slotCount];
	}
	public int SlotCount => slots.Length;
	public long Revision { get; private set; } = 1;
	public ItemStack GetSlot(int slot) => (uint)slot < slots.Length ? slots[slot] : throw new ArgumentOutOfRangeException(nameof(slot));
	public ReadOnlySpan<ItemStack> GetSlots() => slots;
	public void CopySlotsTo(Span<ItemStack> destination)
	{
		if (destination.Length < slots.Length) throw new ArgumentException("Destination is too small.", nameof(destination));
		slots.CopyTo(destination);
	}
	public void Restore(ReadOnlySpan<ItemStack> values)
	{
		if (values.Length != slots.Length || values.ToArray().Any(static stack => !ItemCatalog.IsCanonical(stack)))
			throw new ArgumentException("Container slots are invalid.", nameof(values));
		values.CopyTo(slots); Revision = 1;
	}
	internal bool TryCommit(ReadOnlySpan<ItemStack> values, long expectedRevision)
	{
		if (expectedRevision != Revision || values.Length != slots.Length || values.ToArray().Any(static stack => !ItemCatalog.IsCanonical(stack))) return false;
		values.CopyTo(slots); Revision++; return true;
	}
}

public readonly record struct InventoryTransactionResult(
	bool Accepted,
	bool Changed,
	long PlayerRevision,
	long ContainerRevision,
	SlotAddress? CursorOrigin);

/// <summary>One atomic slot/cursor primitive used by player and container interactions.</summary>
public sealed class InventoryTransactionService
{
	private readonly Dictionary<string, SlotAddress> cursorOrigins = new(StringComparer.Ordinal);

	public InventoryTransactionResult ApplyPlayerClick(
		string playerKey, PlayerInventory player, InventoryActionKind kind, int slot, long expectedRevision)
	{
		ArgumentNullException.ThrowIfNull(player);
		if (player.Revision != expectedRevision) return Rejected(player, null, playerKey);
		InventoryMutationResult result = player.ApplyClick(kind, slot);
		UpdatePlayerOrigin(playerKey, player, kind, slot);
		return new(result.Accepted, result.Changed, player.Revision, 0, GetOrigin(playerKey));
	}

	public InventoryTransactionResult ApplyContainerClick(
		string playerKey,
		PlayerInventory player,
		ContainerInventory container,
		string containerKey,
		InventoryActionKind kind,
		int slot,
		long expectedPlayerRevision,
		long expectedContainerRevision)
	{
		ArgumentNullException.ThrowIfNull(player); ArgumentNullException.ThrowIfNull(container);
		if (kind is not (InventoryActionKind.LeftClickSlot or InventoryActionKind.RightClickSlot) ||
			(uint)slot >= container.SlotCount || player.Revision != expectedPlayerRevision || container.Revision != expectedContainerRevision)
			return Rejected(player, container, playerKey);

		ItemStack[] playerSlots = player.GetSlots().ToArray();
		ItemStack[] containerSlots = container.GetSlots().ToArray();
		ItemStack cursor = player.Cursor;
		ItemStack previousCursor = cursor;
		ItemStack target = containerSlots[slot];
		bool cursorWasEmpty = cursor.IsEmpty;
		ItemStack previousTarget = target;
		bool changed = ApplyClickCore(kind, ref cursor, ref target);
		if (!changed) return new(true, false, player.Revision, container.Revision, GetOrigin(playerKey));
		containerSlots[slot] = target;
		int compatibilityOrigin = cursor.IsEmpty ? PlayerInventory.NoCursorOrigin : slot;
		if (!player.TryCommitTransaction(playerSlots, cursor, compatibilityOrigin, expectedPlayerRevision) ||
			!container.TryCommit(containerSlots, expectedContainerRevision))
			throw new InvalidOperationException("Validated inventory transaction lost atomic ownership.");
		if (cursor.IsEmpty) cursorOrigins.Remove(playerKey);
		else if (cursorWasEmpty || (!previousTarget.IsEmpty && previousTarget.Item != previousCursor.Item))
			cursorOrigins[playerKey] = new SlotAddress(InventoryStoreKind.Container, containerKey, slot);
		return new(true, true, player.Revision, container.Revision, GetOrigin(playerKey));
	}

	public ItemStack ResolveCursor(
		string playerKey,
		PlayerInventory player,
		Func<string, ContainerInventory> resolveContainer)
	{
		if (player.Cursor.IsEmpty) { cursorOrigins.Remove(playerKey); return ItemStack.Empty; }
		ItemStack cursor = player.Cursor;
		if (cursorOrigins.TryGetValue(playerKey, out SlotAddress origin) && origin.StoreKind == InventoryStoreKind.Container)
		{
			ContainerInventory container = resolveContainer?.Invoke(origin.OwnerKey);
			if (container != null && (uint)origin.Slot < container.SlotCount)
			{
				ItemStack[] slots = container.GetSlots().ToArray();
				cursor = InsertInto(ref slots[origin.Slot], cursor);
				if (cursor.Count != player.Cursor.Count) container.TryCommit(slots, container.Revision);
			}
		}
		player.TryReplaceCursor(cursor, player.Revision);
		ItemStack remainder = player.ResolveCursorForDeath();
		cursorOrigins.Remove(playerKey);
		return remainder;
	}

	public bool TryTransfer(ContainerInventory source, int sourceSlot, PlayerInventory target, ushort count)
	{
		if ((uint)sourceSlot >= source.SlotCount || count == 0) return false;
		ItemStack stack = source.GetSlot(sourceSlot);
		if (stack.IsEmpty || stack.Count < count) return false;
		ItemStack moving = new(stack.Item, count);
		InventoryInsertionResult insertion = target.TryInsert(moving);
		if (!insertion.Remainder.IsEmpty) return false;
		ItemStack[] slots = source.GetSlots().ToArray();
		ushort left = checked((ushort)(stack.Count - count));
		slots[sourceSlot] = left == 0 ? ItemStack.Empty : new ItemStack(stack.Item, left);
		return source.TryCommit(slots, source.Revision);
	}

	private static bool ApplyClickCore(InventoryActionKind kind, ref ItemStack cursor, ref ItemStack target)
	{
		if (cursor.IsEmpty)
		{
			if (target.IsEmpty) return false;
			ushort taken = kind == InventoryActionKind.LeftClickSlot ? target.Count : checked((ushort)((target.Count + 1) / 2));
			ushort left = checked((ushort)(target.Count - taken)); cursor = new ItemStack(target.Item, taken);
			target = left == 0 ? ItemStack.Empty : new ItemStack(target.Item, left); return true;
		}
		if (target.IsEmpty)
		{
			ushort moved = kind == InventoryActionKind.LeftClickSlot ? cursor.Count : (ushort)1;
			target = new ItemStack(cursor.Item, moved); ushort left = checked((ushort)(cursor.Count - moved));
			cursor = left == 0 ? ItemStack.Empty : new ItemStack(cursor.Item, left); return true;
		}
		if (target.Item != cursor.Item)
		{
			if (kind == InventoryActionKind.RightClickSlot) return false;
			(target, cursor) = (cursor, target); return true;
		}
		ushort maximum = ItemCatalog.Get(target.Item).MaximumStack;
		int capacity = maximum - target.Count; if (capacity <= 0) return false;
		ushort amount = kind == InventoryActionKind.LeftClickSlot ? checked((ushort)Math.Min(capacity, cursor.Count)) : (ushort)1;
		target = new ItemStack(target.Item, checked((ushort)(target.Count + amount)));
		ushort remainder = checked((ushort)(cursor.Count - amount)); cursor = remainder == 0 ? ItemStack.Empty : new ItemStack(cursor.Item, remainder); return true;
	}

	private void UpdatePlayerOrigin(string playerKey, PlayerInventory player, InventoryActionKind kind, int slot)
	{
		if (player.Cursor.IsEmpty) cursorOrigins.Remove(playerKey);
		else if (kind != InventoryActionKind.CancelCursor && !cursorOrigins.ContainsKey(playerKey))
			cursorOrigins[playerKey] = new SlotAddress(InventoryStoreKind.Player, playerKey, slot);
	}
	private SlotAddress? GetOrigin(string playerKey) => cursorOrigins.TryGetValue(playerKey, out SlotAddress origin) ? origin : null;
	private InventoryTransactionResult Rejected(PlayerInventory player, ContainerInventory container, string playerKey) => new(false, false, player.Revision, container?.Revision ?? 0, GetOrigin(playerKey));
	private static ItemStack InsertInto(ref ItemStack target, ItemStack source)
	{
		if (!target.IsEmpty && target.Item != source.Item) return source;
		ushort maximum = ItemCatalog.Get(source.Item).MaximumStack; int capacity = target.IsEmpty ? maximum : maximum - target.Count;
		ushort moved = checked((ushort)Math.Min(capacity, source.Count));
		if (moved > 0) target = new ItemStack(source.Item, checked((ushort)((target.IsEmpty ? 0 : target.Count) + moved)));
		ushort left = checked((ushort)(source.Count - moved)); return left == 0 ? ItemStack.Empty : new ItemStack(source.Item, left);
	}
}
