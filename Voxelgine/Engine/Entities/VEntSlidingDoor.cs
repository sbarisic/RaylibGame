using Raylib_cs;

using System;
using System.IO;
using System.Numerics;

using Voxelgine.Graphics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// A door entity that rotates open around a vertical hinge when the player approaches.
	/// Collision is disabled when the door is not fully closed.
	/// </summary>
	public class VEntSlidingDoor : VoxEntity
	{
		// Door state
		public enum DoorState
		{
			Closed,
			Opening,
			Open,
			Closing
		}

		public DoorState State { get; private set; } = DoorState.Closed;

		// Configuration
		public float TriggerRadius = 3.0f;          // Distance at which door starts opening
		public float OpenAngleDeg = 90f;            // How far the door opens in degrees
		public float OpenSpeed = 2.0f;              // Progress per second (1.0 = full open)
		public float OpenDelay = 0.5f;              // Time to stay open after player leaves trigger

		/// <summary>Direction the door faces (used for model orientation and hinge placement).</summary>
		public Vector3 FacingDirection = Vector3.UnitZ;

		// JSON model rendering
		CustomModel CModel;
		static Texture2D? _doorTexture;

		// Internal state
		float OpenProgress = 0f;                    // 0 = closed, 1 = fully open
		float OpenTimer = 0f;                       // Timer for open delay
		Vector3 ClosedPosition;                     // Original position when closed
		bool CollisionEnabled = true;

		public VEntSlidingDoor() : base()
		{
			IsRotating = false;
		}

		/// <summary>
		/// Initialize the door with position and size.
		/// </summary>
		/// <param name="position">Door position when closed</param>
		/// <param name="size">Door size for collision</param>
		/// <param name="openAngleDeg">Maximum opening angle in degrees</param>
		public void Initialize(Vector3 position, Vector3 size, float openAngleDeg = 90f)
		{
			Position = position;
			ClosedPosition = position;
			Size = size;
			OpenAngleDeg = openAngleDeg;
		}

		public override void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr)
		{
			base.UpdateLockstep(TotalTime, Dt, InMgr);

			GameSimulation sim = GetSimulation();
				if (sim?.LocalPlayer == null)
					return;

				// Check distance to player
				Vector3 doorCenter = Position + Size * 0.5f;
				Vector3 playerPos = sim.LocalPlayer.Position;
			float distanceToPlayer = Vector3.Distance(doorCenter, playerPos);
			bool playerInRange = distanceToPlayer <= TriggerRadius;

			// State machine for door
			switch (State)
			{
				case DoorState.Closed:
					if (playerInRange)
					{
						State = DoorState.Opening;
						CollisionEnabled = false;
					}
					break;

				case DoorState.Opening:
					OpenProgress += OpenSpeed * Dt;
					if (OpenProgress >= 1.0f)
					{
						OpenProgress = 1.0f;
						State = DoorState.Open;
					}
					break;

				case DoorState.Open:
					if (!playerInRange)
					{
						OpenTimer += Dt;
						if (OpenTimer >= OpenDelay)
						{
							State = DoorState.Closing;
							OpenTimer = 0f;
						}
					}
					else
					{
						OpenTimer = 0f;
					}
					break;

				case DoorState.Closing:
					if (playerInRange)
					{
						State = DoorState.Opening;
						break;
					}
					OpenProgress -= OpenSpeed * Dt;
					if (OpenProgress <= 0f)
					{
						OpenProgress = 0f;
						State = DoorState.Closed;
						CollisionEnabled = true;
					}
					break;
			}
		}

		/// <summary>
		/// Returns whether the door should block movement.
		/// </summary>
		public bool IsCollisionEnabled()
		{
			return CollisionEnabled;
		}

		/// <summary>
		/// Get the AABB for collision checking (only valid when collision is enabled).
		/// </summary>
		public AABB GetCollisionAABB()
		{
			if (!CollisionEnabled)
				return AABB.Empty;
			return new AABB(Position, Position + Size);
		}

		protected override void WriteSnapshotExtra(BinaryWriter writer)
		{
			writer.Write((byte)State);
			writer.Write(OpenProgress);
		}

		protected override void ReadSnapshotExtra(BinaryReader reader)
		{
			State = (DoorState)reader.ReadByte();
			OpenProgress = reader.ReadSingle();
			CollisionEnabled = State == DoorState.Closed;
		}

		protected override void WriteSpawnPropertiesExtra(BinaryWriter writer)
		{
			writer.Write(OpenAngleDeg);
			writer.Write(TriggerRadius);
			writer.Write(ClosedPosition.X);
			writer.Write(ClosedPosition.Y);
			writer.Write(ClosedPosition.Z);
			writer.Write(FacingDirection.X);
			writer.Write(FacingDirection.Y);
			writer.Write(FacingDirection.Z);
		}

		protected override void ReadSpawnPropertiesExtra(BinaryReader reader)
		{
			OpenAngleDeg = reader.ReadSingle();
			TriggerRadius = reader.ReadSingle();
			ClosedPosition = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			FacingDirection = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
		}

		public override void SetModel(string MdlName)
		{
			HasModel = false;
			ModelOffset = Vector3.Zero;
			CenterOffset = Vector3.Zero;
			ModelRotationDeg = 0;
			ModelColor = Color.White;
			ModelScale = Vector3.One;

			EntModelName = MdlName;
			MinecraftModel JMdl = ResMgr.GetJsonModel(MdlName);
			CModel = MeshGenerator.Generate(JMdl);

			if (_doorTexture == null || _doorTexture.Value.Id == 0)
				_doorTexture = GenerateDoorTexture();
			CModel.SetTexture(_doorTexture.Value);

			HasModel = true;

			if (Size != Vector3.Zero)
				ModelOffset = new Vector3(Size.X / 2, 0, Size.Z / 2);
		}

		protected override void EntityDrawModel(float TimeAlpha, ref GameFrameInfo LastFrame)
		{
			if (!HasModel)
				return;

			float hingeAngle = -OpenProgress * Utils.ToRad(OpenAngleDeg);

			// Facing rotation (same formula as CustomModel.GetModelMatrix)
			Vector2 dir = Vector2.Normalize(new Vector2(FacingDirection.X, FacingDirection.Z));
			float facingAngle = MathF.Atan2(dir.X, dir.Y) + Utils.ToRad(-180);

			// Build hinge rotation matrix:
			// 1. Translate so the hinge edge (model x=+0.5) is at the Y axis
			// 2. Rotate around Y by the hinge angle
			// 3. Translate the hinge edge back
			// 4. Apply facing rotation
			// 5. Translate to world position
			Matrix4x4 mat = Matrix4x4.CreateTranslation(-0.5f, 0, 0)
				* Matrix4x4.CreateRotationY(hingeAngle)
				* Matrix4x4.CreateTranslation(0.5f, 0, 0)
				* Matrix4x4.CreateRotationY(facingAngle)
				* Matrix4x4.CreateTranslation(GetDrawPosition());

			CModel.DrawWithMatrix(mat);
		}

		static Texture2D GenerateDoorTexture()
		{
			Color woodBase = new Color(139, 90, 43, 255);
			Color woodDark = new Color(101, 67, 33, 255);
			Color woodLight = new Color(160, 110, 60, 255);
			Color handle = new Color(200, 170, 50, 255);

			Image img = Raylib.GenImageColor(64, 64, woodBase);

			// Front face (UV area 0-16, 0-32): frame borders and panels
			Raylib.ImageDrawRectangle(ref img, 0, 0, 16, 1, woodDark);
			Raylib.ImageDrawRectangle(ref img, 0, 31, 16, 1, woodDark);
			Raylib.ImageDrawRectangle(ref img, 0, 0, 1, 32, woodDark);
			Raylib.ImageDrawRectangle(ref img, 15, 0, 1, 32, woodDark);
			Raylib.ImageDrawRectangle(ref img, 0, 15, 16, 2, woodDark);
			Raylib.ImageDrawRectangle(ref img, 2, 2, 12, 12, woodLight);
			Raylib.ImageDrawRectangle(ref img, 2, 18, 12, 12, woodLight);
			Raylib.ImageDrawRectangle(ref img, 12, 14, 2, 3, handle);

			// Back face (UV area 18-34, 0-32): matching panels
			Raylib.ImageDrawRectangle(ref img, 18, 0, 16, 1, woodDark);
			Raylib.ImageDrawRectangle(ref img, 18, 31, 16, 1, woodDark);
			Raylib.ImageDrawRectangle(ref img, 18, 0, 1, 32, woodDark);
			Raylib.ImageDrawRectangle(ref img, 33, 0, 1, 32, woodDark);
			Raylib.ImageDrawRectangle(ref img, 18, 15, 16, 2, woodDark);
			Raylib.ImageDrawRectangle(ref img, 20, 2, 12, 12, woodLight);
			Raylib.ImageDrawRectangle(ref img, 20, 18, 12, 12, woodLight);

			// Side edges (UV areas 16-18 and 34-36, 0-32)
			Raylib.ImageDrawRectangle(ref img, 16, 0, 2, 32, woodDark);
			Raylib.ImageDrawRectangle(ref img, 34, 0, 2, 32, woodDark);

			Texture2D tex = Raylib.LoadTextureFromImage(img);
			Raylib.SetTextureFilter(tex, TextureFilter.Point);
			Raylib.UnloadImage(img);
			return tex;
		}

		protected override void DrawCollisionBox()
		{
			if (!Eng.DebugMode)
				return;

			Vector3 min = Position;
			Vector3 max = Position + Size;
			Color color = CollisionEnabled ? Color.Red : Color.Green;

			Vector3[] corners = new Vector3[8];
			corners[0] = new Vector3(min.X, min.Y, min.Z);
			corners[1] = new Vector3(max.X, min.Y, min.Z);
			corners[2] = new Vector3(max.X, min.Y, max.Z);
			corners[3] = new Vector3(min.X, min.Y, max.Z);
			corners[4] = new Vector3(min.X, max.Y, min.Z);
			corners[5] = new Vector3(max.X, max.Y, min.Z);
			corners[6] = new Vector3(max.X, max.Y, max.Z);
			corners[7] = new Vector3(min.X, max.Y, max.Z);

			Raylib.DrawLine3D(corners[0], corners[1], color);
			Raylib.DrawLine3D(corners[1], corners[2], color);
			Raylib.DrawLine3D(corners[2], corners[3], color);
			Raylib.DrawLine3D(corners[3], corners[0], color);
			Raylib.DrawLine3D(corners[4], corners[5], color);
			Raylib.DrawLine3D(corners[5], corners[6], color);
			Raylib.DrawLine3D(corners[6], corners[7], color);
			Raylib.DrawLine3D(corners[7], corners[4], color);
			Raylib.DrawLine3D(corners[0], corners[4], color);
			Raylib.DrawLine3D(corners[1], corners[5], color);
			Raylib.DrawLine3D(corners[2], corners[6], color);
			Raylib.DrawLine3D(corners[3], corners[7], color);

			// Draw trigger radius in debug mode
			Raylib.DrawCircle3D(ClosedPosition + Size * 0.5f, TriggerRadius, Vector3.UnitX, 90, Color.Yellow);
		}
	}
}
