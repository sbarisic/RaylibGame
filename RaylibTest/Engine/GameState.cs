using RaylibSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaylibTest.Engine {
	abstract class GameStateImpl {
		public virtual void SwapTo() {
		}

		public virtual void Update(float Dt) {
		}

		public virtual void Draw() {
			Raylib.ClearBackground(new Color(200, 150, 100));
		}
	}
}
