using Raylib_cs;
using Voxelgine.Engine;
using Voxelgine;

using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Voxelgine.GUI;

namespace RaylibGame.States {
	class MainMenuState : GameStateImpl {
		Camera2D Cam = new Camera2D();
		GUIManager GUI;

		GUILabel Lbl;
		GUILabel OutLbl;
		Vector2 MousePos;

		public MainMenuState(GameWindow window) : base(window) {
			GUI = new GUIManager();
			GUI.CreateConsole(window, out Lbl, out OutLbl);
		}

		public override void SwapTo() {
			Cam.Zoom = 1;
		}

		public override void Update(float Dt) {
			MousePos = Raylib.GetMousePosition();
			GUI.Update(Dt);
		}

		public override void Draw() {
			const int BtnWidth = 400;
			const int BtnHeight = 40;
			const int BtnPadding = 10;
			int BtnX = (Program.Window.Width / 2) - (BtnWidth / 2);

			//RL.ClearBackground(new Color(160, 180, 190, 255));
			Raylib.ClearBackground(new Color(200, 200, 200));
			Raylib.BeginMode2D(Cam);

			GUI.Draw();

			/*if (Raygui.GuiButton(new Rectangle(BtnX, 100 + (BtnHeight + BtnPadding) * 0, BtnWidth, BtnHeight), "New Game"))
				Program.Window.SetState(Program.GameState);

			if (Raygui.GuiButton(new Rectangle(BtnX, 100 + (BtnHeight + BtnPadding) * 1, BtnWidth, BtnHeight), "Options"))
				Program.Window.SetState(Program.OptionsState);

			if (Raygui.GuiButton(new Rectangle(BtnX, 100 + (BtnHeight + BtnPadding) * 2, BtnWidth, BtnHeight), "Quit"))
				Program.Window.Close();
			*/


			Raylib.EndMode2D();
		}
	}
}
