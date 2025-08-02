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

		public override void Update() {
			if (!Enabled) return;

			// Use public MousePos from GUIElement base (set by GUIManager)
			Vector2 mouse = this.MousePos;
			// Check if mouse is over the title bar
			bool overTitleBar = Raylib.CheckCollisionPointRec(mouse, new Rectangle(Pos, new Vector2(Size.X, TitleBarHeight)));

			if (overTitleBar && Raylib.IsMouseButtonPressed(MouseButton.Left)) {
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
			foreach (var child in Children) {
				child.MousePos = mouse - Pos - child.Pos;
				child.Update();
			}
		}

		public override void Draw(bool Hovered, bool MouseClicked, bool MouseDown) {
			// Draw panel background using 9-patch
			Mgr.Draw9Patch(PanelTex, new Rectangle(Pos, Size), PanelColor);

			// Draw title bar (filled rect)
			Raylib.DrawRectangleV(Pos, new Vector2(Size.X, TitleBarHeight), new Color(60, 60, 60, 255));
			Mgr.DrawText(Title, Pos + new Vector2(10, 6), Color.White);

			// Draw border
			Mgr.DrawRectLines(Pos, Size, Color.Black);

			// Draw children (relative to window position)
			foreach (var child in Children) {
				Vector2 childDrawPos = Pos + child.Pos;
				// Save and set child.Pos for drawing
				var oldPos = child.Pos;
				child.Pos = childDrawPos;
				child.Draw(child.IsInside(this.MousePos - Pos - oldPos), MouseClicked, MouseDown);
				child.Pos = oldPos;
			}
		}
	}
}
