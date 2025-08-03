using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Engine;

namespace Voxelgine.GUI {
	class GUIWindow : GUIElement {
		private GUIManager Mgr;
		private List<GUIElement> Children = new List<GUIElement>();
		private bool IsDragging = false;
		private Vector2 DragOffset = Vector2.Zero;
		private Color PanelColor = new Color(40, 40, 40, 230);
		private float TitleBarHeight = 32f;
		public string Title = "Window";
		private Texture2D PanelTex;

		public GUIWindow(GUIManager Mgr) {
			this.Mgr = Mgr;
			Size = new Vector2(300, 200);
			// Use the same panel texture as buttons for 9-patch
			PanelTex = ResMgr.GetTexture("gui/btn.png");
		}

		public void AddChild(GUIElement child) {
			Children.Add(child);
		}

		public override GUIUpdateResult Update() {
			if (!Enabled)
				return GUIUpdateResult.Disabled;

			GUIUpdateResult Res = GUIUpdateResult.OK;

			// Use public MousePos from GUIElement base (set by GUIManager)
			Vector2 mouse = this.MousePos;
			// Check if mouse is over the title bar
			bool overTitleBar = Raylib.CheckCollisionPointRec(mouse, new Rectangle(Pos, new Vector2(Size.X, TitleBarHeight)));
			bool insideWindow = Raylib.CheckCollisionPointRec(mouse, new Rectangle(Pos, Size));

			if (insideWindow) {
				Res = GUIUpdateResult.ConsumedInput;
			}

			if (overTitleBar && Raylib.IsMouseButtonPressed(MouseButton.Left)) {
				Mgr.BringToFront(this);

				IsDragging = true;
				DragOffset = mouse - Pos;
			}
			if (IsDragging) {
				if (Raylib.IsMouseButtonDown(MouseButton.Left)) {
					Pos = mouse - DragOffset;
				} else {
					IsDragging = false;
				}
			}

			// Update children positions and call their Update
			Vector2 childMouse = mouse - Pos - new Vector2(0, TitleBarHeight);
			foreach (var child in Children) {
				child.MousePos = childMouse;
				child.Update();
			}

			return Res;
		}

		public override void Draw(bool Hovered, bool MouseClicked, bool MouseDown) {
			// Draw panel background using 9-patch
			Mgr.Draw9Patch(PanelTex, new Rectangle(Pos, Size), PanelColor);

			// Draw title bar (filled rect)
			Raylib.DrawRectangleV(Pos, new Vector2(Size.X, TitleBarHeight), new Color(60, 60, 60, 255));
			Mgr.DrawText(Title, Pos + new Vector2(10, 6), Color.White);

			// Draw border
			Mgr.DrawRectLines(Pos, Size, Color.Black);

			// Draw children (relative to window position, below title bar)
			Vector2 localMouse = this.MousePos - Pos - new Vector2(0, TitleBarHeight);
			foreach (var child in Children) {
				bool hovered = child.IsInside(localMouse);
				child.OnHovered(localMouse, hovered);
				// Draw at correct position by offsetting with window position and title bar height
				var oldPos = child.Pos;
				child.Pos = Pos + new Vector2(0, TitleBarHeight) + child.Pos;
				child.Draw(hovered, MouseClicked && hovered, MouseDown && hovered);
				child.Pos = oldPos;
			}
		}
	}
}
