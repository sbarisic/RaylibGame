using Raylib_cs;

using RaylibGame.States;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Graphics;
using Voxelgine.GUI;

using Windows.Storage.Pickers;

namespace Voxelgine.Engine {
	public unsafe class GameWindow {
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
		GBuffer WindowG;

		// SSAA has a screen space rendering bug, scale UI accordingly?
		bool Enable_SSAA = false;

		Shader DefaultShader;

		public GameWindow(int W, int H, string Title) {
			Open = true;

			if (Program.Cfg.HighDpiWindow)
				Raylib.SetWindowState(ConfigFlags.HighDpiWindow);

			if (Program.Cfg.VSync)
				Raylib.SetWindowState(ConfigFlags.VSyncHint);

			if (Program.Cfg.Borderless)
				Raylib.SetWindowState(ConfigFlags.BorderlessWindowMode);

			if (Program.Cfg.Resizable)
				Raylib.SetWindowState(ConfigFlags.ResizableWindow);

			if (Program.Cfg.Fullscreen && !Program.Cfg.Borderless)
				Raylib.SetWindowState(ConfigFlags.FullscreenMode);

			if (Program.Cfg.Msaa) {
				Enable_SSAA = true;
				//Raylib.SetWindowState(ConfigFlags.Msaa4xHint);
			} else {
				Enable_SSAA = false;
			}
			//Raylib.SetWindowState(ConfigFlags.Msaa4xHint);
			//Raylib.SetWindowState(ConfigFlags.)

			Raylib.InitWindow(Width = W, Height = H, Title);

			int MonCount = Raylib.GetMonitorCount();
			int UseMon = 0;

			if (Program.Cfg.Monitor >= 0 && Program.Cfg.Monitor < MonCount) {
				UseMon = Program.Cfg.Monitor;
			} else {
				Program.Cfg.Monitor = 0;
			}

			int MW = Raylib.GetMonitorWidth(UseMon);
			int MH = Raylib.GetMonitorHeight(UseMon);
			int MFPS = Raylib.GetMonitorRefreshRate(UseMon);
			string MonName = new string(Raylib.GetMonitorName(UseMon));

			Console.WriteLine("Using monitor '{0}' ({1}x{2})", MonName, MW, MH);

			if (MW < W)
				W = MW;
			if (MH < H)
				H = MH;


			int FPS = Program.Cfg.TargetFPS;
			if (FPS <= 0) {
				FPS = MFPS;
			}

			Console.WriteLine("Target FPS: {0}", FPS);
			Raylib.SetTargetFPS(FPS);
			Raylib.SetExitKey(0);


			if (Program.Cfg.Borderless) {
				Raylib.ToggleBorderlessWindowed();
				Raylib.SetWindowSize(W, H);
				Raylib.SetWindowPosition(MW / 2 - W / 2, MH / 2 - H / 2);
			}

			if (Program.Cfg.Fullscreen) {
				if (Program.Cfg.Borderless) {
					Raylib.SetWindowSize(MW, MH);
				} else {
					Raylib.SetWindowMonitor(UseMon);

					if (Program.Cfg.UseFSDesktopRes) {
						Width = W = MW;
						Height = H = MH;
						Raylib.SetWindowSize(W, H);
					}

					Raylib.ToggleFullscreen();
				}
			}

			Raylib.SetWindowFocused();

			InMgr = new InputMgr();

			HasWindowRT = true;
			ReloadRT();

			DefaultShader = ResMgr.GetShader("default");
		}

		void ReloadRT() {
			if (HasWindowRT) {
				WindowG?.Dispose();
				WindowG = null;
			}

			float Factor = 1;

			if (Enable_SSAA)
				Factor = 2;

			//WindowRT = Raylib.LoadRenderTexture((int)(Width * Factor), (int)(Height * Factor));
			//WindowRT = LoadRenderTextureDepthTex((int)(Width * Factor), (int)(Height * Factor));
			WindowG = new GBuffer((int)(Width * Factor), (int)(Height * Factor));

			Raylib.SetTextureFilter(WindowG.Target.Texture, TextureFilter.Bilinear);
			Raylib.SetTextureWrap(WindowG.Target.Texture, TextureWrap.Clamp);

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

		public void Tick(float GameTime) {
			InMgr.Tick(GameTime);
			State.Tick();
		}

		public void UpdateLockstep(float TotalTime, float Dt) {
			State.UpdateLockstep(TotalTime, Dt, InMgr);
		}

		void BeginScreenShader() {
			if (!HasWindowRT)
				return;

			Raylib.BeginTextureMode(WindowG.Target);
			Raylib.ClearBackground(new Color(0, 0, 0, 0));
		}

		void EndScreenShader(string ShaderName) {
			if (!HasWindowRT)
				return;

			Raylib.EndTextureMode();

			Rectangle Src = new Rectangle(0, 0, WindowG.Target.Texture.Width, -WindowG.Target.Texture.Height);
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

			Raylib.DrawTexturePro(WindowG.Target.Texture, Src, Dst, Vector2.Zero, 0, Color.White);

			Raylib.EndShaderMode();
		}

		void GetCurrentFrame(ref GameFrameInfo FInfo, GameState GState) {
			if (GState == null)
				return;

			FInfo.Empty = false;
			FInfo.Pos = FPSCamera.Position;
			FInfo.Cam = GState.Ply.Cam;
			FInfo.CamAngle = GState.Ply.GetCamAngle();
			FInfo.FeetPosition = GState.Ply.FeetPosition;
			//FInfo.Pos = FPSCamera.Position;

			FInfo.ViewModelPos = GState.Ply.ViewMdl.ViewModelPos;
			FInfo.ViewModelRot = GState.Ply.ViewMdl.VMRot;
		}

		// State = CurrentState * TimeAlpha + PreviousState * (1.0f - TimeAlpha);
		public GameFrameInfo Draw(float TimeAlpha, GameFrameInfo LastFrame) {
			GameFrameInfo FInfo = new GameFrameInfo();
			GetCurrentFrame(ref FInfo, State as GameState);

			//FPSCamera.Position = LastFrame.Pos;
			if (State is GameState GS && !LastFrame.Empty) {
				GameFrameInfo Interp = FInfo.Interpolate(LastFrame, TimeAlpha);

				//FPSCamera.Position = Interp.Pos;
				GS.Ply.Cam = Interp.Cam;
				GS.Ply.SetCamAngle(Interp.CamAngle);
				GS.PlayerCollisionBoxPos = Interp.FeetPosition;

				//GS.Ply.ViewMdl.ViewModelPos = Interp.ViewModelPos;
				//GS.Ply.ViewMdl.VMRot = Interp.ViewModelRot;

				//FPSCamera.Position = Vector3.Lerp(LastFrame.Pos, FPSCamera.Position, TimeAlpha);
			}

			if (Raylib.IsWindowResized()) {
				Width = Raylib.GetRenderWidth();
				Height = Raylib.GetRenderHeight();
				ReloadRT();
				State.OnResize(this);
			}

			Raylib.BeginDrawing();
			Raylib.ClearBackground(new Color(200, 150, 100, 255));

			BeginScreenShader();
			{
				State.Draw(TimeAlpha, ref LastFrame, ref FInfo);
			}
			EndScreenShader("screen");

			BeginScreenShader();
			{
				State.Draw2D();

			}
			EndScreenShader("screen_gui");

			Raylib.EndDrawing();

			GetCurrentFrame(ref FInfo, State as GameState);
			return FInfo;
		}
	}
}
