using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine {
	public abstract class GameStateImpl {
		public GameWindow Window;

		public GameStateImpl(GameWindow window) {
			Window = window;
		}

		public virtual void SwapTo() {
		}

		public virtual void OnResize(GameWindow Window) {
		}

		public virtual void Tick(float GameTime) {
			// Once per frame
		}

		public virtual void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr) {
			// Multiple times per frame, fixed delta
		}

		public virtual void Draw(float TimeAlpha, ref GameFrameInfo LastFrame, ref GameFrameInfo FInfo) {
		}

		public virtual void Draw2D() {
		}
	}
}
