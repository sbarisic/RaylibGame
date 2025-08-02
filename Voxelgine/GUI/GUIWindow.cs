using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Engine;

namespace Voxelgine.GUI {
	class GUIWindow : GUIElement {
		GUIManager Mgr;

		public GUIWindow(GUIManager Mgr) {
			this.Mgr = Mgr;
		}

		public override void Draw(bool Hovered, bool MouseClicked, bool MouseDown) {

		}
	}
}
