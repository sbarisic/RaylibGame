using Raylib_cs;

using System;
using System.Numerics;

using Voxelgine.Graphics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// A door entity that slides into the wall when the player approaches.
	/// Collision is disabled when the door is open.
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
		public float SlideDistance = 1.0f;          // How far the door slides
		public float SlideSpeed = 3.0f;             // Units per second
		public Vector3 SlideDirection = Vector3.UnitY; // Direction to slide (default: up)
		public float OpenDelay = 0.5f;              // Time to stay open after player leaves trigger

		// Internal state
		float SlideProgress = 0f;                   // 0 = closed, 1 = fully open
		float OpenTimer = 0f;                       // Timer for open delay
		Vector3 ClosedPosition;                     // Original position when closed
		bool CollisionEnabled = true;

		public VEntSlidingDoor() : base()
		{
			IsRotating = false;
		}

		/// <summary>
		/// Initialize the door with position and slide direction.
		/// </summary>
		/// <param name="position">Door position when closed</param>
		/// <param name="size">Door size for collision</param>
		/// <param name="slideDirection">Direction the door slides (e.g., Vector3.UnitY for up)</param>
		/// <param name="slideDistance">How far to slide</param>
		public void Initialize(Vector3 position, Vector3 size, Vector3 slideDirection, float slideDistance = 1.0f)
		{
			Position = position;
			ClosedPosition = position;
			Size = size;
			SlideDirection = Vector3.Normalize(slideDirection);
			SlideDistance = slideDistance;
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
					}
					break;

				case DoorState.Opening:
					SlideProgress += SlideSpeed * Dt / SlideDistance;
					if (SlideProgress >= 1.0f)
					{
						SlideProgress = 1.0f;
						State = DoorState.Open;
						CollisionEnabled = false;
					}
					UpdateDoorPosition();
					break;

				case DoorState.Open:
					CollisionEnabled = false;
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
						OpenTimer = 0f; // Reset timer if player is still in range
					}
					break;

				case DoorState.Closing:
					if (playerInRange)
					{
						// Player re-entered, open again
						State = DoorState.Opening;
						break;
					}
					SlideProgress -= SlideSpeed * Dt / SlideDistance;
					if (SlideProgress <= 0f)
					{
						SlideProgress = 0f;
						State = DoorState.Closed;
						CollisionEnabled = true;
					}
					UpdateDoorPosition();
					break;
			}
		}

		void UpdateDoorPosition()
		{
			Position = ClosedPosition + SlideDirection * SlideDistance * SlideProgress;
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
