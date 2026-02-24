using Raylib_cs;
using System;
using System.Numerics;
using Voxelgine.Graphics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Material category for hit effect selection.
	/// Determines which particle effects spawn when a projectile hits a surface.
	/// </summary>
	public enum HitMaterial : byte
	{
		/// <summary>No particles (air, water, foliage).</summary>
		None,
		/// <summary>Hard mineral blocks — bright sparks.</summary>
		Stone,
		/// <summary>Wooden/organic — fire puffs with smoke.</summary>
		Wood,
		/// <summary>Dirt, sand, grass — dust/smoke puffs.</summary>
		Earth,
		/// <summary>Transparent hard blocks — small bright sparks.</summary>
		Glass,
	}

	/// <summary>
	/// Context-sensitive hit effect system. Selects and spawns particle effects
	/// based on block type, entity type, hit position, and hit normal.
	/// Replaces hardcoded effect logic in weapon fire handlers.
	/// </summary>
	static class HitEffects
	{
		// Pre-computed material lookup indexed by BlockType
		private static readonly HitMaterial[] _blockMaterials;

		static HitEffects()
		{
			var values = Enum.GetValues<BlockType>();
			int count = 0;
			foreach (var v in values)
				if ((int)v >= count) count = (int)v + 1;

			_blockMaterials = new HitMaterial[count];
			foreach (var v in values)
				_blockMaterials[(int)v] = ClassifyBlock(v);
		}

		private static HitMaterial ClassifyBlock(BlockType type)
		{
			return type switch
			{
				BlockType.None => HitMaterial.None,
				BlockType.Water => HitMaterial.None,
				BlockType.Leaf => HitMaterial.None,
				BlockType.Foliage => HitMaterial.None,

				BlockType.Stone => HitMaterial.Stone,
				BlockType.StoneBrick => HitMaterial.Stone,
				BlockType.EndStoneBrick => HitMaterial.Stone,
				BlockType.Bricks => HitMaterial.Stone,
				BlockType.Gravel => HitMaterial.Stone,
				BlockType.Glowstone => HitMaterial.Stone,

				BlockType.Plank => HitMaterial.Wood,
				BlockType.Wood => HitMaterial.Wood,
				BlockType.CraftingTable => HitMaterial.Wood,
				BlockType.Barrel => HitMaterial.Wood,
				BlockType.Campfire => HitMaterial.Wood,
				BlockType.Torch => HitMaterial.Wood,

				BlockType.Dirt => HitMaterial.Earth,
				BlockType.Grass => HitMaterial.Earth,
				BlockType.Sand => HitMaterial.Earth,

				BlockType.Glass => HitMaterial.Glass,
				BlockType.Ice => HitMaterial.Glass,

				_ => HitMaterial.Stone,
			};
		}

		/// <summary>
		/// Returns the hit material category for a block type.
		/// </summary>
		public static HitMaterial GetBlockMaterial(BlockType type) => _blockMaterials[(int)type];

		/// <summary>
		/// Determines the block type at a world hit position by stepping inward
		/// along the negative normal to find the struck block.
		/// </summary>
		public static BlockType GetBlockAtHit(ChunkMap map, Vector3 hitPos, Vector3 hitNormal)
		{
			Vector3 checkPos = hitPos - hitNormal * 0.5f;
			return map.GetBlock((int)checkPos.X, (int)checkPos.Y, (int)checkPos.Z);
		}

		/// <summary>
		/// Spawns context-sensitive hit effects for a world block hit.
		/// Effect type depends on the block material category.
		/// </summary>
		public static void SpawnBlockHit(ParticleSystem particles, BlockType blockType, Vector3 hitPos, Vector3 hitNormal)
		{
			HitMaterial material = GetBlockMaterial(blockType);

			switch (material)
			{
				case HitMaterial.None:
					break;

				case HitMaterial.Stone:
					SpawnDirectional(particles, ParticleEffect.Spark, 6, hitPos, hitNormal,
						Color.White, scaleMin: 0.5f, scaleRange: 0.5f, forceFactor: 10.6f);
					break;

				case HitMaterial.Wood:
					SpawnDirectional(particles, ParticleEffect.Fire, 4, hitPos, hitNormal,
						Color.White, scaleMin: 0.5f, scaleRange: 0.5f, forceFactor: 8f);
					SpawnDirectional(particles, ParticleEffect.Smoke, 2, hitPos, hitNormal,
						new Color(180, 160, 130, 255), scaleMin: 0.4f, scaleRange: 0.3f, forceFactor: 4f);
					break;

				case HitMaterial.Earth:
					SpawnDirectional(particles, ParticleEffect.Smoke, 5, hitPos, hitNormal,
						new Color(160, 140, 100, 255), scaleMin: 0.5f, scaleRange: 0.3f, forceFactor: 5f);
					break;

				case HitMaterial.Glass:
					SpawnDirectional(particles, ParticleEffect.Spark, 8, hitPos, hitNormal,
						new Color(200, 220, 255, 255), scaleMin: 0.3f, scaleRange: 0.3f, forceFactor: 12f);
					break;
			}
		}

		/// <summary>
		/// Spawns hit effects for an entity hit. NPCs/Players produce blood; other entities produce sparks.
		/// </summary>
		public static void SpawnEntityHit(ParticleSystem particles, bool isFlesh, Vector3 hitPos, Vector3 hitNormal)
		{
			if (isFlesh)
			{
				for (int i = 0; i < 8; i++)
				{
					particles.SpawnBlood(hitPos, hitNormal * 0.5f, (0.8f + (float)Random.Shared.NextDouble() * 0.4f) * 0.85f);
				}
			}
			else
			{
				SpawnDirectional(particles, ParticleEffect.Spark, 6, hitPos, hitNormal,
					Color.White, scaleMin: 0.5f, scaleRange: 0.5f, forceFactor: 10.6f);
			}
		}

		/// <summary>
		/// Which particle spawn method to use for directional hit effects.
		/// </summary>
		private enum ParticleEffect { Fire, Spark, Smoke }

		/// <summary>
		/// Spawns directional particles along a hit normal with force and random spread.
		/// Wall hits (horizontal normals) get boosted force and tighter spread.
		/// </summary>
		private static void SpawnDirectional(
			ParticleSystem particles, ParticleEffect effect, int count,
			Vector3 hitPos, Vector3 hitNormal,
			Color color, float scaleMin, float scaleRange, float forceFactor)
		{
			float randomUnitFactor = 0.6f;

			// Wall hits: boost force and tighten spread
			if (hitNormal.Y == 0)
			{
				forceFactor *= 2;
				randomUnitFactor = 0.4f;
			}

			for (int i = 0; i < count; i++)
			{
				Vector3 rndDir = Vector3.Normalize(hitNormal + Utils.GetRandomUnitVector() * randomUnitFactor);
				float scale = scaleMin + (float)Random.Shared.NextDouble() * scaleRange;

				switch (effect)
				{
					case ParticleEffect.Fire:
						particles.SpawnFire(hitPos, rndDir * forceFactor, color, scale, true, 1.0f);
						break;
					case ParticleEffect.Spark:
						particles.SpawnSpark(hitPos, rndDir * forceFactor, color, scale);
						break;
					case ParticleEffect.Smoke:
						particles.SpawnSmokeShort(hitPos, rndDir * forceFactor, color);
						break;
				}
			}
		}
	}
}
