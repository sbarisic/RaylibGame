using RaylibGame.States;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Graphics;

namespace Voxelgine.Engine {
	public class EntityManager {
		List<VoxEntity> Entities;

		public EntityManager() {
			Entities = new List<VoxEntity>();
		}

		public void Spawn(GameState GState, VoxEntity Ent) {
			if (Ent == null)
				return;

			Ent.SetEntityManager(this);
			Ent.SetGameState(GState);
			Entities.Add(Ent);
		}

		void UpdateEntityPhysics(VoxEntity Ent, float Dt) {
			GameState GS = Ent.GetGameState();
			ChunkMap map = GS.Map;

			// Apply gravity
			PhysicsUtils.ApplyGravity(ref Ent.Velocity, 9.81f, Dt);

			// Move with axis-separated collision
			Ent.Position = PhysicsUtils.MoveWithCollision(map, Ent.Position, Ent.Size, ref Ent.Velocity, Dt);

			// --- Player collision check using AABB ---
			if (GS?.Ply != null) {
				AABB playerAABB = PhysicsUtils.CreatePlayerAABB(GS.Ply.Position);
				AABB entityAABB = PhysicsUtils.CreateEntityAABB(Ent.Position, Ent.Size);

				bool touching = playerAABB.Overlaps(entityAABB);

				if (touching && !Ent._WasPlayerTouching) {
					Ent.OnPlayerTouch(GS.Ply);
					Ent._WasPlayerTouching = true;
				} else if (!touching) {
					Ent._WasPlayerTouching = false;
				}
			}
		}

		public void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr) {
			for (int i = 0; i < Entities.Count; i++) {
				VoxEntity Ent = Entities[i];

				if (Ent == null)
					continue;

				UpdateEntityPhysics(Ent, Dt);
				Ent.UpdateLockstep(TotalTime, Dt, InMgr);
				Ent.OnUpdatePhysics(Dt);
			}
		}

		public void Draw3D(float TimeAlpha, ref GameFrameInfo LastFrame) {
			for (int i = 0; i < Entities.Count; i++) {
				VoxEntity Ent = Entities[i];

				if (Ent == null)
					continue;

				Ent.Draw3D(TimeAlpha, ref LastFrame);
			}
		}
	}
}
