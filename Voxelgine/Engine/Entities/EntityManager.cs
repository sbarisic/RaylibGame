using RaylibGame.States;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine {
	internal class EntityManager {
		List<IEntity> Entities;

		public EntityManager() {
			Entities = new List<IEntity>();
		}

		public void Spawn(GameState GState, IEntity Ent) {
			if (Ent == null)
				return;

			Ent.SetEntityManager(this);
			Ent.SetGameState(GState);
			Entities.Add(Ent);
		}

		public void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr) {
			for (int i = 0; i < Entities.Count; i++) {
				IEntity Ent = Entities[i];

				if (Ent == null)
					continue;

				Ent.UpdateLockstep(TotalTime, Dt, InMgr);
			}
		}

		public void Draw3D(float TimeAlpha, ref GameFrameInfo LastFrame) {
			for (int i = 0; i < Entities.Count; i++) {
				IEntity Ent = Entities[i];

				if (Ent == null)
					continue;

				Ent.Draw3D(TimeAlpha, ref LastFrame);
			}
		}
	}
}
