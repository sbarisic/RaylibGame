using RaylibGame.States;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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

		public void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr) {
			for (int i = 0; i < Entities.Count; i++) {
				VoxEntity Ent = Entities[i];

				if (Ent == null)
					continue;

				Ent.UpdateLockstep(TotalTime, Dt, InMgr);
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
