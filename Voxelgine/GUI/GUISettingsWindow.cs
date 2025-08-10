using Microsoft.Win32.SafeHandles;

using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Engine;
using Voxelgine.Graphics;

using static System.Net.Mime.MediaTypeNames;

namespace Voxelgine.GUI {
	class GUISettingsWindow : GUIWindow {
		const string InBoxStyle = "width: 600; left: 0; height: 120; padding: 0; flex-direction: column;";
		const string BtnStyle = "width: 100%; height: 70; padding: 0;";
		const string WindowStyle = "width: 400; height: 500; padding: 10; flex-direction: column;";
		const string TitleImageStyle = "left: 50%; top: 20%;";

		public GUISettingsWindow(GameWindow Window, GUIManager Mgr, GUIElement Parent) : base(Mgr, Parent) {
			Vector2 BtnSize = Mgr.WindowScale(new Vector2(0.2f, 0.07f));
			List<GUIElement> OptIB = new List<GUIElement>();

			Vector2 CenterSize = new Vector2(400, 500);
			Rectangle DbgRect = new Rectangle(
				new Vector2(
					(Window.Width / 2) - (CenterSize.X / 2),
					(Window.Height / 1.65f) - (CenterSize.Y / 2)
				), CenterSize);


			CreateOptionsButtons(this, OptIB, BtnSize * new Vector2(1, 0.6f));

			foreach (var el in OptIB) {
				//el.Pos -= DbgRect.Position;
				AddChild(el);
			}

			//Mgr.CenterVertical(Vector2.Zero, Size, new Vector2(15, 10), 5, GetChildren());
		}

		public void StoreSizePos() {
			Program.Cfg.LastOptWnd_X = (int)Pos.X;
			Program.Cfg.LastOptWnd_Y = (int)Pos.Y;
			Program.Cfg.LastOptWnd_W = (int)Size.X;
			Program.Cfg.LastOptWnd_H = (int)Size.Y;
		}

		public void RestoreSizePos() {
			int X = Program.Cfg.LastOptWnd_X;
			int Y = Program.Cfg.LastOptWnd_Y;
			int W = Program.Cfg.LastOptWnd_W;
			int H = Program.Cfg.LastOptWnd_H;

			if (!(X == 0 && Y == 0 && W == 0 && H == 0)) {
				Pos = new Vector2(X, Y);
				Size = new Vector2(W, H);
			}
		}

		public override void OnResize() {
			base.OnResize();
			StoreSizePos();
		}

		void CreateOptionsButtons(GUIElement Wnd, List<GUIElement> IB, Vector2 BtnSize) {
			ConfigValueRef[] Vars = Program.Cfg.GetVariables().ToArray();

			for (int i = 0; i < Vars.Length; i++) {
				ConfigValueRef VRef = Vars[i];

				GUIInputBox IBx = new GUIInputBox(Mgr, Wnd, VRef.FieldName, VRef.GetValueString());
				IBx.OnValueChanged = (V) => {
					try {
						VRef.SetValueString(V);
						IBx.SetValue(V, V);
					} catch (Exception E) {
						string VStr = VRef.GetValueString();
						IBx.SetValue(VStr, VStr);
					}
				};

				IBx.FlexNode.nodeStyle.Set(InBoxStyle);
				IB.Add(IBx);
			}

			GUIButton Btn_ResetConfig = new GUIButton(Mgr, Wnd);
			//Btn_ResetConfig.Size = BtnSize;
			Btn_ResetConfig.Text = "Reset Cfg";
			Btn_ResetConfig.OnClickedFunc = (E) => {
				Program.Cfg.SetDefaults();
				Program.Cfg.GenerateDefaultKeybinds();
				Program.Cfg.SaveToJson();
			};
			Btn_ResetConfig.FlexNode.nodeStyle.Apply(BtnStyle);
			IB.Add(Btn_ResetConfig);

			GUIButton Btn_Save = new GUIButton(Mgr, Wnd);
			//Btn_Save.Size = BtnSize;
			Btn_Save.Text = "Save & Restart";
			Btn_Save.OnClickedFunc = (E) => {
				Program.Cfg.SaveToJson();
				Utils.RestartGame();
			};
			Btn_Save.FlexNode.nodeStyle.Apply(BtnStyle);
			IB.Add(Btn_Save);

			GUIButton Btn_Close = new GUIButton(Mgr, Wnd);
			//Btn_Close.Size = BtnSize;
			Btn_Close.Text = "Close";
			Btn_Close.OnClickedFunc = (E) => {
				this.Enabled = false;
			};
			Btn_Close.FlexNode.nodeStyle.Apply(BtnStyle);
			IB.Add(Btn_Close);
		}
	}
}
