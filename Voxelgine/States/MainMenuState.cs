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

		/*GUILabel Lbl;
		GUILabel OutLbl;
		Vector2 MousePos;*/

		Rectangle DbgRect = new Rectangle();

		public MainMenuState(GameWindow window) : base(window) {
			GUI = new GUIManager(window);
			//GUI.CreateConsole(window, out Lbl, out OutLbl);

			Vector2 BtnSize = GUI.WindowScale(new Vector2(0.2f, 0.07f));
			Vector2 Pos = GUI.WindowScale(new Vector2(0.3f, 0.3f));

			GUIButton Btn_NewGame = new GUIButton(GUI);
			Btn_NewGame.Pos = GUI.WindowScale(new Vector2(0.3f, 0.3f));
			Btn_NewGame.Size = BtnSize;
			Btn_NewGame.Text = "New Game";
			Btn_NewGame.OnClickedFunc = (E) => {
				Program.Window.SetState(Program.GameState);
			};
			GUI.AddElement(Btn_NewGame);

			//Pos.Y = Pos.Y + BtnSize.Y + BtnMargin;

			GUIButton Btn_Quit = new GUIButton(GUI);
			Btn_Quit.Pos = GUI.WindowScale(new Vector2(0.3f, 0.4f));
			Btn_Quit.Size = BtnSize;
			Btn_Quit.Text = "Quit";
			Btn_Quit.OnClickedFunc = (E) => {
				Program.Window.Close();
			};
			GUI.AddElement(Btn_Quit);

			GUIButton Btn_Wat = new GUIButton(GUI);
			Btn_Wat.Pos = GUI.WindowScale(new Vector2(0.5f, 0.4f));
			Btn_Wat.Size = BtnSize;
			Btn_Wat.Text = "Wat";
			Btn_Wat.OnClickedFunc = (E) => {
				Console.WriteLine("Wat");
			};
			GUI.AddElement(Btn_Wat);

			List<GUIElement> IB = new List<GUIElement>();
			IB.Add(Btn_NewGame);
			IB.Add(Btn_Quit);

			IB.Add(AddItmBox(GUI.WindowScale(new Vector2(0.3f, 0.5f)), ResMgr.GetTexture("items/heart_empty.png")));
			IB.Add(AddItmBox(GUI.WindowScale(new Vector2(0.3f, 0.6f)), ResMgr.GetTexture("items/heart_half.png")));
			IB.Add(AddItmBox(GUI.WindowScale(new Vector2(0.3f, 0.7f)), ResMgr.GetTexture("items/heart_full.png")));

			IB.Add(Btn_Wat);

			Vector2 CenterSize = new Vector2(400, 500);

			DbgRect = new Rectangle(new Vector2(Window.Width, Window.Height) / 2 - CenterSize / 2, CenterSize);
			GUI.CenterVertical(DbgRect.Position, DbgRect.Size, new Vector2(15, 10), 5, IB.ToArray());

			/*GUIIconBar IcnBar = new GUIIconBar(GUI, IconBarStyle.XpBar);
			IcnBar.Pos = new Vector2(800, 200);
			IcnBar.Txt = "XP Level";
			GUI.AddElement(IcnBar);

			GUIIconBar IcnBar2 = new GUIIconBar(GUI, IconBarStyle.Hearts);
			IcnBar2.Pos = new Vector2(800, 300);
			IcnBar2.TxtOffset = new Vector2(0, -10);
			IcnBar2.Txt = "Helth";
			IcnBar2.Value = 20;
			GUI.AddElement(IcnBar2);*/


			Texture2D Icon = ResMgr.GetTexture("items/pickaxe.png");
			Raylib.SetTextureFilter(Icon, TextureFilter.Point);
			Btn_NewGame.SetIcon(Icon);

			Texture2D Icon2 = ResMgr.GetTexture("items/lava.png");
			Raylib.SetTextureFilter(Icon2, TextureFilter.Point);
			Btn_Quit.SetIcon(Icon2);

			//============


		}

		GUIItemBox AddItmBox(Vector2 Pos, Texture2D Icn) {
			GUIItemBox IBox = new GUIItemBox(GUI);
			IBox.Pos = Pos;
			IBox.Size = new Vector2(64, 64);
			IBox.IsSelected = false;
			IBox.Text = "64";
			IBox.OnClickedFunc = (E) => {
				Console.WriteLine("Clicked item!");
			};
			GUI.AddElement(IBox);

			Raylib.SetTextureFilter(Icn, TextureFilter.Point);
			IBox.SetIcon(Icn, 3);
			return IBox;
		}

		public override void SwapTo() {
			Cam.Zoom = 1;
			Cam.Rotation = 0;
			Cam.Offset = Vector2.Zero;
			Cam.Target = Vector2.Zero;
		}

		public override void Tick() {
			GUI.Tick();
		}

		public override void Draw2D() {
			Raylib.ClearBackground(new Color(200, 200, 200));
			Raylib.BeginMode2D(Cam);

			GUI.DrawWindowBorder(DbgRect.Position, DbgRect.Size);
			GUI.Draw();

			Raylib.EndMode2D();
		}
	}
}
