using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

using Voxelgine.Engine.AI;
using Voxelgine.Engine.Pathfinding;
using Voxelgine.Graphics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Authoritative NPC state with pathfinding, AI, and renderer-neutral pose data.
	/// </summary>
	public class VEntNPC : VoxEntity
	{
		// Stable presentation asset and animation identifiers.
		private string _textureName;
		private string _renderAnimationName = "idle";

		/// <summary>Available NPC skin textures, randomly assigned at spawn.</summary>
		public static readonly string[] AvailableTextures =
		[
			"npc/humanoid.png",
			"npc/humanoid2.png",
		];

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

		// Speech bubble
		private string _speechText = "";
		private float _speechTimer;
		private bool _speechDirty;

		/// <summary>Gets the current speech text (empty if not speaking).</summary>
		public string SpeechText => _speechText;

		/// <summary>Gets the remaining speech duration.</summary>
		public float SpeechDuration => _speechTimer;

		/// <summary>
		/// Returns true if speech state changed since last call to <see cref="ConsumeSpeechDirty"/>.
		/// Used by the server to know when to broadcast an <see cref="EntitySpeechPacket"/>.
		/// </summary>
		public bool IsSpeechDirty => _speechDirty;

		/// <summary>Clears the speech dirty flag after broadcasting.</summary>
		public void ConsumeSpeechDirty() => _speechDirty = false;

		// Hit twitch effect
		private Dictionary<string, Vector3> _twitchOffsets = new();
		private const float TwitchDecayRate = 12f;          // How fast twitch decays per second
		private const float TwitchStrength = 25f;           // Max rotation degrees on hit

		// Head tracking
		private Vector3 _headTrackRotation;                 // Current smoothed head rotation (degrees: X=pitch, Y=yaw)
		private const float HeadTrackSpeed = 6f;            // Smoothing speed (higher = faster)
		private const float HeadTrackRange = 12f;           // Max distance to track a player
		private const float HeadMaxYaw = 70f;               // Max horizontal head turn in degrees
		private const float HeadMaxPitch = 30f;             // Max vertical head tilt in degrees

		// Health
		/// <summary>NPC health. Server-authoritative.</summary>
		public float Health { get; set; } = 100f;

		/// <summary>Maximum NPC health.</summary>
		public float MaxHealth { get; set; } = 100f;

		/// <summary>True when health is at or below zero.</summary>
		public bool IsDead => Health <= 0f;

		/// <summary>Current health as a fraction of max health (0..1).</summary>
		public float HealthPercent => MaxHealth > 0 ? Health / MaxHealth : 0f;

		/// <summary>Team ID for ally/enemy determination. 0 = neutral (no team).</summary>
		public int Team { get; set; }

		/// <summary>Whether the NPC is currently crouching.</summary>
		public bool IsCrouching { get; set; }

		/// <summary>Whether the NPC has a weapon equipped.</summary>
		public bool WeaponEquipped { get; set; }

		// Animation override timer — suppresses auto walk/idle animation while active
		private float _animOverrideTimer;

		/// <summary>Stable texture identifier used by the client renderer.</summary>
		public string TextureAssetId => string.IsNullOrWhiteSpace(_textureName)
			? AvailableTextures[0]
			: _textureName;

		/// <summary>Renderer-neutral facing direction.</summary>
		public Vector3 LookDirection => _lookDirection;

		/// <summary>Renderer-neutral head tracking rotation in degrees.</summary>
		public Vector3 HeadTrackRotation => _headTrackRotation;

		/// <summary>Current renderer-neutral animation clip identifier.</summary>
		public string CurrentAnimationName => _renderAnimationName;

		/// <summary>Sets the NPC look direction (used by client-side interpolation).</summary>
		public void SetLookDirection(Vector3 dir) => _lookDirection = dir;

		/// <summary>Gets the path follower for this NPC (null if not initialized).</summary>
		public PathFollower GetPathFollower() => _pathFollower;

		/// <summary>
		/// Sets the stable NPC skin asset ID. Synced via spawn properties.
		/// </summary>
		public void SetTextureName(string textureName)
		{
			_textureName = textureName;
		}

		/// <summary>
		/// Assigns an AI behavior program to this NPC.
		/// The program is executed by the server each tick.
		/// </summary>
		public void SetAIProgram(AIStep[] program)
		{
			_aiRunner = program != null && program.Length > 0 ? new AIRunner(program, Logging) : null;
		}

		/// <summary>Applies damage to the NPC, clamping health to zero.</summary>
		public void TakeDamage(float amount)
		{
			if (IsDead) return;
			Health = MathF.Max(0f, Health - amount);
		}

		/// <summary>Raises an AI event on this NPC's AI runner.</summary>
		public void RaiseAIEvent(AIEvent evt) => _aiRunner?.RaiseEvent(evt, NetworkId);

		/// <summary>
		/// Called when a player sends a chat message. Pushes the message to the AI runner
		/// and raises the OnPlayerChat event.
		/// </summary>
		public void OnPlayerChat(string message)
		{
			_aiRunner?.PushChatMessage(message);
			_aiRunner?.RaiseEvent(AIEvent.OnPlayerChat, NetworkId);
		}

		/// <summary>Sets the NPC's movement speed based on mode name.</summary>
		public void SetMoveMode(string mode)
		{
			if (_pathFollower == null) return;
			_pathFollower.MoveSpeed = mode switch
			{
				"run" => 4.0f,
				"sprint" => 6.0f,
				_ => 2.5f, // walk
			};
		}

		/// <summary>Sets the NPC crouching state.</summary>
		public void SetCrouching(bool crouching) => IsCrouching = crouching;

		/// <summary>Plays an animation and suppresses auto walk/idle for the given duration.</summary>
		public void PlayAnimationOverride(string name, float duration = 2f)
		{
			_renderAnimationName = string.IsNullOrWhiteSpace(name) ? "idle" : name;
			_animOverrideTimer = duration;
		}

		/// <summary>Applies the compact animation state replicated by the server.</summary>
		public void SetAnimationState(byte animationState)
		{
			_renderAnimationName = animationState switch
			{
				1 => "walk",
				2 => "attack",
				_ => "idle",
			};
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
		/// Applies damage and raises the OnAttacked AI event.
		/// Also notifies allies (same team) via OnAllyAttacked.
		/// </summary>
		public void OnAttacked(float damage = 0f)
		{
			if (damage > 0f)
				TakeDamage(damage);

			_aiRunner?.RaiseEvent(AIEvent.OnAttacked, NetworkId);

			// Notify allies
			if (Team != 0)
			{
				var entMgr = GetEntityManager();
				if (entMgr != null)
				{
					foreach (var ent in entMgr.GetAllEntities())
					{
						if (ent is VEntNPC otherNpc && otherNpc != this && otherNpc.Team == Team)
							otherNpc.RaiseAIEvent(AIEvent.OnAllyAttacked);
					}
				}
			}
		}

		/// <summary>
		/// Makes the NPC display a speech bubble for the given duration.
		/// The text is synced to clients via entity snapshots.
		/// </summary>
		public void Speak(string text, float duration)
		{
			_speechText = text ?? "";
			_speechTimer = duration;
			_speechDirty = true;

			// Notify nearby NPCs of speech (server-side only)
			var entMgr = GetEntityManager();
			if (entMgr != null && entMgr.IsAuthority)
			{
				const float hearingRange = 15f;
				float rangeSq = hearingRange * hearingRange;

				foreach (var ent in entMgr.GetAllEntities())
				{
					if (ent is VEntNPC otherNpc && otherNpc != this)
					{
						if (Vector3.DistanceSquared(Position, otherNpc.Position) < rangeSq)
							otherNpc.RaiseAIEvent(AIEvent.OnNPCChat);
					}
				}
			}
		}

		/// <summary>Returns true if the NPC is currently displaying a speech bubble.</summary>
		public bool IsSpeaking => _speechTimer > 0 && !string.IsNullOrEmpty(_speechText);

		/// <summary>
		/// Applies a twitch effect to a body part (e.g., when hit by gunfire).
		/// The twitch will decay over time automatically.
		/// </summary>
		/// <param name="bodyPartName">Name of the mesh to twitch (e.g., "head", "leg_r").</param>
		/// <param name="hitNormal">Direction of impact (twitch rotates away from this).</param>
		public void TwitchBodyPart(string bodyPartName, Vector3 hitNormal)
		{
			if (string.IsNullOrEmpty(bodyPartName))
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
		private void UpdateHeadTracking(float dt)
		{
			Vector3 targetRotation = Vector3.Zero;

			GameSimulation sim = GetSimulation();
			if (sim != null)
			{
				// Find nearest player within range
				Player nearest = null;
				float nearestDistSq = HeadTrackRange * HeadTrackRange;

				foreach (Player player in sim.Players.GetAllPlayers())
				{
					if (player.IsDead)
						continue;

					float distSq = Vector3.DistanceSquared(Position, player.Position);
					if (distSq < nearestDistSq)
					{
						nearest = player;
						nearestDistSq = distSq;
					}
				}

				if (nearest != null)
				{
					// Direction from NPC head to player head
						Vector3 toPlayer = (nearest.Position + new Vector3(0, 1.5f, 0)) - (Position + new Vector3(0, Size.Y * 0.85f, 0));

					// Body forward direction from look direction (XZ plane)
					float bodyAngle = MathF.Atan2(_lookDirection.X, _lookDirection.Z);

					// Target direction angle
					Vector3 toPlayerHoriz = new Vector3(toPlayer.X, 0, toPlayer.Z);
					if (toPlayerHoriz.LengthSquared() > 0.001f)
					{
						float targetAngle = MathF.Atan2(toPlayerHoriz.X, toPlayerHoriz.Z);

						// Relative yaw (difference between target and body heading)
						float relativeYaw = Utils.ToDeg(targetAngle - bodyAngle);

						// Normalize to -180..180
						while (relativeYaw > 180f) relativeYaw -= 360f;
						while (relativeYaw < -180f) relativeYaw += 360f;

						// Clamp to max head turn range
						relativeYaw = Math.Clamp(relativeYaw, -HeadMaxYaw, HeadMaxYaw);

						// Pitch (vertical)
							float horizDist = toPlayerHoriz.Length();
							float pitch = Utils.ToDeg(MathF.Atan2(toPlayer.Y, horizDist));
						pitch = Math.Clamp(pitch, -HeadMaxPitch, HeadMaxPitch);

						targetRotation = new Vector3(pitch, relativeYaw, 0);
					}
				}
			}

			// Smooth interpolation toward target
			float t = 1f - MathF.Exp(-HeadTrackSpeed * dt);
			_headTrackRotation = Vector3.Lerp(_headTrackRotation, targetRotation, t);

			// Zero out tiny values
			if (_headTrackRotation.LengthSquared() < 0.01f)
				_headTrackRotation = Vector3.Zero;
		}

		protected override void WriteSpawnPropertiesExtra(BinaryWriter writer)
		{
			writer.Write(_textureName ?? string.Empty);
		}

		protected override void ReadSpawnPropertiesExtra(BinaryReader reader)
		{
			string texName = reader.ReadString();
			if (!string.IsNullOrEmpty(texName))
				SetTextureName(texName);
		}

		protected override void WriteSnapshotExtra(BinaryWriter writer)
		{
			// Look direction (12 bytes)
			writer.Write(_lookDirection.X);
			writer.Write(_lookDirection.Y);
			writer.Write(_lookDirection.Z);

			// Preserve the established wire representation. Animation is inferred by
			// clients from replicated motion and remains an empty length-prefixed value.
			writer.Write(string.Empty);
		}

		protected override void ReadSnapshotExtra(BinaryReader reader)
		{
			// Look direction
			_lookDirection = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

			// Current animation name
			string animName = reader.ReadString();
			if (!string.IsNullOrWhiteSpace(animName))
				_renderAnimationName = animName;
		}
	}
}
