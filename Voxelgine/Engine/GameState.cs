using Raylib_cs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine {
	abstract class GameStateImpl {
		public GameWindow Window;

		public GameStateImpl(GameWindow window) {
			Window = window;
		}

		public virtual void SwapTo() {
		}

		public virtual void Tick() {
			// Once per frame
		}

		public virtual void UpdateLockstep(float TotalTime, float Dt) {
			// Multiple times per frame, fixed delta
		}

		public virtual void Draw() {
			Raylib.ClearBackground(new Color(200, 150, 100));
		}

		public virtual void Draw2D() {
		}
	}
}
