using System.Numerics;

namespace Voxelgine.Engine
{
	public unsafe partial class Player
	{
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
