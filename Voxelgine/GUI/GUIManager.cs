using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Engine;

namespace Voxelgine.GUI {
	class GUIManager {
		public int FntSize = 32;
		public Font TxtFont;

		GameWindow Window;

		List<GUIElement> Elements = new List<GUIElement>();
		Vector2 MousePos = Vector2.Zero;

		public GUIManager(GameWindow Window) {
			this.Window = Window;
			TxtFont = Raylib.LoadFontEx("data/fonts/medodica.otf", FntSize, null, 128);
			Raylib.SetTextureFilter(TxtFont.Texture, TextureFilter.Point);
		}

		public Vector2 CenterWindow(Vector2 Pos) {
			return new Vector2(Window.Width, Window.Height) / 2 + Pos;
		}

		public Vector2 WindowScale(Vector2 Pos) {
			return Pos * new Vector2(Window.Width, Window.Height);
		}

		public void Clear() {
			Elements.Clear();
		}

		public void AddElement(GUIElement E) {
			Elements.Add(E);
		}

		public void Update(float Dt) {
			MousePos = Raylib.GetMousePosition();

			foreach (GUIElement E in Elements) {
				E.MousePos = MousePos;
				E.Update(Dt);
			}
		}

		public void Draw() {
			bool Hovered = false;
			bool MouseClicked = Raylib.IsMouseButtonPressed(MouseButton.Left);
			bool MouseDown = Raylib.IsMouseButtonDown(MouseButton.Left);

			foreach (GUIElement E in Elements) {
				Hovered = E.IsInside(MousePos);
				E.Draw(Hovered, MouseClicked, MouseDown);
			}
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

		public void DrawTextOutline(string Txt, Vector2 Pos, Color Clr, float Outline) {
			Raylib.SetTextureFilter(TxtFont.Texture, TextureFilter.Bilinear);
			DrawText(Txt, Pos + new Vector2(Outline, 0), Color.Black);
			DrawText(Txt, Pos + new Vector2(-Outline, 0), Color.Black);
			DrawText(Txt, Pos + new Vector2(0, Outline), Color.Black);
			DrawText(Txt, Pos + new Vector2(0, -Outline), Color.Black);
			Raylib.SetTextureFilter(TxtFont.Texture, TextureFilter.Point);

			DrawText(Txt, Pos, Clr);
		}

		public void DrawTexture(Texture2D Tex, Vector2 Pos, float Rot, float Scale) {
			Vector2 TexSize = new Vector2(Tex.Width, Tex.Height);
			Vector2 Origin = (TexSize * Scale) / 2;
			Raylib.DrawTexturePro(Tex, new Rectangle(Vector2.Zero, TexSize), new Rectangle(Pos, TexSize * Scale), Origin, Rot, Color.White);
		}

		public void DrawRectLines(Vector2 Pos, Vector2 Sz, Color Clr) {
			Raylib.DrawRectangleLinesEx(new Rectangle(Pos, Sz), 1, Clr);
		}

		public Vector2 MeasureText(string Txt) {
			return Raylib.MeasureTextEx(TxtFont, Txt, FntSize, 1);
		}

		public void Draw9Patch(Texture2D Tex, Rectangle Dest, Color Clr) {
			NPatchInfo NInf = new NPatchInfo();

			NInf.Layout = NPatchLayout.NinePatch;
			NInf.Left = 8;
			NInf.Right = 8;
			NInf.Top = 8;
			NInf.Bottom = 8;
			NInf.Source = new Rectangle(0, 0, Tex.Width, Tex.Height);

			Raylib.DrawTextureNPatch(Tex, NInf, Dest, Vector2.Zero, 0, Clr);
		}

		public void CreateConsole(GameWindow window, out GUILabel Lbl, out GUILabel OutLbl) {
			float W = window.Width;
			float H = window.Height;

			Lbl = new GUILabel(this, 80);
			OutLbl = new GUILabel(this);

			OutLbl.Size = new Vector2(Lbl.Size.X, 500);
			OutLbl.Pos = new Vector2(W / 2 - OutLbl.Size.X / 2, H - (H / 2 + OutLbl.Size.Y / 2 + H * 0.05f));
			OutLbl.ScrollText = true;
			AddElement(OutLbl);

			Lbl.Pos = new Vector2(OutLbl.Pos.X, OutLbl.Pos.Y + OutLbl.Size.Y);
			Lbl.IsReading = true;
			AddElement(Lbl);
		}
	}
}
