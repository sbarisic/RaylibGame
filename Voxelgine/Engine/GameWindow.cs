using Raylib_cs;

using RaylibGame.States;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine {
	class GameWindow {
		public InputMgr InMgr;

		public int Width {
			get; private set;
		}

		public int Height {
			get; private set;
		}

		GameStateImpl State;

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

			InMgr = new InputMgr();

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

		public void Tick() {
			InMgr.Tick();
			State.Tick();
		}

		public void UpdateLockstep(float TotalTime, float Dt) {
			State.UpdateLockstep(TotalTime, Dt);
		}

		void BeginScreenShader() {
			if (!HasWindowRT)
				return;

			Raylib.BeginTextureMode(WindowRT);
			Raylib.ClearBackground(new Color(0, 0, 0, 0));
		}

		void EndScreenShader(string ShaderName) {
			if (!HasWindowRT)
				return;

			Raylib.EndTextureMode();

			Rectangle Src = new Rectangle(0, 0, WindowRT.Texture.Width, -WindowRT.Texture.Height);
			Rectangle Dst = new Rectangle(0, 0, Width, Height);

			Shader ScreenShader = ResMgr.GetShader(ShaderName);
			Raylib.BeginShaderMode(ScreenShader);

			int Loc_Resolution = Raylib.GetShaderLocation(ScreenShader, "resolution");
			if (Loc_Resolution >= 0) {
				Raylib.SetShaderValue(ScreenShader, Loc_Resolution, new Vector2(Width, Height), ShaderUniformDataType.Vec2);
			}

			int Loc_Time = Raylib.GetShaderLocation(ScreenShader, "time");
			if (Loc_Time >= 0) {
				Raylib.SetShaderValue(ScreenShader, Loc_Time, Program.TotalTime, ShaderUniformDataType.Float);
			}

			Raylib.DrawTexturePro(WindowRT.Texture, Src, Dst, Vector2.Zero, 0, Color.White);

			Raylib.EndShaderMode();
		}

		public void Draw(float TimeAlpha) {
			if (Raylib.IsWindowResized()) {
				Width = Raylib.GetRenderWidth();
				Height = Raylib.GetRenderHeight();
				ReloadRT();
			}

			Raylib.BeginDrawing();
			Raylib.ClearBackground(new Color(200, 150, 100, 255));

			BeginScreenShader();
			State.Draw(TimeAlpha);
			EndScreenShader("screen");

			BeginScreenShader();
			State.Draw2D();
			EndScreenShader("screen_gui");

			Raylib.EndDrawing();
		}
	}
}
