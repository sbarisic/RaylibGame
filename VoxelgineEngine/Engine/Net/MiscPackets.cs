using System.IO;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Bidirectional (reliable). A text chat message from a player.
	/// </summary>
	public class ChatMessagePacket : Packet
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

	/// <summary>
	/// Server → Client (reliable). Synchronizes the time of day.
	/// </summary>
	public class DayTimeSyncPacket : Packet
	{
		public override PacketType Type => PacketType.DayTimeSync;

		public float TimeOfDay { get; set; }

		public override void Write(BinaryWriter writer)
		{
			writer.Write(TimeOfDay);
		}

		public override void Read(BinaryReader reader)
		{
			TimeOfDay = reader.ReadSingle();
		}
	}

	/// <summary>
	/// Bidirectional (unreliable). Sends a timestamp for RTT measurement.
	/// </summary>
	public class PingPacket : Packet
	{
		public override PacketType Type => PacketType.Ping;

		public long Timestamp { get; set; }

		public override void Write(BinaryWriter writer)
		{
			writer.Write(Timestamp);
		}

		public override void Read(BinaryReader reader)
		{
			Timestamp = reader.ReadInt64();
		}
	}

	/// <summary>
	/// Bidirectional (unreliable). Echoes back the timestamp from a <see cref="PingPacket"/>.
	/// </summary>
	public class PongPacket : Packet
	{
		public override PacketType Type => PacketType.Pong;

		public long Timestamp { get; set; }

		public override void Write(BinaryWriter writer)
		{
			writer.Write(Timestamp);
		}

		public override void Read(BinaryReader reader)
		{
			Timestamp = reader.ReadInt64();
		}
	}
}
