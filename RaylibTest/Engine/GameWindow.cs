using RaylibSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaylibTest.Engine {
	class GameWindow {
		GameStateImpl State;

		public int Width { get; private set; }
		public int Height { get; private set; }
		bool Open;

		public GameWindow(int W, int H, string Title) {
			Open = true;

			Raylib.InitWindow(Width = W, Height = H, Title);
			Raylib.SetTargetFPS(60);
		}

		public void SetState(GameStateImpl State) {
			this.State = State;
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
