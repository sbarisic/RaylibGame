using System;
using System.Numerics;
using Voxelgine.Engine.AI;
using Voxelgine.Engine.Pathfinding;
using Voxelgine.Graphics;

namespace Voxelgine.Engine
{
	public partial class VEntNPC
	{
		public void InitPathfinding(ChunkMap map)
		{
			_map = map;
			int entityHeight = (int)MathF.Ceiling(Size.Y);
			int entityWidth = (int)MathF.Ceiling(MathF.Max(Size.X, Size.Z));
			_pathFollower = new PathFollower(map, entityHeight, entityWidth)
			{
				MoveSpeed = 3.0f,
				WaypointReachDistance = 0.4f
			};
		}

		/// <summary>
		/// Commands the NPC to navigate to a target position.
		/// </summary>
		/// <param name="target">World position to navigate to.</param>
		/// <returns>True if a path was found.</returns>
		public bool NavigateTo(Vector3 target)
		{
			if (_pathFollower == null)
				return false;

			// Reset stuck detection when starting new navigation
			_lastStuckCheckPos = Position;
			_stuckCheckTimer = 0f;
			_stuckRecalculateAttempts = 0;
			_lastWaypointIndex = 0;
			_isUnstuckWandering = false;
			_hasOriginalTarget = false;
			_triedJumpingToUnstuck = false;

			return _pathFollower.SetTarget(Position, target);
		}

		/// <summary>
		/// Stops the NPC from following its current path.
		/// </summary>
		public void StopNavigation()
		{
			_pathFollower?.ClearPath();
			_isUnstuckWandering = false;
			_hasOriginalTarget = false;
		}

		/// <summary>
		/// Returns true if the NPC is currently navigating.
		/// </summary>
		public bool IsNavigating => _pathFollower?.IsFollowingPath ?? false;

		/// <summary>
		/// Returns true if the NPC has reached its navigation target.
		/// </summary>
		public bool HasReachedTarget => _pathFollower?.HasReachedTarget ?? false;

		public override void SetModel(string modelAssetId)
		{
			base.SetModel(modelAssetId);
			_renderAnimationName = "idle";
		}

		public override void UpdateVisuals(float Dt)
			{
				base.UpdateVisuals(Dt);

				// Tick speech timer on the client (UpdateLockstep only runs server-side)
				if (_speechTimer > 0)
				{
					_speechTimer -= Dt;
					if (_speechTimer <= 0)
						_speechText = "";
				}

				// Head tracking — smoothly rotate head toward nearest player
				UpdateHeadTracking(Dt);
			}

			public override void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr)
			{
				base.UpdateLockstep(TotalTime, Dt, InMgr);

				// Check if grounded
				bool isGrounded = IsGrounded();

			// Update pathfinding movement
			if (_pathFollower != null && _pathFollower.IsFollowingPath)
			{
				// Stuck detection
				_stuckCheckTimer += Dt;
				if (_stuckCheckTimer >= StuckCheckInterval)
				{
					_stuckCheckTimer = 0f;

					// Check if we've moved enough since last check
					float distanceMoved = Vector3.Distance(Position, _lastStuckCheckPos);
					int currentWaypointIdx = _pathFollower.CurrentWaypointIndex;
					bool waypointAdvanced = currentWaypointIdx > _lastWaypointIndex;

					if (distanceMoved < StuckDistanceThreshold && !waypointAdvanced)
					{
						// We're stuck - try different recovery methods
						HandleStuckRecovery(isGrounded);
					}
					else
					{
						// Making progress
						if (_isUnstuckWandering)
						{
							// Successfully unstuck - return to original target
							Logging.WriteLine("NPC unstuck, resuming original navigation");
							_isUnstuckWandering = false;
							_stuckRecalculateAttempts = 0;
							_triedJumpingToUnstuck = false;

							if (_hasOriginalTarget)
							{
								_pathFollower.SetTarget(Position, _originalTarget);
								_hasOriginalTarget = false;
							}
						}
						else
						{
							// Normal progress - reset attempts
							_stuckRecalculateAttempts = 0;
							_triedJumpingToUnstuck = false;
						}
					}

					_lastStuckCheckPos = Position;
					_lastWaypointIndex = _pathFollower.CurrentWaypointIndex;
				}

				PathSteering steering = _pathFollower.Step(Position);
				Vector3 moveDir = steering.HorizontalDirection;

				if (moveDir.LengthSquared() > 0.001f)
				{
					// Apply horizontal velocity
					Velocity = new Vector3(
						moveDir.X * _pathFollower.MoveSpeed,
						Velocity.Y, // Keep vertical velocity (gravity)
						moveDir.Z * _pathFollower.MoveSpeed
					);

					// Update look direction
					_lookDirection = moveDir;
				}
				else
				{
					// Stop horizontal movement when no direction
					Velocity = new Vector3(0, Velocity.Y, 0);
				}

				// Handle jumping for navigation
				if (isGrounded)
				{
					// Jump if path requires it (waypoint is higher)
					if (steering.JumpRequested)
					{
						Jump();
					}
					// Also jump if target is significantly higher than current position
					else if (_pathFollower.HasTarget)
					{
						float heightDiff = _pathFollower.TargetPosition.Y - Position.Y;
						if (heightDiff > 0.5f && IsBlockedAhead(moveDir))
						{
							Jump();
						}
					}
				}
			}

			// AI behavior program
			_aiRunner?.Tick(this, Dt);

			// Speech timer
			if (_speechTimer > 0)
			{
				_speechTimer -= Dt;
				if (_speechTimer <= 0)
					_speechText = "";
			}

			// Keep hit-reaction pose state renderer-neutral. The client adapter may
			// consume these offsets without the simulation owning a mesh.
			if (_twitchOffsets.Count > 0)
			{
				var keysToRemove = new List<string>();

				foreach (var kvp in _twitchOffsets)
				{
					string partName = kvp.Key;
					Vector3 twitch = kvp.Value;

					// Decay the twitch
					twitch *= MathF.Exp(-TwitchDecayRate * Dt);

					// Remove if negligible
					if (twitch.LengthSquared() < 0.01f)
					{
						keysToRemove.Add(partName);
					}
					else
					{
						_twitchOffsets[partName] = twitch;
					}
				}

				foreach (var key in keysToRemove)
					_twitchOffsets.Remove(key);
			}

			// Play walk animation when moving, idle when stationary (unless overridden)
			if (_animOverrideTimer > 0)
			{
				_animOverrideTimer -= Dt;
			}
			else
			{
				float horizontalSpeed = MathF.Sqrt(Velocity.X * Velocity.X + Velocity.Z * Velocity.Z);
				if (horizontalSpeed > 0.5f)
				{
					_renderAnimationName = "walk";
				}
				else
				{
					_renderAnimationName = "idle";
				}
			}
		}

		/// <summary>
		/// Handles recovery when the NPC is stuck.
		/// Tries jumping first, then wandering in random directions.
		/// </summary>
		private void HandleStuckRecovery(bool isGrounded)
		{
			_aiRunner?.RaiseEvent(AIEvent.OnStuck, NetworkId);

			// First, try recalculating the path from current (possibly pushed-out) position
			if (!_triedJumpingToUnstuck && _pathFollower.HasTarget)
			{
				Logging.WriteLine("NPC stuck, recalculating path");
				if (_pathFollower.RecalculatePath(Position))
				{
					_triedJumpingToUnstuck = true; // Use this flag to avoid re-recalculating
					return;
				}
			}

			// If recalculating didn't help, try jumping over the obstacle
			if (!_triedJumpingToUnstuck && isGrounded)
			{
				Logging.WriteLine("NPC stuck, trying to jump out");
				_triedJumpingToUnstuck = true;
				Jump();
				return;
			}

			// If jumping didn't help, try wandering in a random walkable direction
			_stuckRecalculateAttempts++;

			if (_stuckRecalculateAttempts <= MaxRecalculateAttempts)
			{
				// Save original target if not already wandering
				if (!_isUnstuckWandering && _pathFollower.HasTarget)
				{
					_originalTarget = _pathFollower.TargetPosition;
					_hasOriginalTarget = true;
				}

				_isUnstuckWandering = true;
				_triedJumpingToUnstuck = false;
				Vector3 randomTarget = GetRandomWanderTarget();
				Logging.WriteLine($"NPC stuck, wandering to random position (attempt {_stuckRecalculateAttempts}/{MaxRecalculateAttempts})");

				if (!_pathFollower.SetTarget(Position, randomTarget))
				{
					// No path to wander target either — try again next tick
					Logging.WriteLine("NPC could not find wander path, will retry");
				}
			}
			else
			{
				// Give up after max attempts
				Logging.WriteLine("NPC giving up on navigation after max attempts");
				StopNavigation();
			}
		}

		/// <summary>
		/// Makes the NPC jump if grounded.
		/// </summary>
		private void Jump()
		{
			Velocity = new Vector3(Velocity.X, JumpVelocity, Velocity.Z);
		}

		/// <summary>
		/// Checks if the NPC is standing on solid ground.
		/// </summary>
		private bool IsGrounded()
		{
			if (_map == null)
				return false;

			float halfWidth = Size.X / 2f;

			// Check multiple points under the NPC's feet
			Vector2[] checkPoints =
			{
				new Vector2(Position.X, Position.Z),                          // Center
				new Vector2(Position.X - halfWidth * 0.5f, Position.Z),       // Left
				new Vector2(Position.X + halfWidth * 0.5f, Position.Z),       // Right
				new Vector2(Position.X, Position.Z - halfWidth * 0.5f),       // Back
				new Vector2(Position.X, Position.Z + halfWidth * 0.5f),       // Front
			};

			float checkY = Position.Y - GroundCheckDistance;

			foreach (var point in checkPoints)
			{
				int blockX = (int)MathF.Floor(point.X);
				int blockY = (int)MathF.Floor(checkY);
				int blockZ = (int)MathF.Floor(point.Y);

				if (_map.IsSolid(blockX, blockY, blockZ))
					return true;
			}

			return false;
		}

		/// <summary>
		/// Checks if there's a solid block ahead in the movement direction.
		/// Used to detect when jumping might help navigate obstacles.
		/// </summary>
		private bool IsBlockedAhead(Vector3 moveDir)
		{
			if (_map == null || moveDir.LengthSquared() < 0.001f)
				return false;

			// Check at knee height (where we'd bump into blocks)
			Vector3 checkPos = Position + new Vector3(0, 0.5f, 0) + moveDir * (Size.X / 2f + 0.3f);

			int blockX = (int)MathF.Floor(checkPos.X);
			int blockY = (int)MathF.Floor(checkPos.Y);
			int blockZ = (int)MathF.Floor(checkPos.Z);

			return _map.IsSolid(blockX, blockY, blockZ);
		}

		/// <summary>
		/// Gets a random walkable position near the NPC for unstuck wandering.
		/// </summary>
		private Vector3 GetRandomWanderTarget()
		{
			if (_map == null)
				return Position;

			int currentY = (int)MathF.Floor(Position.Y);

			// Try random directions and pick the first walkable one
			for (int i = 0; i < 8; i++)
			{
				float angle = (float)(_random.NextDouble() * Math.PI * 2);
				float distance = WanderDistance * (0.5f + (float)_random.NextDouble() * 0.5f);

				int targetX = (int)MathF.Floor(Position.X + MathF.Cos(angle) * distance);
				int targetZ = (int)MathF.Floor(Position.Z + MathF.Sin(angle) * distance);

				bool groundSolid = _map.IsSolid(targetX, currentY - 1, targetZ);
				bool feetClear = !_map.IsSolid(targetX, currentY, targetZ);
				bool headClear = !_map.IsSolid(targetX, currentY + 1, targetZ);

				if (groundSolid && feetClear && headClear)
					return new Vector3(targetX + 0.5f, currentY, targetZ + 0.5f);
			}

			// Fallback: offset slightly from current position
			return Position + new Vector3(
				(float)(_random.NextDouble() - 0.5) * WanderDistance * 2,
				0,
				(float)(_random.NextDouble() - 0.5) * WanderDistance * 2
			);
		}

		/// <summary>
		/// Updates head tracking rotation toward the nearest player.
		/// Computes the angle between the body's forward direction and the direction
		/// to the target, then smoothly interpolates the head mesh rotation.
		/// </summary>
	}
}
