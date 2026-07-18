using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Voxelgine.Engine.DI;
using Voxelgine.Graphics;

namespace Voxelgine.Engine
{
	public unsafe partial class Player
	{
		private const float GroundGraceDuration = 0.1f;
		private const float JumpCooldownDuration = 0.05f;
		private const float RecentJumpDuration = 0.6f;
		private const float HeadBumpCooldownDuration = 0.5f;

		private Vector3 PlyVelocity = Vector3.Zero;
		private bool WasLastLegsOnFloor;
		private float GroundGraceTimer;
		private float JumpCooldownRemaining;
		private float RecentJumpRemaining;
		private Vector3 LastWallNormal = Vector3.Zero;
		private float HeadBumpCooldown;
		private bool WasInWater;

		private static float ClampToZero(float value, float hysteresis)
		{
			if (value < 0 && value > -hysteresis)
				return 0;
			if (value > 0 && value < hysteresis)
				return 0;
			return value;
		}

		private static void ClampToZero(ref Vector3 value, float hysteresis)
		{
			if (!float.IsFinite(value.X))
				value.X = 0;
			if (!float.IsFinite(value.Y))
				value.Y = 0;
			if (!float.IsFinite(value.Z))
				value.Z = 0;
			value.X = ClampToZero(value.X, hysteresis);
			value.Y = ClampToZero(value.Y, hysteresis);
			value.Z = ClampToZero(value.Z, hysteresis);
		}

		private IEnumerable<Vector3> GetLedgeSupportPoints(Vector3 feetPosition, float radius)
		{
			const int radialDivisions = 12;
			for (int index = 0; index < radialDivisions; index++)
			{
				float angle = index / (float)radialDivisions * 2f * MathF.PI;
				yield return feetPosition + new Vector3(MathF.Cos(angle) * radius, 0, MathF.Sin(angle) * radius);
			}
			yield return feetPosition;
		}

		public PlayerPhysicsState CapturePhysicsState()
		{
			return new PlayerPhysicsState(
				Position,
				PlyVelocity,
				GroundGraceTimer,
				JumpCooldownRemaining,
				RecentJumpRemaining,
				HeadBumpCooldown,
				LastWallNormal,
				WasLastLegsOnFloor,
				WasInWater
			);
		}

		public bool ApplyPhysicsState(in PlayerPhysicsState state)
		{
			if (!IsFinite(state.Position) ||
				!IsFinite(state.Velocity) ||
				!IsFinite(state.LastWallNormal) ||
				!float.IsFinite(state.GroundGraceRemaining) ||
				!float.IsFinite(state.JumpCooldownRemaining) ||
				!float.IsFinite(state.RecentJumpRemaining) ||
				!float.IsFinite(state.HeadBumpCooldownRemaining))
			{
				Logging.Log(GameLogLevel.Warning, "Physics", $"Rejected non-finite player physics state playerId={PlayerId}");
				return false;
			}

			SetPosition(state.Position);
			PlyVelocity = state.Velocity;
			GroundGraceTimer = MathF.Max(0f, state.GroundGraceRemaining);
			JumpCooldownRemaining = MathF.Max(0f, state.JumpCooldownRemaining);
			RecentJumpRemaining = MathF.Max(0f, state.RecentJumpRemaining);
			HeadBumpCooldown = MathF.Max(0f, state.HeadBumpCooldownRemaining);
			LastWallNormal = state.LastWallNormal;
			WasLastLegsOnFloor = state.WasGrounded;
			WasInWater = state.WasInWater;
			return true;
		}

		private void NoclipMove(PhysData physicsData, float deltaTime, InputMgr inputManager)
		{
			Vector3 move = Vector3.Zero;
			Vector3 forward = GetForward();
			Vector3 left = GetLeft();
			Vector3 up = GetUp();

			if (inputManager.IsInputDown(InputKey.W))
				move += forward;
			if (inputManager.IsInputDown(InputKey.S))
				move -= forward;
			if (inputManager.IsInputDown(InputKey.A))
				move += left;
			if (inputManager.IsInputDown(InputKey.D))
				move -= left;
			if (inputManager.IsInputDown(InputKey.Space))
				move += up;
			if (inputManager.IsInputDown(InputKey.Shift))
				move -= up;

			PlyVelocity = Vector3.Zero;
			GroundGraceTimer = 0f;
			WasLastLegsOnFloor = false;
			if (move != Vector3.Zero)
			{
				move = Vector3.Normalize(move) * physicsData.NoClipMoveSpeed * deltaTime;
				SetPosition(Position + move);
			}
		}

		private void UpdateSwimmingPhysics(
			PhysicsWorld world,
			PhysData physicsData,
			float deltaTime,
			InputMgr inputManager,
			bool headInWater)
		{
			ChunkMap map = world.Map;
			ClampToZero(ref PlyVelocity, physicsData.ClampHyst);

			Vector3 wishDirection = Vector3.Zero;
			Vector3 forward = GetForward();
			Vector3 left = GetLeft();
			if (inputManager.IsInputDown(InputKey.W))
				wishDirection += forward;
			if (inputManager.IsInputDown(InputKey.S))
				wishDirection -= forward;
			if (inputManager.IsInputDown(InputKey.A))
				wishDirection += left;
			if (inputManager.IsInputDown(InputKey.D))
				wishDirection -= left;
			if (inputManager.IsInputDown(InputKey.Space))
				wishDirection += Vector3.UnitY;
			if (inputManager.IsInputDown(InputKey.Shift))
				wishDirection -= Vector3.UnitY;
			if (wishDirection != Vector3.Zero)
				wishDirection = Vector3.Normalize(wishDirection);

			PhysicsUtils.ApplyDrag(ref PlyVelocity, physicsData.WaterFriction, deltaTime);
			if (wishDirection != Vector3.Zero)
				PhysicsUtils.Accelerate(ref PlyVelocity, wishDirection, physicsData.MaxWaterSpeed, physicsData.WaterAccel, deltaTime);

			bool activelySwimming = wishDirection != Vector3.Zero;
			if (activelySwimming && LegTimer.ElapsedMilliseconds > LastSwimSound + 600)
			{
				LastSwimSound = LegTimer.ElapsedMilliseconds;
				PlaySound("swim", Position);
			}

			if (headInWater)
			{
				float netBuoyancy = physicsData.WaterBuoyancy - physicsData.WaterSinkSpeed;
				PlyVelocity.Y += netBuoyancy * deltaTime;
			}
			else
			{
				PhysicsUtils.ApplyGravity(ref PlyVelocity, physicsData.WaterGravity, deltaTime);
			}

			Vector3 surfaceCheck = Position + new Vector3(0, 0.5f, 0);
			if (!map.IsWaterAt(surfaceCheck) && inputManager.IsInputDown(InputKey.Space))
				PlyVelocity.Y = MathF.Max(PlyVelocity.Y, physicsData.WaterJumpImpulse);

			float speed = PlyVelocity.Length();
			float maximumSpeed = physicsData.MaxWaterSpeed * 1.5f;
			if (speed > maximumSpeed)
				PlyVelocity = PlyVelocity / speed * maximumSpeed;

			Vector3 feetPosition = FeetPosition;
			Vector3 playerSize = new(PlayerRadius * 2f, PlayerHeight, PlayerRadius * 2f);
			PhysicsMoveResult move = WorldCollision.MoveAndSlide(
				world,
				feetPosition,
				playerSize,
				PlyVelocity,
				deltaTime,
				PhysicsCollisionMask.Player
			);
			PlyVelocity = move.Velocity;
			LastWallNormal = move.WallNormal;
			SetPosition(move.Position + new Vector3(0, PlayerEyeOffset, 0));
			WasLastLegsOnFloor = false;
			GroundGraceTimer = 0f;
		}

		public void UpdatePhysics(PhysicsWorld world, PhysData physicsData, float deltaTime, InputMgr inputManager)
		{
			if (world == null)
				throw new ArgumentNullException(nameof(world));
			if (physicsData == null)
				throw new ArgumentNullException(nameof(physicsData));
			if (inputManager == null)
				throw new ArgumentNullException(nameof(inputManager));
			if (!float.IsFinite(deltaTime) || deltaTime <= 0f)
				return;

			DecreaseTimers(deltaTime);
			if (NoClip)
			{
				NoclipMove(physicsData, deltaTime, inputManager);
				return;
			}

			ClampToZero(ref PlyVelocity, physicsData.ClampHyst);
			ChunkMap map = world.Map;
			Vector3 feetPosition = FeetPosition;
			Vector3 playerSize = new(PlayerRadius * 2f, PlayerHeight, PlayerRadius * 2f);

			bool inWater = map.IsWaterAt(Position) ||
				map.IsWaterAt(feetPosition + new Vector3(0, PlayerHeight * 0.5f, 0));
			bool headInWater = map.IsWaterAt(Position);
			if (WasInWater && !inWater && PlyVelocity.Y > 0f)
				PlyVelocity.Y *= 1.15f;
			WasInWater = inWater;

			if (inWater)
			{
				UpdateSwimmingPhysics(world, physicsData, deltaTime, inputManager, headInWater);
				return;
			}

			bool groundedBeforeMove = PlyVelocity.Y <= 0f &&
				ProbeGround(world, feetPosition, playerSize, physicsData.GroundCheckDist, out Vector3 groundNormal);
			if (groundedBeforeMove)
				GroundGraceTimer = GroundGraceDuration;
			else
				GroundGraceTimer = MathF.Max(0f, GroundGraceTimer - deltaTime);
			bool canCoyoteJump = groundedBeforeMove || GroundGraceTimer > 0f;

			Vector3 wishDirection = GetHorizontalWishDirection(inputManager);
			if (groundedBeforeMove && inputManager.IsInputDown(InputKey.Shift) && wishDirection != Vector3.Zero)
				ApplyLedgeSafety(map, feetPosition, ref wishDirection);

			float preAccelerationSpeed = PlyVelocity.Length();
			if (groundedBeforeMove)
				PhysicsUtils.ApplyPlanarFriction(ref PlyVelocity, physicsData.GroundFriction, deltaTime);

			bool jumped = inputManager.IsInputDown(InputKey.Space) &&
				canCoyoteJump &&
				JumpCooldownRemaining <= 0f &&
				HeadBumpCooldown <= 0f;
			if (jumped)
			{
				PlyVelocity.Y = physicsData.JumpImpulse;
				JumpCooldownRemaining = JumpCooldownDuration;
				RecentJumpRemaining = RecentJumpDuration;
				GroundGraceTimer = 0f;
				groundedBeforeMove = false;
				PhysicsHit(feetPosition, preAccelerationSpeed, false, false, false, true);
			}

			if (wishDirection != Vector3.Zero)
			{
				float wishSpeed = inputManager.IsInputDown(InputKey.Shift)
					? physicsData.MaxWalkSpeed
					: physicsData.MaxGroundSpeed;
				if (groundedBeforeMove)
				{
					PhysicsUtils.Accelerate(ref PlyVelocity, wishDirection, wishSpeed, physicsData.GroundAccel, deltaTime);
				}
				else
				{
					Vector3 accelerationDirection = wishDirection;
					if (LastWallNormal != Vector3.Zero)
					{
						accelerationDirection -= Vector3.Dot(accelerationDirection, LastWallNormal) * LastWallNormal;
						if (accelerationDirection.LengthSquared() > 1e-4f)
							accelerationDirection = Vector3.Normalize(accelerationDirection);
						else
							accelerationDirection = Vector3.Zero;
					}
					if (accelerationDirection != Vector3.Zero)
						PhysicsUtils.AirAccelerate(ref PlyVelocity, accelerationDirection, wishSpeed, physicsData.AirAccel, deltaTime);
				}
			}

			if (!groundedBeforeMove)
				PhysicsUtils.ApplyGravity(ref PlyVelocity, physicsData.Gravity, deltaTime);
			else if (PlyVelocity.Y < 0f)
				PlyVelocity.Y = 0f;

			if (groundedBeforeMove)
				CapGroundSpeed(inputManager, physicsData);

			float verticalVelocityBeforeMove = PlyVelocity.Y;
			PhysicsMoveResult slideMove = WorldCollision.MoveAndSlide(
				world,
				feetPosition,
				playerSize,
				PlyVelocity,
				deltaTime,
				PhysicsCollisionMask.Player
			);

			PhysicsMoveResult selectedMove = slideMove;
			if (groundedBeforeMove && PlyVelocity.Y <= 0f && TryStepMove(
				world,
				feetPosition,
				playerSize,
				PlyVelocity,
				deltaTime,
				0.5f,
				physicsData.GroundCheckDist,
				out PhysicsMoveResult stepMove))
			{
				float slideProgress = HorizontalDistanceSquared(feetPosition, slideMove.Position);
				float stepProgress = HorizontalDistanceSquared(feetPosition, stepMove.Position);
				if (stepProgress > slideProgress + 1e-5f)
					selectedMove = stepMove;
			}

			PlyVelocity = selectedMove.Velocity;
			LastWallNormal = selectedMove.WallNormal;
			SetPosition(selectedMove.Position + new Vector3(0, PlayerEyeOffset, 0));

			bool groundedAfterMove = selectedMove.Grounded ||
				(PlyVelocity.Y <= 0f && ProbeGround(
					world,
					selectedMove.Position,
					playerSize,
					physicsData.GroundCheckDist,
					out groundNormal));
			if (groundedAfterMove && PlyVelocity.Y < 0f)
				PlyVelocity.Y = 0f;

			if (groundedAfterMove && !WasLastLegsOnFloor)
				PhysicsHit(Position, preAccelerationSpeed, false, true, false, false);
			else if (groundedAfterMove && PlyVelocity.Length() >= physicsData.MaxGroundSpeed * 0.5f)
				PhysicsHit(selectedMove.Position, PlyVelocity.Length(), false, true, true, false);
			WasLastLegsOnFloor = groundedAfterMove;

			if (verticalVelocityBeforeMove > 0.1f && PlyVelocity.Y <= 0f && RecentJumpRemaining > 0f)
				HeadBumpCooldown = HeadBumpCooldownDuration;
		}

		private void DecreaseTimers(float deltaTime)
		{
			JumpCooldownRemaining = MathF.Max(0f, JumpCooldownRemaining - deltaTime);
			RecentJumpRemaining = MathF.Max(0f, RecentJumpRemaining - deltaTime);
			HeadBumpCooldown = MathF.Max(0f, HeadBumpCooldown - deltaTime);
		}

		private Vector3 GetHorizontalWishDirection(InputMgr inputManager)
		{
			Vector3 forward = GetForward();
			forward.Y = 0f;
			forward = forward.LengthSquared() > 0.0001f ? Vector3.Normalize(forward) : Vector3.UnitX;

			Vector3 left = GetLeft();
			left.Y = 0f;
			if (left.LengthSquared() > 0.0001f)
				left = Vector3.Normalize(left);

			Vector3 wishDirection = Vector3.Zero;
			if (inputManager.IsInputDown(InputKey.W))
				wishDirection += forward;
			if (inputManager.IsInputDown(InputKey.S))
				wishDirection -= forward;
			if (inputManager.IsInputDown(InputKey.A))
				wishDirection += left;
			if (inputManager.IsInputDown(InputKey.D))
				wishDirection -= left;

			return wishDirection == Vector3.Zero ? Vector3.Zero : Vector3.Normalize(wishDirection);
		}

		private void ApplyLedgeSafety(ChunkMap map, Vector3 feetPosition, ref Vector3 wishDirection)
		{
			List<Vector3> supported = new();
			foreach (Vector3 point in GetLedgeSupportPoints(feetPosition, PlayerRadius))
			{
				Vector3 probe = point - new Vector3(0, 0.15f, 0);
				if (map.IsSolid(probe))
					supported.Add(point);
			}

			bool movingTowardSupport = false;
			foreach (Vector3 point in supported)
			{
				Vector3 offset = point - feetPosition;
				offset.Y = 0f;
				if (offset.LengthSquared() <= 1e-8f)
					continue;
				if (Vector3.Dot(wishDirection, Vector3.Normalize(offset)) > 0f)
				{
					movingTowardSupport = true;
					break;
				}
			}

			if (supported.Count == 0 || !movingTowardSupport)
			{
				PlyVelocity.X = 0f;
				PlyVelocity.Z = 0f;
				wishDirection = Vector3.Zero;
			}
		}

		private void CapGroundSpeed(InputMgr inputManager, PhysData physicsData)
		{
			float maximumSpeed = inputManager.IsInputDown(InputKey.Shift)
				? physicsData.MaxWalkSpeed
				: physicsData.MaxGroundSpeed;
			float horizontalSpeed = MathF.Sqrt(PlyVelocity.X * PlyVelocity.X + PlyVelocity.Z * PlyVelocity.Z);
			if (horizontalSpeed <= maximumSpeed)
				return;

			float scale = maximumSpeed / horizontalSpeed;
			PlyVelocity.X *= scale;
			PlyVelocity.Z *= scale;
		}

		private static bool ProbeGround(
			PhysicsWorld world,
			Vector3 feetPosition,
			Vector3 playerSize,
			float distance,
			out Vector3 groundNormal)
		{
			groundNormal = Vector3.Zero;
			AABB bounds = PhysicsUtils.CreateEntityAABB(feetPosition, playerSize);
			if (!world.SweepAabb(bounds, -Vector3.UnitY * distance, PhysicsCollisionMask.Player, out SweepHit hit))
				return false;

			for (int index = 0; index < hit.NormalCount; index++)
			{
				Vector3 normal = hit.GetNormal(index);
				if (normal.Y > 0.7f)
				{
					groundNormal = normal;
					return true;
				}
			}
			return false;
		}

		private static bool TryStepMove(
			PhysicsWorld world,
			Vector3 start,
			Vector3 size,
			Vector3 velocity,
			float deltaTime,
			float stepHeight,
			float groundProbeDistance,
			out PhysicsMoveResult result)
		{
			result = default;
			AABB startBounds = PhysicsUtils.CreateEntityAABB(start, size);
			Vector3 upwardDelta = Vector3.UnitY * stepHeight;
			if (world.SweepAabb(startBounds, upwardDelta, PhysicsCollisionMask.Player, out _))
				return false;

			Vector3 raisedPosition = start + upwardDelta;
			Vector3 horizontalVelocity = new(velocity.X, 0f, velocity.Z);
			PhysicsMoveResult forward = WorldCollision.MoveAndSlide(
				world,
				raisedPosition,
				size,
				horizontalVelocity,
				deltaTime,
				PhysicsCollisionMask.Player
			);
			if (HorizontalDistanceSquared(raisedPosition, forward.Position) <= 1e-8f)
				return false;

			float downwardDistance = stepHeight + groundProbeDistance;
			AABB forwardBounds = PhysicsUtils.CreateEntityAABB(forward.Position, size);
			if (!world.SweepAabb(
				forwardBounds,
				-Vector3.UnitY * downwardDistance,
				PhysicsCollisionMask.Player,
				out SweepHit landing))
			{
				return false;
			}

			Vector3 groundNormal = Vector3.Zero;
			for (int index = 0; index < landing.NormalCount; index++)
			{
				Vector3 normal = landing.GetNormal(index);
				if (normal.Y > 0.7f)
				{
					groundNormal = normal;
					break;
				}
			}
			if (groundNormal == Vector3.Zero)
				return false;

			float safeDistance = MathF.Max(0f, downwardDistance * landing.Fraction - WorldCollision.CollisionSkin);
			Vector3 landedPosition = forward.Position - Vector3.UnitY * safeDistance;
			result = new PhysicsMoveResult(
				landedPosition,
				new Vector3(forward.Velocity.X, 0f, forward.Velocity.Z),
				true,
				groundNormal,
				forward.WallNormal
			);
			return true;
		}

		private static float HorizontalDistanceSquared(Vector3 first, Vector3 second)
		{
			float x = second.X - first.X;
			float z = second.Z - first.Z;
			return x * x + z * z;
		}

		private static bool IsFinite(Vector3 value) =>
			float.IsFinite(value.X) &&
			float.IsFinite(value.Y) &&
			float.IsFinite(value.Z);

		public Vector3 GetVelocity() => PlyVelocity;
		public void SetVelocity(Vector3 velocity) => PlyVelocity = IsFinite(velocity) ? velocity : Vector3.Zero;
		public bool GetWasLastLegsOnFloor() => WasLastLegsOnFloor;

		public void PhysicsHit(Vector3 position, float force, bool side, bool feet, bool walk, bool jump)
		{
			if (walk)
			{
				if (LegTimer.ElapsedMilliseconds > LastWalkSound + 350)
				{
					LastWalkSound = LegTimer.ElapsedMilliseconds;
					PlaySound("walk", position);
				}
			}
			else if (jump)
			{
				if (LegTimer.ElapsedMilliseconds > LastJumpSound + 350)
				{
					LastJumpSound = LegTimer.ElapsedMilliseconds;
					PlaySound("jump", position);
				}
			}
			else if (feet && !side && LegTimer.ElapsedMilliseconds > LastCrashSound + 350)
			{
				LastCrashSound = LegTimer.ElapsedMilliseconds;
				PlaySound("crash1", position);
			}
		}
	}
}
