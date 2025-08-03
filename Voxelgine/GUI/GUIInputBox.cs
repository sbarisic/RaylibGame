using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.GUI {
	class GUIInputBox : GUIElement {
		GUIManager Mgr;

		public string Label = "DefaultLabel";
		public string Value = "";

		public GUIInputBox(GUIManager Mgr, string Label, string Value) {
			this.Mgr = Mgr;
			this.Label = Label;
			this.Value = Value;
		}

		public override void Draw(bool Hovered, bool MouseClicked, bool MouseDown) {
			throw new NotImplementedException();
		}
	}
}
