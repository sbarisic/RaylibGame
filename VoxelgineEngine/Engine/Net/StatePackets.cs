using System;
using System.IO;
using System.Numerics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Client → Server (unreliable). Sends local player input state for a tick.
	/// Key states are packed into a bitmask for compact transmission.
	/// </summary>
	public class InputStatePacket : Packet
	{
		public override PacketType Type => PacketType.InputState;

		public int TickNumber { get; set; }
		public ulong KeysBitmask { get; set; }
		public Vector2 CameraAngle { get; set; }
		public float MouseWheel { get; set; }

		/// <summary>
		/// Packs an <see cref="InputState"/> struct's key states into the bitmask.
		/// </summary>
		public unsafe void PackKeys(InputState state)
		{
			ulong mask = 0;
			int count = Math.Min((int)InputKey.InputKeyCount, 64);

			for (int i = 0; i < count; i++)
			{
				if (state.KeysDown[i])
					mask |= 1UL << i;
			}

			KeysBitmask = mask;
		}

		/// <summary>
		/// Unpacks the bitmask into an <see cref="InputState"/> struct's key states.
		/// </summary>
		public unsafe void UnpackKeys(ref InputState state)
		{
			int count = Math.Min((int)InputKey.InputKeyCount, 64);

			for (int i = 0; i < count; i++)
			{
				state.KeysDown[i] = (KeysBitmask & (1UL << i)) != 0;
			}
		}

		public override void Write(BinaryWriter writer)
		{
			writer.Write(TickNumber);
			writer.Write(KeysBitmask);
			writer.WriteVector2(CameraAngle);
			writer.Write(MouseWheel);
		}

		public override void Read(BinaryReader reader)
		{
			TickNumber = reader.ReadInt32();
			KeysBitmask = reader.ReadUInt64();
			CameraAngle = reader.ReadVector2();
			MouseWheel = reader.ReadSingle();
		}
	}

	/// <summary>
	/// Server → Client (unreliable). Authoritative state for a single player at a tick.
	/// </summary>
	public class PlayerSnapshotPacket : Packet
	{
		public override PacketType Type => PacketType.PlayerSnapshot;

		public int TickNumber { get; set; }
		public int PlayerId { get; set; }
		public Vector3 Position { get; set; }
		public Vector3 Velocity { get; set; }
		public Vector2 CameraAngle { get; set; }
		public byte AnimationState { get; set; }

		public override void Write(BinaryWriter writer)
		{
			writer.Write(TickNumber);
			writer.Write(PlayerId);
			writer.WriteVector3(Position);
			writer.WriteVector3(Velocity);
			writer.WriteVector2(CameraAngle);
			writer.Write(AnimationState);
		}

		public override void Read(BinaryReader reader)
		{
			TickNumber = reader.ReadInt32();
			PlayerId = reader.ReadInt32();
			Position = reader.ReadVector3();
			Velocity = reader.ReadVector3();
			CameraAngle = reader.ReadVector2();
			AnimationState = reader.ReadByte();
		}
	}

	/// <summary>
	/// Server → Client (unreliable). Bulk update of all player positions at a tick.
	/// </summary>
	public class WorldSnapshotPacket : Packet
	{
		public override PacketType Type => PacketType.WorldSnapshot;

		public int TickNumber { get; set; }

		/// <summary>
		/// Per-player entry in the world snapshot.
		/// </summary>
		public struct PlayerEntry
		{
			public int PlayerId;
			public Vector3 Position;
			public Vector3 Velocity;
			public Vector2 CameraAngle;
			public float Health;
			public byte AnimationState;

			/// <summary>
			/// The tick number of the last <see cref="InputStatePacket"/> the server
			/// processed for this player. Clients use this (not the server tick) to
			/// look up predicted state and determine the replay range during
			/// reconciliation. 0 if no input has been received yet.
			/// </summary>
			public int LastInputTick;
		}

		public PlayerEntry[] Players { get; set; } = Array.Empty<PlayerEntry>();

		public override void Write(BinaryWriter writer)
		{
			writer.Write(TickNumber);
			writer.Write(Players.Length);

			for (int i = 0; i < Players.Length; i++)
			{
				writer.Write(Players[i].PlayerId);
				writer.WriteVector3(Players[i].Position);
				writer.WriteVector3(Players[i].Velocity);
				writer.WriteVector2(Players[i].CameraAngle);
				writer.Write(Players[i].Health);
				writer.Write(Players[i].AnimationState);
				writer.Write(Players[i].LastInputTick);
			}
		}

		public override void Read(BinaryReader reader)
		{
			TickNumber = reader.ReadInt32();
			int count = reader.ReadInt32();
			Players = new PlayerEntry[count];

			for (int i = 0; i < count; i++)
			{
				Players[i].PlayerId = reader.ReadInt32();
				Players[i].Position = reader.ReadVector3();
				Players[i].Velocity = reader.ReadVector3();
				Players[i].CameraAngle = reader.ReadVector2();
				Players[i].Health = reader.ReadSingle();
				Players[i].AnimationState = reader.ReadByte();
				Players[i].LastInputTick = reader.ReadInt32();
			}
		}
	}
}
