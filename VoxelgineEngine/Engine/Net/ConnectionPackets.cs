using System.IO;
using System.Numerics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Client → Server. Requests connection with player name and protocol version.
	/// </summary>
	public class ConnectPacket : Packet
	{
		public override PacketType Type => PacketType.Connect;

		public string PlayerName { get; set; } = string.Empty;
		public int ProtocolVersion { get; set; }

		public override void Write(BinaryWriter writer)
		{
			writer.Write(PlayerName);
			writer.Write(ProtocolVersion);
		}

		public override void Read(BinaryReader reader)
		{
			PlayerName = reader.ReadString();
			ProtocolVersion = reader.ReadInt32();
		}
	}

	/// <summary>
	/// Server → Client. Accepts connection with assigned player ID, server tick, and world seed.
	/// </summary>
	public class ConnectAcceptPacket : Packet
	{
		public override PacketType Type => PacketType.ConnectAccept;

		public int PlayerId { get; set; }
		public int ServerTick { get; set; }
		public int WorldSeed { get; set; }

		public override void Write(BinaryWriter writer)
		{
			writer.Write(PlayerId);
			writer.Write(ServerTick);
			writer.Write(WorldSeed);
		}

		public override void Read(BinaryReader reader)
		{
			PlayerId = reader.ReadInt32();
			ServerTick = reader.ReadInt32();
			WorldSeed = reader.ReadInt32();
		}
	}

	/// <summary>
	/// Server → Client. Rejects connection with a reason string.
	/// </summary>
	public class ConnectRejectPacket : Packet
	{
		public override PacketType Type => PacketType.ConnectReject;

		public string Reason { get; set; } = string.Empty;

		public override void Write(BinaryWriter writer)
		{
			writer.Write(Reason);
		}

		public override void Read(BinaryReader reader)
		{
			Reason = reader.ReadString();
		}
	}

	/// <summary>
	/// Bidirectional. Signals intentional disconnect with a reason string.
	/// </summary>
	public class DisconnectPacket : Packet
	{
		public override PacketType Type => PacketType.Disconnect;

		public string Reason { get; set; } = string.Empty;

		public override void Write(BinaryWriter writer)
		{
			writer.Write(Reason);
		}

		public override void Read(BinaryReader reader)
		{
			Reason = reader.ReadString();
		}
	}

	/// <summary>
	/// Server → Client. Notifies that a player has joined with their ID, name, and position.
	/// </summary>
	public class PlayerJoinedPacket : Packet
	{
		public override PacketType Type => PacketType.PlayerJoined;

		public int PlayerId { get; set; }
		public string PlayerName { get; set; } = string.Empty;
		public Vector3 Position { get; set; }

		public override void Write(BinaryWriter writer)
		{
			writer.Write(PlayerId);
			writer.Write(PlayerName);
			writer.WriteVector3(Position);
		}

		public override void Read(BinaryReader reader)
		{
			PlayerId = reader.ReadInt32();
			PlayerName = reader.ReadString();
			Position = reader.ReadVector3();
		}
	}

	/// <summary>
	/// Server → Client. Notifies that a player has left.
	/// </summary>
	public class PlayerLeftPacket : Packet
	{
		public override PacketType Type => PacketType.PlayerLeft;

		public int PlayerId { get; set; }

		public override void Write(BinaryWriter writer)
		{
			writer.Write(PlayerId);
		}

		public override void Read(BinaryReader reader)
		{
			PlayerId = reader.ReadInt32();
		}
	}
}
