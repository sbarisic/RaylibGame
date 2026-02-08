using System;
using System.IO;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Server → Client (reliable). Updates one or more inventory slots with new item counts.
	/// Used for initial inventory sync on connect and server-authoritative count corrections.
	/// </summary>
	public class InventoryUpdatePacket : Packet
	{
		public override PacketType Type => PacketType.InventoryUpdate;

		/// <summary>Inventory slot entries: (slotIndex, count). Count of -1 means infinite.</summary>
		public InventorySlotEntry[] Slots { get; set; } = Array.Empty<InventorySlotEntry>();

		public struct InventorySlotEntry
		{
			public byte SlotIndex;
			public int Count;
		}

		public override void Write(BinaryWriter writer)
		{
			writer.Write((byte)Slots.Length);
			for (int i = 0; i < Slots.Length; i++)
			{
				writer.Write(Slots[i].SlotIndex);
				writer.Write(Slots[i].Count);
			}
		}

		public override void Read(BinaryReader reader)
		{
			int count = reader.ReadByte();
			Slots = new InventorySlotEntry[count];
			for (int i = 0; i < count; i++)
			{
				Slots[i].SlotIndex = reader.ReadByte();
				Slots[i].Count = reader.ReadInt32();
			}
		}
	}

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
