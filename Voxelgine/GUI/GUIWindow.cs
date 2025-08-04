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
	class GUIWindow : GUIElement {
		protected GUIManager Mgr;

		private List<GUIElement> Children = new List<GUIElement>();
		private bool IsDragging = false;
		private Vector2 DragOffset = Vector2.Zero;
		private Color PanelColor = new Color(40, 40, 40, 230);
		private float TitleBarHeight = 32f;
		public string Title = "Window";
		private Texture2D PanelTex;

		private bool IsResizing = false;
		private Vector2 ResizeStartMouse = Vector2.Zero;
		private Vector2 ResizeStartSize = Vector2.Zero;
		private const float ResizeHandleSize = 16f;
		private Vector2 MinWindowSize = new Vector2(120, 80);

		private bool WasResizing = false;
		private Vector2 CenterMargin = new Vector2(15, 10);
		private float CenterIconMargin = 5f;

		public bool Resizable = false;
		public GUIWindow(GUIManager Mgr) {
			this.Mgr = Mgr;
			Size = new Vector2(300, 200);
			// Use the same panel texture as buttons for 9-patch
			PanelTex = ResMgr.GetTexture("gui/btn.png");
		}

		public void AddChild(GUIElement child) {
			Children.Add(child);
		}

		public GUIElement[] GetChildren() {
			return Children.ToArray();
		}

		public void CenterVertical() {
			Mgr.CenterVertical(Vector2.Zero, new Vector2(Size.X, Size.Y - TitleBarHeight), CenterMargin, CenterIconMargin, Children.ToArray());
		}

		public virtual void OnResize() {
			CenterVertical();
		}

		public override GUIUpdateResult Update() {
			if (!Enabled)
				return GUIUpdateResult.Disabled;

			GUIUpdateResult Res = GUIUpdateResult.OK;
			Vector2 mouse = this.MousePos;

			bool overTitleBar = Raylib.CheckCollisionPointRec(mouse, new Rectangle(Pos, new Vector2(Size.X, TitleBarHeight)));
			bool insideWindow = Raylib.CheckCollisionPointRec(mouse, new Rectangle(Pos, Size));
			Rectangle resizeRect = new Rectangle(
				Pos.X + Size.X - ResizeHandleSize,
				Pos.Y + Size.Y - ResizeHandleSize,
				ResizeHandleSize,
				ResizeHandleSize
			);
			bool overResize = Raylib.CheckCollisionPointRec(mouse, resizeRect);

			if (insideWindow || overTitleBar || overResize) {
				Res = GUIUpdateResult.ConsumedInput;
				if (Raylib.IsMouseButtonPressed(MouseButton.Left)) {
					Mgr.BringToFront(this);
				}
			}

			HandleResizing(mouse, overResize);
			if (!IsResizing) {
				HandleDragging(mouse, overTitleBar);
			}

			// Always recenter children while resizing
			if (IsResizing && Children.Count > 0) {
				OnResize();
			}

			UpdateChildren(mouse);

			return Res;
		}

		private void HandleResizing(Vector2 mouse, bool overResize) {
			if (!Resizable) {
				IsResizing = false;
				return;
			}

			if (overResize && Raylib.IsMouseButtonPressed(MouseButton.Left)) {
				IsResizing = true;
				ResizeStartMouse = mouse;
				ResizeStartSize = Size;
			}

			if (IsResizing) {
				if (Raylib.IsMouseButtonDown(MouseButton.Left)) {
					Vector2 delta = mouse - ResizeStartMouse;
					Size = new Vector2(
						MathF.Max(MinWindowSize.X, ResizeStartSize.X + delta.X),
						MathF.Max(MinWindowSize.Y, ResizeStartSize.Y + delta.Y)
					);
				} else {
					IsResizing = false;
				}
				// Don't allow dragging while resizing
				IsDragging = false;
			}
		}

		private void HandleDragging(Vector2 mouse, bool overTitleBar) {
			// Start dragging
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
		}

		private void UpdateChildren(Vector2 mouse) {
			Vector2 childMouse = mouse - Pos - new Vector2(0, TitleBarHeight);
			foreach (var child in Children) {
				child.MousePos = childMouse;
				child.Update();
			}
		}

		public override void Draw(bool Hovered, bool MouseClicked, bool MouseDown) {
			ScissorManager.BeginScissor(Pos.X, Pos.Y, Size.X, Size.Y);

			// Draw panel background using 9-patch
			Mgr.Draw9Patch(PanelTex, new Rectangle(Pos, Size), PanelColor);

			// Draw title bar (filled rect)
			Raylib.DrawRectangleV(Pos, new Vector2(Size.X, TitleBarHeight), new Color(60, 60, 60, 255));
			Mgr.DrawText(Title, Pos + new Vector2(10, 6), Color.White);

			// Draw border
			Mgr.DrawRectLines(Pos, Size, Color.Black);

			if (Resizable) {
				// Draw resize handle (bottom right corner)
				Rectangle resizeRect = new Rectangle(
					Pos.X + Size.X - ResizeHandleSize,
					Pos.Y + Size.Y - ResizeHandleSize,
					ResizeHandleSize,
					ResizeHandleSize
				);
				Raylib.DrawRectangleRec(resizeRect, new Color(100, 100, 100, 180));
				// Optionally, draw diagonal lines for the handle
				for (int i = 2; i < (int)ResizeHandleSize; i += 4) {
					Raylib.DrawLine(
						(int)(resizeRect.X + i),
						(int)(resizeRect.Y + ResizeHandleSize),
						(int)(resizeRect.X + ResizeHandleSize),
						(int)(resizeRect.Y + ResizeHandleSize - i),
						Color.Gray
					);
				}
			}

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

			ScissorManager.EndScissor();
		}

		public void Show() {
			Enabled = true;
			Mgr.BringToFront(this);
		}
	}
}
