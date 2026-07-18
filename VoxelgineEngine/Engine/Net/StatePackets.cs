using System;
using System.IO;
using System.Numerics;

namespace Voxelgine.Engine
{
	/// <summary>A single session-local player command.</summary>
	public struct InputCommand
	{
		public int TickNumber;
		public ulong KeysBitmask;
		public Vector2 CameraAngle;
		public float MouseWheel;
		public bool NoClip;

		public static unsafe InputCommand FromState(
			int tickNumber,
			InputState state,
			Vector2 cameraAngle,
			bool noClip = false)
		{
			ulong mask = 0;
			int count = Math.Min((int)InputKey.InputKeyCount, 64);
			for (int i = 0; i < count; i++)
			{
				if (state.KeysDown[i])
					mask |= 1UL << i;
			}

			return new InputCommand
			{
				TickNumber = tickNumber,
				KeysBitmask = mask,
				CameraAngle = cameraAngle,
				MouseWheel = state.MouseWheel,
				NoClip = noClip,
			};
		}

		public unsafe void UnpackKeys(ref InputState state)
		{
			int count = Math.Min((int)InputKey.InputKeyCount, 64);
			for (int i = 0; i < count; i++)
				state.KeysDown[i] = (KeysBitmask & (1UL << i)) != 0;
			state.MouseWheel = MouseWheel;
		}
	}

	/// <summary>
	/// Client → Server (unreliable). Sends local player input state for a tick.
	/// Key states are packed into a bitmask for compact transmission.
	/// </summary>
	public class InputStatePacket : Packet
	{
		public override PacketType Type => PacketType.InputState;

		/// <summary>Newest command first, followed by up to three older commands.</summary>
		public InputCommand[] Commands { get; set; } = Array.Empty<InputCommand>();

		// Compatibility accessors refer to the newest command.
		public int TickNumber
		{
			get => GetNewest().TickNumber;
			set
			{
				InputCommand command = GetNewest();
				command.TickNumber = value;
				SetNewest(command);
			}
		}
		public ulong KeysBitmask
		{
			get => GetNewest().KeysBitmask;
			set
			{
				InputCommand command = GetNewest();
				command.KeysBitmask = value;
				SetNewest(command);
			}
		}
		public Vector2 CameraAngle
		{
			get => GetNewest().CameraAngle;
			set
			{
				InputCommand command = GetNewest();
				command.CameraAngle = value;
				SetNewest(command);
			}
		}
		public float MouseWheel
		{
			get => GetNewest().MouseWheel;
			set
			{
				InputCommand command = GetNewest();
				command.MouseWheel = value;
				SetNewest(command);
			}
		}
		public bool NoClip
		{
			get => GetNewest().NoClip;
			set
			{
				InputCommand command = GetNewest();
				command.NoClip = value;
				SetNewest(command);
			}
		}

		/// <summary>
		/// Packs an <see cref="InputState"/> struct's key states into the bitmask.
		/// </summary>
		public unsafe void PackKeys(InputState state)
		{
			InputCommand newest = GetNewest();
			newest = InputCommand.FromState(
				newest.TickNumber,
				state,
				newest.CameraAngle,
				newest.NoClip
			);
			SetNewest(newest);
		}

		/// <summary>
		/// Unpacks the bitmask into an <see cref="InputState"/> struct's key states.
		/// </summary>
		public unsafe void UnpackKeys(ref InputState state)
		{
			GetNewest().UnpackKeys(ref state);
		}

		public override void Write(BinaryWriter writer)
		{
			if (Commands.Length is < 1 or > 4)
				throw new InvalidDataException("Input packets must contain one to four commands.");

			writer.Write((byte)Commands.Length);
			for (int i = 0; i < Commands.Length; i++)
			{
				writer.Write(Commands[i].TickNumber);
				writer.Write(Commands[i].KeysBitmask);
				writer.WriteVector2(Commands[i].CameraAngle);
				writer.Write(Commands[i].MouseWheel);
				writer.Write(Commands[i].NoClip);
			}
		}

		public override void Read(BinaryReader reader)
		{
			int count = reader.ReadByte();
			if (count is < 1 or > 4)
				throw new InvalidDataException($"Invalid input command count: {count}.");

			Commands = new InputCommand[count];
			for (int i = 0; i < count; i++)
			{
				Commands[i].TickNumber = reader.ReadInt32();
				Commands[i].KeysBitmask = reader.ReadUInt64();
				Commands[i].CameraAngle = reader.ReadVector2();
				Commands[i].MouseWheel = reader.ReadSingle();
				Commands[i].NoClip = reader.ReadBoolean();
			}
		}

		private InputCommand GetNewest() => Commands.Length == 0 ? default : Commands[0];

		private void SetNewest(InputCommand command)
		{
			if (Commands.Length == 0)
				Commands = new InputCommand[1];
			Commands[0] = command;
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
			public PlayerPhysicsState PhysicsState;

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
				WritePhysicsState(writer, Players[i].PhysicsState);
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
				Players[i].PhysicsState = ReadPhysicsState(reader);
			}
		}

		private static void WritePhysicsState(BinaryWriter writer, in PlayerPhysicsState state)
		{
			writer.WriteVector3(state.Position);
			writer.WriteVector3(state.Velocity);
			writer.Write(state.GroundGraceRemaining);
			writer.Write(state.JumpCooldownRemaining);
			writer.Write(state.RecentJumpRemaining);
			writer.Write(state.HeadBumpCooldownRemaining);
			writer.WriteVector3(state.LastWallNormal);
			writer.Write(state.WasGrounded);
			writer.Write(state.WasInWater);
			writer.Write(state.NoClip);
		}

		private static PlayerPhysicsState ReadPhysicsState(BinaryReader reader) => new(
			reader.ReadVector3(),
			reader.ReadVector3(),
			reader.ReadSingle(),
			reader.ReadSingle(),
			reader.ReadSingle(),
			reader.ReadSingle(),
			reader.ReadVector3(),
			reader.ReadBoolean(),
			reader.ReadBoolean(),
			reader.ReadBoolean()
		);
	}
}
