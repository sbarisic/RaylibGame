using System.IO;
using System.Numerics;

namespace Voxelgine.Engine.Server
{
	public partial class ServerLoop
	{
		/// <summary>
		/// Spawns the initial server-side entities (matching the world setup).
		/// </summary>
		private void SpawnEntities()
		{
			// Spawn pickup entity (use SetModelName to avoid Raylib GPU calls on headless server)
			var pickup = new VEntPickup();
			pickup.SetPosition(_pickupSpawnPos);
			pickup.SetSize(Vector3.One);
			pickup.SetModelName("orb_xp/orb_xp.obj");
			_simulation.Entities.Spawn(_simulation, pickup);

			// Spawn NPC entity (use SetModelName to avoid Raylib GPU calls on headless server)
			var npc = new VEntNPC();
			npc.SetSize(new Vector3(0.9f, 1.8f, 0.9f));
			npc.SetPosition(_npcSpawnPos);
			npc.SetModelName("npc/humanoid.json");
			_simulation.Entities.Spawn(_simulation, npc);
			npc.InitPathfinding(_simulation.Map);

			_logging.ServerWriteLine($"Spawned {_simulation.Entities.GetEntityCount()} entities.");
		}

		/// <summary>
		/// Gets a compact animation state byte for an entity.
		/// 0 = idle, 1 = walk, 2 = attack.
		/// </summary>
		private static byte GetEntityAnimationState(VoxEntity entity)
		{
			if (entity is VEntNPC npc)
			{
				var animator = npc.GetAnimator();
				if (animator != null)
				{
					return animator.CurrentAnimation switch
					{
						"walk" => 1,
						"attack" => 2,
						_ => 0,
					};
				}
			}
			return 0;
		}

		/// <summary>
		/// Builds an <see cref="EntitySpawnPacket"/> from an existing entity.
		/// Serializes the entity's spawn properties (size, model, subclass data) into the Properties byte array.
		/// </summary>
		private static EntitySpawnPacket BuildEntitySpawnPacket(VoxEntity entity)
		{
			byte[] properties;
			using (var ms = new MemoryStream())
			using (var writer = new BinaryWriter(ms))
			{
				entity.WriteSpawnProperties(writer);
				properties = ms.ToArray();
			}

			return new EntitySpawnPacket
			{
				EntityType = entity.EntityTypeName,
				NetworkId = entity.NetworkId,
				Position = entity.Position,
				Properties = properties,
			};
		}
	}
}
