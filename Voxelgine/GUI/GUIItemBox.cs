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
		public GUIInventory Parent;

		public string Text;

		Texture2D Tex;
		Texture2D TexSel;

		Vector2 IconSize;
		Texture2D Icon;
		bool HasIcon = false;
		float IconScale = 2.0f;

		Vector2 TextureCoords;
		Vector2 TextureSize;

		GUIManager Mgr;

		public GUIItemBox(GUIManager Mgr) {
			this.Mgr = Mgr;

			Size = new Vector2(64, 64);

			Tex = ResMgr.GetTexture("gui/itembox.png");
			Raylib.SetTextureFilter(Tex, TextureFilter.Point);

			TexSel = ResMgr.GetTexture("gui/itembox_sel.png");
			Raylib.SetTextureFilter(TexSel, TextureFilter.Point);
		}

		public void SetIcon(Texture2D? Icon, float Scale, Vector2 Coords, Vector2 Size) {
			if (Icon.HasValue) {
				TextureCoords = Coords;
				TextureSize = Size;

				HasIcon = true;
				this.Icon = Icon.Value;
				this.IconScale = Scale;
				IconSize = new Vector2(this.Icon.Width, this.Icon.Height);

				Raylib.SetTextureFilter(this.Icon, TextureFilter.Point);
			} else {
				HasIcon = false;
			}
		}

		public void SetIcon(Texture2D? Icon, float Scale) {
			SetIcon(Icon, Scale, Vector2.Zero, Vector2.One);
		}

		public override void Draw(bool Hovered, bool MouseClicked, bool MouseDown) {
			Rectangle BtnLoc = new Rectangle(Pos, Size);
			Vector2 DrawOffset = Vector2.Zero;

			if (IsSelected) {
				Mgr.Draw9Patch(TexSel, BtnLoc, Color.White);
			} else {
				Mgr.Draw9Patch(Tex, BtnLoc, Color.White);
			}

			float Clr = 1.0f;

			if (MouseDown_Left) {
				DrawOffset = new Vector2(2, 2);
				Clr = 0.7f;
			}

			if (HasIcon) {
				Mgr.DrawTexture(Icon, Pos + Size / 2 + DrawOffset, 0, IconScale, new Color(Clr, Clr, Clr), TextureCoords, TextureSize);
			}

			if (!string.IsNullOrEmpty(Text)) {
				Vector2 TextSize = Mgr.MeasureText(Text);
				Mgr.DrawTextOutline(Text, Pos - new Vector2(TextSize.X - Size.X + 4, -Size.Y + TextSize.Y), Color.White, 2);
			}
		}
	}
}
