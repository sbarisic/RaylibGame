using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Engine;

namespace Voxelgine.GUI {
	class GUIImage : GUIElement {
		Texture2D Img;
		float ImgScale = 1.0f;

		GUIManager Mgr;


		public GUIImage(GUIManager Mgr, string ImageName, float ImgScale = 1.0f) {
			this.Mgr = Mgr;
			this.ImgScale = ImgScale;

			Img = ResMgr.GetTexture(ImageName);
			Size = new Vector2(Img.Width, Img.Height);
		}

		public override void Draw(bool Hovered, bool MouseClicked, bool MouseDown) {
			Rectangle IcnLoc = new Rectangle(Pos, Size);
			Mgr.DrawTexture(Img, Pos, 0, ImgScale);
		}
	}
}
