using System.Numerics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Represents a weapon fire intent — the player's desire to fire a weapon.
	/// The client sends this to the server for authoritative hit detection.
	/// </summary>
	public readonly struct FireIntent
	{
		/// <summary>Ray origin (player eye position).</summary>
		public readonly Vector3 Origin;

		/// <summary>Normalized ray direction (player aim direction).</summary>
		public readonly Vector3 Direction;

		/// <summary>Maximum raycast distance.</summary>
		public readonly float MaxRange;

		/// <summary>Weapon type identifier (e.g., "Gun").</summary>
		public readonly string WeaponType;

		/// <summary>The player who fired.</summary>
		public readonly Player SourcePlayer;

		public FireIntent(Vector3 origin, Vector3 direction, float maxRange, string weaponType, Player sourcePlayer)
		{
			Origin = origin;
			Direction = direction;
			MaxRange = maxRange;
			WeaponType = weaponType;
			SourcePlayer = sourcePlayer;
		}
	}

	/// <summary>
	/// The result of resolving a fire intent — what was hit and where.
	/// Produced by server-authoritative hit detection.
	/// </summary>
	public readonly struct FireResult
	{
		/// <summary>Type of surface that was hit.</summary>
		public readonly FireHitType HitType;

		/// <summary>World position of the hit point.</summary>
		public readonly Vector3 HitPosition;

		/// <summary>Surface normal at the hit point.</summary>
		public readonly Vector3 HitNormal;

		/// <summary>Entity that was hit, or null if world/miss.</summary>
		public readonly VoxEntity HitEntity;

		/// <summary>Body part name if an NPC was hit (e.g., "head", "body"), or null.</summary>
		public readonly string BodyPartName;

		/// <summary>Distance from origin to hit point.</summary>
		public readonly float HitDistance;

		public FireResult(FireHitType hitType, Vector3 hitPosition, Vector3 hitNormal, float hitDistance, VoxEntity hitEntity = null, string bodyPartName = null)
		{
			HitType = hitType;
			HitPosition = hitPosition;
			HitNormal = hitNormal;
			HitDistance = hitDistance;
			HitEntity = hitEntity;
			BodyPartName = bodyPartName;
		}

		public static FireResult Miss(Vector3 origin, Vector3 direction, float maxRange)
		{
			return new FireResult(FireHitType.None, origin + direction * maxRange, -direction, maxRange);
		}
	}

	/// <summary>
	/// What type of surface a weapon fire hit.
	/// </summary>
	public enum FireHitType : byte
	{
		/// <summary>No hit — ray reached max range.</summary>
		None = 0,

		/// <summary>Hit a world block.</summary>
		World = 1,

		/// <summary>Hit an entity (NPC, pickup, door, etc.).</summary>
		Entity = 2,

		/// <summary>Hit another player.</summary>
		Player = 3
	}
}
