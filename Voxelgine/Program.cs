using Raylib_cs;

using RaylibGame.States;

using TextCopy;

using Voxelgine.Engine;
using Voxelgine.Graphics;

namespace Voxelgine {
	internal class Program {
		public static GameWindow Window;

		public static GameStateImpl MainMenuState;
		public static GameStateImpl GameState;
		public static GameStateImpl OptionsState;

		public static Clipboard Clipb;

		static void Main(string[] args) {
			Clipb = new Clipboard();

			Window = new GameWindow(1920, 1080, nameof(Voxelgine));
			GraphicsUtils.Init();
			Scripting.Init();

			MainMenuState = new MainMenuState(Window);
			GameState = new GameState(Window);
			OptionsState = new OptionsState(Window);

			Window.SetState(MainMenuState);


			float Dt = 0;

			while (Window.IsOpen()) {
				Dt = Raylib.GetFrameTime();

				if (Dt != 0 && Dt < 1.5f) {
					Window.Update(Dt);
				}

				Window.Draw();
			}
		}
	}
}
