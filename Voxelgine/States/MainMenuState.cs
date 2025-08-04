using Raylib_cs;
using Voxelgine.Engine;
using Voxelgine;
using Voxelgine.GUI;

using System;
using System.Numerics;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RaylibGame.States {
	class MainMenuState : GameStateImpl {
		Camera2D Cam = new Camera2D();
		GUIManager GUI;

		/*GUILabel Lbl;
		GUILabel OutLbl;
		Vector2 MousePos;*/

		Rectangle DbgRect = new Rectangle();
		GUIWindow OptionsWnd;

		private void CreateMenuButtons(List<GUIElement> IB, Vector2 BtnSize) {
			GUIButton Btn_NewGame = new GUIButton(GUI);
			Btn_NewGame.Pos = GUI.WindowScale(new Vector2(0.3f, 0.3f));
			Btn_NewGame.Size = BtnSize;
			Btn_NewGame.Text = "New Game";
			Btn_NewGame.OnClickedFunc = (E) => {
				Program.Window.SetState(Program.GameState);
			};

			GUIButton Btn_Options = new GUIButton(GUI);
			Btn_Options.Pos = GUI.WindowScale(new Vector2(0.3f, 0.35f));
			Btn_Options.Size = BtnSize;
			Btn_Options.Text = "Options";
			Btn_Options.OnClickedFunc = (E) => {
				Console.WriteLine("Options clicked");
				OptionsWnd.Show();
			};

			GUIButton Btn_Quit = new GUIButton(GUI);
			Btn_Quit.Pos = GUI.WindowScale(new Vector2(0.3f, 0.4f));
			Btn_Quit.Size = BtnSize;
			Btn_Quit.Text = "Quit";
			Btn_Quit.OnClickedFunc = (E) => {
				Program.Window.Close();
			};

			/*GUIButton Btn_Wat = new GUIButton(GUI);
			Btn_Wat.Pos = GUI.WindowScale(new Vector2(0.5f, 0.4f));
			Btn_Wat.Size = BtnSize;
			Btn_Wat.Text = "Wat";
			Btn_Wat.OnClickedFunc = (E) => {
				Console.WriteLine("Wat");
			};*/

			IB.Add(Btn_NewGame);
			IB.Add(Btn_Options);
			IB.Add(Btn_Quit);

			Texture2D Icon = ResMgr.GetTexture("items/pickaxe.png");
			Raylib.SetTextureFilter(Icon, TextureFilter.Point);
			Btn_NewGame.SetIcon(Icon);

			Texture2D IconOptions = ResMgr.GetTexture("items/hammer.png");
			Raylib.SetTextureFilter(IconOptions, TextureFilter.Point);
			Btn_Options.SetIcon(IconOptions);

			Texture2D Icon2 = ResMgr.GetTexture("items/lava.png");
			Raylib.SetTextureFilter(Icon2, TextureFilter.Point);
			Btn_Quit.SetIcon(Icon2);

			//IB.Add(Btn_Wat);
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

			Raylib.SetTextureFilter(Icn, TextureFilter.Point);
			IBox.SetIcon(Icn, 3);
			return IBox;
		}

		public MainMenuState(GameWindow window) : base(window) {
			GUI = new GUIManager(window);
			//GUI.CreateConsole(window, out Lbl, out OutLbl);

			Vector2 BtnSize = GUI.WindowScale(new Vector2(0.2f, 0.07f));
			Vector2 Pos = GUI.WindowScale(new Vector2(0.3f, 0.3f));

			GUIImage TitleImage = new GUIImage(GUI, "title.png", 10);
			TitleImage.Pos = GUI.WindowScale(new Vector2(0.5f, 0.2f));
			GUI.AddElement(TitleImage);

			Vector2 CenterSize = new Vector2(400, 500);
			DbgRect = new Rectangle(
				new Vector2(
					(Window.Width / 2) - (CenterSize.X / 2),
					(Window.Height / 1.65f) - (CenterSize.Y / 2)
				), CenterSize);

			GUIWindow GWnd = new GUIWindow(GUI);
			GWnd.Title = "Main Menu";
			GWnd.Size = DbgRect.Size;
			GWnd.Pos = DbgRect.Position;
			GUI.AddElement(GWnd);

			// Create the options window, same size/pos as GWnd, but disabled by default
			OptionsWnd = new GUISettingsWindow(GUI, Window);
			OptionsWnd.Size = DbgRect.Size;
			OptionsWnd.Pos = DbgRect.Position;
			((GUISettingsWindow)OptionsWnd).RestoreSizePos();
			OptionsWnd.Enabled = false;
			OptionsWnd.Title = "Options";
			OptionsWnd.Resizable = true;
			OptionsWnd.CenterVertical();
			GUI.AddElement(OptionsWnd);

			List<GUIElement> IB = new List<GUIElement>();
			CreateMenuButtons(IB, BtnSize);
			//IB.Add(AddItmBox(GUI.WindowScale(new Vector2(0.3f, 0.5f)), ResMgr.GetTexture("items/heart_empty.png")));
			//IB.Add(AddItmBox(GUI.WindowScale(new Vector2(0.3f, 0.6f)), ResMgr.GetTexture("items/heart_half.png")));
			//IB.Add(AddItmBox(GUI.WindowScale(new Vector2(0.3f, 0.7f)), ResMgr.GetTexture("items/heart_full.png")));

			// Adjust all element positions to be relative to the window
			foreach (var el in IB) {
				el.Pos -= DbgRect.Position;
				GWnd.AddChild(el);
			}

			GUI.CenterVertical(Vector2.Zero, GWnd.Size, new Vector2(15, 10), 5, IB.ToArray());

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
			Raylib.BeginMode2D(Cam);
			Raylib.ClearBackground(new Color(150, 150, 150, 255));

			//Raylib.BeginBlendMode(BlendMode.Alpha);
			//GUI.DrawWindowBorder(DbgRect.Position, DbgRect.Size);
			//Raylib.EndBlendMode();

			GUI.Draw();

			Raylib.EndMode2D();
		}
	}
}
