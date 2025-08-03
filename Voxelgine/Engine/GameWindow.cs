using Raylib_cs;

using RaylibGame.States;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Windows.Storage.Pickers;

namespace Voxelgine.Engine {
	struct GameFrameInfo {
		public bool Empty;
		public Camera3D Cam;
		public Vector3 Pos;
		public Vector3 CamAngle;
		public Quaternion ViewModelRot;
		public Vector3 ViewModelPos;
		public Vector3 FeetPosition;

		public GameFrameInfo() {
			Empty = true;
		}

		public GameFrameInfo Interpolate(GameFrameInfo Old, float T) {
			// State = CurrentState * TimeAlpha + PreviousState * (1.0f - TimeAlpha);

			GameFrameInfo New = new GameFrameInfo();

			New.Cam.FovY = float.Lerp(Old.Cam.FovY, Cam.FovY, T);
			New.Cam.Position = Vector3.Lerp(Old.Cam.Position, Cam.Position, T);
			New.Cam.Target = Vector3.Lerp(Old.Cam.Target, Cam.Target, T);
			New.Cam.Up = Cam.Up;
			New.Cam.Projection = Cam.Projection;
			New.Pos = Vector3.Lerp(Old.Pos, Pos, T);
			New.CamAngle = Vector3.Lerp(Old.CamAngle, CamAngle, T);
			New.ViewModelPos = Vector3.Lerp(Old.ViewModelPos, ViewModelPos, T);
			New.ViewModelRot = Quaternion.Slerp(Old.ViewModelRot, ViewModelRot, T);
			New.FeetPosition = Vector3.Lerp(Old.FeetPosition, FeetPosition, T);

			return New;
		}

		public static Camera3D Lerp(Camera3D Old, Camera3D Cam, float T) {
			Camera3D New = new Camera3D();

			New.Projection = Cam.Projection;
			New.FovY = float.Lerp(Old.FovY, Cam.FovY, T);
			New.Position = Vector3.Lerp(Old.Position, Cam.Position, T);
			New.Target = Vector3.Lerp(Old.Target, Cam.Target, T);
			New.Up = Cam.Up;

			return New;
		}
	}

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

			if (Program.Cfg.HighDpiWindow)
				Raylib.SetWindowState(ConfigFlags.HighDpiWindow);

			if (Program.Cfg.VSync)
				Raylib.SetWindowState(ConfigFlags.VSyncHint);

			if (Program.Cfg.Msaa) {
				Enable_SSAA = true;
				//Raylib.SetWindowState(ConfigFlags.Msaa4xHint);
			} else {
				Enable_SSAA = false;
			}
			//Raylib.SetWindowState(ConfigFlags.Msaa4xHint);
			//Raylib.SetWindowState(ConfigFlags.)

			Raylib.InitWindow(Width = W, Height = H, Title);
			Raylib.SetTargetFPS(Program.Cfg.TargetFPS);
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
			}

			Raylib.BeginDrawing();
			Raylib.ClearBackground(new Color(200, 150, 100, 255));

			BeginScreenShader();
			State.Draw(TimeAlpha, ref LastFrame, ref FInfo);
			EndScreenShader("screen");

			BeginScreenShader();
			State.Draw2D();
			EndScreenShader("screen_gui");

			Raylib.EndDrawing();

			GetCurrentFrame(ref FInfo, State as GameState);
			return FInfo;
		}
	}
}
