using MoonSharp.Interpreter.Interop.LuaStateInterop;

using Raylib_cs;

using RaylibGame.States;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using TextCopy;

using Voxelgine.Engine;

using Windows.Graphics.Printing3D;
using Windows.Media.Core;

namespace Voxelgine.GUI {
	delegate void OnMouseClickedFunc(GUIElement El);

	abstract class GUIElement {
		public Vector2 Pos;
		public Vector2 Size;
		public Vector2 MousePos;

		public Vector2 HoverPos;
		public bool IsHoveredOn;

		public bool Enabled = true;

		public OnMouseClickedFunc OnClickedFunc;

		protected bool MouseDown_Left = false;

		public virtual bool IsInside(Vector2 Pos2) {
			Rectangle Rect = new Rectangle(Pos, Size);
			return Raylib.CheckCollisionPointRec(Pos2, Rect);
		}

		public virtual void OnMouseClick() {
			OnClickedFunc?.Invoke(this);
		}

		bool ButtonHeldDown = false;

		public virtual void Update() {
			if (!Enabled) {
				ButtonHeldDown = false;
				return;
			}

			if (IsInside(MousePos)) {
				if (Raylib.IsMouseButtonDown(MouseButton.Left)) {
					if (Raylib.IsMouseButtonPressed(MouseButton.Left) && !ButtonHeldDown) {
						ButtonHeldDown = true;
					} else {
					}
				} else if (Raylib.IsMouseButtonReleased(MouseButton.Left)) {
					if (ButtonHeldDown) {
						ButtonHeldDown = false;
						OnMouseClick();
					}
				} else {
					ButtonHeldDown = false;
				}
			} else {
				if (!Raylib.IsMouseButtonDown(MouseButton.Left)) {
					ButtonHeldDown = false;
				}
			}

			MouseDown_Left = ButtonHeldDown;
		}

		public virtual void OnHovered(Vector2 HoverPos, bool Hovered) {
			if (Hovered)
				this.HoverPos = HoverPos;

			IsHoveredOn = Hovered;
		}

		public abstract void Draw(bool Hovered, bool MouseClicked, bool MouseDown);
	}
}
