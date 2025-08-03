using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Graphics {
	static class ScissorManager {
		static int ScissorCount = 0;

		static Stack<Rectangle> ScissorStack = new Stack<Rectangle>();
		static Rectangle CurrentScissorRect;

		// TODO: Implement the rest of the functionality to clip rectangles
		static Rectangle ClipAllRects() {
			Rectangle Result = new Rectangle();
			Rectangle[] Rects = ScissorStack.ToArray();

			for (int i = 0; i < Rects.Length; i++) {
				// TODO: Calculate inner most rectangle into result
			}

			return Result;
		}

		public static void BeginScissor(float X, float Y, float W, float H) {
			Rectangle ScissorRect = new Rectangle(X, Y, W, H);
			ScissorStack.Push(ScissorRect);

			if (ScissorStack.Count > 0) {
				ScissorRect = ClipAllRects();
			}

			Raylib.BeginScissorMode((int)ScissorRect.X, (int)ScissorRect.Y, (int)ScissorRect.Width, (int)ScissorRect.Height);
			ScissorCount++;
		}

		public static void EndScissor() {
			ScissorStack.Pop();
			ScissorCount--;

			if (ScissorCount == 0) {
				Raylib.EndScissorMode();
			} else if (ScissorCount < 0) {
				throw new InvalidOperationException("ScissorManager: EndScissor called without matching BeginScissor");
			}
		}
	}
}
