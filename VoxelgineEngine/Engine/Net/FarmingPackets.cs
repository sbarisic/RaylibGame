using System.IO;

namespace Voxelgine.Engine;

public enum WorldObjectPlacementKind : byte { Wheat = 1, ItemBasket = 2, Bed = 3 }
public enum WorldInteractionKind : byte { Interact, RemoveFurniture }

public sealed class WorldInteractRequestPacket : Packet
{
	public override PacketType Type => PacketType.WorldInteractRequest;
	public int X { get; set; } public int Y { get; set; } public int Z { get; set; }
	public WorldInteractionKind Interaction { get; set; }
	public override void Write(BinaryWriter writer) { writer.Write(X); writer.Write(Y); writer.Write(Z); writer.Write((byte)Interaction); }
	public override void Read(BinaryReader reader) { X = reader.ReadInt32(); Y = reader.ReadInt32(); Z = reader.ReadInt32(); Interaction=(WorldInteractionKind)reader.ReadByte();if(!Enum.IsDefined(Interaction))throw new InvalidDataException("World interaction kind is invalid."); }
}

public sealed class WorldObjectPlaceRequestPacket : Packet
{
	public override PacketType Type => PacketType.WorldObjectPlaceRequest;
	public uint ActionId { get; set; }
	public int X { get; set; } public int Y { get; set; } public int Z { get; set; }
	public byte PlantType { get; set; }
	public override void Write(BinaryWriter writer) { writer.Write(ActionId); writer.Write(X); writer.Write(Y); writer.Write(Z); writer.Write(PlantType); }
	public override void Read(BinaryReader reader) { ActionId = reader.ReadUInt32(); X = reader.ReadInt32(); Y = reader.ReadInt32(); Z = reader.ReadInt32(); PlantType = reader.ReadByte(); }
}

public sealed class WorldObjectColumnPacket : Packet
{
	public const int MaximumRecordsPerPart = 256;
	public const int MaximumPartBytes = 64 * 1024;
	public const int MaximumParts = 128;
	public override PacketType Type => PacketType.WorldObjectColumn;
	public int StreamId { get; set; } public int X { get; set; } public int Z { get; set; }
	public ulong Epoch { get; set; } public long Revision { get; set; } public ulong SnapshotId { get; set; }
	public ushort PartIndex { get; set; } public ushort PartCount { get; set; }
	public int TotalRecordCount { get; set; } public int TotalDecodedBytes { get; set; }
	public uint FullPayloadChecksum { get; set; } public ushort PartRecordCount { get; set; }
	public byte[] Payload { get; set; } = Array.Empty<byte>();
	public override void Write(BinaryWriter writer) { Validate(); writer.Write(StreamId); writer.Write(X); writer.Write(Z); writer.Write(Epoch); writer.Write(Revision); writer.Write(SnapshotId); writer.Write(PartIndex); writer.Write(PartCount); writer.Write(TotalRecordCount); writer.Write(TotalDecodedBytes); writer.Write(FullPayloadChecksum); writer.Write(PartRecordCount); writer.Write(Payload.Length); writer.Write(Payload); }
	public override void Read(BinaryReader reader) { StreamId=reader.ReadInt32(); X=reader.ReadInt32(); Z=reader.ReadInt32(); Epoch=reader.ReadUInt64(); Revision=reader.ReadInt64(); SnapshotId=reader.ReadUInt64(); PartIndex=reader.ReadUInt16(); PartCount=reader.ReadUInt16(); TotalRecordCount=reader.ReadInt32(); TotalDecodedBytes=reader.ReadInt32(); FullPayloadChecksum=reader.ReadUInt32(); PartRecordCount=reader.ReadUInt16(); int length=reader.ReadInt32(); if(length<0||length>MaximumPartBytes) throw new InvalidDataException("Invalid world-object part length."); Payload=reader.ReadBytes(length); if(Payload.Length!=length) throw new EndOfStreamException(); Validate(); }
	private void Validate() { if(PartCount is 0 or >MaximumParts || PartIndex>=PartCount || PartRecordCount>MaximumRecordsPerPart || Payload.Length>MaximumPartBytes || TotalRecordCount is <0 or >16384 || TotalDecodedBytes is <0 or >4*1024*1024) throw new InvalidDataException("Invalid world-object column part limits."); }
}

public sealed class WorldObjectDeltaPacket : Packet
{
	public override PacketType Type => PacketType.WorldObjectDelta;
	public int StreamId { get; set; } public int X { get; set; } public int Z { get; set; }
	public ulong Epoch { get; set; } public long BaseRevision { get; set; } public long Revision { get; set; }
	public ushort OperationCount { get; set; } public byte[] Payload { get; set; } = Array.Empty<byte>();
	public override void Write(BinaryWriter writer) { Validate(); writer.Write(StreamId); writer.Write(X); writer.Write(Z); writer.Write(Epoch); writer.Write(BaseRevision); writer.Write(Revision); writer.Write(OperationCount); writer.Write(Payload.Length); writer.Write(Payload); }
	public override void Read(BinaryReader reader) { StreamId=reader.ReadInt32(); X=reader.ReadInt32(); Z=reader.ReadInt32(); Epoch=reader.ReadUInt64(); BaseRevision=reader.ReadInt64(); Revision=reader.ReadInt64(); OperationCount=reader.ReadUInt16(); int length=reader.ReadInt32(); if(length<0||length>1024*1024) throw new InvalidDataException("Invalid world-object delta length."); Payload=reader.ReadBytes(length); if(Payload.Length!=length) throw new EndOfStreamException(); Validate(); }
	private void Validate() { if(OperationCount>1024 || Payload.Length>1024*1024) throw new InvalidDataException("Invalid world-object delta limits."); }
}

public abstract class WorldObjectColumnReferencePacket : Packet
{
	public int StreamId { get; set; } public int X { get; set; } public int Z { get; set; }
	public ulong Epoch { get; set; } public long Revision { get; set; }
	public override void Write(BinaryWriter writer) { writer.Write(StreamId); writer.Write(X); writer.Write(Z); writer.Write(Epoch); writer.Write(Revision); }
	public override void Read(BinaryReader reader) { StreamId=reader.ReadInt32(); X=reader.ReadInt32(); Z=reader.ReadInt32(); Epoch=reader.ReadUInt64(); Revision=reader.ReadInt64(); }
}
public sealed class WorldObjectResyncRequestPacket : WorldObjectColumnReferencePacket { public override PacketType Type => PacketType.WorldObjectResyncRequest; }
public sealed class WorldObjectColumnAppliedPacket : WorldObjectColumnReferencePacket { public override PacketType Type => PacketType.WorldObjectColumnApplied; }

public sealed class CraftRequestPacket : Packet
{
	public override PacketType Type => PacketType.CraftRequest;
	public uint ActionId { get; set; } public ushort RecipeId { get; set; }
	public override void Write(BinaryWriter writer) { writer.Write(ActionId); writer.Write(RecipeId); }
	public override void Read(BinaryReader reader) { ActionId=reader.ReadUInt32(); RecipeId=reader.ReadUInt16(); }
}
public sealed class CraftResultPacket : Packet
{
	public override PacketType Type => PacketType.CraftResult;
	public uint ActionId { get; set; } public bool Accepted { get; set; } public byte Reason { get; set; }
	public override void Write(BinaryWriter writer) { writer.Write(ActionId); writer.Write(Accepted); writer.Write(Reason); }
	public override void Read(BinaryReader reader) { ActionId=reader.ReadUInt32(); Accepted=reader.ReadBoolean(); Reason=reader.ReadByte(); }
}
