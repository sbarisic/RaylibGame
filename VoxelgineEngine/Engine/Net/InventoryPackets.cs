namespace Voxelgine.Engine;

public sealed class InventoryActionRequestPacket : Packet
{
	public const byte NoSlot = byte.MaxValue;
	public override PacketType Type => PacketType.InventoryActionRequest;
	public uint ActionId { get; set; }
	public long ExpectedRevision { get; set; }
	public InventoryActionKind Kind { get; set; }
	public byte Slot { get; set; } = NoSlot;

	public override void Write(BinaryWriter writer)
	{
		writer.Write(ActionId);
		writer.Write(ExpectedRevision);
		writer.Write((byte)Kind);
		writer.Write(Slot);
	}

	public override void Read(BinaryReader reader)
	{
		ActionId = reader.ReadUInt32();
		ExpectedRevision = reader.ReadInt64();
		Kind = (InventoryActionKind)reader.ReadByte();
		Slot = reader.ReadByte();
	}
}

public sealed class InventoryStatePacket : Packet
{
	public const byte NoCursorOrigin = byte.MaxValue;
	public override PacketType Type => PacketType.InventoryState;
	public uint AcknowledgedActionId { get; set; }
	public bool ActionAccepted { get; set; }
	public long Revision { get; set; }
	public byte SelectedHotbarSlot { get; set; }
	public int SelectionCommandTick { get; set; }
	public ItemStack Cursor { get; set; }
	public byte CursorOriginSlot { get; set; } = NoCursorOrigin;
	public ItemStack[] Slots { get; set; } = Array.Empty<ItemStack>();

	public override void Write(BinaryWriter writer)
	{
		Validate();
		writer.Write(AcknowledgedActionId);
		writer.Write(ActionAccepted);
		writer.Write(Revision);
		writer.Write(SelectedHotbarSlot);
		writer.Write(SelectionCommandTick);
		WriteStack(writer, Cursor);
		writer.Write(CursorOriginSlot);
		for (int i = 0; i < Slots.Length; i++)
			WriteStack(writer, Slots[i]);
	}

	public override void Read(BinaryReader reader)
	{
		AcknowledgedActionId = reader.ReadUInt32();
		ActionAccepted = reader.ReadBoolean();
		Revision = reader.ReadInt64();
		SelectedHotbarSlot = reader.ReadByte();
		SelectionCommandTick = reader.ReadInt32();
		Cursor = ReadStack(reader);
		CursorOriginSlot = reader.ReadByte();
		Slots = new ItemStack[PlayerInventory.SlotCount];
		for (int i = 0; i < Slots.Length; i++)
			Slots[i] = ReadStack(reader);
		Validate();
	}

	public void Validate()
	{
		if (Slots.Length != PlayerInventory.SlotCount)
			throw new InvalidDataException($"Inventory state requires exactly {PlayerInventory.SlotCount} slots.");
		if (Revision < 1)
			throw new InvalidDataException("Inventory revision must be positive.");
		if (SelectedHotbarSlot >= PlayerInventory.HotbarSlotCount)
			throw new InvalidDataException("Selected hotbar slot is invalid.");
		if (!ItemCatalog.IsCanonical(Cursor))
			throw new InvalidDataException("Cursor stack is invalid.");
		if (Cursor.IsEmpty != (CursorOriginSlot == NoCursorOrigin))
			throw new InvalidDataException("Cursor origin is not canonical.");
		if (!Cursor.IsEmpty && CursorOriginSlot >= PlayerInventory.SlotCount)
			throw new InvalidDataException("Cursor origin is out of range.");
		for (int i = 0; i < Slots.Length; i++)
		{
			if (!ItemCatalog.IsCanonical(Slots[i]))
				throw new InvalidDataException($"Inventory slot {i} is invalid.");
		}
	}

	private static void WriteStack(BinaryWriter writer, ItemStack stack)
	{
		writer.Write(stack.Item.Value);
		writer.Write(stack.Count);
	}

	private static ItemStack ReadStack(BinaryReader reader) =>
		new(new ItemId(reader.ReadUInt16()), reader.ReadUInt16());
}

public enum ItemUseChannel : byte
{
	Primary,
	Secondary,
}

public enum ItemUseRejectionReason : byte
{
	None,
	QueueFull,
	CommandTooFarAhead,
	CommandExpired,
	InvalidSelection,
	ItemMismatch,
	InvalidTarget,
	OutOfReach,
	CollisionBlocked,
	NotBreakable,
	WorldConflict,
	NoEffect,
	ChannelAlreadyConsumed,
}

public sealed class ItemUseResultPacket : Packet
{
	public override PacketType Type => PacketType.ItemUseResult;
	public uint ItemUseActionId { get; set; }
	public int CommandTick { get; set; }
	public bool Accepted { get; set; }
	public ItemUseRejectionReason RejectionReason { get; set; }
	public long InventoryRevision { get; set; }

	public override void Write(BinaryWriter writer)
	{
		writer.Write(ItemUseActionId);
		writer.Write(CommandTick);
		writer.Write(Accepted);
		writer.Write((byte)RejectionReason);
		writer.Write(InventoryRevision);
	}

	public override void Read(BinaryReader reader)
	{
		ItemUseActionId = reader.ReadUInt32();
		CommandTick = reader.ReadInt32();
		Accepted = reader.ReadBoolean();
		RejectionReason = (ItemUseRejectionReason)reader.ReadByte();
		InventoryRevision = reader.ReadInt64();
	}
}
