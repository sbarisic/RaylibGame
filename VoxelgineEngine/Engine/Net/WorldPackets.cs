using System;
using System.IO;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Server → Client (reliable). Notifies a block change at a world position.
	/// </summary>
	public class BlockChangePacket : Packet
	{
		public override PacketType Type => PacketType.BlockChange;

		public int X { get; set; }
		public int Y { get; set; }
		public int Z { get; set; }
		public byte BlockType { get; set; }

		public override void Write(BinaryWriter writer)
		{
			writer.Write(X);
			writer.Write(Y);
			writer.Write(Z);
			writer.Write(BlockType);
		}

		public override void Read(BinaryReader reader)
		{
			X = reader.ReadInt32();
			Y = reader.ReadInt32();
			Z = reader.ReadInt32();
			BlockType = reader.ReadByte();
		}
	}

	/// <summary>
	/// Client → Server (reliable). Requests placing a block at a world position.
	/// </summary>
	public class BlockPlaceRequestPacket : Packet
	{
		public override PacketType Type => PacketType.BlockPlaceRequest;

		public int X { get; set; }
		public int Y { get; set; }
		public int Z { get; set; }
		public byte BlockType { get; set; }

		public override void Write(BinaryWriter writer)
		{
			writer.Write(X);
			writer.Write(Y);
			writer.Write(Z);
			writer.Write(BlockType);
		}

		public override void Read(BinaryReader reader)
		{
			X = reader.ReadInt32();
			Y = reader.ReadInt32();
			Z = reader.ReadInt32();
			BlockType = reader.ReadByte();
		}
	}

	/// <summary>
	/// Client → Server (reliable). Requests removing a block at a world position.
	/// </summary>
	public class BlockRemoveRequestPacket : Packet
	{
		public override PacketType Type => PacketType.BlockRemoveRequest;

		public int X { get; set; }
		public int Y { get; set; }
		public int Z { get; set; }

		public override void Write(BinaryWriter writer)
		{
			writer.Write(X);
			writer.Write(Y);
			writer.Write(Z);
		}

		public override void Read(BinaryReader reader)
		{
			X = reader.ReadInt32();
			Y = reader.ReadInt32();
			Z = reader.ReadInt32();
		}
	}

	/// <summary>
	/// Server → Client (reliable). A fragment of GZip-compressed world data sent during connect.
	/// </summary>
	public class WorldDataPacket : Packet
	{
		public override PacketType Type => PacketType.WorldData;

		public int FragmentIndex { get; set; }
		public byte[] Data { get; set; } = Array.Empty<byte>();

		public override void Write(BinaryWriter writer)
		{
			writer.Write(FragmentIndex);
			writer.Write(Data.Length);
			writer.Write(Data);
		}

		public override void Read(BinaryReader reader)
		{
			FragmentIndex = reader.ReadInt32();
			int length = reader.ReadInt32();
			Data = reader.ReadBytes(length);
		}
	}

	/// <summary>
	/// Server → Client (reliable). Signals that all world data fragments have been sent.
	/// </summary>
	public class WorldDataCompletePacket : Packet
	{
		public override PacketType Type => PacketType.WorldDataComplete;

		public int TotalFragments { get; set; }
		public uint Checksum { get; set; }

		public override void Write(BinaryWriter writer)
		{
			writer.Write(TotalFragments);
			writer.Write(Checksum);
		}

		public override void Read(BinaryReader reader)
		{
			TotalFragments = reader.ReadInt32();
			Checksum = reader.ReadUInt32();
		}
	}
}
