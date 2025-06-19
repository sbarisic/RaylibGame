using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine {
	class GameWindow {
		GameStateImpl State;

		public int Width {
			get; private set;
		}

		public int Height {
			get; private set;
		}

		bool Open;

		bool HasWindowRT;
		RenderTexture2D WindowRT;

		// SSAA has a screen space rendering bug, scale UI accordingly?
		bool Enable_SSAA = false;

		public GameWindow(int W, int H, string Title) {
			Open = true;

			Raylib.SetWindowState(ConfigFlags.HighDpiWindow);
			Raylib.SetWindowState(ConfigFlags.VSyncHint);
			//Raylib.SetWindowState(ConfigFlags.Msaa4xHint);
			//Raylib.SetWindowState(ConfigFlags.)

			Raylib.InitWindow(Width = W, Height = H, Title);
			Raylib.SetTargetFPS(240);
			Raylib.SetExitKey(0);

			HasWindowRT = false;
			ReloadRT();
		}

		void ReloadRT() {
			if (HasWindowRT) {
				Raylib.UnloadRenderTexture(WindowRT);
			}

			float Factor = 1;

			if (Enable_SSAA)
				Factor = 2;

			WindowRT = Raylib.LoadRenderTexture((int)(Width * Factor), (int)(Height * Factor));
			Raylib.SetTextureFilter(WindowRT.Texture, TextureFilter.Bilinear);
			Raylib.SetTextureWrap(WindowRT.Texture, TextureWrap.Clamp);

			HasWindowRT = true;
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
			if (Raylib.IsWindowResized()) {
				Width = Raylib.GetRenderWidth();
				Height = Raylib.GetRenderHeight();
				ReloadRT();
			}

			Raylib.BeginDrawing();
			if (HasWindowRT) {
				Raylib.BeginTextureMode(WindowRT);
			}

			State.Draw();

			if (HasWindowRT) {
				Raylib.EndTextureMode();

				Rectangle Src = new Rectangle(0, 0, WindowRT.Texture.Width, -WindowRT.Texture.Height);
				Rectangle Dst = new Rectangle(0, 0, Width, Height);

				Shader ScreenShader = ResMgr.GetShader("screen");
				Raylib.BeginShaderMode(ScreenShader);
				Raylib.SetShaderValue(ScreenShader, Raylib.GetShaderLocation(ScreenShader, "resolution"), new Vector2(Width, Height), ShaderUniformDataType.Vec2);

				Raylib.DrawTexturePro(WindowRT.Texture, Src, Dst, Vector2.Zero, 0, Color.White);

				Raylib.EndShaderMode();
			}

			State.Draw2D();
			Raylib.EndDrawing();
		}
	}
}
