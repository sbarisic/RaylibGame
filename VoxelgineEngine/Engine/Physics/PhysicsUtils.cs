using System;
using System.Numerics;

namespace Voxelgine.Engine {
	/// <summary>
	/// Shared physics utilities for collision detection and response.
	/// Used by both Player and EntityManager for consistent physics behavior.
	/// Pure math — no world/ChunkMap dependency.
	/// </summary>
	public static class PhysicsUtils {
		/// <summary>
		/// Quake-style ClipVelocity - clips velocity against a surface normal.
		/// Preserves speed along the surface for smooth wall sliding.
		/// </summary>
		/// <param name="velocity">The velocity to clip.</param>
		/// <param name="normal">The surface normal to clip against.</param>
		/// <param name="overbounce">Overbounce factor (1.0 = no bounce, >1.0 = slight push away).</param>
		/// <returns>The clipped velocity.</returns>
		public static Vector3 ClipVelocity(Vector3 velocity, Vector3 normal, float overbounce = 1.001f) {
			float backoff = Vector3.Dot(velocity, normal) * overbounce;
			Vector3 clipped = velocity - normal * backoff;

			// Prevent tiny oscillations
			if (MathF.Abs(clipped.X) < 0.001f) clipped.X = 0;
			if (MathF.Abs(clipped.Y) < 0.001f) clipped.Y = 0;
			if (MathF.Abs(clipped.Z) < 0.001f) clipped.Z = 0;

			return clipped;
		}

		/// <summary>
		/// Creates a player AABB from eye position.
		/// Default values match Player.PlayerRadius (0.4f), Player.PlayerHeight (1.7f), Player.PlayerEyeOffset (1.6f).
		/// </summary>
		public static AABB CreatePlayerAABB(Vector3 eyePosition, float radius = 0.4f, float height = 1.7f, float eyeOffset = 1.6f) {
			Vector3 feetPos = eyePosition - new Vector3(0, eyeOffset, 0);
			return new AABB(
				new Vector3(feetPos.X - radius, feetPos.Y, feetPos.Z - radius),
				new Vector3(radius * 2, height, radius * 2)
			);
		}

		/// <summary>
		/// Creates an entity AABB from position and size.
		/// </summary>
		public static AABB CreateEntityAABB(Vector3 position, Vector3 size) {
			return new AABB(position, size);
		}

		/// <summary>
		/// Quake-style ground acceleration.
		/// </summary>
		public static void Accelerate(ref Vector3 velocity, Vector3 wishdir, float wishspeed, float accel, float dt) {
			float currentspeed = Vector3.Dot(velocity, wishdir);
			float addspeed = wishspeed - currentspeed;
			if (addspeed <= 0)
				return;
			float accelspeed = accel * dt * wishspeed;
			if (accelspeed > addspeed)
				accelspeed = addspeed;
			velocity += accelspeed * wishdir;
		}

		/// <summary>
		/// Quake-style air acceleration (key for strafe jumping).
		/// </summary>
		public static void AirAccelerate(ref Vector3 velocity, Vector3 wishdir, float wishspeed, float accel, float dt, float maxAirWishSpeed = 0.7f) {
			float wishspd = wishspeed;
			if (wishspd > maxAirWishSpeed)
				wishspd = maxAirWishSpeed;

			float currentspeed = Vector3.Dot(velocity, wishdir);
			float addspeed = wishspd - currentspeed;
			if (addspeed <= 0)
				return;
			float accelspeed = accel * dt * wishspeed;
			if (accelspeed > addspeed)
				accelspeed = addspeed;
			velocity += accelspeed * wishdir;
		}

		/// <summary>
		/// Applies ground friction to velocity.
		/// </summary>
		public static void ApplyFriction(ref Vector3 velocity, float friction, float dt) {
			float speed = MathF.Sqrt(velocity.X * velocity.X + velocity.Z * velocity.Z);
			if (speed < 0.1f) {
				velocity.X = 0;
				velocity.Z = 0;
				return;
			}

			float drop = speed * friction * dt;
			float newspeed = speed - drop;
			if (newspeed < 0)
				newspeed = 0;
			newspeed /= speed;

			velocity.X *= newspeed;
			velocity.Z *= newspeed;
		}

		/// <summary>
		/// Applies gravity to velocity.
		/// </summary>
		public static void ApplyGravity(ref Vector3 velocity, float gravity, float dt) {
			velocity.Y -= gravity * dt;
		}
	}
}
