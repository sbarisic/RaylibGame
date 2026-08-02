namespace Voxelgine.Engine;

public sealed class ContainerStatePacket : Packet
{
	public override PacketType Type => PacketType.ContainerState;
	public ulong SessionId { get; set; }
	public string ContainerKey { get; set; } = string.Empty;
	public long ContainerRevision { get; set; }
	public long PlayerRevision { get; set; }
	public bool IsOpen { get; set; }
	public ItemStack[] Slots { get; set; } = Array.Empty<ItemStack>();
	public override void Write(BinaryWriter writer)
	{
		Validate(); writer.Write(SessionId); writer.Write(ContainerKey); writer.Write(ContainerRevision); writer.Write(PlayerRevision); writer.Write(IsOpen); writer.Write((byte)Slots.Length);
		foreach(ItemStack stack in Slots){writer.Write(stack.Item.Value);writer.Write(stack.Count);}
	}
	public override void Read(BinaryReader reader)
	{
		SessionId=reader.ReadUInt64();ContainerKey=reader.ReadString();ContainerRevision=reader.ReadInt64();PlayerRevision=reader.ReadInt64();IsOpen=reader.ReadBoolean();int count=reader.ReadByte();Slots=new ItemStack[count];
		for(int index=0;index<count;index++)Slots[index]=new ItemStack(new ItemId(reader.ReadUInt16()),reader.ReadUInt16());Validate();
	}
	private void Validate(){if(SessionId==0||string.IsNullOrWhiteSpace(ContainerKey)||ContainerRevision<1||PlayerRevision<1||Slots.Length>64||Slots.Any(static stack=>!ItemCatalog.IsCanonical(stack)))throw new InvalidDataException("Invalid container state.");}
}

public sealed class ContainerActionRequestPacket : Packet
{
	public override PacketType Type => PacketType.ContainerActionRequest;
	public ulong SessionId { get; set; } public uint ActionId { get; set; }
	public long ExpectedPlayerRevision { get; set; } public long ExpectedContainerRevision { get; set; }
	public InventoryActionKind Kind { get; set; } public byte Slot { get; set; }
	public override void Write(BinaryWriter writer){writer.Write(SessionId);writer.Write(ActionId);writer.Write(ExpectedPlayerRevision);writer.Write(ExpectedContainerRevision);writer.Write((byte)Kind);writer.Write(Slot);}
	public override void Read(BinaryReader reader){SessionId=reader.ReadUInt64();ActionId=reader.ReadUInt32();ExpectedPlayerRevision=reader.ReadInt64();ExpectedContainerRevision=reader.ReadInt64();Kind=(InventoryActionKind)reader.ReadByte();Slot=reader.ReadByte();}
}

public sealed class ContainerClosePacket : Packet
{
	public override PacketType Type => PacketType.ContainerClose;
	public ulong SessionId { get; set; }
	public override void Write(BinaryWriter writer)=>writer.Write(SessionId);
	public override void Read(BinaryReader reader)=>SessionId=reader.ReadUInt64();
}
