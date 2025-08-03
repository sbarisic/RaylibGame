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

		private void CreateOptionsButtons(List<GUIElement> IB, Vector2 BtnSize) {


			GUIInputBox InBx = new GUIInputBox(Mgr, "Test", "Okay");
			InBx.OnValueChanged = (V) => {
				Console.WriteLine("Test: '{0}'", V);
				InBx.SetValue(V, V);
			};
			IB.Add(InBx);

			GUIInputBox InBx2 = new GUIInputBox(Mgr, "Test2", "Okay2");
			InBx2.OnValueChanged = (V) => {
				Console.WriteLine("Test2: '{0}'", V);
				InBx2.SetValue(V, V);
			};
			IB.Add(InBx2);

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
			Btn_Close.Text = "Close";
			Btn_Close.OnClickedFunc = (E) => {
				this.Enabled = false;
			};
			IB.Add(Btn_Close);
		}
	}
}
