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
			GUI = new GUIManager(window);
			//GUI.CreateConsole(window, out Lbl, out OutLbl);

			float BtnMargin = 16;
			Vector2 BtnSize = GUI.WindowScale(new Vector2(0.2f, 0.07f));
			Vector2 Offset = new Vector2(0, 0);
			Vector2 Pos = GUI.WindowScale(new Vector2(0.3f, 0.3f));

			GUIButton Btn_NewGame = new GUIButton(GUI);
			Btn_NewGame.Pos = Pos;
			Btn_NewGame.Size = BtnSize;
			Btn_NewGame.Text = "New Game";
			Btn_NewGame.OnClickedFunc = (E) => {
				Program.Window.SetState(Program.GameState);
			};
			GUI.AddElement(Btn_NewGame);

			Pos.Y = Pos.Y + BtnSize.Y + BtnMargin;

			GUIButton Btn_Quit = new GUIButton(GUI);
			Btn_Quit.Pos = Pos;
			Btn_Quit.Size = BtnSize;
			Btn_Quit.Text = "Quit";
			Btn_Quit.OnClickedFunc = (E) => {
				Program.Window.Close();
			};
			GUI.AddElement(Btn_Quit);

			GUIItemBox IBox = new GUIItemBox(GUI);
			IBox.Pos = new Vector2(100, 100);
			IBox.Size = new Vector2(64, 64);
			IBox.IsSelected = true;
			IBox.Text = "64";
			IBox.OnClickedFunc = (E) => {
				Console.WriteLine("Clicked item!");
			};
			GUI.AddElement(IBox);


			Texture2D Icon = ResMgr.GetTexture("items/pickaxe.png");
			Raylib.SetTextureFilter(Icon, TextureFilter.Point);
			Btn_NewGame.SetIcon(Icon);

			Texture2D Icon2 = ResMgr.GetTexture("items/lava.png");
			Raylib.SetTextureFilter(Icon2, TextureFilter.Point);
			Btn_Quit.SetIcon(Icon2);

			Texture2D Icon3 = ResMgr.GetTexture("items/lava.png");
			Raylib.SetTextureFilter(Icon3, TextureFilter.Point);
			IBox.SetIcon(Icon3, 3);
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
