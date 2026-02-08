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
		public void ApplySnapshot(Vector3 position, Vector3 velocity, Vector2 cameraAngle, float currentTime)
		{
			var snapshot = new PlayerSnapshot
			{
				Position = position,
				Velocity = velocity,
				CameraAngle = cameraAngle,
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

		/// <summary>
		/// Updates interpolated position for rendering. Call each frame.
		/// </summary>
		public void Update(float currentTime, float deltaTime)
		{
			if (_snapshotBuffer.Count == 0)
				return;

			if (_snapshotBuffer.Count >= 2)
			{
				float renderTime = currentTime - InterpolationDelay;

				if (_snapshotBuffer.Sample(renderTime, out var from, out var to, out float t))
				{
					Position = Vector3.Lerp(from.Position, to.Position, t);
					Velocity = Vector3.Lerp(from.Velocity, to.Velocity, t);
					CameraAngle = LerpAngle(from.CameraAngle, to.CameraAngle, t);
				}
			}

			// Update animation based on velocity
			if (_animator != null)
			{
				float speed = new Vector2(Velocity.X, Velocity.Z).Length();
				string targetAnim = speed > 0.5f ? "walk" : "idle";

				if (_animator.CurrentAnimation != targetAnim)
					_animator.Play(targetAnim);

				_animator.Update(deltaTime);
			}
		}

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

			_model.Draw();

			if (_eng.DebugMode)
			{
				// Draw bounding box
				Vector3 bboxMin = new Vector3(feetPos.X - PlayerRadius, feetPos.Y, feetPos.Z - PlayerRadius);
				Vector3 bboxMax = new Vector3(feetPos.X + PlayerRadius, feetPos.Y + PlayerHeight, feetPos.Z + PlayerRadius);
				Raylib.DrawBoundingBox(new BoundingBox(bboxMin, bboxMax), Color.Green);

				// Draw player name above head
				Vector3 namePos = feetPos + new Vector3(0, PlayerHeight + 0.3f, 0);
				DrawPlayerName(namePos);
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

		private void DrawPlayerName(Vector3 worldPos)
		{
			// Simple debug name rendering at world position
			// Full billboard text rendering is a separate TODO (Player name tags)
			Raylib.DrawSphere(worldPos, 0.05f, Color.White);
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
