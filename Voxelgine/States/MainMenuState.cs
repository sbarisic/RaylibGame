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

			Lbl = new GUILabel(GUI, 64);
			Lbl.Pos = new Vector2(430, 700);

			OutLbl = new GUILabel(GUI);
			OutLbl.Pos = new Vector2(430, 200);
			OutLbl.Size = new Vector2(Lbl.Size.X, 500);
			OutLbl.ScrollText = true;

			Lbl.OnInputFunc = (Txt) => {
				OutLbl.WriteLine(Txt);
			};
		}

		public override void SwapTo() {
			Cam.Zoom = 1;
		}

		public override void Update(float Dt) {
			MousePos = Raylib.GetMousePosition();

			Lbl.Update(Dt);
			OutLbl.Update(Dt);
		}

		public override void Draw() {
			const int BtnWidth = 400;
			const int BtnHeight = 40;
			const int BtnPadding = 10;
			int BtnX = (Program.Window.Width / 2) - (BtnWidth / 2);

			//RL.ClearBackground(new Color(160, 180, 190, 255));
			Raylib.ClearBackground(new Color(200, 200, 200));
			Raylib.BeginMode2D(Cam);

			Lbl.IsReading = Lbl.IsInside(MousePos) || OutLbl.IsInside(MousePos);
			Lbl.Draw(Lbl.IsReading, Raylib.IsMouseButtonPressed(MouseButton.Left), Raylib.IsMouseButtonDown(MouseButton.Left));

			OutLbl.Draw(Lbl.IsReading, Raylib.IsMouseButtonPressed(MouseButton.Left), Raylib.IsMouseButtonDown(MouseButton.Left));


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
