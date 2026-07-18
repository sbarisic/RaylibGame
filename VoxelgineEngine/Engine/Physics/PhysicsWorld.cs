using System;
using System.Numerics;
using Voxelgine.Graphics;

namespace Voxelgine.Engine;

public enum PhysicsCollisionMask
{
	Player,
	Entity,
}

public readonly record struct PhysicsCollider(
	AABB Bounds,
	VoxEntity Entity,
	bool IsVoxel,
	int VoxelX,
	int VoxelY,
	int VoxelZ);

public readonly record struct SweepHit(
	float Fraction,
	Vector3 Normal,
	Vector3 SecondaryNormal,
	Vector3 TertiaryNormal,
	int NormalCount,
	float PenetrationDepth,
	PhysicsCollider Collider)
{
	public Vector3 GetNormal(int index) => index switch
	{
		0 => Normal,
		1 => SecondaryNormal,
		2 => TertiaryNormal,
		_ => throw new ArgumentOutOfRangeException(nameof(index)),
	};
}

/// <summary>
/// Read-only collision view shared by authoritative and predicted physics.
/// </summary>
public sealed class PhysicsWorld
{
	private const float ContactEpsilon = 1e-5f;

	public ChunkMap Map { get; }
	public EntityManager Entities { get; }

	public PhysicsWorld(ChunkMap map, EntityManager entities = null)
	{
		Map = map ?? throw new ArgumentNullException(nameof(map));
		Entities = entities;
	}

	public bool SweepAabb(
		in AABB movingBounds,
		Vector3 delta,
		PhysicsCollisionMask mask,
		out SweepHit hit,
		VoxEntity ignoredEntity = null)
	{
		hit = default;
		if (movingBounds.IsEmpty || !IsFinite(delta))
			return false;

		Vector3 broadphaseMinimum = Vector3.Min(movingBounds.Min, movingBounds.Min + delta);
		Vector3 broadphaseMaximum = Vector3.Max(movingBounds.Max, movingBounds.Max + delta);

		bool found = false;
		float bestFraction = float.PositiveInfinity;
		float bestPenetration = float.PositiveInfinity;
		Vector3 normal1 = Vector3.Zero;
		Vector3 normal2 = Vector3.Zero;
		Vector3 normal3 = Vector3.Zero;
		int normalCount = 0;
		PhysicsCollider bestCollider = default;

		int minimumX = (int)MathF.Floor(broadphaseMinimum.X);
		int minimumY = (int)MathF.Floor(broadphaseMinimum.Y);
		int minimumZ = (int)MathF.Floor(broadphaseMinimum.Z);
		int maximumX = (int)MathF.Ceiling(broadphaseMaximum.X) - 1;
		int maximumY = (int)MathF.Ceiling(broadphaseMaximum.Y) - 1;
		int maximumZ = (int)MathF.Ceiling(broadphaseMaximum.Z) - 1;

		for (int x = minimumX; x <= maximumX; x++)
		{
			for (int y = minimumY; y <= maximumY; y++)
			{
				for (int z = minimumZ; z <= maximumZ; z++)
				{
					if (!Map.IsSolid(x, y, z))
						continue;

					AABB bounds = new(new Vector3(x, y, z), Vector3.One);
					PhysicsCollider collider = new(bounds, null, true, x, y, z);
					ConsiderCollider(
						movingBounds,
						delta,
						collider,
						ref found,
						ref bestFraction,
						ref bestPenetration,
						ref normal1,
						ref normal2,
						ref normal3,
						ref normalCount,
						ref bestCollider
					);
				}
			}
		}

		if (Entities != null)
		{
			AABB broadphase = AABB.FromMinMax(broadphaseMinimum, broadphaseMaximum);
			foreach (VoxEntity entity in Entities.GetAllEntities())
			{
				if (entity == null || entity == ignoredEntity)
					continue;

				EntityPhysicsProperties properties = entity.PhysicsProperties;
				bool blocks = mask == PhysicsCollisionMask.Player
					? properties.BlocksPlayers
					: properties.BlocksEntities;
				if (!blocks)
					continue;

				AABB bounds = entity.WorldBounds;
				if (!broadphase.Overlaps(bounds) && !movingBounds.Overlaps(bounds))
					continue;

				PhysicsCollider collider = new(bounds, entity, false, 0, 0, 0);
				ConsiderCollider(
					movingBounds,
					delta,
					collider,
					ref found,
					ref bestFraction,
					ref bestPenetration,
					ref normal1,
					ref normal2,
					ref normal3,
					ref normalCount,
					ref bestCollider
				);
			}
		}

		if (!found)
			return false;

		hit = new SweepHit(
			bestFraction,
			normal1,
			normal2,
			normal3,
			normalCount,
			float.IsPositiveInfinity(bestPenetration) ? 0f : bestPenetration,
			bestCollider
		);
		return true;
	}

	private static void ConsiderCollider(
		in AABB movingBounds,
		Vector3 delta,
		in PhysicsCollider collider,
		ref bool found,
		ref float bestFraction,
		ref float bestPenetration,
		ref Vector3 normal1,
		ref Vector3 normal2,
		ref Vector3 normal3,
		ref int normalCount,
		ref PhysicsCollider bestCollider)
	{
		if (!TrySweepAgainstAabb(
			movingBounds,
			delta,
			collider.Bounds,
			out float fraction,
			out float penetration,
			out Vector3 candidate1,
			out Vector3 candidate2,
			out Vector3 candidate3,
			out int candidateCount))
		{
			return;
		}

		bool isPenetrating = penetration > 0f;
		bool replace = !found ||
			fraction < bestFraction - ContactEpsilon ||
			(MathF.Abs(fraction - bestFraction) <= ContactEpsilon &&
				isPenetrating && penetration < bestPenetration);

		if (replace)
		{
			found = true;
			bestFraction = fraction;
			bestPenetration = isPenetrating ? penetration : float.PositiveInfinity;
			normal1 = candidate1;
			normal2 = candidate2;
			normal3 = candidate3;
			normalCount = candidateCount;
			bestCollider = collider;
			return;
		}

		if (MathF.Abs(fraction - bestFraction) > ContactEpsilon || isPenetrating)
			return;

		for (int i = 0; i < candidateCount; i++)
		{
			Vector3 normal = i switch
			{
				0 => candidate1,
				1 => candidate2,
				_ => candidate3,
			};
			AddNormal(normal, ref normal1, ref normal2, ref normal3, ref normalCount);
		}
	}

	private static bool TrySweepAgainstAabb(
		in AABB moving,
		Vector3 delta,
		in AABB target,
		out float fraction,
		out float penetrationDepth,
		out Vector3 normal1,
		out Vector3 normal2,
		out Vector3 normal3,
		out int normalCount)
	{
		fraction = 0f;
		penetrationDepth = 0f;
		normal1 = Vector3.Zero;
		normal2 = Vector3.Zero;
		normal3 = Vector3.Zero;
		normalCount = 0;

		if (moving.IsEmpty || target.IsEmpty)
			return false;

		if (moving.Overlaps(target))
		{
			FindMinimumTranslation(moving, target, out normal1, out penetrationDepth);
			normalCount = 1;
			return true;
		}

		float entryX;
		float exitX;
		Vector3 entryNormalX;
		if (!CalculateAxis(
			moving.Min.X,
			moving.Max.X,
			target.Min.X,
			target.Max.X,
			delta.X,
			Vector3.UnitX,
			out entryX,
			out exitX,
			out entryNormalX))
		{
			return false;
		}

		float entryY;
		float exitY;
		Vector3 entryNormalY;
		if (!CalculateAxis(
			moving.Min.Y,
			moving.Max.Y,
			target.Min.Y,
			target.Max.Y,
			delta.Y,
			Vector3.UnitY,
			out entryY,
			out exitY,
			out entryNormalY))
		{
			return false;
		}

		float entryZ;
		float exitZ;
		Vector3 entryNormalZ;
		if (!CalculateAxis(
			moving.Min.Z,
			moving.Max.Z,
			target.Min.Z,
			target.Max.Z,
			delta.Z,
			Vector3.UnitZ,
			out entryZ,
			out exitZ,
			out entryNormalZ))
		{
			return false;
		}

		float entry = MathF.Max(entryX, MathF.Max(entryY, entryZ));
		float exit = MathF.Min(exitX, MathF.Min(exitY, exitZ));
		if (entry > exit || exit < 0f || entry < 0f || entry > 1f)
			return false;

		fraction = entry;
		if (MathF.Abs(entryX - entry) <= ContactEpsilon)
			AddNormal(entryNormalX, ref normal1, ref normal2, ref normal3, ref normalCount);
		if (MathF.Abs(entryY - entry) <= ContactEpsilon)
			AddNormal(entryNormalY, ref normal1, ref normal2, ref normal3, ref normalCount);
		if (MathF.Abs(entryZ - entry) <= ContactEpsilon)
			AddNormal(entryNormalZ, ref normal1, ref normal2, ref normal3, ref normalCount);

		return normalCount > 0;
	}

	private static bool CalculateAxis(
		float movingMinimum,
		float movingMaximum,
		float targetMinimum,
		float targetMaximum,
		float delta,
		Vector3 axis,
		out float entry,
		out float exit,
		out Vector3 entryNormal)
	{
		entryNormal = Vector3.Zero;
		if (MathF.Abs(delta) <= 1e-12f)
		{
			if (movingMaximum <= targetMinimum || movingMinimum >= targetMaximum)
			{
				entry = 0f;
				exit = 0f;
				return false;
			}

			entry = float.NegativeInfinity;
			exit = float.PositiveInfinity;
			return true;
		}

		if (delta > 0f)
		{
			entry = (targetMinimum - movingMaximum) / delta;
			exit = (targetMaximum - movingMinimum) / delta;
			entryNormal = -axis;
		}
		else
		{
			entry = (targetMaximum - movingMinimum) / delta;
			exit = (targetMinimum - movingMaximum) / delta;
			entryNormal = axis;
		}

		return true;
	}

	private static void FindMinimumTranslation(
		in AABB moving,
		in AABB target,
		out Vector3 normal,
		out float depth)
	{
		float moveNegativeX = moving.Max.X - target.Min.X;
		float movePositiveX = target.Max.X - moving.Min.X;
		float moveNegativeY = moving.Max.Y - target.Min.Y;
		float movePositiveY = target.Max.Y - moving.Min.Y;
		float moveNegativeZ = moving.Max.Z - target.Min.Z;
		float movePositiveZ = target.Max.Z - moving.Min.Z;

		depth = moveNegativeX;
		normal = -Vector3.UnitX;
		ChooseTranslation(movePositiveX, Vector3.UnitX, ref depth, ref normal);
		ChooseTranslation(moveNegativeY, -Vector3.UnitY, ref depth, ref normal);
		ChooseTranslation(movePositiveY, Vector3.UnitY, ref depth, ref normal);
		ChooseTranslation(moveNegativeZ, -Vector3.UnitZ, ref depth, ref normal);
		ChooseTranslation(movePositiveZ, Vector3.UnitZ, ref depth, ref normal);
	}

	private static void ChooseTranslation(float candidateDepth, Vector3 candidateNormal, ref float depth, ref Vector3 normal)
	{
		if (candidateDepth < depth)
		{
			depth = candidateDepth;
			normal = candidateNormal;
		}
	}

	private static void AddNormal(
		Vector3 normal,
		ref Vector3 normal1,
		ref Vector3 normal2,
		ref Vector3 normal3,
		ref int normalCount)
	{
		if (normal == Vector3.Zero)
			return;

		for (int i = 0; i < normalCount; i++)
		{
			Vector3 existing = i switch
			{
				0 => normal1,
				1 => normal2,
				_ => normal3,
			};
			if (Vector3.Dot(existing, normal) > 0.999f)
				return;
		}

		switch (normalCount)
		{
			case 0:
				normal1 = normal;
				break;
			case 1:
				normal2 = normal;
				break;
			case 2:
				normal3 = normal;
				break;
			default:
				return;
		}
		normalCount++;
	}

	private static bool IsFinite(Vector3 value) =>
		float.IsFinite(value.X) &&
		float.IsFinite(value.Y) &&
		float.IsFinite(value.Z);
}
