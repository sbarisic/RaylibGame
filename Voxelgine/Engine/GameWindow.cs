using Raylib_cs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine {
	class GameWindow {
		GameStateImpl State;

		public int Width { get; private set; }
		public int Height { get; private set; }
		bool Open;

		public GameWindow(int W, int H, string Title) {
			Open = true;

			Raylib.InitWindow(Width = W, Height = H, Title);
			Raylib.SetTargetFPS(60);
			Raylib.SetExitKey(0);
		}

		public void SetState(GameStateImpl State) {
			this.State = State;
			Raylib.EnableCursor();
			State.SwapTo();
		}

		public bool IsOpen() {
			return (!Raylib.WindowShouldClose() && Open);
		}

		public void Close() {
			Open = false;
		}

		public void Update(float Dt) {
			State.Update(Dt);
		}

		public void Draw() {
			Raylib.BeginDrawing();
			State.Draw();
			Raylib.EndDrawing();
		}
	}
}
