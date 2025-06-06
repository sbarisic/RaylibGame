using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Engine;

namespace Voxelgine.GUI {
	class GUIItemBox : GUIElement {
		public bool IsSelected = false;

		public string Text;

		Texture2D Tex;
		Texture2D TexSel;

		Vector2 IconSize;
		Texture2D Icon;
		bool HasIcon = false;
		float IconScale = 2.0f;

		GUIManager Mgr;

		public OnMouseClickedFunc OnClickedFunc;

		public GUIItemBox(GUIManager Mgr) {
			this.Mgr = Mgr;

			Tex = ResMgr.GetTexture("gui/itembox.png");
			TexSel = ResMgr.GetTexture("gui/itembox_sel.png");
			Raylib.SetTextureFilter(Tex, TextureFilter.Point);
		}

		public void SetIcon(Texture2D? Icon, float Scale) {
			if (Icon.HasValue) {
				HasIcon = true;
				this.Icon = Icon.Value;
				this.IconScale = Scale;
				IconSize = new Vector2(this.Icon.Width, this.Icon.Height);
			} else {
				HasIcon = false;
			}
		}

		public virtual void OnMouseClick() {
			OnClickedFunc?.Invoke(this);
		}

		bool ButtonHeldDown = false;

		public override void Update(float Dt) {
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
		}

		public override void Draw(bool Hovered, bool MouseClicked, bool MouseDown) {
			Rectangle BtnLoc = new Rectangle(Pos, Size);

			if (IsSelected) {
				Mgr.Draw9Patch(TexSel, BtnLoc, Color.White);
			} else {
				Mgr.Draw9Patch(Tex, BtnLoc, Color.White);
			}

			float Clr = ButtonHeldDown ? 0.8f : 1.0f;

			Mgr.DrawTexture(Icon, Pos + Size / 2, 0, IconScale, new Color(Clr, Clr, Clr));

			if (!string.IsNullOrEmpty(Text)) {
				Mgr.DrawTextOutline(Text, Pos + Size / 2, Color.White, 2);
			}
		}
	}
}
