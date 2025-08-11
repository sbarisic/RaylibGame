using Flexbox;

using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using Voxelgine;
using Voxelgine.Engine;
using Voxelgine.Engine.Flexbox;
using Voxelgine.GUI;

namespace RaylibGame.States {
	class MainMenuState : GameStateImpl {
		const string BtnStyle = "width: 100%; height: 76; padding: 10; margin-bottom: 5;";
		const string WindowStyle = "width: 400; height: 500; padding-top: 15; padding-left: 20; padding-right: 20; flex-direction: column;";
		const string OptWindowStyle = "width: 700; height: 1000; padding-top: 15; padding-left: 20; padding-right: 20; flex-direction: column;";
		const string TitleImageStyle = "left: 50%; top: 20%;";

		Camera2D Cam = new Camera2D();
		GUIManager GUI;

		/*GUILabel Lbl;
		GUILabel OutLbl;
		Vector2 MousePos;*/

		Rectangle DbgRect = new Rectangle();
		GUIWindow OptionsWnd;

		private void CreateMenuButtons(GUIElement MenuWindow, List<GUIElement> IB, Vector2 BtnSize) {
			GUIButton Btn_NewGame = new GUIButton(GUI, MenuWindow);
			Btn_NewGame.Text = "New Game";
			Btn_NewGame.OnClickedFunc = (E) => {
				Program.Window.SetState(Program.GameState);
			};
			Btn_NewGame.FlexNode.nodeStyle.Apply(BtnStyle);

			GUIButton Btn_Options = new GUIButton(GUI, MenuWindow);
			Btn_Options.Text = "Options";
			Btn_Options.OnClickedFunc = (E) => {
				Console.WriteLine("Options clicked");
				OptionsWnd.Show();
			};
			Btn_Options.FlexNode.nodeStyle.Apply(BtnStyle);

			GUIButton Btn_Quit = new GUIButton(GUI, MenuWindow);
			Btn_Quit.Text = "Quit";
			Btn_Quit.OnClickedFunc = (E) => {
				Program.Window.Close();
			};
			Btn_Quit.FlexNode.nodeStyle.Apply(BtnStyle);

			GUIButton Btn_Wat = new GUIButton(GUI, MenuWindow);
			Btn_Wat.Text = "OS: " + Utils.GetOSName();
			Btn_Wat.OnClickedFunc = (E) => {
				Console.WriteLine("Running on {0}", Utils.GetOSName());
			};
			Btn_Wat.FlexNode.nodeStyle.Apply(BtnStyle);

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

			IB.Add(Btn_Wat);
		}

		GUIItemBox AddItmBox(GUIElement MenuWindow, Vector2 Pos, Texture2D Icn) {
			GUIItemBox IBox = new GUIItemBox(GUI, MenuWindow);
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

			GUIImage TitleImage = new GUIImage(GUI, null, "title.png", 10);
			TitleImage.Pos = GUI.WindowScale(new Vector2(0.5f, 0.2f));
			TitleImage.FlexNode.nodeStyle.Apply(TitleImageStyle);
			GUI.AddElement(TitleImage);

			Vector2 CenterSize = new Vector2(400, 500);
			DbgRect = new Rectangle(
				new Vector2(
					(Window.Width / 2) - (CenterSize.X / 2),
					(Window.Height / 1.65f) - (CenterSize.Y / 2)
				), CenterSize);

			List<GUIElement> IB = new List<GUIElement>();

			GUIWindow GWnd = new GUIWindow(GUI, null);
			CreateMenuButtons(GWnd, IB, BtnSize);
			GWnd.Title = "Main Menu";
			GWnd.Size = DbgRect.Size;
			GWnd.Pos = DbgRect.Position;
			GWnd.FlexNode.nodeStyle.Apply(WindowStyle);
			GUI.AddElement(GWnd);

			// Adjust all element positions to be relative to the window
			foreach (var el in IB) {
				el.Pos -= DbgRect.Position;
				GWnd.AddChild(el);
			}

			// Create the options window, same size/pos as GWnd, but disabled by default
			OptionsWnd = new GUISettingsWindow(Window, GUI, null, new Vector2(700, 1000), new Vector2(10, 10));
			OptionsWnd.Title = "Options";
			OptionsWnd.Enabled = true;
			OptionsWnd.Resizable = true;
			//OptionsWnd.CenterVertical();
			OptionsWnd.FlexNode.nodeStyle.Apply(OptWindowStyle);
			GUI.AddElement(OptionsWnd);



			//IB.Add(AddItmBox(GUI.WindowScale(new Vector2(0.3f, 0.5f)), ResMgr.GetTexture("items/heart_empty.png")));
			//IB.Add(AddItmBox(GUI.WindowScale(new Vector2(0.3f, 0.6f)), ResMgr.GetTexture("items/heart_half.png")));
			//IB.Add(AddItmBox(GUI.WindowScale(new Vector2(0.3f, 0.7f)), ResMgr.GetTexture("items/heart_full.png")));



			//GUI.CenterVertical(Vector2.Zero, GWnd.Size, new Vector2(15, 10), 5, IB.ToArray());
			NodePrinter.Print(GUI.RootNode);
			//OptionsWnd.Show();
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
