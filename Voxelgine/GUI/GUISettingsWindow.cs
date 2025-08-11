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
		const string InBoxStyle = "width: 100%; height: 64; padding: 0; margin-bottom: 5; left: -400;";
		const string BtnStyle = "width: 100%; height: 56; padding: 10; margin-bottom: 5; left: -400;";

		Rectangle DbgRect;

		public GUISettingsWindow(GameWindow Window, GUIManager Mgr, GUIElement Parent, Vector2 Size, Vector2 Pos) : base(Mgr, Parent) {
			List<GUIElement> OptIB = new List<GUIElement>();
			this.Size = Size;
			this.Pos = Pos;

			CreateOptionsButtons(this, OptIB);

			Vector2 CenterSize = Size;
			DbgRect = new Rectangle(
				new Vector2(
					(Window.Width / 2) - (CenterSize.X / 2),
					(Window.Height / 1.65f) - (CenterSize.Y / 2)
				), CenterSize);

			foreach (var el in OptIB) {
				//el.Pos -= DbgRect.Position;
				AddChild(el);
			}

			//Mgr.CenterVertical(Vector2.Zero, Size, new Vector2(15, 10), 5, GetChildren());
		}

		void CreateOptionsButtons(GUIElement Wnd, List<GUIElement> IB) {
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
