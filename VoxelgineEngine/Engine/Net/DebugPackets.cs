using System.IO;
using System.Numerics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Client → Server (reliable). Requests spawning a debug entity at the given position.
	/// The server creates the entity, spawns it, and broadcasts an <see cref="EntitySpawnPacket"/> to all clients.
	/// </summary>
	public class DebugSpawnEntityRequestPacket : Packet
	{
		public override PacketType Type => PacketType.DebugSpawnEntityRequest;

		public string EntityType { get; set; } = string.Empty;
		public Vector3 Position { get; set; }
		public Vector3 FacingDirection { get; set; }

		public override void Write(BinaryWriter writer)
		{
			writer.Write(EntityType);
			writer.WriteVector3(Position);
			writer.WriteVector3(FacingDirection);
		}

		public override void Read(BinaryReader reader)
		{
			EntityType = reader.ReadString();
			Position = reader.ReadVector3();
			FacingDirection = reader.ReadVector3();
		}
	}

	/// <summary>
	/// Client → Server (reliable). Requests placing a block at the given position,
	/// bypassing inventory validation. Used for debug/creative mode.
	/// The server applies the change and broadcasts a <see cref="BlockChangePacket"/> to all clients.
	/// </summary>
	public class DebugPlaceBlockRequestPacket : Packet
	{
		public override PacketType Type => PacketType.DebugPlaceBlockRequest;

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
}
