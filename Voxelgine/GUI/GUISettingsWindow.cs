using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Engine;
using Voxelgine.Graphics;

namespace Voxelgine.GUI {
	class GUISettingsWindow : GUIWindow {

		public GUISettingsWindow(GUIManager Mgr, GameWindow Window) : base(Mgr) {
			Vector2 BtnSize = Mgr.WindowScale(new Vector2(0.2f, 0.07f));
			List<GUIElement> OptIB = new List<GUIElement>();

			Vector2 CenterSize = new Vector2(400, 500);
			Rectangle DbgRect = new Rectangle(
				new Vector2(
					(Window.Width / 2) - (CenterSize.X / 2),
					(Window.Height / 1.65f) - (CenterSize.Y / 2)
				), CenterSize);

			CreateOptionsButtons(OptIB, BtnSize * new Vector2(1, 0.6f));
			foreach (var el in OptIB) {
				el.Pos -= DbgRect.Position;
				AddChild(el);
			}

			Mgr.CenterVertical(Vector2.Zero, Size, new Vector2(15, 10), 5, GetChildren());
		}

		void CreateOptionsButtons(List<GUIElement> IB, Vector2 BtnSize) {
			ConfigValueRef[] Vars = Program.Cfg.GetVariables().ToArray();

			for (int i = 0; i < Vars.Length; i++) {
				ConfigValueRef VRef = Vars[i];

				GUIInputBox IBx = new GUIInputBox(Mgr, VRef.FieldName, VRef.GetValueString());
				IBx.OnValueChanged = (V) => {
					try {
						VRef.SetValueString(V);
						IBx.SetValue(V, V);
					} catch (Exception E) {
						string VStr = VRef.GetValueString();
						IBx.SetValue(VStr, VStr);
					}
				};

				IB.Add(IBx);
			}

			GUIButton Btn_ResetConfig = new GUIButton(Mgr);
			Btn_ResetConfig.Size = BtnSize;
			Btn_ResetConfig.Text = "Reset Cfg";
			Btn_ResetConfig.OnClickedFunc = (E) => {
				Program.Cfg.GenerateDefaultKeybinds();
				Program.Cfg.SaveToJson();
			};
			IB.Add(Btn_ResetConfig);

			GUIButton Btn_Close = new GUIButton(Mgr);
			Btn_Close.Size = BtnSize;
			Btn_Close.Text = "Save & Close";
			Btn_Close.OnClickedFunc = (E) => {
				Program.Cfg.SaveToJson();
				this.Enabled = false;
			};
			IB.Add(Btn_Close);
		}
	}
}
