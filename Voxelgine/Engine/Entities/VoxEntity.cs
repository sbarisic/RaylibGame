using RaylibGame.States;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine {
	public abstract class VoxEntity {
		public abstract Vector3 GetPosition();
		public abstract void SetPosition(Vector3 Pos);

		public abstract Vector3 GetVelocity();
		public abstract void SetVelocity(Vector3 Velocity);

		public abstract Vector3 GetSize();
		public abstract void SetSize(Vector3 Size);

		public abstract GameState GetGameState();
		public abstract void SetGameState(GameState State);

		public abstract EntityManager GetEntityManager();
		public abstract void SetEntityManager(EntityManager EntMgr);

		public abstract void OnPlayerTouch(Player Ply);

		public virtual void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr) {
		}

		public virtual void Draw3D(float TimeAlpha, ref GameFrameInfo LastFrame) {
		}
	}
}
