using Raylib_cs;

using RaylibGame.States;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mail;
using System.Numerics;
using System.Runtime.Intrinsics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Graphics;
using Voxelgine.GUI;

namespace Voxelgine.Engine
{
	public unsafe class GameWindow
	{
		public InputMgr InMgr;

		public int Width
		{
			get; private set;
		}

		public int Height
		{
			get; private set;
		}

		public float AspectRatio
		{
			get
			{
				if (Height == 0 || Width == 0)
					return 1.0f;

				return (float)Width / Height;
			}
		}

		GameStateImpl State;

		bool Open;
		bool HasWindowRT;
		public GBuffer WindowG;

		public RenderTexture2D ViewmodelRT;
		bool HasViewmodelRT = false;

		// SSAA has a screen space rendering bug, scale UI accordingly?
		bool Enable_SSAA = false;

		Shader DefaultShader;

		public GameWindow(int W, int H, string Title)
		{
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

			if (Program.Cfg.Msaa)
			{
				Enable_SSAA = true;
				//Raylib.SetWindowState(ConfigFlags.Msaa4xHint);
			}
			else
			{
				Enable_SSAA = false;
			}
			//Raylib.SetWindowState(ConfigFlags.Msaa4xHint);
			//Raylib.SetWindowState(ConfigFlags.)

			Raylib.InitWindow(Width = W, Height = H, Title);

			int MonCount = Raylib.GetMonitorCount();
			int UseMon = 0;

			if (Program.Cfg.Monitor >= 0 && Program.Cfg.Monitor < MonCount)
			{
				UseMon = Program.Cfg.Monitor;
			}
			else
			{
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
			if (FPS <= 0)
			{
				FPS = MFPS;
			}

			Console.WriteLine("Target FPS: {0}", FPS);
			Raylib.SetTargetFPS(FPS);
			Raylib.SetExitKey(0);


			if (Program.Cfg.Borderless)
			{
				Raylib.ToggleBorderlessWindowed();
				Raylib.SetWindowSize(W, H);
				Raylib.SetWindowPosition(MW / 2 - W / 2, MH / 2 - H / 2);
			}

			if (Program.Cfg.Fullscreen)
			{
				if (Program.Cfg.Borderless)
				{
					Raylib.SetWindowSize(MW, MH);
				}
				else
				{
					Raylib.SetWindowMonitor(UseMon);

					if (Program.Cfg.UseFSDesktopRes)
					{
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

		void ReloadRT()
		{
			if (HasWindowRT)
			{
				WindowG?.Dispose();
				WindowG = null;
			}

			if (HasViewmodelRT)
			{
				Raylib.UnloadRenderTexture(ViewmodelRT);
				HasViewmodelRT = false;
			}

			if (!HasViewmodelRT)
			{
				ViewmodelRT = Raylib.LoadRenderTexture(Width, Height);
				HasViewmodelRT = true;
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

		public void SetState(GameStateImpl State)
		{
			this.State = State;
			Raylib.EnableCursor();
			State.SwapTo();
		}

		public bool IsOpen()
		{
			return (!Raylib.WindowShouldClose() && Open);
		}

		public void Close()
		{
			Open = false;
		}

		public void Tick(float GameTime)
		{
			InMgr.Tick(GameTime);
			State.Tick(GameTime);
		}

		public void UpdateLockstep(float TotalTime, float Dt)
		{
			State.UpdateLockstep(TotalTime, Dt, InMgr);
		}

		Shader CurrentShader;

		public void BeginShaderMode(string shaderName)
		{
			CurrentShader = ResMgr.GetShader(shaderName);
			Raylib.BeginShaderMode(CurrentShader);
		}

		public void SetShaderValue<T>(string valName, T Val)
		{
			int Loc = Raylib.GetShaderLocation(CurrentShader, valName);
			if (Loc <= 0)
				return;

			if (typeof(T) == typeof(Vector2))
			{
				Raylib.SetShaderValue(CurrentShader, Loc, (Vector2)(object)Val, ShaderUniformDataType.Vec2);
			}
			else if (typeof(T) == typeof(float))
			{
				Raylib.SetShaderValue(CurrentShader, Loc, (float)(object)Val, ShaderUniformDataType.Float);
			}
			else if (typeof(T) == typeof(Vector3))
			{
				Raylib.SetShaderValue(CurrentShader, Loc, (Vector3)(object)Val, ShaderUniformDataType.Vec3);
			}
			else if (typeof(T) == typeof(Vector4))
			{
				Raylib.SetShaderValue(CurrentShader, Loc, (Vector4)(object)Val, ShaderUniformDataType.Vec4);
			}
			else if (typeof(T) == typeof(int))
			{
				Raylib.SetShaderValue(CurrentShader, Loc, (int)(object)Val, ShaderUniformDataType.Int);
			}
			else if (typeof(T) == typeof(uint))
			{
				Raylib.SetShaderValue(CurrentShader, Loc, (uint)(object)Val, ShaderUniformDataType.UInt);
			}
			else if (typeof(T) == typeof(Texture2D))
			{
				Texture2D TT = (Texture2D)(object)Val;
				Raylib.SetShaderValue(CurrentShader, Loc, TT, ShaderUniformDataType.Sampler2D);
			}
			else
				throw new NotImplementedException($"Not implemented SetShaderValue for type {typeof(T).Name}");
		}

		public void EndShaderMode()
		{
			Raylib.EndShaderMode();
		}

		void BeginScreenShader()
		{
			if (!HasWindowRT)
				return;

			Raylib.BeginTextureMode(WindowG.Target);
			Raylib.ClearBackground(new Color(0, 0, 0, 0));
		}

		void EndScreenShader(string ShaderName)
		{
			if (!HasWindowRT)
				return;

			Raylib.EndTextureMode();

			Rectangle Src = new Rectangle(0, 0, WindowG.Target.Texture.Width, -WindowG.Target.Texture.Height);
			Rectangle Dst = new Rectangle(0, 0, Width, Height);

			BeginShaderMode(ShaderName);
			SetShaderValue("resolution", new Vector2(Width, Height));
			SetShaderValue("time", Program.TotalTime);

			Raylib.DrawTexturePro(WindowG.Target.Texture, Src, Dst, Vector2.Zero, 0, Color.White);

			EndShaderMode();
		}

		void GetCurrentFrame(ref GameFrameInfo FInfo, GameState GState)
		{
			if (GState == null)
				return;

			FInfo.Empty = false;
			FInfo.Pos = FPSCamera.Position;
			FInfo.Cam = GState.Ply.Cam;
			FInfo.CamAngle = GState.Ply.GetCamAngle();
			FInfo.FeetPosition = GState.Ply.FeetPosition;
			FInfo.Frustum = new Frustum(ref FInfo.Cam);

			FInfo.ViewModelOffset = GState.Ply.ViewMdl.ViewModelOffset;
			FInfo.ViewModelRot = GState.Ply.ViewMdl.VMRot;
		}

		// State = CurrentState * TimeAlpha + PreviousState * (1.0f - TimeAlpha);
		public GameFrameInfo Draw(float TimeAlpha, GameFrameInfo LastFrame)
		{
			GameFrameInfo FInfo = new GameFrameInfo();
			GetCurrentFrame(ref FInfo, State as GameState);

			// Store the original physics state to return (before interpolation modifies anything)
			GameFrameInfo PhysicsFrame = FInfo;

			if (State is GameState GS && !LastFrame.Empty)
			{
				GameFrameInfo Interp = FInfo.Interpolate(LastFrame, TimeAlpha);

				// Apply interpolated camera to RenderCam only (don't modify physics Cam)
				GS.Ply.RenderCam = Interp.Cam;
				GS.PlayerCollisionBoxPos = Interp.FeetPosition;

				// Apply interpolated view model offset (position calculated at draw time from camera + offset)
				GS.Ply.ViewMdl.ViewModelOffset = Interp.ViewModelOffset;
				GS.Ply.ViewMdl.VMRot = Interp.ViewModelRot;
			}
			else
			{
				// First frame - use physics camera as render camera
				if (State is GameState GS2)
				{
					GS2.Ply.RenderCam = GS2.Ply.Cam;
				}
			}

			if (Raylib.IsWindowResized())
			{
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

			// Return the physics state (not interpolated) for next frame's interpolation
			return PhysicsFrame;
		}
	}
}
