using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.GUI {
	class GUIManager {
		public int FntSize = 32;
		public Font TxtFont;


		GUILabel Lbl;

		public GUIManager() {
			TxtFont = Raylib.LoadFontEx("data/fonts/medodica.otf", FntSize, null, 128);
			Raylib.SetTextureFilter(TxtFont.Texture, TextureFilter.Point);
		}

		public void DrawWindowBorder(Vector2 Pos, Vector2 Size) {
			Rectangle Rect = new Rectangle(Pos, Size);

			Raylib.DrawRectanglePro(Rect, Vector2.Zero, 0, new Color(0, 0, 0, 128));
			Raylib.DrawRectangleLinesEx(Rect, 1, new Color(200, 200, 200, 255));
		}

		public void DrawText(string Txt, Vector2 Pos, Color Clr) {
			Raylib.DrawTextEx(TxtFont, Txt, Pos, FntSize, 1, Clr);

			//Vector2 Sz = Raylib.MeasureTextEx(TxtFont, Txt, FntSize, 1);
			//Raylib.DrawRectangleLinesEx(new Rectangle(Pos, Sz), 1, Color.Red);
		}

		public void DrawRectLines(Vector2 Pos, Vector2 Sz, Color Clr) {
			Raylib.DrawRectangleLinesEx(new Rectangle(Pos, Sz), 1, Clr);
		}
	}
}
