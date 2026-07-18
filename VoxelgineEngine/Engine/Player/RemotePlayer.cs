using System.Diagnostics;
using System.Numerics;
using Voxelgine.Engine.DI;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Renderer-neutral state for a remote player. The client presentation adapter
	/// consumes its interpolated transform and compact animation state.
	/// </summary>
	public class RemotePlayer
	{
		public const float InterpolationDelay = 0.1f;
		public const float PlayerHeight = Player.PlayerHeight;
		public const float PlayerEyeOffset = Player.PlayerEyeOffset;
		public const float PlayerRadius = Player.PlayerRadius;

		private const float FootstepSpeedThreshold = 1f;
		private const long FootstepIntervalMs = 350;

		private readonly SnapshotBuffer<PlayerSnapshot> _snapshotBuffer = new();
		private readonly Stopwatch _footstepWatch = Stopwatch.StartNew();
		private long _lastFootstepMs;
		private byte _currentAnimationState;

		public int PlayerId { get; }

		public string PlayerName { get; set; }

		/// <summary>Current interpolated eye position.</summary>
		public Vector3 Position { get; private set; }

		public Vector3 Velocity { get; private set; }

		/// <summary>Yaw and pitch in degrees.</summary>
		public Vector2 CameraAngle { get; private set; }

		public byte CurrentAnimationState => _currentAnimationState;

		public Vector3 FeetPosition => Position - new Vector3(0f, PlayerEyeOffset, 0f);

		public Vector3 LookDirection
		{
			get
			{
				float yawRadians = CameraAngle.X * (MathF.PI / 180f);
				return new Vector3(MathF.Sin(yawRadians), 0f, MathF.Cos(yawRadians));
			}
		}

		public AABB CollisionBounds
		{
			get
			{
				Vector3 feet = FeetPosition;
				return AABB.FromMinMax(
					new Vector3(feet.X - PlayerRadius, feet.Y, feet.Z - PlayerRadius),
					new Vector3(feet.X + PlayerRadius, feet.Y + PlayerHeight, feet.Z + PlayerRadius)
				);
			}
		}

		public struct PlayerSnapshot
		{
			public Vector3 Position;
			public Vector3 Velocity;
			public Vector2 CameraAngle;
			public byte AnimationState;
		}

		public RemotePlayer(int playerId, string playerName, IFishEngineRunner engine)
		{
			PlayerId = playerId;
			PlayerName = playerName ?? $"Player {playerId}";
			_ = engine;
		}

		public void ApplySnapshot(
			Vector3 position,
			Vector3 velocity,
			Vector2 cameraAngle,
			byte animationState,
			float currentTime)
		{
			PlayerSnapshot snapshot = new()
			{
				Position = position,
				Velocity = velocity,
				CameraAngle = cameraAngle,
				AnimationState = animationState,
			};
			_snapshotBuffer.Add(snapshot, currentTime);

			if (_snapshotBuffer.Count == 1)
			{
				Position = position;
				Velocity = velocity;
				CameraAngle = cameraAngle;
				_currentAnimationState = animationState;
			}
		}

		public void Update(float currentTime, float deltaTime)
		{
			_ = deltaTime;
			if (_snapshotBuffer.Count == 0)
				return;

			if (_snapshotBuffer.Count < 2)
				return;

			float renderTime = currentTime - InterpolationDelay;
			if (!_snapshotBuffer.Sample(renderTime, out PlayerSnapshot from, out PlayerSnapshot to, out float amount))
				return;

			Position = Vector3.Lerp(from.Position, to.Position, amount);
			Velocity = Vector3.Lerp(from.Velocity, to.Velocity, amount);
			CameraAngle = LerpAngle(from.CameraAngle, to.CameraAngle, amount);
			_currentAnimationState = to.AnimationState;
		}

		/// <summary>Returns true when motion has crossed the remote footstep cadence.</summary>
		public bool TryPlayFootstep()
		{
			float horizontalSpeed = new Vector2(Velocity.X, Velocity.Z).Length();
			if (horizontalSpeed < FootstepSpeedThreshold)
				return false;

			if (MathF.Abs(Velocity.Y) > 2f)
				return false;

			long now = _footstepWatch.ElapsedMilliseconds;
			if (now - _lastFootstepMs < FootstepIntervalMs)
				return false;

			_lastFootstepMs = now;
			return true;
		}

		public void SetPosition(Vector3 position)
		{
			Position = position;
			_snapshotBuffer.Reset();
		}

		private static Vector2 LerpAngle(Vector2 from, Vector2 to, float amount)
		{
			return new Vector2(
				LerpAngleSingle(from.X, to.X, amount),
				LerpAngleSingle(from.Y, to.Y, amount)
			);
		}

		private static float LerpAngleSingle(float from, float to, float amount)
		{
			float difference = to - from;
			while (difference > 180f)
				difference -= 360f;
			while (difference < -180f)
				difference += 360f;

			return from + difference * amount;
		}
	}
}
