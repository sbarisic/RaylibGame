using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Raylib_cs;

using Voxelgine.Engine.DI;
using Voxelgine.Graphics;

namespace Voxelgine.Engine
{
	public class EntityManager
	{
		List<VoxEntity> Entities;
		Dictionary<int, VoxEntity> EntitiesById;
		int _nextNetworkId = 1;

		/// <summary>
		/// When true (default), entity physics and AI run during <see cref="UpdateLockstep"/>.
		/// Set to false on multiplayer clients where the server is authoritative over entity state.
		/// </summary>
		public bool IsAuthority { get; set; } = true;

		IFishEngineRunner Eng;
		IFishLogging Logging;

		/// <summary>
		/// Raised on the authority when a player first touches an entity (AABB overlap entry).
		/// Parameters: entity, touching player.
		/// </summary>
		public event Action<VoxEntity, Player> PlayerTouchedEntity;

		public EntityManager(IFishEngineRunner eng)
		{
			Entities = new List<VoxEntity>();
			EntitiesById = new Dictionary<int, VoxEntity>();
			Logging = eng.DI.GetRequiredService<IFishLogging>();

			this.Eng = eng;
		}

		public void Spawn(GameSimulation simulation, VoxEntity Ent)
		{
			Logging.WriteLine($"[Spawn] {(Ent == null ? "NULL" : Ent.GetType().Name)}");

			if (Ent == null)
				return;

			Ent.NetworkId = _nextNetworkId++;
			Ent.Eng = Eng.DI.GetRequiredService<IFishEngineRunner>();
			Ent.SetEntityManager(this);
			Ent.SetSimulation(simulation);
			Entities.Add(Ent);
			EntitiesById[Ent.NetworkId] = Ent;
			Ent.OnInit();
		}

		/// <summary>
		/// Spawns an entity with a specific network ID assigned by the server.
		/// Used on multiplayer clients to create entities with matching IDs.
		/// </summary>
		public void SpawnWithNetworkId(GameSimulation simulation, VoxEntity Ent, int networkId)
		{
			Logging.WriteLine($"[SpawnWithNetworkId] {(Ent == null ? "NULL" : Ent.GetType().Name)} (netId={networkId})");

			if (Ent == null)
				return;

			Ent.NetworkId = networkId;
			Ent.Eng = Eng.DI.GetRequiredService<IFishEngineRunner>();
			Ent.SetEntityManager(this);
			Ent.SetSimulation(simulation);
			Entities.Add(Ent);
			EntitiesById[networkId] = Ent;
			Ent.OnInit();

			// Keep _nextNetworkId above any server-assigned ID to avoid collisions
			if (networkId >= _nextNetworkId)
				_nextNetworkId = networkId + 1;
		}

		/// <summary>
		/// Returns the entity with the given network ID, or null if not found.
		/// </summary>
		public VoxEntity GetEntityByNetworkId(int networkId)
		{
			EntitiesById.TryGetValue(networkId, out VoxEntity ent);
			return ent;
		}

		/// <summary>
		/// Removes the entity with the given network ID. Returns the removed entity, or null if not found.
		/// </summary>
		public VoxEntity Remove(int networkId)
		{
			if (!EntitiesById.TryGetValue(networkId, out VoxEntity ent))
				return null;

			EntitiesById.Remove(networkId);
			Entities.Remove(ent);
			return ent;
		}

		/// <summary>
		/// Removes the given entity. Returns true if the entity was found and removed.
		/// </summary>
		public bool Remove(VoxEntity entity)
		{
			if (entity == null)
				return false;

			if (entity.NetworkId != 0)
				EntitiesById.Remove(entity.NetworkId);

			return Entities.Remove(entity);
		}

		/// <summary>
		/// Returns the total number of active entities.
		/// </summary>
		public int GetEntityCount() => Entities.Count;

		void UpdateEntityPhysics(VoxEntity Ent, float Dt)
		{
			GameSimulation sim = Ent.GetSimulation();
			ChunkMap map = sim.Map;

			// Apply gravity
			PhysicsUtils.ApplyGravity(ref Ent.Velocity, 9.81f, Dt);

			// Move with axis-separated collision
			Ent.Position = WorldCollision.MoveWithCollision(map, Ent.Position, Ent.Size, ref Ent.Velocity, Dt);

			// --- Player collision check using AABB ---
			if (sim != null)
			{
				AABB entityAABB = PhysicsUtils.CreateEntityAABB(Ent.Position, Ent.Size);

				foreach (Player player in sim.Players.GetAllPlayers())
				{
					AABB playerAABB = PhysicsUtils.CreatePlayerAABB(player.Position);
					bool touching = playerAABB.Overlaps(entityAABB);

					if (touching && !Ent._TouchingPlayerIds.Contains(player.PlayerId))
					{
						Ent._TouchingPlayerIds.Add(player.PlayerId);
						Ent.OnPlayerTouch(player);
						PlayerTouchedEntity?.Invoke(Ent, player);
					}
					else if (!touching)
					{
						Ent._TouchingPlayerIds.Remove(player.PlayerId);
					}
				}
			}
		}

		public void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr)
		{
			for (int i = 0; i < Entities.Count; i++)
			{
				VoxEntity Ent = Entities[i];

				if (Ent == null)
					continue;

				if (IsAuthority)
				{
					UpdateEntityPhysics(Ent, Dt);
					Ent.UpdateLockstep(TotalTime, Dt, InMgr);
					Ent.OnUpdatePhysics(Dt);
				}
			}
		}

		public void Draw3D(float TimeAlpha, ref GameFrameInfo LastFrame)
		{
			for (int i = 0; i < Entities.Count; i++)
			{
				VoxEntity Ent = Entities[i];

				if (Ent == null)
					continue;

				Ent.Draw3D(TimeAlpha, ref LastFrame);
			}
		}

		/// <summary>
		/// Draws 2D overlays for all entities (speech bubbles, etc.).
		/// Called during the 2D rendering pass after EndMode3D.
		/// </summary>
		public void Draw2D(Camera3D camera)
		{
			for (int i = 0; i < Entities.Count; i++)
			{
				VoxEntity Ent = Entities[i];

				if (Ent == null)
					continue;

				Ent.Draw2D(camera);
			}
		}

		/// <summary>
		/// Returns all entities that emit light (LightEmission > 0).
		/// </summary>
		public IEnumerable<VoxEntity> GetLightEmittingEntities()
		{
			for (int i = 0; i < Entities.Count; i++)
			{
				VoxEntity Ent = Entities[i];
				if (Ent != null && Ent.EmitsLight())
					yield return Ent;
			}
		}

		/// <summary>
		/// Returns all entities in the manager.
		/// </summary>
		public IEnumerable<VoxEntity> GetAllEntities()
		{
			for (int i = 0; i < Entities.Count; i++)
			{
				if (Entities[i] != null)
					yield return Entities[i];
			}
		}

		/// <summary>
		/// Casts a ray against all entities and returns the closest hit.
		/// </summary>
		/// <param name="rayOrigin">Origin point of the ray.</param>
		/// <param name="rayDir">Normalized direction of the ray.</param>
		/// <param name="maxDistance">Maximum distance to check.</param>
		/// <param name="excludeEntity">Optional entity to exclude from testing.</param>
		/// <returns>RaycastHit with closest hit information, or RaycastHit.None if no hit.</returns>
		public RaycastHit Raycast(Vector3 rayOrigin, Vector3 rayDir, float maxDistance = 1000f, VoxEntity excludeEntity = null)
		{
			return Engine.Raycast.CastAgainstEntities(rayOrigin, rayDir, Entities, maxDistance, excludeEntity);
		}

		/// <summary>
		/// Casts a ray against all entities and returns all hits sorted by distance.
		/// </summary>
		/// <param name="rayOrigin">Origin point of the ray.</param>
		/// <param name="rayDir">Normalized direction of the ray.</param>
		/// <param name="maxDistance">Maximum distance to check.</param>
		/// <param name="excludeEntity">Optional entity to exclude from testing.</param>
		/// <returns>List of all hits sorted by distance (closest first).</returns>
		public List<RaycastHit> RaycastAll(Vector3 rayOrigin, Vector3 rayDir, float maxDistance = 1000f, VoxEntity excludeEntity = null)
		{
			return Engine.Raycast.CastAgainstEntitiesAll(rayOrigin, rayDir, Entities, maxDistance, excludeEntity);
		}
	}
}
