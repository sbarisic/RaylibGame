using RaylibGame.Engine;

using RaylibSharp;

using RaylibTest.Engine;
using RaylibTest.Graphics;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using RaylibGame.States;

namespace RaylibTest {
	class Program {
		public static GameWindow Window;

		public static GameStateImpl MainMenuState;
		public static GameStateImpl GameState;
		public static GameStateImpl OptionsState;

		static void Main(string[] args) {
			Window = new GameWindow(1920, 1080, "3D Test");
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
