using System;
using System.IO;
using System.Numerics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Server → Client (reliable). Spawns an entity with the given type, network ID, position, and properties.
	/// </summary>
	public class EntitySpawnPacket : Packet
	{
		public override PacketType Type => PacketType.EntitySpawn;

		public string EntityType { get; set; } = string.Empty;
		public int NetworkId { get; set; }
		public Vector3 Position { get; set; }
		public byte[] Properties { get; set; } = Array.Empty<byte>();

		public override void Write(BinaryWriter writer)
		{
			writer.Write(EntityType);
			writer.Write(NetworkId);
			writer.WriteVector3(Position);
			writer.Write(Properties.Length);
			writer.Write(Properties);
		}

		public override void Read(BinaryReader reader)
		{
			EntityType = reader.ReadString();
			NetworkId = reader.ReadInt32();
			Position = reader.ReadVector3();
			int length = reader.ReadInt32();
			Properties = reader.ReadBytes(length);
		}
	}

	/// <summary>
	/// Server → Client (reliable). Removes the entity with the given network ID.
	/// </summary>
	public class EntityRemovePacket : Packet
	{
		public override PacketType Type => PacketType.EntityRemove;

		public int NetworkId { get; set; }

		public override void Write(BinaryWriter writer)
		{
			writer.Write(NetworkId);
		}

		public override void Read(BinaryReader reader)
		{
			NetworkId = reader.ReadInt32();
		}
	}

	/// <summary>
	/// Server → Client (unreliable). Position/velocity/animation update for an entity.
	/// </summary>
	public class EntitySnapshotPacket : Packet
	{
		public override PacketType Type => PacketType.EntitySnapshot;

		public int NetworkId { get; set; }
		public Vector3 Position { get; set; }
		public Vector3 Velocity { get; set; }
		public byte AnimationState { get; set; }

		public override void Write(BinaryWriter writer)
		{
			writer.Write(NetworkId);
			writer.WriteVector3(Position);
			writer.WriteVector3(Velocity);
			writer.Write(AnimationState);
		}

		public override void Read(BinaryReader reader)
		{
			NetworkId = reader.ReadInt32();
			Position = reader.ReadVector3();
			Velocity = reader.ReadVector3();
			AnimationState = reader.ReadByte();
		}
	}

	/// <summary>
	/// Server → Client (reliable). NPC speech bubble display.
	/// Empty text means the speech bubble should be hidden.
	/// </summary>
	public class EntitySpeechPacket : Packet
	{
		public override PacketType Type => PacketType.EntitySpeech;

		public int NetworkId { get; set; }
		public string Text { get; set; } = string.Empty;
		public float Duration { get; set; }

		public override void Write(BinaryWriter writer)
		{
			writer.Write(NetworkId);
			writer.Write(Text);
			writer.Write(Duration);
		}

		public override void Read(BinaryReader reader)
		{
			NetworkId = reader.ReadInt32();
			Text = reader.ReadString();
			Duration = reader.ReadSingle();
		}
	}
}
