using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Engine;

namespace Voxelgine.GUI {
	enum IconBarStyle {
		Hearts,
		XpBar,
	}

	class GUIIconBar : GUIElement {
		public int NumIcons;
		public int MaxValue;
		public int Value;

		public string Txt;
		public float Outline = 2.0f;
		public Color TxtColor = Color.White;
		public Vector2 TxtOffset = Vector2.Zero;

		Vector2 Margin = new Vector2(10, 0);

		List<Texture2D> Texs = new List<Texture2D>();

		Vector2 IconSize;
		Texture2D Icon;
		float IconScale = 3.0f;

		GUIManager Mgr;


		public GUIIconBar(GUIManager Mgr, IconBarStyle Style, int NumIcons = 10, float IconScale = 3.0f) {
			this.Mgr = Mgr;
			this.IconScale = IconScale;

			this.NumIcons = NumIcons;
			MaxValue = 100;
			Value = 100;

			if (Style == IconBarStyle.Hearts) {
				Margin = new Vector2(10, 0);

				AddTex(ResMgr.GetTexture("items/heart_empty.png"));
				AddTex(ResMgr.GetTexture("items/heart_half.png"));
				AddTex(ResMgr.GetTexture("items/heart_full.png"));
			} else if (Style == IconBarStyle.XpBar) {
				Margin = new Vector2(0, 0);

				AddTex(ResMgr.GetTexture("progress/empty.png"));
				AddTex(ResMgr.GetTexture("progress/half.png"));
				AddTex(ResMgr.GetTexture("progress/full.png"));
			}

			IconSize = new Vector2(Texs[0].Width, Texs[0].Height) * IconScale;
			Size.X = NumIcons * (IconSize.X + Margin.X);
			Size.Y = IconSize.Y;
		}

		void AddTex(Texture2D Tex) {
			Raylib.SetTextureFilter(Tex, TextureFilter.Point);
			Texs.Add(Tex);
		}

		public override void Update(float Dt) {
			base.Update(Dt);
		}

		public override void Draw(bool Hovered, bool MouseClicked, bool MouseDown) {
			Rectangle IcnLoc = new Rectangle(Pos, Size);
			Vector2 IcnPos = Pos;
			Texture2D DrawTex = Texs[0];

			for (int i = 0; i < NumIcons; i++) {
				IcnPos = IcnPos + (Margin + new Vector2(IconSize.X, 0));

				float NumPerDiv = MaxValue / NumIcons;
				float CVal = NumPerDiv * i;
				float CMin = CVal;
				float CMax = CMin + NumPerDiv;

				float DivVal = Value - CVal;

				if (Value <= CMin) {
					DrawTex = Texs[0];
				} else if (Value > CMin && Value <= CMax) {
					float Perc = DivVal / NumPerDiv;
					int Idx = (int)(Perc * (Texs.Count - 1));
					DrawTex = Texs[Idx];
				} else if (Value > CMax) {
					DrawTex = Texs[Texs.Count - 1];
				}

				Mgr.DrawTexture(DrawTex, IcnPos + new Vector2(-IconSize.X / 2, IconSize.Y / 2), 0, IconScale);
			}

			if (!string.IsNullOrEmpty(Txt)) {
				Vector2 TxtSz = Mgr.MeasureText(Txt);
				Vector2 TxtPos = Pos + new Vector2(Size.X / 2 - TxtSz.X / 2, -TxtSz.Y / 2);
				Mgr.DrawTextOutline(Txt, TxtPos + TxtOffset, Color.White, Outline);
			}

			//Mgr.DrawWindowBorder(Pos, Size);
			//Mgr.DrawTexture(Icon, Pos + Size / 2 + DrawOffset, 0, IconScale, new Color(Clr, Clr, Clr));
		}
	}
}
