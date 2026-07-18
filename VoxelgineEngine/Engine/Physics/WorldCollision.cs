using System;
using System.Numerics;

namespace Voxelgine.Engine;

public readonly record struct PhysicsMoveResult(
	Vector3 Position,
	Vector3 Velocity,
	bool Grounded,
	Vector3 GroundNormal,
	Vector3 WallNormal);

/// <summary>
/// Shared swept movement and collision response for players and entities.
/// Positions use bottom-center coordinates.
/// </summary>
public static class WorldCollision
{
	public const float CollisionSkin = 0.001f;

	public static PhysicsMoveResult MoveAndSlide(
		PhysicsWorld world,
		Vector3 position,
		Vector3 size,
		Vector3 velocity,
		float deltaTime,
		PhysicsCollisionMask mask,
		int maximumSlides = 4,
		VoxEntity ignoredEntity = null)
	{
		if (!IsFinite(position) || !IsFinite(size) || !IsFinite(velocity) || !float.IsFinite(deltaTime) || deltaTime < 0f)
			return new PhysicsMoveResult(position, Vector3.Zero, false, Vector3.Zero, Vector3.Zero);

		AABB bounds = PhysicsUtils.CreateEntityAABB(position, size);
		if (bounds.IsEmpty)
			return new PhysicsMoveResult(position, Vector3.Zero, false, Vector3.Zero, Vector3.Zero);

		Vector3 currentPosition = position;
		Vector3 currentVelocity = velocity;
		bool grounded = false;
		Vector3 groundNormal = Vector3.Zero;
		Vector3 wallNormal = Vector3.Zero;

		for (int iteration = 0; iteration < maximumSlides; iteration++)
		{
			bounds = PhysicsUtils.CreateEntityAABB(currentPosition, size);
			if (!world.SweepAabb(bounds, Vector3.Zero, mask, out SweepHit overlap, ignoredEntity) || overlap.PenetrationDepth <= 0f)
				break;

			currentPosition += overlap.Normal * (overlap.PenetrationDepth + CollisionSkin);
			ClipIntoPlane(ref currentVelocity, overlap.Normal);
		}

		float timeRemaining = deltaTime;
		for (int slide = 0; slide < maximumSlides && timeRemaining > 1e-6f; slide++)
		{
			Vector3 delta = currentVelocity * timeRemaining;
			if (delta.LengthSquared() <= 1e-12f)
				break;

			bounds = PhysicsUtils.CreateEntityAABB(currentPosition, size);
			if (!world.SweepAabb(bounds, delta, mask, out SweepHit hit, ignoredEntity))
			{
				currentPosition += delta;
				break;
			}

			float deltaLength = delta.Length();
			float safeFraction = MathF.Max(0f, hit.Fraction - CollisionSkin / deltaLength);
			currentPosition += delta * safeFraction;
			timeRemaining *= MathF.Max(0f, 1f - hit.Fraction);

			for (int normalIndex = 0; normalIndex < hit.NormalCount; normalIndex++)
			{
				Vector3 normal = hit.GetNormal(normalIndex);
				if (normal.Y > 0.7f && velocity.Y <= 0f)
				{
					grounded = true;
					groundNormal = normal;
				}
				else if (MathF.Abs(normal.Y) < 0.7f)
				{
					wallNormal = normal;
				}

				ClipIntoPlane(ref currentVelocity, normal);
			}
		}

		return new PhysicsMoveResult(currentPosition, currentVelocity, grounded, groundNormal, wallNormal);
	}

	private static void ClipIntoPlane(ref Vector3 velocity, Vector3 normal)
	{
		float into = Vector3.Dot(velocity, normal);
		if (into < 0f)
			velocity -= normal * into;
	}

	private static bool IsFinite(Vector3 value) =>
		float.IsFinite(value.X) &&
		float.IsFinite(value.Y) &&
		float.IsFinite(value.Z);
}
