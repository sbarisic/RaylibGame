using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Voxelgine.Graphics;

namespace Voxelgine.Engine
{
	public unsafe partial class Player
	{
		// --- Player movement/physics fields ---
		Vector3 PlyVelocity = Vector3.Zero;
		bool WasLastLegsOnFloor = false;
		float GroundGraceTimer = 0f; // Coyote time for ground detection
		Vector3 LastWallNormal = Vector3.Zero;
		float HeadBumpCooldown = 0f; // Cooldown applied when hitting head shortly after jumping
		bool WasInWater = false; // Track previous frame's water state for exit boost

		float ClampToZero(float Num, float ClampHyst)
		{
			if (Num < 0 && Num > -ClampHyst)
				return 0;
			if (Num > 0 && Num < ClampHyst)
				return 0;
			return Num;
		}

		void ClampToZero(ref Vector3 Vec, float ClampHyst)
		{
			if (float.IsNaN(Vec.X))
				Vec.X = 0;
			if (float.IsNaN(Vec.Y))
				Vec.Y = 0;
			if (float.IsNaN(Vec.Z))
				Vec.Z = 0;
			Vec.X = ClampToZero(Vec.X, ClampHyst);
			Vec.Y = ClampToZero(Vec.Y, ClampHyst);
			Vec.Z = ClampToZero(Vec.Z, ClampHyst);
		}

		IEnumerable<Vector3> Phys_PlayerCollisionPointsImproved(Vector3 feetPos, float Radius = -1, float Height = -1)
		{
			if (Radius < 0)
				Radius = Player.PlayerRadius;
			if (Height < 0)
				Height = Player.PlayerHeight;
			int RadialDivs = 12;
			int HeightDivs = 4;
			for (int h = 0; h < HeightDivs; h++)
			{
				float heightRatio = (float)h / (HeightDivs - 1);
				float currentHeight = heightRatio * Height;
				for (int i = 0; i < RadialDivs; i++)
				{
					float angle = (float)i / RadialDivs * 2.0f * MathF.PI;
					float x = MathF.Cos(angle) * Radius;
					float z = MathF.Sin(angle) * Radius;
					yield return new Vector3(feetPos.X + x, feetPos.Y + currentHeight, feetPos.Z + z);
				}
			}
			for (int h = 0; h < HeightDivs; h++)
			{
				float heightRatio = (float)h / (HeightDivs - 1);
				float currentHeight = heightRatio * Height;
				yield return new Vector3(feetPos.X, feetPos.Y + currentHeight, feetPos.Z);
			}
			yield return new Vector3(feetPos.X, feetPos.Y + Height, feetPos.Z);
			yield return new Vector3(feetPos.X, feetPos.Y, feetPos.Z);
		}

		/// <summary>
		/// Finds the collision normal for an AABB at the given position.
		/// Returns the primary axis of penetration.
		/// Prioritizes horizontal (wall) collisions over vertical to preserve jump velocity.
		/// </summary>
		Vector3 FindCollisionNormal(ChunkMap Map, Vector3 feetPos, Vector3 move, float playerRadius, float playerHeight)
		{
			// Test each axis to find which one is blocked
			Vector3 testX = new Vector3(feetPos.X + move.X, feetPos.Y, feetPos.Z);
			Vector3 testY = new Vector3(feetPos.X, feetPos.Y + move.Y, feetPos.Z);
			Vector3 testZ = new Vector3(feetPos.X, feetPos.Y, feetPos.Z + move.Z);

			bool blockedX = Map.HasBlocksInBoundsMinMax(
				testX - new Vector3(playerRadius, 0, playerRadius),
				testX + new Vector3(playerRadius, playerHeight, playerRadius));
			bool blockedY = Map.HasBlocksInBoundsMinMax(
				testY - new Vector3(playerRadius, 0, playerRadius),
				testY + new Vector3(playerRadius, playerHeight, playerRadius));
			bool blockedZ = Map.HasBlocksInBoundsMinMax(
				testZ - new Vector3(playerRadius, 0, playerRadius),
				testZ + new Vector3(playerRadius, playerHeight, playerRadius));

			// Prioritize horizontal collisions (walls) to preserve vertical velocity during jumps
			// Only return one axis at a time to prevent multi-axis clipping that kills jump velocity

			// First, handle horizontal (wall) collisions - these should NOT affect Y velocity
			if (blockedX && MathF.Abs(move.X) > MathF.Abs(move.Z) && MathF.Abs(move.X) > 0.0001f)
			{
				return new Vector3(-MathF.Sign(move.X), 0, 0);
			}
			if (blockedZ && MathF.Abs(move.Z) > 0.0001f)
			{
				return new Vector3(0, 0, -MathF.Sign(move.Z));
			}
			if (blockedX && MathF.Abs(move.X) > 0.0001f)
			{
				return new Vector3(-MathF.Sign(move.X), 0, 0);
			}

			// Only return Y normal if no horizontal collision and Y is actually blocked
			if (blockedY && MathF.Abs(move.Y) > 0.0001f)
			{
				return new Vector3(0, -MathF.Sign(move.Y), 0);
			}

			return Vector3.Zero;
		}

		private Vector3 QuakeMoveWithCollision(ChunkMap Map, Vector3 pos, Vector3 velocity, float dt, float stepHeight = 0.5f, int maxSlides = 4, bool onGround = false)
		{
			float playerRadius = Player.PlayerRadius;
			float playerHeight = Player.PlayerHeight;
			Vector3 feetPos = FeetPosition;
			Vector3 originalVelocity = velocity;
			Vector3 primalVelocity = velocity;
			Vector3 move = velocity * dt;
			LastWallNormal = Vector3.Zero;

			Vector3[] planes = new Vector3[5];
			int numPlanes = 0;

			// Only add ground plane if moving downward (not when jumping up)
			if (onGround && velocity.Y <= 0)
			{
				planes[numPlanes++] = Vector3.UnitY;
			}

			float timeLeft = dt;

			for (int slide = 0; slide < maxSlides && timeLeft > 0; slide++)
			{
				Vector3 endPos = feetPos + velocity * timeLeft;

				// Check if move is clear
				if (!Map.HasBlocksInBoundsMinMax(
					endPos - new Vector3(playerRadius, 0, playerRadius),
					endPos + new Vector3(playerRadius, playerHeight, playerRadius)))
				{
					feetPos = endPos;
					break;
				}

				// Try step up if on ground
				if (onGround && slide == 0)
				{
					Vector3 stepUp = feetPos + new Vector3(0, stepHeight, 0);
					Vector3 stepEnd = stepUp + velocity * timeLeft;
					if (!Map.HasBlocksInBoundsMinMax(
						stepEnd - new Vector3(playerRadius, 0, playerRadius),
						stepEnd + new Vector3(playerRadius, playerHeight, playerRadius)))
					{
						feetPos = stepEnd;
						break;
					}
				}

				// Find collision normal
				Vector3 normal = FindCollisionNormal(Map, feetPos, velocity * timeLeft, playerRadius, playerHeight);

				if (normal == Vector3.Zero)
				{
					// Stuck - try to nudge out
					break;
				}

				// Store wall normal for air control
				if (MathF.Abs(normal.Y) < 0.5f)
				{
					LastWallNormal = normal;
				}

				// Clip velocity against this plane using shared PhysicsUtils
				velocity = PhysicsUtils.ClipVelocity(velocity, normal);

				// Check if velocity is now moving into a previous plane
				for (int i = 0; i < numPlanes; i++)
				{
					if (Vector3.Dot(velocity, planes[i]) < 0)
					{
						// Clip against the previous plane too
						velocity = PhysicsUtils.ClipVelocity(velocity, planes[i]);
					}
				}

				// Add this plane to the list
				if (numPlanes < planes.Length)
				{
					planes[numPlanes++] = normal;
				}

				// Calculate how much of the move we completed (approximate)
				float moveFraction = 0.1f; // Small step to avoid getting stuck
				feetPos += velocity * timeLeft * moveFraction;
				timeLeft *= (1.0f - moveFraction);

				// Check if we're not moving anymore
				if (velocity.LengthSquared() < 0.0001f)
				{
					break;
				}
			}

			// Update the player's velocity to the clipped version
			PlyVelocity = velocity;

			return feetPos + new Vector3(0, Player.PlayerEyeOffset, 0);
		}

		void NoclipMove(PhysData PhysicsData, float Dt, InputMgr InMgr)
		{
			Vector3 move = Vector3.Zero;
			Vector3 fwd = GetForward();
			Vector3 lft = GetLeft();
			Vector3 up = GetUp();

			if (InMgr.IsInputDown(InputKey.W))
				move += fwd;

			if (InMgr.IsInputDown(InputKey.S))
				move -= fwd;

			if (InMgr.IsInputDown(InputKey.A))
				move += lft;

			if (InMgr.IsInputDown(InputKey.D))
				move -= lft;

			if (InMgr.IsInputDown(InputKey.Space))
				move += up;

			if (InMgr.IsInputDown(InputKey.Shift))
				move -= up;

			if (move != Vector3.Zero)
			{
				move = Vector3.Normalize(move) * PhysicsData.NoClipMoveSpeed * Dt;
				SetPosition(Position + move);
			}
		}

		/// <summary>
		/// Quake-style swimming physics. Player can move in all directions while in water.
		/// Space to swim up, Shift to swim down, WASD moves in look direction.
		/// Reduced gravity, water friction applied.
		/// </summary>
		void UpdateSwimmingPhysics(ChunkMap Map, PhysData PhysicsData, float Dt, InputMgr InMgr, bool headInWater)
		{
			Vector3 feetPos = FeetPosition;

			ClampToZero(ref PlyVelocity, PhysicsData.ClampHyst);

			// Get movement direction in 3D (swimming allows vertical movement based on look direction)
			Vector3 wishdir = Vector3.Zero;
			Vector3 fwd = GetForward(); // Full 3D forward (includes pitch)
			Vector3 lft = GetLeft();

			if (InMgr.IsInputDown(InputKey.W))
				wishdir += fwd;

			if (InMgr.IsInputDown(InputKey.S))
				wishdir -= fwd;

			if (InMgr.IsInputDown(InputKey.A))
				wishdir += lft;

			if (InMgr.IsInputDown(InputKey.D))
				wishdir -= lft;

			// Vertical swimming controls
			if (InMgr.IsInputDown(InputKey.Space))
				wishdir += Vector3.UnitY;

			if (InMgr.IsInputDown(InputKey.Shift))
				wishdir -= Vector3.UnitY;

			if (wishdir != Vector3.Zero)
				wishdir = Vector3.Normalize(wishdir);

			// --- Apply water friction ---
			PhysicsUtils.ApplyFriction(ref PlyVelocity, PhysicsData.WaterFriction, Dt);

			// --- Apply swimming acceleration ---
			if (wishdir != Vector3.Zero)
			{
				PhysicsUtils.Accelerate(ref PlyVelocity, wishdir, PhysicsData.MaxWaterSpeed, PhysicsData.WaterAccel, Dt);
			}

			// --- Apply reduced gravity (sink slowly if not actively swimming) ---
			bool activelySwimming = wishdir != Vector3.Zero;

			// --- Play swimming sound when actively moving in water ---
			if (activelySwimming && LegTimer.ElapsedMilliseconds > LastSwimSound + 600)
			{
				LastSwimSound = LegTimer.ElapsedMilliseconds;
				Vector3 Fwd = FPSCamera.GetForward();
				Snd.PlayCombo("swim", FPSCamera.Position, Fwd, Position);
			}

			// --- Apply buoyancy and gravity in water ---
			if (headInWater)
			{
				// When fully submerged: apply buoyancy (upward force) countered by gentle sinking
				// Net effect: player floats slowly upward or stays neutral
				float netBuoyancy = PhysicsData.WaterBuoyancy - PhysicsData.WaterSinkSpeed;
				PlyVelocity.Y += netBuoyancy * Dt;

				// Dampen vertical velocity more when not actively swimming (water resistance)
				if (!activelySwimming)
				{
					PlyVelocity.Y *= (1.0f - PhysicsData.WaterFriction * 0.5f * Dt);
				}
			}
			else
			{
				// Head above water - apply normal gravity but reduced
				PhysicsUtils.ApplyGravity(ref PlyVelocity, PhysicsData.WaterGravity, Dt);
			}

			// --- Check if player can jump out of water (head near surface) ---
			Vector3 surfaceCheck = Position + new Vector3(0, 0.5f, 0);
			bool nearSurface = !Map.IsWaterAt(surfaceCheck);
			if (nearSurface && InMgr.IsInputDown(InputKey.Space))
			{
				// Jump out of water
				PlyVelocity.Y = MathF.Max(PlyVelocity.Y, PhysicsData.WaterJumpImpulse);
			}

			// --- Cap swimming speed ---
			float currentSpeed = PlyVelocity.Length();
			if (currentSpeed > PhysicsData.MaxWaterSpeed * 1.5f)
			{
				PlyVelocity = Vector3.Normalize(PlyVelocity) * PhysicsData.MaxWaterSpeed * 1.5f;
			}

			// --- Move and collide (no step-up in water) ---
			Vector3 newPos = QuakeMoveWithCollision(Map, Position, PlyVelocity, Dt, 0f, 4, false);

			if (newPos != Position)
			{
				SetPosition(newPos);
			}

			// Reset ground state when in water
			WasLastLegsOnFloor = false;
			GroundGraceTimer = 0;
		}

		public void UpdatePhysics(ChunkMap Map, PhysData PhysicsData, float Dt, InputMgr InMgr)
		{
			const float GroundHitBelowFeet = -0.075f;
			float playerHeight = Player.PlayerHeight;
			float playerRadius = Player.PlayerRadius;

			if (NoClip)
			{
				NoclipMove(PhysicsData, Dt, InMgr);
				return;
			}

			if (!Utils.HasRecord())
				Utils.BeginRaycastRecord();

			ClampToZero(ref PlyVelocity, PhysicsData.ClampHyst);
			Vector3 feetPos = FeetPosition;

			// --- Check if player is in water (check at eye level and feet level) ---
			bool inWater = Map.IsWaterAt(Position) || Map.IsWaterAt(feetPos + new Vector3(0, playerHeight * 0.5f, 0));
			bool headInWater = Map.IsWaterAt(Position);

			// --- Water exit boost: apply 15% velocity boost when exiting water with upward velocity ---
			if (WasInWater && !inWater && PlyVelocity.Y > 0)
			{
				PlyVelocity.Y *= 1.15f;
			}
			WasInWater = inWater;

			if (inWater)
			{
				UpdateSwimmingPhysics(Map, PhysicsData, Dt, InMgr, headInWater);
				return;
			}

			Vector3[] groundCheckPoints = new Vector3[] {
				new Vector3(feetPos.X - playerRadius, feetPos.Y + PhysicsData.GroundEpsilon, feetPos.Z - playerRadius),
				new Vector3(feetPos.X + playerRadius, feetPos.Y + PhysicsData.GroundEpsilon, feetPos.Z - playerRadius),
				new Vector3(feetPos.X - playerRadius, feetPos.Y + PhysicsData.GroundEpsilon, feetPos.Z + playerRadius),
				new Vector3(feetPos.X + playerRadius, feetPos.Y + PhysicsData.GroundEpsilon, feetPos.Z + playerRadius),
				feetPos + new Vector3(0, PhysicsData.GroundEpsilon, 0)
			};

			bool OnGround = false;
			Vector3 HitFloor = Vector3.Zero;

			foreach (var pt in groundCheckPoints)
			{
				Vector3 localFace;
				Vector3 hit = Map.RaycastPos(pt, PhysicsData.GroundCheckDist, new Vector3(0, -1f, 0), out localFace);

				if (hit != Vector3.Zero && localFace.Y > 0.99f && Math.Abs(localFace.X) < 0.05f && Math.Abs(localFace.Z) < 0.05f && PlyVelocity.Y <= 0 && hit.Y < feetPos.Y + GroundHitBelowFeet)
				{
					OnGround = true;
					HitFloor = hit;
					break;
				}
			}
			if (!OnGround)
			{
				foreach (var pt in groundCheckPoints)
				{
					Vector3 TestPoint = pt + PlyVelocity * Dt;

					if (Map.Collide(TestPoint, new Vector3(0, -1, 0), out Vector3 PicNorm))
					{
						if (PicNorm.Y > 0.99f && Math.Abs(PicNorm.X) < 0.05f && Math.Abs(PicNorm.Z) < 0.05f && PlyVelocity.Y <= 0 && TestPoint.Y < feetPos.Y + GroundHitBelowFeet)
						{
							OnGround = true;
							HitFloor = TestPoint;
							break;
						}
					}
				}
			}
			if (OnGround)
			{
				GroundGraceTimer = 0.1f;
			}
			else
			{
				GroundGraceTimer -= Dt;
				if (GroundGraceTimer < 0)
					GroundGraceTimer = 0;
			}

			bool OnGroundGrace = GroundGraceTimer > 0f;
			Vector3 wishdir = Vector3.Zero;

			// Flatten forward direction to horizontal plane for movement (prevents climbing walls by looking up)
			Vector3 fwd2 = GetForward();
			fwd2.Y = 0;
			if (fwd2.LengthSquared() > 0.0001f)
				fwd2 = Vector3.Normalize(fwd2);
			else
				fwd2 = Vector3.UnitX; // Fallback when looking straight up/down

			// Left is already horizontal (perpendicular to up vector)
			Vector3 lft2 = GetLeft();
			lft2.Y = 0;
			if (lft2.LengthSquared() > 0.0001f)
				lft2 = Vector3.Normalize(lft2);

			if (InMgr.IsInputDown(InputKey.W))
				wishdir += fwd2;

			if (InMgr.IsInputDown(InputKey.S))
				wishdir -= fwd2;

			if (InMgr.IsInputDown(InputKey.A))
				wishdir += lft2;

			if (InMgr.IsInputDown(InputKey.D))
				wishdir -= lft2;

			if (wishdir != Vector3.Zero)
				wishdir = Vector3.Normalize(wishdir);

			bool ledgeSafety = OnGroundGrace && InMgr.IsInputDown(InputKey.Shift);
			if (ledgeSafety && wishdir != Vector3.Zero)
			{
				float innerRadius = 0.4f;
				var points = Phys_PlayerCollisionPointsImproved(feetPos, innerRadius, Player.PlayerHeight).ToArray();
				float minY = points.Min(p => p.Y);
				var feetPoints = points.Where(p => Math.Abs(p.Y - minY) < 0.01f).ToArray();

				List<Vector3> supportedPoints = new();
				foreach (var pt in feetPoints)
				{
					Vector3 groundCheck = pt + new Vector3(0, -0.15f, 0);
					if (Map.GetBlock((int)MathF.Floor(groundCheck.X), (int)MathF.Floor(groundCheck.Y), (int)MathF.Floor(groundCheck.Z)) != BlockType.None)
					{
						supportedPoints.Add(pt);
					}
				}

				if (supportedPoints.Count == 0)
				{
					PlyVelocity.X = 0;
					PlyVelocity.Z = 0;
					wishdir = Vector3.Zero;
				}
				else
				{
					bool allow = false;

					foreach (var spt in supportedPoints)
					{
						Vector3 toSupport = Vector3.Normalize(spt - feetPos);
						if (Vector3.Dot(wishdir, toSupport) > 0)
						{
							allow = true;
							break;
						}
					}

					if (!allow)
					{
						PlyVelocity.X = 0;
						PlyVelocity.Z = 0;
						wishdir = Vector3.Zero;
					}
				}
			}

			float VelLen = PlyVelocity.Length();

			if (OnGroundGrace)
			{
				if (!WasLastLegsOnFloor)
				{
					WasLastLegsOnFloor = true;
					this.PhysicsHit(Position, VelLen, false, true, false, false);
				}
				else if (VelLen >= (PhysicsData.MaxGroundSpeed / 2))
				{
					this.PhysicsHit(HitFloor, VelLen, false, true, true, false);
				}
			}
			else
			{
				WasLastLegsOnFloor = false;
			}

			// --- Apply friction BEFORE acceleration (Quake order) ---
			if (OnGroundGrace)
			{
				PhysicsUtils.ApplyFriction(ref PlyVelocity, PhysicsData.GroundFriction, Dt);
			}

			// --- Update head bump cooldown ---
			if (HeadBumpCooldown > 0)
				HeadBumpCooldown -= Dt;

			// --- Jumping (before movement for bunny hop) ---
			bool canJump = HeadBumpCooldown <= 0 && JumpCounter.ElapsedMilliseconds > 50;
			if (InMgr.IsInputDown(InputKey.Space) && OnGroundGrace && canJump)
			{
				JumpCounter.Restart();
				PlyVelocity.Y = PhysicsData.JumpImpulse;
				this.PhysicsHit(HitFloor, VelLen, false, false, false, true);
				GroundGraceTimer = 0;
				OnGroundGrace = false; // Immediately in air after jump
			}

			// --- Apply acceleration ---
			if (wishdir != Vector3.Zero)
			{
				float wishspeed = InMgr.IsInputDown(InputKey.Shift) ? PhysicsData.MaxWalkSpeed : PhysicsData.MaxGroundSpeed;

				if (OnGroundGrace)
				{
					// Ground acceleration - standard Quake ground move
					PhysicsUtils.Accelerate(ref PlyVelocity, wishdir, wishspeed, PhysicsData.GroundAccel, Dt);
				}
				else
				{
					// Air acceleration - key for strafe jumping
					// Wall sliding adjustment
					Vector3 accelDir = wishdir;
					if (LastWallNormal != Vector3.Zero)
					{
						accelDir -= Vector3.Dot(accelDir, LastWallNormal) * LastWallNormal;
						if (accelDir.LengthSquared() > 1e-4f)
							accelDir = Vector3.Normalize(accelDir);
						else
							accelDir = Vector3.Zero;
					}

					if (accelDir != Vector3.Zero)
					{
						PhysicsUtils.AirAccelerate(ref PlyVelocity, accelDir, wishspeed, PhysicsData.AirAccel, Dt);
					}
				}
			}

			// --- Gravity ---
			if (!OnGroundGrace)
			{
				PhysicsUtils.ApplyGravity(ref PlyVelocity, PhysicsData.Gravity, Dt);
			}
			else if (PlyVelocity.Y < 0)
			{
				PlyVelocity.Y = 0;
			}

			// --- Cap ground speed only (NOT air speed - allows bunny hopping) ---
			if (OnGroundGrace)
			{
				float maxSpeed = InMgr.IsInputDown(InputKey.Shift) ? PhysicsData.MaxWalkSpeed : PhysicsData.MaxGroundSpeed;
				float horizSpeed = MathF.Sqrt(PlyVelocity.X * PlyVelocity.X + PlyVelocity.Z * PlyVelocity.Z);
				if (horizSpeed > maxSpeed)
				{
					float scale = maxSpeed / horizSpeed;
					PlyVelocity.X *= scale;
					PlyVelocity.Z *= scale;
				}
			}

			// --- Move and collide ---
			float stepHeight = OnGroundGrace ? 0.5f : 0.0f;
			float preMovePlyVelocityY = PlyVelocity.Y; // Save Y velocity before collision may clip it
			Vector3 newPos = QuakeMoveWithCollision(Map, Position, PlyVelocity, Dt, stepHeight, 4, OnGroundGrace);

			if (newPos != Position)
			{
				SetPosition(newPos);
			}
			// Note: PlyVelocity is now updated inside QuakeMoveWithCollision via ClipVelocity

			// --- Head collision detection ---
			// If player was moving upward and velocity was clipped to 0 or less, they hit a ceiling
			if (preMovePlyVelocityY > 0.1f && PlyVelocity.Y <= 0)
			{
				// Apply 0.5s jump cooldown if head was hit within 0.6s of jumping
				if (JumpCounter.ElapsedMilliseconds < 600)
				{
					HeadBumpCooldown = 0.5f;
				}
			}

			Utils.EndRaycastRecord();
		}

		// Add accessors for velocity and ground state
		public Vector3 GetVelocity() => PlyVelocity;
		public bool GetWasLastLegsOnFloor() => WasLastLegsOnFloor;

		/// <summary>
		/// Handles physics hit events (footsteps, landing, jumping sounds).
		/// </summary>
		public void PhysicsHit(Vector3 Pos, float Force, bool Side, bool Feet, bool Walk, bool Jump)
		{
			Vector3 Fwd = FPSCamera.GetForward();
			if (Walk)
			{
				if (LegTimer.ElapsedMilliseconds > LastWalkSound + 350)
				{
					LastWalkSound = LegTimer.ElapsedMilliseconds;
					Snd.PlayCombo("walk", FPSCamera.Position, Fwd, Pos);
				}
			}
			else if (Jump)
			{
				if (LegTimer.ElapsedMilliseconds > LastJumpSound + 350)
				{
					LastJumpSound = LegTimer.ElapsedMilliseconds;
					Snd.PlayCombo("jump", FPSCamera.Position, Fwd, Pos);
				}
			}
			else if (Feet && !Side)
			{
				if (LegTimer.ElapsedMilliseconds > LastCrashSound + 350)
				{
					LastCrashSound = LegTimer.ElapsedMilliseconds;
					Snd.PlayCombo("crash1", FPSCamera.Position, Fwd, Pos);
				}
			}
		}
	}
}
