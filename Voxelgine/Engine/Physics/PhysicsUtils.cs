using System.Numerics;
using Voxelgine.Graphics;

namespace Voxelgine.Engine {
	/// <summary>
	/// Shared physics utilities for collision detection and response.
	/// Used by both Player and EntityManager for consistent physics behavior.
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
		/// </summary>
		public static AABB CreatePlayerAABB(Vector3 eyePosition, float radius = Player.PlayerRadius, float height = Player.PlayerHeight, float eyeOffset = Player.PlayerEyeOffset) {
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
		/// Tests if a position with given size overlaps any blocks in the world.
		/// </summary>
		public static bool CollidesWithWorld(ChunkMap map, Vector3 position, Vector3 size) {
			return map.HasBlocksInBounds(position, size);
		}

		/// <summary>
		/// Tests if an AABB overlaps any blocks in the world.
		/// </summary>
		public static bool CollidesWithWorld(ChunkMap map, AABB aabb) {
			return map.HasBlocksInBoundsMinMax(aabb.Min, aabb.Max);
		}

		/// <summary>
		/// Performs axis-separated movement with collision response.
		/// Returns the new position after collision.
		/// </summary>
		/// <param name="map">The chunk map for collision testing.</param>
		/// <param name="position">Current position (bottom-center for entities, or feet position).</param>
		/// <param name="size">Size of the collision box.</param>
		/// <param name="velocity">Current velocity (modified on collision).</param>
		/// <param name="dt">Delta time.</param>
		/// <returns>New position after collision.</returns>
		public static Vector3 MoveWithCollision(ChunkMap map, Vector3 position, Vector3 size, ref Vector3 velocity, float dt) {
			Vector3 newPos = position;
			Vector3 move = velocity * dt;

			// X axis
			if (!map.HasBlocksInBounds(new Vector3(newPos.X + move.X, newPos.Y, newPos.Z), size))
				newPos.X += move.X;
			else
				velocity.X = 0;

			// Y axis
			if (!map.HasBlocksInBounds(new Vector3(newPos.X, newPos.Y + move.Y, newPos.Z), size))
				newPos.Y += move.Y;
			else
				velocity.Y = 0;

			// Z axis
			if (!map.HasBlocksInBounds(new Vector3(newPos.X, newPos.Y, newPos.Z + move.Z), size))
				newPos.Z += move.Z;
			else
				velocity.Z = 0;

			return newPos;
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
