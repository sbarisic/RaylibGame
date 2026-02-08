using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Network packet type identifiers matching the protocol table.
	/// </summary>
	public enum PacketType : byte
	{
		// Connection (0x01–0x06)
		Connect = 0x01,
		ConnectAccept = 0x02,
		ConnectReject = 0x03,
		Disconnect = 0x04,
		PlayerJoined = 0x05,
		PlayerLeft = 0x06,

		// State (0x10–0x12)
		InputState = 0x10,
		PlayerSnapshot = 0x11,
		WorldSnapshot = 0x12,

		// World (0x20–0x22, 0x40–0x41)
		BlockChange = 0x20,
		BlockPlaceRequest = 0x21,
		BlockRemoveRequest = 0x22,
		WorldData = 0x40,
		WorldDataComplete = 0x41,

		// Entity (0x30–0x32)
		EntitySpawn = 0x30,
		EntityRemove = 0x31,
		EntitySnapshot = 0x32,

		// Combat (0x60–0x62)
		WeaponFire = 0x60,
		WeaponFireEffect = 0x61,
		PlayerDamage = 0x62,

		// Misc
		ChatMessage = 0x50,
		DayTimeSync = 0x70,
		Ping = 0x80,
		Pong = 0x81,

		// Inventory
		InventoryUpdate = 0x90,

		// Sound/particle events
		SoundEvent = 0xA0,

		// Kill feed
		KillFeed = 0xB0,
	}

	/// <summary>
	/// Extension methods for <see cref="BinaryWriter"/> and <see cref="BinaryReader"/>
	/// to handle common network serialization types.
	/// </summary>
	public static class BinaryExtensions
	{
		public static void WriteVector3(this BinaryWriter writer, Vector3 v)
		{
			writer.Write(v.X);
			writer.Write(v.Y);
			writer.Write(v.Z);
		}

		public static Vector3 ReadVector3(this BinaryReader reader)
		{
			float x = reader.ReadSingle();
			float y = reader.ReadSingle();
			float z = reader.ReadSingle();
			return new Vector3(x, y, z);
		}

		public static void WriteVector2(this BinaryWriter writer, Vector2 v)
		{
			writer.Write(v.X);
			writer.Write(v.Y);
		}

		public static Vector2 ReadVector2(this BinaryReader reader)
		{
			float x = reader.ReadSingle();
			float y = reader.ReadSingle();
			return new Vector2(x, y);
		}
	}

	/// <summary>
	/// Base class for all network packets. Provides binary serialization
	/// via <see cref="Write(BinaryWriter)"/> and <see cref="Read(BinaryReader)"/>,
	/// plus convenience methods <see cref="Serialize()"/> and <see cref="Deserialize(byte[])"/>.
	/// </summary>
	public abstract class Packet
	{
		/// <summary>
		/// The packet type identifier.
		/// </summary>
		public abstract PacketType Type { get; }

		/// <summary>
		/// Writes the packet payload to the writer. Does not write the type byte header.
		/// </summary>
		public abstract void Write(BinaryWriter writer);

		/// <summary>
		/// Reads the packet payload from the reader. The type byte has already been consumed.
		/// </summary>
		public abstract void Read(BinaryReader reader);

		/// <summary>
		/// Serializes this packet to a byte array with the type byte as the first byte.
		/// </summary>
		public byte[] Serialize()
		{
			using var ms = new MemoryStream();
			using var writer = new BinaryWriter(ms);
			writer.Write((byte)Type);
			Write(writer);
			return ms.ToArray();
		}

		/// <summary>
		/// Deserializes a byte array into a typed <see cref="Packet"/> instance.
		/// The first byte is the packet type; the remainder is the payload.
		/// </summary>
		public static Packet Deserialize(byte[] data)
		{
			using var ms = new MemoryStream(data);
			using var reader = new BinaryReader(ms);
			byte typeId = reader.ReadByte();
			Packet packet = PacketRegistry.Create((PacketType)typeId);
			packet.Read(reader);
			return packet;
		}
	}

	/// <summary>
	/// Maps <see cref="PacketType"/> IDs to factory functions for deserialization.
	/// All packet types are registered in the static constructor.
	/// </summary>
	public static class PacketRegistry
	{
		private static readonly Dictionary<PacketType, Func<Packet>> _factories = new();

		/// <summary>
		/// Registers a packet type with its factory function.
		/// </summary>
		public static void Register<T>(PacketType type) where T : Packet, new()
		{
			_factories[type] = () => new T();
		}

		/// <summary>
		/// Creates a new empty packet instance for the given type.
		/// </summary>
		public static Packet Create(PacketType type)
		{
			if (_factories.TryGetValue(type, out var factory))
				return factory();

			throw new InvalidOperationException($"Unknown packet type: 0x{(byte)type:X2}");
		}

		static PacketRegistry()
		{
			// Connection
			Register<ConnectPacket>(PacketType.Connect);
			Register<ConnectAcceptPacket>(PacketType.ConnectAccept);
			Register<ConnectRejectPacket>(PacketType.ConnectReject);
			Register<DisconnectPacket>(PacketType.Disconnect);
			Register<PlayerJoinedPacket>(PacketType.PlayerJoined);
			Register<PlayerLeftPacket>(PacketType.PlayerLeft);

			// State
			Register<InputStatePacket>(PacketType.InputState);
			Register<PlayerSnapshotPacket>(PacketType.PlayerSnapshot);
			Register<WorldSnapshotPacket>(PacketType.WorldSnapshot);

			// World
			Register<BlockChangePacket>(PacketType.BlockChange);
			Register<BlockPlaceRequestPacket>(PacketType.BlockPlaceRequest);
			Register<BlockRemoveRequestPacket>(PacketType.BlockRemoveRequest);
			Register<WorldDataPacket>(PacketType.WorldData);
			Register<WorldDataCompletePacket>(PacketType.WorldDataComplete);

			// Entity
			Register<EntitySpawnPacket>(PacketType.EntitySpawn);
			Register<EntityRemovePacket>(PacketType.EntityRemove);
			Register<EntitySnapshotPacket>(PacketType.EntitySnapshot);

			// Combat
			Register<WeaponFirePacket>(PacketType.WeaponFire);
			Register<WeaponFireEffectPacket>(PacketType.WeaponFireEffect);
			Register<PlayerDamagePacket>(PacketType.PlayerDamage);

			// Misc
			Register<ChatMessagePacket>(PacketType.ChatMessage);
			Register<DayTimeSyncPacket>(PacketType.DayTimeSync);
			Register<PingPacket>(PacketType.Ping);
			Register<PongPacket>(PacketType.Pong);

			// Inventory
			Register<InventoryUpdatePacket>(PacketType.InventoryUpdate);

			// Sound/particle events
			Register<SoundEventPacket>(PacketType.SoundEvent);

			// Kill feed
			Register<KillFeedPacket>(PacketType.KillFeed);
		}
	}
}
