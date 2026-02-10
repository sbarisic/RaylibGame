using Raylib_cs;
using System;
using System.Diagnostics;
using System.Numerics;
using Voxelgine.Engine.DI;
using Voxelgine.Graphics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Represents a remote player visible to the local client.
	/// Lightweight rendering-only class — no physics, input, GUI, or viewmodel.
	/// Interpolates between received server snapshots for smooth visual movement.
	/// </summary>
	public class RemotePlayer
	{
		/// <summary>Interpolation delay in seconds. Remote players are rendered this far behind real time.</summary>
		public const float InterpolationDelay = 0.1f;

		/// <summary>Unique player ID assigned by the server.</summary>
		public int PlayerId { get; }

		/// <summary>Display name of the remote player.</summary>
		public string PlayerName { get; set; }

		/// <summary>Current interpolated render position (eye level).</summary>
		public Vector3 Position { get; private set; }

		/// <summary>Current velocity (from latest snapshot, used for animation state).</summary>
		public Vector3 Velocity { get; private set; }

		/// <summary>Current camera angle (yaw, pitch) in degrees for head/body orientation.</summary>
		public Vector2 CameraAngle { get; private set; }

		/// <summary>Player collision dimensions matching <see cref="Player"/> constants.</summary>
		public const float PlayerHeight = Player.PlayerHeight;
		public const float PlayerEyeOffset = Player.PlayerEyeOffset;
		public const float PlayerRadius = Player.PlayerRadius;

		// Model
		private CustomModel _model;
		private bool _modelLoaded;
		private NPCAnimator _animator;
		private IFishEngineRunner _eng;
		private IFishLogging _logging;

		// Interpolation buffer — ring buffer of timestamped snapshots
		private readonly SnapshotBuffer<PlayerSnapshot> _snapshotBuffer = new SnapshotBuffer<PlayerSnapshot>();

		// Footstep detection for remote player sounds
		private readonly Stopwatch _footstepWatch = Stopwatch.StartNew();
		private long _lastFootstepMs;
		const float FootstepSpeedThreshold = 1.0f;
		const long FootstepIntervalMs = 350;

		/// <summary>Snapshot data stored in the interpolation buffer.</summary>
		public struct PlayerSnapshot
		{
			public Vector3 Position;
			public Vector3 Velocity;
			public Vector2 CameraAngle;
			public byte AnimationState;
		}

		public RemotePlayer(int playerId, string playerName, IFishEngineRunner eng)
		{
			PlayerId = playerId;
			PlayerName = playerName ?? $"Player {playerId}";
			_eng = eng;
			_logging = eng.DI.GetRequiredService<IFishLogging>();

			Position = Vector3.Zero;
			Velocity = Vector3.Zero;
			CameraAngle = Vector2.Zero;

			LoadModel();
		}

		private void LoadModel()
		{
			try
			{
				MinecraftModel jsonModel = ResMgr.GetJsonModel("npc/humanoid.json");
				_model = MeshGenerator.Generate(jsonModel);
				_model.SetupHumanoidHierarchy();

				_animator = new NPCAnimator(_model, _logging);
				_animator.LoadAllClips();
				_animator.Play("idle");

				_modelLoaded = true;
			}
			catch (Exception ex)
			{
				_logging?.WriteLine($"RemotePlayer: Failed to load model for player {PlayerId}: {ex.Message}");
				_modelLoaded = false;
			}
		}

		/// <summary>
		/// Applies a snapshot from the server (from <see cref="WorldSnapshotPacket.PlayerEntry"/>).
		/// Adds the snapshot to the interpolation buffer.
		/// </summary>
		public void ApplySnapshot(Vector3 position, Vector3 velocity, Vector2 cameraAngle, byte animationState, float currentTime)
		{
			var snapshot = new PlayerSnapshot
			{
				Position = position,
				Velocity = velocity,
				CameraAngle = cameraAngle,
				AnimationState = animationState,
			};
			_snapshotBuffer.Add(snapshot, currentTime);

			// On first snapshot, set render position immediately
			if (_snapshotBuffer.Count == 1)
			{
				Position = position;
				Velocity = velocity;
				CameraAngle = cameraAngle;
			}
		}

		/// <summary>Current animation state byte from the server (0=idle, 1=walk, 2=attack).</summary>
		private byte _currentAnimState;

		/// <summary>
		/// Updates interpolated position for rendering. Call each frame.
		/// </summary>
		public void Update(float currentTime, float deltaTime)
		{
			if (_snapshotBuffer.Count == 0)
				return;

			byte animState = _currentAnimState;

			if (_snapshotBuffer.Count >= 2)
			{
				float renderTime = currentTime - InterpolationDelay;

				if (_snapshotBuffer.Sample(renderTime, out var from, out var to, out float t))
				{
					Position = Vector3.Lerp(from.Position, to.Position, t);
					Velocity = Vector3.Lerp(from.Velocity, to.Velocity, t);
					CameraAngle = LerpAngle(from.CameraAngle, to.CameraAngle, t);
					// Use the 'to' snapshot's animation state (most recent authoritative state)
					animState = to.AnimationState;
				}
			}

			_currentAnimState = animState;

			// Update animation based on server-driven animation state
			if (_animator != null)
			{
				// Base layer: idle or walk
				string targetBaseAnim = animState == 1 ? "walk" : "idle";
				if (_animator.CurrentAnimation != targetBaseAnim)
					_animator.Play(targetBaseAnim);

				// Action layer: attack animation overlay
				if (animState == 2)
				{
					if (!_attackLayerActive)
					{
						_animator.PlayOnLayer("action", "attack");
						_animator.SetLayerWeight("action", 1.0f);
						_attackLayerActive = true;
					}
				}
				else if (_attackLayerActive)
				{
					_animator.StopLayer("action");
					_attackLayerActive = false;
				}

				_animator.Update(deltaTime);
			}

			// Apply head pitch from camera angle after animation update
			if (_modelLoaded && _model != null)
			{
				CustomMesh headMesh = _model.GetMeshByName("head");
				if (headMesh != null)
				{
					// CameraAngle.Y is pitch in degrees; clamp to reasonable range
					float pitch = Math.Clamp(CameraAngle.Y, -80f, 80f);
					headMesh.AnimationRotation = new Vector3(pitch, headMesh.AnimationRotation.Y, headMesh.AnimationRotation.Z);
					headMesh.UpdateAnimationMatrix();
				}
			}
		}

		private bool _attackLayerActive;

		/// <summary>
		/// Renders the remote player model in 3D space.
		/// </summary>
		public void Draw3D()
		{
			if (!_modelLoaded || _model == null)
			{
				// Fallback: draw a simple capsule placeholder
				DrawPlaceholder();
				return;
			}

			// Position the model at feet position
			Vector3 feetPos = Position - new Vector3(0, PlayerEyeOffset, 0);
			_model.Position = feetPos;

			// Convert camera yaw angle to look direction for model facing
			float yawRad = CameraAngle.X * (MathF.PI / 180f);
			_model.LookDirection = new Vector3(MathF.Sin(yawRad), 0, MathF.Cos(yawRad));

			// Sample world lighting at feet position
			Color lightColor = _eng.MultiplayerGameState?.Map?.GetLightColor(feetPos) ?? Color.White;
			_model.Draw(lightColor);

			// Draw held item at right hand position
			DrawHeldItem();

			if (_eng.DebugMode)
				{
					// Draw bounding box
					Vector3 bboxMin = new Vector3(feetPos.X - PlayerRadius, feetPos.Y, feetPos.Z - PlayerRadius);
					Vector3 bboxMax = new Vector3(feetPos.X + PlayerRadius, feetPos.Y + PlayerHeight, feetPos.Z + PlayerRadius);
					Raylib.DrawBoundingBox(new BoundingBox(bboxMin, bboxMax), Color.Green);
				}
		}

		private void DrawPlaceholder()
		{
			Vector3 feetPos = Position - new Vector3(0, PlayerEyeOffset, 0);
			Vector3 center = feetPos + new Vector3(0, PlayerHeight / 2f, 0);

			// Draw a wireframe capsule approximation (cylinder + spheres)
			Raylib.DrawCubeWires(center, PlayerRadius * 2, PlayerHeight, PlayerRadius * 2, Color.Lime);

			// Draw look direction line from eye position
			float yawRad = CameraAngle.X * (MathF.PI / 180f);
			Vector3 lookDir = new Vector3(MathF.Sin(yawRad), 0, MathF.Cos(yawRad));
			Raylib.DrawLine3D(Position, Position + lookDir * 1.5f, Color.Yellow);
		}

		/// <summary>Max distance at which name tags are visible.</summary>
		private const float MaxNameTagDistance = 50f;

		/// <summary>Distance at which name tags start fading out.</summary>
		private const float NameTagFadeStart = 30f;

		/// <summary>Base font size for name tags at close range.</summary>
		private const int NameTagBaseFontSize = 20;

		/// <summary>Minimum font size for name tags at far range.</summary>
		private const int NameTagMinFontSize = 10;

		/// <summary>
		/// Draws the player's name tag as billboard text above their head.
		/// Scales with distance, fades at long range, hidden when obstructed by blocks.
		/// Must be called during the 2D rendering pass (after EndMode3D).
		/// </summary>
		public void DrawNameTag(Camera3D camera, ChunkMap map)
		{
			// Name tag position: above the player's head
			Vector3 feetPos = Position - new Vector3(0, PlayerEyeOffset, 0);
			Vector3 nameTagWorldPos = feetPos + new Vector3(0, PlayerHeight + 0.3f, 0);

			// Distance check
			float distance = Vector3.Distance(camera.Position, nameTagWorldPos);
			if (distance > MaxNameTagDistance || distance < 0.5f)
				return;

			// Check if behind camera (dot product with camera forward)
			Vector3 toTag = Vector3.Normalize(nameTagWorldPos - camera.Position);
			Vector3 camForward = Vector3.Normalize(camera.Target - camera.Position);
			if (Vector3.Dot(toTag, camForward) <= 0)
				return;

			// Obstruction check: raycast from camera toward name tag
			if (map != null)
			{
				Vector3 dir = nameTagWorldPos - camera.Position;
				Vector3 dirNorm = Vector3.Normalize(dir);
				if (map.RaycastPrecise(camera.Position, distance, dirNorm, out Vector3 hitPoint, out Vector3 _))
				{
					float hitDist = Vector3.Distance(camera.Position, hitPoint);
					if (hitDist < distance - 0.5f)
						return;
				}
			}

			// Project to screen
			Vector2 screenPos = Raylib.GetWorldToScreen(nameTagWorldPos, camera);

			// Check if within screen bounds
			int screenW = Raylib.GetScreenWidth();
			int screenH = Raylib.GetScreenHeight();
			if (screenPos.X < -100 || screenPos.X > screenW + 100 || screenPos.Y < -50 || screenPos.Y > screenH + 50)
				return;

			// Scale font size with distance
			float distanceFactor = 1f - Math.Clamp((distance - 5f) / (MaxNameTagDistance - 5f), 0f, 1f);
			int fontSize = (int)(NameTagMinFontSize + (NameTagBaseFontSize - NameTagMinFontSize) * distanceFactor);
			if (fontSize < NameTagMinFontSize)
				fontSize = NameTagMinFontSize;

			// Fade alpha with distance
			float alpha;
			if (distance > NameTagFadeStart)
				alpha = 1f - Math.Clamp((distance - NameTagFadeStart) / (MaxNameTagDistance - NameTagFadeStart), 0f, 1f);
			else
				alpha = 1f;

			byte alphaByte = (byte)(alpha * 255);
			if (alphaByte == 0)
				return;

			// Measure text for centering
			int textWidth = Raylib.MeasureText(PlayerName, fontSize);
			int textX = (int)(screenPos.X - textWidth / 2f);
			int textY = (int)(screenPos.Y - fontSize / 2f);

			// Draw background for readability
			int padding = 4;
			int bgAlpha = (int)(alphaByte * 0.5f);
			Raylib.DrawRectangle(textX - padding, textY - padding / 2, textWidth + padding * 2, fontSize + padding, new Color(0, 0, 0, bgAlpha));

			// Draw name text
			Raylib.DrawText(PlayerName, textX, textY, fontSize, new Color(255, 255, 255, (int)alphaByte));
		}

		/// <summary>
		/// Draws a simple held item (cube) at the right hand position using the hand mesh's world transform.
		/// </summary>
		private void DrawHeldItem()
		{
			if (!_modelLoaded || _model == null)
				return;

			CustomMesh handR = _model.GetMeshByName("hand_r");
			if (handR == null)
				return;

			// Reconstruct the model matrix (same logic as CustomModel.GetModelMatrix)
			Vector2 dir = Vector2.Normalize(new Vector2(_model.LookDirection.X, _model.LookDirection.Z));
			float ang = MathF.Atan2(dir.X, dir.Y) - MathF.PI;
			Matrix4x4 modelMat = Matrix4x4.CreateRotationY(ang) * Matrix4x4.CreateTranslation(_model.Position);

			// Get the hand's world-space transform
			Matrix4x4 handWorld = handR.GetWorldMatrix(modelMat);

			// Extract world position from the transform matrix
			Vector3 handPos = new Vector3(handWorld.M41, handWorld.M42, handWorld.M43);

			// Draw a small cube representing the held item
			Raylib.DrawCube(handPos, 0.12f, 0.12f, 0.35f, Color.Gray);
			Raylib.DrawCubeWires(handPos, 0.12f, 0.12f, 0.35f, Color.DarkGray);
		}

		/// <summary>
		/// Returns true if a footstep sound should be played for this remote player.
		/// Detects walking from XZ velocity magnitude and a cooldown timer.
		/// </summary>
		public bool TryPlayFootstep()
		{
			float xzSpeed = new Vector2(Velocity.X, Velocity.Z).Length();
			if (xzSpeed < FootstepSpeedThreshold)
				return false;

			// Skip if likely airborne (significant vertical velocity)
			if (MathF.Abs(Velocity.Y) > 2.0f)
				return false;

			long now = _footstepWatch.ElapsedMilliseconds;
			if (now - _lastFootstepMs < FootstepIntervalMs)
				return false;

			_lastFootstepMs = now;
			return true;
		}

		/// <summary>
		/// Directly sets the position (used for initial placement from <see cref="PlayerJoinedPacket"/>).
		/// Resets the interpolation buffer and starts fresh from this position.
		/// </summary>
		public void SetPosition(Vector3 position)
		{
			Position = position;
			_snapshotBuffer.Reset();
		}

		/// <summary>
		/// Interpolates angles handling wrapping (for angles in degrees).
		/// </summary>
		private static Vector2 LerpAngle(Vector2 from, Vector2 to, float t)
		{
			return new Vector2(
				LerpAngleSingle(from.X, to.X, t),
				LerpAngleSingle(from.Y, to.Y, t)
			);
		}

		private static float LerpAngleSingle(float from, float to, float t)
		{
			float diff = to - from;
			while (diff > 180f) diff -= 360f;
			while (diff < -180f) diff += 360f;
			return from + diff * t;
		}
	}
}
