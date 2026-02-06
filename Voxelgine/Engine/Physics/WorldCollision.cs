using System.Numerics;
using Voxelgine.Graphics;

namespace Voxelgine.Engine {
	/// <summary>
	/// World-dependent collision utilities that require ChunkMap access.
	/// Separated from PhysicsUtils (pure math) for project split.
	/// </summary>
	public static class WorldCollision {
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
	}
}
