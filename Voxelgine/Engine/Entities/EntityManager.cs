using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Voxelgine.Engine.DI;
using Voxelgine.Graphics;

namespace Voxelgine.Engine
{
	public class EntityManager
	{
		List<VoxEntity> Entities;

		IFishEngineRunner Eng;
		IFishLogging Logging;

		public EntityManager(IFishEngineRunner eng)
		{
			Entities = new List<VoxEntity>();
			Logging = eng.DI.GetRequiredService<IFishLogging>();

			this.Eng = eng;
		}

		public void Spawn(GameSimulation simulation, VoxEntity Ent)
		{
			Logging.WriteLine($"[Spawn] {(Ent == null ? "NULL" : Ent.GetType().Name)}");

			if (Ent == null)
				return;

			Ent.Eng = Eng.DI.GetRequiredService<IFishEngineRunner>();
			Ent.SetEntityManager(this);
			Ent.SetSimulation(simulation);
			Entities.Add(Ent);
			Ent.OnInit();
		}

		void UpdateEntityPhysics(VoxEntity Ent, float Dt)
		{
			GameSimulation sim = Ent.GetSimulation();
			ChunkMap map = sim.Map;

			// Apply gravity
			PhysicsUtils.ApplyGravity(ref Ent.Velocity, 9.81f, Dt);

			// Move with axis-separated collision
			Ent.Position = PhysicsUtils.MoveWithCollision(map, Ent.Position, Ent.Size, ref Ent.Velocity, Dt);

			// --- Player collision check using AABB ---
			if (sim?.LocalPlayer != null)
			{
				AABB playerAABB = PhysicsUtils.CreatePlayerAABB(sim.LocalPlayer.Position);
				AABB entityAABB = PhysicsUtils.CreateEntityAABB(Ent.Position, Ent.Size);

				bool touching = playerAABB.Overlaps(entityAABB);

				if (touching && !Ent._WasPlayerTouching)
				{
					Ent.OnPlayerTouch(sim.LocalPlayer);
					Ent._WasPlayerTouching = true;
				}
				else if (!touching)
				{
					Ent._WasPlayerTouching = false;
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

				UpdateEntityPhysics(Ent, Dt);
				Ent.UpdateLockstep(TotalTime, Dt, InMgr);
				Ent.OnUpdatePhysics(Dt);
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
