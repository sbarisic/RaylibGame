using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

using Raylib_cs;

using RaylibGame.States;

using TextCopy;

using Voxelgine.Engine;
using Voxelgine.Graphics;

namespace Voxelgine
{
	internal class Program
	{
		// Fancy info
		public static int ChunkDrawCalls;

		public static GameConfig Cfg;

		public static bool DebugMode;

		public static GameWindow Window;

		public static MainMenuStateFishUI MainMenuState;
		public static GameState GameState;
		public static NPCPreviewState NPCPreviewState;

		public static Clipboard Clipb;

		public static float TotalTime;
		public static LerpManager LerpMgr;

		static void Main(string[] args)
		{
			DebugMode = Debugger.IsAttached;

			Console.WriteLine("Aurora Falls - Voxelgine Engine");
			Console.WriteLine("Running on {0}", Utils.GetOSName());

			Cfg = new GameConfig();
			Cfg.LoadFromJson();

			// Apply mouse sensitivity from config
			FPSCamera.MouseMoveSen = Cfg.MouseSensitivity;

			Clipb = new Clipboard();
			LerpMgr = new LerpManager();

			Window = new GameWindow(Cfg.WindowWidth, Cfg.WindowHeight, "Aurora Falls");
			ResMgr.InitResources();
			ResMgr.InitHotReload();

			List<Texture2D> TexList = new List<Texture2D>();
			for (int i = 1; i < 12; i++)
			{
				TexList.Add(ResMgr.GetTexture($"smoke/{i}.png"));
			}
			ResMgr.CreateCollection("smoke", TexList.ToArray());

			GraphicsUtils.Init();
			Scripting.Init();

			MainMenuState = new MainMenuStateFishUI(Window);
			GameState = new GameState(Window);
			NPCPreviewState = new NPCPreviewState(Window);

			Window.SetState(MainMenuState);


			Stopwatch SWatch = Stopwatch.StartNew();
			const float MaxFrameTime = 0.25f;

			float Time = 0;

			float DeltaTime = 0.015f;//0.038f;  //float DeltaTime = 0.015f; // 66.6 update ticks per second
									 //float DeltaTime = 0.04f; // 25 updates per second
									 //float DeltaTime = 0.2f; // 5 updates per second

			float Accumulator = 0;
			float CurrentTime = 0;

			GameFrameInfo LastFrame = new GameFrameInfo();
			Rlgl.EnableBackfaceCulling();

			while (Window.IsOpen())
			{
				TotalTime = (float)Raylib.GetTime();
				ResMgr.HandleHotReload();

				float NewTime = (float)SWatch.Elapsed.TotalSeconds;
				float FrameTime = NewTime - CurrentTime;
				if (FrameTime > MaxFrameTime)
				{
					FrameTime = MaxFrameTime;
				}

				CurrentTime = NewTime;
				Accumulator += FrameTime;
				int Updates = 0;

				Window.Tick(NewTime);

				while (Accumulator >= DeltaTime)
				{
					// PreviousState = CurrentState;

					// Update
					LerpMgr.Update(DeltaTime);
					Window.UpdateLockstep(Time, DeltaTime);
					Updates++;

					Time += DeltaTime;
					Accumulator -= DeltaTime;
				}

				float TimeAlpha = Accumulator / DeltaTime;

				// Interpolation between physics frames for smooth rendering
				ChunkDrawCalls = 0;
				LastFrame = Window.Draw(TimeAlpha, LastFrame);
			}
		}
	}
}
