using System;
using System.Collections.Generic;
using System.Numerics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Result of a raycast hit against an entity or surface.
	/// </summary>
	public struct RaycastHit
	{
		/// <summary>The entity that was hit (null if no hit).</summary>
		public VoxEntity Entity;
		/// <summary>World position where the ray hit the surface.</summary>
		public Vector3 HitPosition;
		/// <summary>Surface normal at the hit point.</summary>
		public Vector3 HitNormal;
		/// <summary>Distance from ray origin to hit point.</summary>
		public float Distance;
		/// <summary>True if the ray hit something.</summary>
		public bool Hit;

		public static readonly RaycastHit None = new() { Hit = false };
	}

	/// <summary>
	/// Raycasting utilities for entity picking and collision detection.
	/// </summary>
	public static class Raycast
	{
		/// <summary>
		/// Tests if a ray intersects an AABB and returns the hit distance and normal.
		/// Uses the slab method for efficient ray-box intersection.
		/// </summary>
		/// <param name="rayOrigin">Origin point of the ray.</param>
		/// <param name="rayDir">Normalized direction of the ray.</param>
		/// <param name="aabb">The axis-aligned bounding box to test.</param>
		/// <param name="maxDistance">Maximum distance to check.</param>
		/// <param name="hitDistance">Output: distance to intersection point.</param>
		/// <param name="hitNormal">Output: surface normal at intersection.</param>
		/// <returns>True if the ray intersects the AABB within maxDistance.</returns>
		public static bool RayIntersectsAABB(
			Vector3 rayOrigin,
			Vector3 rayDir,
			AABB aabb,
			float maxDistance,
			out float hitDistance,
			out Vector3 hitNormal)
		{
			hitDistance = 0f;
			hitNormal = Vector3.Zero;

			Vector3 min = aabb.Min;
			Vector3 max = aabb.Max;

			// Slab method for ray-AABB intersection
			float tMin = float.NegativeInfinity;
			float tMax = float.PositiveInfinity;
			Vector3 normalMin = Vector3.Zero;

			// X slab
			if (MathF.Abs(rayDir.X) > 1e-8f)
			{
				float t1 = (min.X - rayOrigin.X) / rayDir.X;
				float t2 = (max.X - rayOrigin.X) / rayDir.X;
				Vector3 n1 = -Vector3.UnitX;
				Vector3 n2 = Vector3.UnitX;

				if (t1 > t2)
				{
					(t1, t2) = (t2, t1);
					(n1, n2) = (n2, n1);
				}

				if (t1 > tMin) { tMin = t1; normalMin = n1; }
				if (t2 < tMax) tMax = t2;
			}
			else if (rayOrigin.X < min.X || rayOrigin.X > max.X)
			{
				return false;
			}

			// Y slab
			if (MathF.Abs(rayDir.Y) > 1e-8f)
			{
				float t1 = (min.Y - rayOrigin.Y) / rayDir.Y;
				float t2 = (max.Y - rayOrigin.Y) / rayDir.Y;
				Vector3 n1 = -Vector3.UnitY;
				Vector3 n2 = Vector3.UnitY;

				if (t1 > t2)
				{
					(t1, t2) = (t2, t1);
					(n1, n2) = (n2, n1);
				}

				if (t1 > tMin) { tMin = t1; normalMin = n1; }
				if (t2 < tMax) tMax = t2;
			}
			else if (rayOrigin.Y < min.Y || rayOrigin.Y > max.Y)
			{
				return false;
			}

			// Z slab
			if (MathF.Abs(rayDir.Z) > 1e-8f)
			{
				float t1 = (min.Z - rayOrigin.Z) / rayDir.Z;
				float t2 = (max.Z - rayOrigin.Z) / rayDir.Z;
				Vector3 n1 = -Vector3.UnitZ;
				Vector3 n2 = Vector3.UnitZ;

				if (t1 > t2)
				{
					(t1, t2) = (t2, t1);
					(n1, n2) = (n2, n1);
				}

				if (t1 > tMin) { tMin = t1; normalMin = n1; }
				if (t2 < tMax) tMax = t2;
			}
			else if (rayOrigin.Z < min.Z || rayOrigin.Z > max.Z)
			{
				return false;
			}

			// Check if we have a valid intersection
			if (tMax < tMin || tMax < 0)
				return false;

			// Use tMin if in front of ray, otherwise use tMax (ray starts inside box)
			hitDistance = tMin >= 0 ? tMin : tMax;
			hitNormal = tMin >= 0 ? normalMin : -normalMin;

			return hitDistance <= maxDistance;
		}

		/// <summary>
		/// Casts a ray against a single entity and returns hit information.
		/// </summary>
		/// <param name="rayOrigin">Origin point of the ray.</param>
		/// <param name="rayDir">Normalized direction of the ray.</param>
		/// <param name="entity">The entity to test against.</param>
		/// <param name="maxDistance">Maximum distance to check.</param>
		/// <returns>RaycastHit with hit information, or RaycastHit.None if no hit.</returns>
		public static RaycastHit CastAgainstEntity(
			Vector3 rayOrigin,
			Vector3 rayDir,
			VoxEntity entity,
			float maxDistance = 1000f)
		{
			if (entity == null)
				return RaycastHit.None;

			AABB entityAABB = PhysicsUtils.CreateEntityAABB(entity.Position, entity.Size);

			if (RayIntersectsAABB(rayOrigin, rayDir, entityAABB, maxDistance, out float dist, out Vector3 normal))
			{
				return new RaycastHit
				{
					Hit = true,
					Entity = entity,
					Distance = dist,
					HitPosition = rayOrigin + rayDir * dist,
					HitNormal = normal
				};
			}

			return RaycastHit.None;
		}

		/// <summary>
		/// Casts a ray against all entities and returns the closest hit.
		/// </summary>
		/// <param name="rayOrigin">Origin point of the ray.</param>
		/// <param name="rayDir">Normalized direction of the ray.</param>
		/// <param name="entities">Collection of entities to test against.</param>
		/// <param name="maxDistance">Maximum distance to check.</param>
		/// <param name="excludeEntity">Optional entity to exclude from testing (e.g., the shooter).</param>
		/// <returns>RaycastHit with closest hit information, or RaycastHit.None if no hit.</returns>
		public static RaycastHit CastAgainstEntities(
			Vector3 rayOrigin,
			Vector3 rayDir,
			IEnumerable<VoxEntity> entities,
			float maxDistance = 1000f,
			VoxEntity excludeEntity = null)
		{
			RaycastHit closestHit = RaycastHit.None;
			float closestDist = maxDistance;

			foreach (var entity in entities)
			{
				if (entity == null || entity == excludeEntity)
					continue;

				AABB entityAABB = PhysicsUtils.CreateEntityAABB(entity.Position, entity.Size);

				if (RayIntersectsAABB(rayOrigin, rayDir, entityAABB, closestDist, out float dist, out Vector3 normal))
				{
					if (dist < closestDist)
					{
						closestDist = dist;
						closestHit = new RaycastHit
						{
							Hit = true,
							Entity = entity,
							Distance = dist,
							HitPosition = rayOrigin + rayDir * dist,
							HitNormal = normal
						};
					}
				}
			}

			return closestHit;
		}

		/// <summary>
		/// Casts a ray against all entities and returns all hits sorted by distance.
		/// </summary>
		/// <param name="rayOrigin">Origin point of the ray.</param>
		/// <param name="rayDir">Normalized direction of the ray.</param>
		/// <param name="entities">Collection of entities to test against.</param>
		/// <param name="maxDistance">Maximum distance to check.</param>
		/// <param name="excludeEntity">Optional entity to exclude from testing.</param>
		/// <returns>List of all hits sorted by distance (closest first).</returns>
		public static List<RaycastHit> CastAgainstEntitiesAll(
			Vector3 rayOrigin,
			Vector3 rayDir,
			IEnumerable<VoxEntity> entities,
			float maxDistance = 1000f,
			VoxEntity excludeEntity = null)
		{
			var hits = new List<RaycastHit>();

			foreach (var entity in entities)
			{
				if (entity == null || entity == excludeEntity)
					continue;

				AABB entityAABB = PhysicsUtils.CreateEntityAABB(entity.Position, entity.Size);

				if (RayIntersectsAABB(rayOrigin, rayDir, entityAABB, maxDistance, out float dist, out Vector3 normal))
				{
					hits.Add(new RaycastHit
					{
						Hit = true,
						Entity = entity,
						Distance = dist,
						HitPosition = rayOrigin + rayDir * dist,
						HitNormal = normal
					});
				}
			}

			// Sort by distance (closest first)
			hits.Sort((a, b) => a.Distance.CompareTo(b.Distance));
			return hits;
		}
	}
}
