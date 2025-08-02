using RaylibGame.States;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine {
	interface IEntity {
		public Vector3 GetPosition();

		public void SetPosition(Vector3 Pos);

		public Vector3 GetSize();

		public void SetSize(Vector3 Size);

		public GameState GetGameState();

		public void SetGameState(GameState State);

		public void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr);

		public void Draw3D(float TimeAlpha, ref GameFrameInfo LastFrame);
	}
}
