using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Engine;

namespace Voxelgine.GUI {
	class GUIButton : GUIElement {
		Texture2D BtnTex;
		Texture2D BtnHoverTex;
		Texture2D BtnPressTex;

		Texture2D Icon;
		float IconScale = 3;
		bool HasIcon = false;

		GUIManager Mgr;

		public string Text;

		public GUIButton(GUIManager Mgr) {
			this.Mgr = Mgr;
			Size = new Vector2(140, 60);

			BtnTex = ResMgr.GetTexture("gui/btn.png");
			Raylib.SetTextureFilter(BtnTex, TextureFilter.Point);

			BtnHoverTex = ResMgr.GetTexture("gui/btn_hover.png");
			Raylib.SetTextureFilter(BtnHoverTex, TextureFilter.Point);

			BtnPressTex = ResMgr.GetTexture("gui/btn_press.png");
			Raylib.SetTextureFilter(BtnPressTex, TextureFilter.Point);
		}

		public void SetIcon(Texture2D? Icon) {
			if (Icon.HasValue) {
				HasIcon = true;
				this.Icon = Icon.Value;
			} else {
				HasIcon = false;
			}
		}

		//Stopwatch SWatc = Stopwatch.StartNew();

		public override void Draw(bool Hovered, bool MouseClicked, bool MouseDown) {
			Rectangle BtnLoc = new Rectangle(Pos, Size);
			Texture2D Tex = BtnTex;
			Vector2 DrawOffset = Vector2.Zero;

			if (Hovered) {
				if (MouseDown) {
					Tex = BtnPressTex;
					DrawOffset = new Vector2(4, 4);
				} else
					Tex = BtnHoverTex;
			}

			Mgr.Draw9Patch(Tex, BtnLoc, Color.White);

			Vector2 IconSize = Vector2.Zero;
			Vector2 IconOffset = Vector2.Zero;
			if (HasIcon) {
				IconSize = new Vector2(Icon.Width, Icon.Height) * IconScale;
				IconOffset = new Vector2(IconSize.X / 2 + 10, 0);
			}

			Vector2 TxtSize = Mgr.MeasureText(Text) - IconOffset;
			Vector2 TxtPos = Pos + Size / 2 - TxtSize / 2;

			Mgr.DrawText(Text, TxtPos + DrawOffset, Color.White);

			if (HasIcon) {
				// Vector2 IconPos = Pos + (Size / 2) - (TxtSize / 2) + DrawOffset - (IconSize / 2);
				Vector2 IconPos = TxtPos + new Vector2(0, TxtSize.Y / 2) - IconOffset;

				//float TT = SWatc.ElapsedMilliseconds / 1000.0f * 50;
				float TT = 0;

				//Mgr.DrawRectLines(IconPos + DrawOffset, IconSize, Color.Blue);
				Mgr.DrawTexture(Icon, IconPos + DrawOffset, TT, IconScale);
			}
		}
	}

}
