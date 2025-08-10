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

namespace Voxelgine.GUI {
	public delegate void OnMouseClickedFunc(GUIElement El);

	public enum GUIUpdateResult {
		OK,
		Disabled,
		ConsumedInput,
	}

	public abstract class GUIElement {
		public GUIElement Parent;

		public Vector2 Pos;
		public Vector2 Size;
		public Vector2 MousePos;

		public Vector2 HoverPos;
		public bool IsHoveredOn;

		public bool Enabled = true;
		public int ZOrder = 0;

		public OnMouseClickedFunc OnClickedFunc;
		public float? OriginalWidth = null;

		public Flexbox.Node FlexNode;
		public GUIManager Mgr;

		protected bool MouseDown_Left = false;

		public GUIElement(GUIManager Mgr, GUIElement Parent) {
			this.Mgr = Mgr;
			this.Parent = Parent;
			CreateFlexbox();
		}


		public virtual bool IsInside(Vector2 Pos2) {
			Rectangle Rect = new Rectangle(Pos, Size);
			return Raylib.CheckCollisionPointRec(Pos2, Rect);
		}

		public virtual void OnMouseClick() {
			OnClickedFunc?.Invoke(this);
		}

		public virtual void OnFlexUpdated() {

		}

		public virtual void SetFlexbox() {

		}

		public virtual void CreateFlexbox() {
			FlexNode = Flexbox.Flex.CreateDefaultNode();

			if (Parent != null)
				Parent.FlexNode.AddChild(FlexNode);
			else
				Mgr.RootNode.AddChild(FlexNode);
		}

		bool ButtonHeldDown = false;

		public virtual GUIUpdateResult Update() {
			GUIUpdateResult Res = GUIUpdateResult.OK;

			if (!Enabled) {
				ButtonHeldDown = false;
				return GUIUpdateResult.Disabled;
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
			return Res;
		}

		public virtual void OnHovered(Vector2 HoverPos, bool Hovered) {
			if (Hovered)
				this.HoverPos = HoverPos;

			IsHoveredOn = Hovered;
		}

		public abstract void Draw(bool Hovered, bool MouseClicked, bool MouseDown);
	}
}
