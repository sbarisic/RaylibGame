using System.Numerics;

namespace Voxelgine.Engine
{
	public unsafe partial class Player
	{
		/// <summary>
		/// Writes a compact network snapshot of this player's state for multiplayer synchronization.
		/// Contains position, velocity, camera angle, animation state, and weapon index.
		/// </summary>
		public void WriteSnapshot(System.IO.BinaryWriter writer, int serverTick)
		{
			writer.Write(serverTick);

			// Position (12 bytes)
			writer.Write(Position.X);
			writer.Write(Position.Y);
			writer.Write(Position.Z);

			// Velocity (12 bytes)
			Vector3 vel = GetVelocity();
			writer.Write(vel.X);
			writer.Write(vel.Y);
			writer.Write(vel.Z);

			// Camera angle (12 bytes)
			Vector3 camAngle = GetCamAngle();
			writer.Write(camAngle.X);
			writer.Write(camAngle.Y);
			writer.Write(camAngle.Z);

			// Animation state (1 byte) - placeholder for future remote player animations
			writer.Write((byte)0);

			// Weapon index (1 byte)
			writer.Write((byte)GetSelectedInventoryIndex());
		}

		/// <summary>
		/// Reads a network snapshot and applies it to this player's state.
		/// Returns the server tick number from the snapshot for ordering/reconciliation.
		/// </summary>
		public int ReadSnapshot(System.IO.BinaryReader reader)
		{
			int serverTick = reader.ReadInt32();

			// Position
			SetPosition(new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));

			// Velocity
			SetVelocity(new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));

			// Camera angle
			SetCamAngle(new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));

			// Animation state (reserved for future use)
			reader.ReadByte();

			// Weapon index
			SetSelectedInventoryIndex(reader.ReadByte());

			return serverTick;
		}

		public void Write(System.IO.BinaryWriter writer)
		{
			// Write position
			writer.Write(Position.X);
			writer.Write(Position.Y);
			writer.Write(Position.Z);

			Vector3 CamAngle = GetCamAngle();
			// Write camera angle
			writer.Write(CamAngle.X);
			writer.Write(CamAngle.Y);
			writer.Write(CamAngle.Z);
			// Write camera (just position and target for now)
			writer.Write(Cam.Position.X);
			writer.Write(Cam.Position.Y);
			writer.Write(Cam.Position.Z);
			writer.Write(Cam.Target.X);
			writer.Write(Cam.Target.Y);
			writer.Write(Cam.Target.Z);
			// Write previous position
			writer.Write(PreviousPosition.X);
			writer.Write(PreviousPosition.Y);
			writer.Write(PreviousPosition.Z);
			// Write cursor state
			writer.Write(CursorDisabled);
		}

		public void Read(System.IO.BinaryReader reader)
		{
			// Read position
			SetPosition(new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()));
			// Read camera angle
			Vector3 CamAngle = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			SetCamAngle(CamAngle);

			// Read camera
			Cam.Position.X = reader.ReadSingle();
			Cam.Position.Y = reader.ReadSingle();
			Cam.Position.Z = reader.ReadSingle();
			Cam.Target.X = reader.ReadSingle();
			Cam.Target.Y = reader.ReadSingle();
			Cam.Target.Z = reader.ReadSingle();
			// Read previous position
			PreviousPosition.X = reader.ReadSingle();
			PreviousPosition.Y = reader.ReadSingle();
			PreviousPosition.Z = reader.ReadSingle();
			// Read cursor state
			CursorDisabled = reader.ReadBoolean();
		}
	}
}
