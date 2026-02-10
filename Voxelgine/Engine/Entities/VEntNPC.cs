using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Raylib_cs;

using Voxelgine.Engine.AI;
using Voxelgine.Engine.Pathfinding;
using Voxelgine.Graphics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// NPC entity with support for animated JSON models and pathfinding navigation.
	/// Uses NPCAnimator to play predefined animations (walk, idle, attack, etc.).
	/// </summary>
	public class VEntNPC : VoxEntity
	{
		CustomModel CModel;
		BoundingBox BBox;
		NPCAnimator Animator;

		// Pathfinding
		PathFollower _pathFollower;
		ChunkMap _map;
		Vector3 _lookDirection = Vector3.UnitZ;

		// Stuck detection and recovery
		private Vector3 _lastStuckCheckPos;
		private float _stuckCheckTimer;
		private int _stuckRecalculateAttempts;
		private int _lastWaypointIndex;
		private bool _isUnstuckWandering;
		private Vector3 _originalTarget;
		private bool _hasOriginalTarget;
		private bool _triedJumpingToUnstuck;
		private static readonly Random _random = new();
		private const float StuckCheckInterval = 0.5f;      // Check every 0.5 seconds
		private const float StuckDistanceThreshold = 0.3f;  // Must move at least this far
		private const int MaxRecalculateAttempts = 3;       // Try random directions this many times
		private const float WanderDistance = 3f;            // How far to wander when stuck
		private const float JumpVelocity = 5.5f;            // Jump impulse strength (enough for ~1 block)
		private const float GroundCheckDistance = 0.15f;    // How far below feet to check for ground

		// AI behavior program
		private AIRunner _aiRunner;

		// Hit twitch effect
		private Dictionary<string, Vector3> _twitchOffsets = new();
		private const float TwitchDecayRate = 12f;          // How fast twitch decays per second
		private const float TwitchStrength = 25f;           // Max rotation degrees on hit

		/// <summary>Gets the animator for this NPC (null if model not loaded).</summary>
		public NPCAnimator GetAnimator() => Animator;

		/// <summary>Gets the custom model for this NPC (null if not loaded).</summary>
		public CustomModel GetCustomModel() => CModel;

		/// <summary>Sets the NPC look direction (used by client-side interpolation).</summary>
		public void SetLookDirection(Vector3 dir) => _lookDirection = dir;

		/// <summary>Gets the path follower for this NPC (null if not initialized).</summary>
		public PathFollower GetPathFollower() => _pathFollower;

		/// <summary>
		/// Assigns an AI behavior program to this NPC.
		/// The program is executed by the server each tick.
		/// </summary>
		public void SetAIProgram(AIStep[] program)
		{
			_aiRunner = program != null && program.Length > 0 ? new AIRunner(program, Logging) : null;
		}

		/// <summary>
		/// Called by EntityManager when a player walks into this NPC's collision box.
		/// Raises the OnPlayerTouch AI event.
		/// </summary>
		public override void OnPlayerTouch(Player Ply)
		{
			_aiRunner?.RaiseEvent(AIEvent.OnPlayerTouch, NetworkId);
		}

		/// <summary>
		/// Called when this NPC is hit by a weapon.
		/// Raises the OnAttacked AI event.
		/// </summary>
		public void OnAttacked()
		{
			_aiRunner?.RaiseEvent(AIEvent.OnAttacked, NetworkId);
		}

		/// <summary>
		/// Performs a detailed raycast against the NPC's model to determine which body part was hit.
		/// Takes animation transforms into account.
		/// </summary>
		/// <param name="rayOrigin">Origin of the ray in world space.</param>
		/// <param name="rayDirection">Normalized direction of the ray.</param>
		/// <param name="hitPosition">Output: world position where the ray hit.</param>
		/// <param name="hitNormal">Output: surface normal at the hit point.</param>
		/// <returns>Name of the hit body part (e.g., "head", "body", "leg_r"), or null if no hit.</returns>
		public string RaycastBodyPart(Vector3 rayOrigin, Vector3 rayDirection, out Vector3 hitPosition, out Vector3 hitNormal)
		{
			hitPosition = Vector3.Zero;
			hitNormal = Vector3.Zero;

			if (CModel == null)
				return null;

			Ray ray = new Ray(rayOrigin, rayDirection);
			RayCollision collision = CModel.Collide(ray, out CustomMesh hitMesh);

			if (collision.Hit && hitMesh != null)
			{
				hitPosition = collision.Point;
				hitNormal = collision.Normal;
				return hitMesh.Name;
			}

			return null;
		}

		/// <summary>
		/// Applies a twitch effect to a body part (e.g., when hit by gunfire).
		/// The twitch will decay over time automatically.
		/// </summary>
		/// <param name="bodyPartName">Name of the mesh to twitch (e.g., "head", "leg_r").</param>
		/// <param name="hitNormal">Direction of impact (twitch rotates away from this).</param>
		public void TwitchBodyPart(string bodyPartName, Vector3 hitNormal)
		{
			if (CModel == null || string.IsNullOrEmpty(bodyPartName))
				return;

			// Generate random twitch rotation based on hit direction
			float twitchX = (hitNormal.Y * 0.5f + (_random.NextSingle() - 0.5f)) * TwitchStrength;
			float twitchY = (_random.NextSingle() - 0.5f) * TwitchStrength * 0.5f;
			float twitchZ = (hitNormal.X * 0.5f + (_random.NextSingle() - 0.5f)) * TwitchStrength;

			Vector3 twitch = new Vector3(twitchX, twitchY, twitchZ) * 15;

			// Add to existing twitch or create new
			if (_twitchOffsets.ContainsKey(bodyPartName))
				_twitchOffsets[bodyPartName] += twitch;
			else
				_twitchOffsets[bodyPartName] = twitch;
		}

		/// <summary>
		/// Initializes pathfinding for this NPC.
		/// Must be called after entity is spawned and has access to the world.
		/// </summary>
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

		public override void SetModel(string MdlName)
		{
			HasModel = false;
			ModelOffset = Vector3.Zero;
			ModelRotationDeg = 0;
			ModelColor = Color.White;
			ModelScale = Vector3.One;

			EntModelName = MdlName;
			MinecraftModel JMdl = ResMgr.GetJsonModel(MdlName);
			CModel = MeshGenerator.Generate(JMdl);

			// Set up parent-child hierarchy for proper animation attachment
			CModel.SetupHumanoidHierarchy();

			HasModel = true;
			BBox = CModel.GetBoundingBox();

			// Initialize animator with standard animations
			Animator = new NPCAnimator(CModel, Logging);
			Animator.LoadAllClips();
			/*Animator.AddClips(
				NPCAnimations.CreateIdleAnimation(),
				NPCAnimations.CreateWalkAnimation(),
				NPCAnimations.CreateAttackAnimation(),
				NPCAnimations.CreateCrouchAnimation()
			);*/
			Animator.Play("idle");

			if (Size != Vector3.Zero)
			{
				Vector3 Off = (BBox.Max - BBox.Min) / 2;
				ModelOffset = new Vector3(Size.X / 2, 0, Size.Z / 2);
			}
		}

		public override void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr)
		{
			base.UpdateLockstep(TotalTime, Dt, InMgr);

			// Resolve any block clipping first
			ResolveBlockCollisions();

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

				// Get movement direction from path follower
				Vector3 moveDir = _pathFollower.Update(Position, Dt);

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
					if (_pathFollower.ShouldJump(Position))
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

			// Update animation
			Animator?.Update(Dt);

			// Update and apply hit twitch effects
			if (CModel != null && _twitchOffsets.Count > 0)
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
						// Reset mesh animation offset
						var mesh = CModel.GetMeshByName(partName);
						if (mesh != null)
							mesh.AnimationRotation -= kvp.Value; // Remove old twitch
					}
					else
					{
						// Apply twitch offset to mesh
						var mesh = CModel.GetMeshByName(partName);
						if (mesh != null)
						{
							// Remove old twitch, apply new
							mesh.AnimationRotation -= kvp.Value;
							mesh.AnimationRotation += twitch;
							mesh.UpdateAnimationMatrix();
						}
						_twitchOffsets[partName] = twitch;
					}
				}

				foreach (var key in keysToRemove)
					_twitchOffsets.Remove(key);
			}

			// Play walk animation when moving, idle when stationary
			float horizontalSpeed = MathF.Sqrt(Velocity.X * Velocity.X + Velocity.Z * Velocity.Z);
			if (Animator != null && horizontalSpeed > 0.5f)
			{
				if (Animator.CurrentAnimation != "walk")
					Animator.Play("walk");
			}
			else if (Animator != null)
			{
				if (Animator.CurrentAnimation != "idle")
					Animator.Play("idle");
			}
		}

		/// <summary>
		/// Handles recovery when the NPC is stuck.
		/// Tries jumping first, then wandering in random directions.
		/// </summary>
		private void HandleStuckRecovery(bool isGrounded)
		{
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
		/// Checks if the NPC is clipping into solid blocks and pushes it out.
		/// Uses minimum penetration axis to resolve collisions smoothly.
		/// </summary>
		private void ResolveBlockCollisions()
		{
			if (_map == null)
				return;

			// NPC collision bounds (centered on X/Z, feet at Y)
			float halfWidth = Size.X / 2f;
			float height = Size.Y;
			const float skinWidth = 0.02f; // Small buffer to prevent sticking

			// Calculate NPC AABB
			float minX = Position.X - halfWidth;
			float maxX = Position.X + halfWidth;
			float minY = Position.Y;
			float maxY = Position.Y + height;
			float minZ = Position.Z - halfWidth;
			float maxZ = Position.Z + halfWidth;

			// Get block range to check
			int blockMinX = (int)MathF.Floor(minX);
			int blockMaxX = (int)MathF.Floor(maxX);
			int blockMinY = (int)MathF.Floor(minY);
			int blockMaxY = (int)MathF.Floor(maxY);
			int blockMinZ = (int)MathF.Floor(minZ);
			int blockMaxZ = (int)MathF.Floor(maxZ);

			// Check each potentially overlapping block
				for (int bx = blockMinX; bx <= blockMaxX; bx++)
				{
					for (int by = blockMinY; by <= blockMaxY; by++)
					{
						for (int bz = blockMinZ; bz <= blockMaxZ; bz++)
						{
							if (!_map.IsSolid(bx, by, bz))
								continue;

							// Block AABB (use float directly from int coords)
							float bMinX = bx;
							float bMaxX = bx + 1f;
							float bMinY = by;
							float bMaxY = by + 1f;
							float bMinZ = bz;
							float bMaxZ = bz + 1f;

							// Check if actually overlapping
							if (maxX <= bMinX || minX >= bMaxX ||
								maxY <= bMinY || minY >= bMaxY ||
								maxZ <= bMinZ || minZ >= bMaxZ)
								continue;

							// Calculate penetration on each axis
							float overlapLeft = maxX - bMinX;   // Push -X
							float overlapRight = bMaxX - minX;  // Push +X
							float overlapBottom = maxY - bMinY; // Push -Y
							float overlapTop = bMaxY - minY;    // Push +Y
							float overlapBack = maxZ - bMinZ;   // Push -Z
							float overlapFront = bMaxZ - minZ;  // Push +Z

							// Find minimum horizontal penetration (ignore Y for now - gravity handles that)
							float minOverlapX = MathF.Min(overlapLeft, overlapRight);
							float minOverlapZ = MathF.Min(overlapBack, overlapFront);

							// Push out on the axis with smallest penetration
							if (minOverlapX < minOverlapZ)
							{
								// Push on X axis
								if (overlapLeft < overlapRight)
								{
									Position.X -= overlapLeft + skinWidth;
									if (Velocity.X > 0) Velocity.X = 0;
								}
								else
								{
									Position.X += overlapRight + skinWidth;
									if (Velocity.X < 0) Velocity.X = 0;
								}
							}
							else
							{
								// Push on Z axis
								if (overlapBack < overlapFront)
								{
									Position.Z -= overlapBack + skinWidth;
									if (Velocity.Z > 0) Velocity.Z = 0;
								}
								else
								{
									Position.Z += overlapFront + skinWidth;
									if (Velocity.Z < 0) Velocity.Z = 0;
								}
							}

							// Recalculate NPC bounds after push
							minX = Position.X - halfWidth;
							maxX = Position.X + halfWidth;
							minZ = Position.Z - halfWidth;
							maxZ = Position.Z + halfWidth;
						}
					}
				}
			}

		protected override void WriteSnapshotExtra(BinaryWriter writer)
		{
			// Look direction (12 bytes)
			writer.Write(_lookDirection.X);
			writer.Write(_lookDirection.Y);
			writer.Write(_lookDirection.Z);

			// Current animation name (string, length-prefixed)
			writer.Write(Animator?.CurrentAnimation ?? "");
		}

		protected override void ReadSnapshotExtra(BinaryReader reader)
		{
			// Look direction
			_lookDirection = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

			// Current animation name
			string animName = reader.ReadString();
			if (Animator != null && Animator.CurrentAnimation != animName && !string.IsNullOrEmpty(animName))
				Animator.Play(animName);
		}

		protected override void EntityDrawModel(float TimeAlpha, ref GameFrameInfo LastFrame)
		{
			if (HasModel)
			{
				BBox = CModel.GetBoundingBox();

				CModel.Position = GetDrawPosition();
				CModel.LookDirection = _lookDirection;
				CModel.Draw(GetEntityLightColor());

				if (Eng.DebugMode)
				{
					Raylib.DrawBoundingBox(BBox, Color.Blue);

					// Draw path if navigating
					if (_pathFollower != null && _pathFollower.IsFollowingPath)
					{
						var path = _pathFollower.CurrentPath;
						for (int i = _pathFollower.CurrentWaypointIndex; i < path.Count - 1; i++)
						{
							Raylib.DrawLine3D(path[i], path[i + 1], Color.Yellow);
							Raylib.DrawSphere(path[i], 0.15f, Color.Green);
						}
						if (path.Count > 0)
							Raylib.DrawSphere(path[^1], 0.2f, Color.Red);
					}
				}
			}
		}
	}
}