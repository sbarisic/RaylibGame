using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

using Raylib_cs;

using RaylibGame.States;

using TextCopy;

using Voxelgine.Engine;
using Voxelgine.Graphics;

namespace Voxelgine {
	internal class Program {
		public static bool DebugMode;

		public static GameWindow Window;

		public static GameStateImpl MainMenuState;
		public static GameStateImpl GameState;
		public static GameStateImpl OptionsState;

		public static Clipboard Clipb;

		public static float TotalTime;

		static void Main(string[] args) {
			DebugMode = Debugger.IsAttached;

			Clipb = new Clipboard();

			Window = new GameWindow(1920, 1080, "Aurora Falls");
			ResMgr.InitResources();
			ResMgr.InitHotReload();

			GraphicsUtils.Init();
			Scripting.Init();

			MainMenuState = new MainMenuState(Window);
			GameState = new GameState(Window);
			OptionsState = new OptionsState(Window);

			Window.SetState(MainMenuState);


			Stopwatch SWatch = Stopwatch.StartNew();
			const float MaxFrameTime = 0.25f;

			float Time = 0;

			float DeltaTime = 0.038f; //0.01f;  //float DeltaTime = 0.015f; // 66.6 update ticks per second
									  //float DeltaTime = 0.04f; // 25 updates per second
									  //float DeltaTime = 0.2f; // 5 updates per second

			float Accumulator = 0;
			float CurrentTime = 0;

			GameFrameInfo LastFrame = new GameFrameInfo();

			while (Window.IsOpen()) {
				TotalTime = (float)Raylib.GetTime();
				ResMgr.HandleHotReload();

				float NewTime = (float)SWatch.Elapsed.TotalSeconds;
				float FrameTime = NewTime - CurrentTime;
				if (FrameTime > MaxFrameTime) {
					FrameTime = MaxFrameTime;
				}

				CurrentTime = NewTime;
				Accumulator += FrameTime;
				int Updates = 0;

				Window.Tick();

				while (Accumulator >= DeltaTime) {
					// PreviousState = CurrentState;

					// Update
					Window.UpdateLockstep(Time, DeltaTime);
					Updates++;

					Time += DeltaTime;
					Accumulator -= DeltaTime;
				}

				float TimeAlpha = Accumulator / DeltaTime;

				// TODO: Interpolation for rendering?
				// State = CurrentState * TimeAlpha + PreviousState * (1.0f - TimeAlpha);
				LastFrame = Window.Draw(TimeAlpha, LastFrame);
			}
		}
	}
}
