using System.IO;
using System.Numerics;

using Voxelgine.Engine.AI;
using Voxelgine.Engine.DI;
using Voxelgine.Engine.World.Structures;

namespace Voxelgine.Engine.Server
{
	public partial class ServerLoop
	{
		/// <summary>
		/// Spawns the initial server-side entities (matching the world setup).
		/// </summary>
		private void SpawnEntities()
		{
			PlannedMarker[] npcMarkers = _simulation.Map.GeneratedFeatures.Markers
				.Where(static marker => marker.Kind == StructureMarkerKind.NpcSpawn)
				.OrderBy(static marker => marker.Id.Site)
				.ThenBy(static marker => marker.Id.BlueprintMarkerId, StringComparer.Ordinal)
				.ToArray();
			if (npcMarkers.Length == 0)
				SpawnNpc(_npcSpawnPos,StableNpcId.Persistent(_simulation.PersistentEntityIds.Allocate()));
			else
				foreach (PlannedMarker marker in npcMarkers)
					SpawnNpc(new Vector3(marker.Position.X + 0.5f, marker.Position.Y, marker.Position.Z + 0.5f),StableNpcId.Generated(marker.Id));

			PlannedMarker[] doorMarkers = _simulation.Map.GeneratedFeatures.Markers
				.Where(static marker => marker.Kind == StructureMarkerKind.Door)
				.OrderBy(static marker => marker.Id.Site)
				.ThenBy(static marker => marker.Id.BlueprintMarkerId, StringComparer.Ordinal)
				.ToArray();
			if (doorMarkers.Length == 0)
				SpawnDoor(PlayerSpawnPosition + new Vector3(4, 0, 0));
			else
				foreach (PlannedMarker marker in doorMarkers)
					SpawnDoor(
						new Vector3(marker.Position.X + 0.5f, marker.Position.Y, marker.Position.Z + 0.5f),
						GetDoorFacing(_simulation.Map.GeneratedFeatures, marker));

			foreach (PersistentFurnitureRecord record in _simulation.Furniture.GetAll().ToArray())
				if (record.Type == FurnitureType.ItemBasket)
				{
					if(!BlockInfo.IsSolid(_simulation.Map.GetBlock(record.Anchor.X,record.Anchor.Y-1,record.Anchor.Z))){_simulation.Furniture.Remove(record.Key,out _);_logging.Log(GameLogLevel.Warning,"Furniture",$"removed unsupported basket key={record.Key} anchor={record.Anchor}");continue;}
					SpawnBasket(record, broadcast: false);
				}
				else if(record.Type==FurnitureType.Bed)
				{
					BlockCoordinate head=record.Anchor+VEntBed.FacingOffset(record.Facing);if(!BlockInfo.IsSolid(_simulation.Map.GetBlock(record.Anchor.X,record.Anchor.Y-1,record.Anchor.Z))||!BlockInfo.IsSolid(_simulation.Map.GetBlock(head.X,head.Y-1,head.Z))){_simulation.Furniture.Remove(record.Key,out _);_logging.Log(GameLogLevel.Warning,"Furniture",$"removed unsupported bed key={record.Key}");continue;}SpawnBed(record,broadcast:false);
				}

			_logging.ServerWriteLine($"Spawned {_simulation.Entities.GetEntityCount()} entities.");
		}

		private void SpawnNpc(Vector3 position,StableNpcId stableId)
		{
			var npc = new VEntNPC();
			npc.SetStableId(stableId);
			npc.SetSize(new Vector3(0.9f, 1.8f, 0.9f));
			npc.SetPosition(position);
			npc.SetModelName("npc/humanoid.json");
			npc.SetTextureName(VEntNPC.AvailableTextures[Random.Shared.Next(VEntNPC.AvailableTextures.Length)]);
			_simulation.Entities.Spawn(_simulation, npc);
			npc.InitPathfinding(_simulation.Map);
			npc.SetAIProgram(AIPrograms.FunkyBehavior());
			_npcLife?.Attach(npc);
		}

		private void SpawnDoor(Vector3 position, Vector3? facing = null)
		{
			var door = new VEntSlidingDoor();
			door.SetModelName("door/door.json");
			door.Initialize(position, new Vector3(1.0f, 2.0f, 0.125f));
			door.FacingDirection = facing ?? Vector3.UnitZ;
			_simulation.Entities.Spawn(_simulation, door);
		}

		private VEntItemBasket SpawnBasket(PersistentFurnitureRecord record, bool broadcast)
		{
			var basket = new VEntItemBasket(); basket.Initialize(record.Key, record.Anchor, record.Facing, record.Slots.ToArray());
			if (broadcast) SpawnEntityAndBroadcast(basket); else _simulation.Entities.Spawn(_simulation, basket);
			return basket;
		}

		private VEntBed SpawnBed(PersistentFurnitureRecord record,bool broadcast)
		{
			var bed=new VEntBed();bed.Initialize(record.Key,record.Anchor,record.Facing);if(broadcast)SpawnEntityAndBroadcast(bed);else _simulation.Entities.Spawn(_simulation,bed);return bed;
		}

		internal static Vector3 GetDoorFacing(WorldFeaturePlan features, PlannedMarker marker)
		{
			PlannedSite site = features.Sites.FirstOrDefault(candidate => candidate.Id == marker.Id.Site);
			if (site == null)
				return Vector3.UnitZ;
			BlockCoordinate direction = WorldStructurePlanner.RotateDirection(new BlockCoordinate(0, 0, 1), site.Rotation);
			return new Vector3(direction.X, direction.Y, direction.Z);
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
		/// Kills and removes NPCs which fell through the world, then reliably tells
		/// clients to discard their replicated presentation state.
		/// </summary>
		private void RemoveFallenNpcs()
		{
			foreach (VEntNPC npc in WorldBoundsPolicy.KillFallenNpcs(
				_simulation.Entities.GetAllEntities()
			))
			{
				int networkId = npc.NetworkId;
				float lastY = npc.Position.Y;
				if (!_simulation.Entities.Remove(npc))
				{
					continue;
				}

				_lastEntitySnapshots.Remove(networkId);
				_server.Broadcast(new EntityRemovePacket
				{
					NetworkId = networkId,
				}, true, CurrentTime);
				_logging.Log(
					GameLogLevel.Info,
					"Entities",
					$"Removed NPC npcId={networkId} reason=void y={lastY:F2}"
				);
			}
		}

		/// <summary>
		/// Gets a compact animation state byte for an entity.
		/// 0 = idle, 1 = walk, 2 = attack.
		/// Derived from velocity since the headless server has no Animator (no GPU model loading).
		/// </summary>
		private static byte GetEntityAnimationState(VoxEntity entity)
		{
			if(entity is VEntNPC sleeping&&sleeping.IsSleeping)return 3;
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

		private static EntitySnapshotPacket BuildEntitySnapshotPacket(VoxEntity entity)
		{
			return new EntitySnapshotPacket
			{
				NetworkId = entity.NetworkId,
				Position = entity.Position,
				Velocity = entity.Velocity,
				AnimationState = GetEntityAnimationState(entity),
				SnapshotData = entity.CaptureSnapshot(),
			};
		}

		/// <summary>
		/// Handles entity-player touch events raised by <see cref="EntityManager"/>.
		/// Legacy refill pickups no longer mutate inventory; item drops use their
		/// own authoritative pickup stage.
		/// </summary>
		private void OnPlayerTouchedEntity(VoxEntity entity, Player player)
		{
			// Intentionally empty until authoritative item-drop pickup is processed.
		}
	}
}
