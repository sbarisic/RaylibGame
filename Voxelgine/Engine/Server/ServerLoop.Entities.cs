using System.IO;
using System.Numerics;

using Voxelgine.Engine.AI;

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
			npc.SetTextureName(VEntNPC.AvailableTextures[Random.Shared.Next(VEntNPC.AvailableTextures.Length)]);
			_simulation.Entities.Spawn(_simulation, npc);
			npc.InitPathfinding(_simulation.Map);
			npc.SetAIProgram(AIPrograms.FunkyBehavior());

			// Spawn door entity near player spawn
			var door = new VEntSlidingDoor();
			door.SetModelName("door/door.json");
			door.Initialize(PlayerSpawnPosition + new Vector3(4, 0, 0), new Vector3(1.0f, 2.0f, 0.125f));
			door.FacingDirection = Vector3.UnitZ;
			_simulation.Entities.Spawn(_simulation, door);

			_logging.ServerWriteLine($"Spawned {_simulation.Entities.GetEntityCount()} entities.");
		}

		/// <summary>
		/// Spawns an entity on the server and broadcasts its spawn packet to all connected clients.
		/// Must be called from the server thread.
		/// </summary>
		public void SpawnEntityAndBroadcast(VoxEntity entity)
		{
			_simulation.Entities.Spawn(_simulation, entity);
			var packet = BuildEntitySpawnPacket(entity);
			_server.Broadcast(packet, true, CurrentTime);
			_logging.ServerWriteLine($"Spawned {entity.EntityTypeName} (netId={entity.NetworkId}) at {entity.Position}");
		}

		/// <summary>
		/// Gets a compact animation state byte for an entity.
		/// 0 = idle, 1 = walk, 2 = attack.
		/// Derived from velocity since the headless server has no Animator (no GPU model loading).
		/// </summary>
		private static byte GetEntityAnimationState(VoxEntity entity)
		{
			if (entity is VEntNPC)
			{
				float horizontalSpeedSq = entity.Velocity.X * entity.Velocity.X + entity.Velocity.Z * entity.Velocity.Z;
				if (horizontalSpeedSq > 0.25f) // > 0.5 blocks/s
					return 1; // walk
			}
			return 0; // idle
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

		/// <summary>
		/// Handles entity-player touch events raised by <see cref="EntityManager"/>.
		/// Refills the player's inventory when they touch a <see cref="VEntPickup"/>.
		/// </summary>
		private void OnPlayerTouchedEntity(VoxEntity entity, Player player)
		{
			if (entity is VEntPickup && _playerInventories.TryGetValue(player.PlayerId, out ServerInventory inventory))
			{
				inventory.ResetToDefaults();
				_server.SendTo(player.PlayerId, inventory.CreateFullUpdatePacket(), true, CurrentTime);
				_logging.ServerWriteLine($"Player [{player.PlayerId}] touched pickup — inventory refilled.");
			}
		}
	}
}
