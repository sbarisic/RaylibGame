using System.Numerics;

namespace Voxelgine.Engine;

public enum SoundEventType : byte
{
	BlockBreak,
	BlockPlace,
}
public sealed class SoundEventPacket : Packet
{
	public override PacketType Type => PacketType.SoundEvent;
	public byte EventType { get; set; }
	public Vector3 Position { get; set; }
	public int SourcePlayerId { get; set; }

	public override void Write(BinaryWriter writer)
	{
		writer.Write(EventType);
		writer.WriteVector3(Position);
		writer.Write(SourcePlayerId);
	}

	public override void Read(BinaryReader reader)
	{
		EventType = reader.ReadByte();
		Position = reader.ReadVector3();
		SourcePlayerId = reader.ReadInt32();
	}
}

public sealed class ChatMessagePacket : Packet
{
	public override PacketType Type => PacketType.ChatMessage;
	public int PlayerId { get; set; }
	public string Message { get; set; } = string.Empty;

	public override void Write(BinaryWriter writer)
	{
		writer.Write(PlayerId);
		writer.Write(Message);
	}

	public override void Read(BinaryReader reader)
	{
		PlayerId = reader.ReadInt32();
		Message = reader.ReadString();
	}
}

public sealed class DayTimeSyncPacket : Packet
{
	public override PacketType Type => PacketType.DayTimeSync;
	public float TimeOfDay { get; set; }
	public override void Write(BinaryWriter writer) => writer.Write(TimeOfDay);
	public override void Read(BinaryReader reader) => TimeOfDay = reader.ReadSingle();
}

public sealed class PingPacket : Packet
{
	public override PacketType Type => PacketType.Ping;
	public long Timestamp { get; set; }
	public override void Write(BinaryWriter writer) => writer.Write(Timestamp);
	public override void Read(BinaryReader reader) => Timestamp = reader.ReadInt64();
}

public sealed class PongPacket : Packet
{
	public override PacketType Type => PacketType.Pong;
	public long Timestamp { get; set; }
	public override void Write(BinaryWriter writer) => writer.Write(Timestamp);
	public override void Read(BinaryReader reader) => Timestamp = reader.ReadInt64();
}

public sealed class KillFeedPacket : Packet
{
	public override PacketType Type => PacketType.KillFeed;
	public string KillerName { get; set; } = string.Empty;
	public string VictimName { get; set; } = string.Empty;
	public byte WeaponType { get; set; }

	public override void Write(BinaryWriter writer)
	{
		writer.Write(KillerName);
		writer.Write(VictimName);
		writer.Write(WeaponType);
	}

	public override void Read(BinaryReader reader)
	{
		KillerName = reader.ReadString();
		VictimName = reader.ReadString();
		WeaponType = reader.ReadByte();
	}
}
