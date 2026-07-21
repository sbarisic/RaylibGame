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
	public partial class VEntNPC : VoxEntity
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
