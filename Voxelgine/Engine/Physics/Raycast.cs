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

			if (RayMath.RayIntersectsAABB(rayOrigin, rayDir, entityAABB, maxDistance, out float dist, out Vector3 normal))
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

				if (RayMath.RayIntersectsAABB(rayOrigin, rayDir, entityAABB, closestDist, out float dist, out Vector3 normal))
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

				if (RayMath.RayIntersectsAABB(rayOrigin, rayDir, entityAABB, maxDistance, out float dist, out Vector3 normal))
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
