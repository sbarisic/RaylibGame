using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Graphics {
	static class ScissorManager {
		static Rectangle ScissorRect;

		public static void BeginScissor(float X, float Y, float W, float H) {
			Rectangle ScissorRect = new Rectangle(X, Y,W,H);
			Raylib.BeginScissorMode((int)ScissorRect.X, (int)ScissorRect.Y, (int)ScissorRect.Width, (int)ScissorRect.Height);
		}

		public static void EndScissor() {
			Raylib.EndScissorMode();
		}
	}
}
