using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Engine;

namespace Voxelgine.GUI {
	public class GUIManager {
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

		int CalcLastZOrder() {
			if (Elements.Count == 0)
				return 0;

			return Elements.Max(e => e.ZOrder);
		}

		public void AddElement(GUIElement E) {
			E.ZOrder = CalcLastZOrder() + 1;
			Elements.Add(E);
		}

		public void BringToFront(GUIElement E) {
			if (E.ZOrder == CalcLastZOrder())
				return; // Already at the front

			if (Elements.Contains(E)) {
				// Remove and re-add to move to the end of the list
				Elements.Remove(E);
				E.ZOrder = CalcLastZOrder() + 1;
				Elements.Add(E);
			}
		}

		public T FindFirstElementOrDefault<T>() where T : GUIElement {
			foreach (var E in Elements) {
				if (E is T TE)
					return TE;
			}

			return null;
		}

		public void Tick() {
			MousePos = Window.InMgr.GetMousePos();

			// Sort elements by ZOrder before updating
			var sortedElements = Elements.OrderByDescending(e => e.ZOrder).ToList();
			foreach (GUIElement E in sortedElements) {
				E.MousePos = MousePos;
				if (E.Update() == GUIUpdateResult.ConsumedInput)
					break;
			}
		}

		public void Draw() {
			bool Hovered = false;
			bool MouseClicked = Window.InMgr.IsInputPressed(InputKey.Click_Left);
			bool MouseDown = Window.InMgr.IsInputDown(InputKey.Click_Left);

			var sortedElements = Elements.OrderBy(e => e.ZOrder).ToList();
			foreach (GUIElement E in sortedElements) {
				if (!E.Enabled)
					continue;

				Hovered = E.IsInside(MousePos);
				E.Draw(Hovered, MouseClicked, MouseDown);
			}
		}

		public void DrawWindowBorder(Vector2 Pos, Vector2 Size) {
			Rectangle Rect = new Rectangle(Pos, Size);

			Raylib.DrawRectanglePro(Rect, Vector2.Zero, 0, new Color(0, 0, 0, 128));
			Raylib.DrawRectangleLinesEx(Rect, 1, new Color(0, 0, 0, 180));
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

		public void DrawTexture(Texture2D Tex, Vector2 Pos, float Rot, float Scale, Color? Clr = null, Vector2? OrigPos = null, Vector2? OrigSize = null) {
			Vector2 TexSize = new Vector2(Tex.Width, Tex.Height);
			Vector2 Origin = (TexSize * Scale) / 2;

			Vector2 A = OrigPos ?? Vector2.Zero;
			Vector2 B = OrigSize ?? Vector2.One;

			Raylib.DrawTexturePro(Tex, new Rectangle(A * TexSize, B * TexSize), new Rectangle(Pos, TexSize * Scale), Origin, Rot, Clr ?? Color.White);
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

		public void CenterVertical(Vector2 Pos, Vector2 Size, Vector2 Margin, float IconMargin, params GUIElement[] Elements) {
			Vector2 Offset = Vector2.Zero;
			Vector2 NewOffs = new Vector2(0, Margin.Y);

			int Counter = 0;
			GUIElement LastE = null;
			foreach (GUIElement E in Elements) {
				Counter++;

				float? origWidth = E.OriginalWidth;
				if (origWidth == null || origWidth <= 0) {
					E.OriginalWidth = E.Size.X;
					origWidth = E.Size.X;
				}
				E.Size.X = origWidth.Value;


				NewOffs = new Vector2(Margin.X, NewOffs.Y);
				Vector2 NewPos = Pos + NewOffs;

				bool SkipSomeCalcs = false;

				if (LastE is GUIItemBox && E is GUIItemBox) {
					NewPos = Pos + new Vector2(LastE.Pos.X + LastE.Size.X - Pos.X + IconMargin, LastE.Pos.Y - Pos.Y);
					SkipSomeCalcs = true;
				}

				E.Pos = NewPos;

				float EndX = E.Pos.X + E.Size.X;
				float MaxX = Pos.X + Size.X - Margin.X;

				if (EndX > MaxX) {
					E.Size.X = E.Size.X - (EndX - MaxX);
				}

				if (!SkipSomeCalcs) {
					NewOffs.Y += E.Size.Y + Margin.Y;
				}

				LastE = E;
			}
		}
	}
}
