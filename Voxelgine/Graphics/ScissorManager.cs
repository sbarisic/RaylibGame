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
			if (ScissorStack.Count == 0)
				return new Rectangle(0, 0, 0, 0);

			// Stack is LIFO, but we want to clip from bottom to top (outer to inner)
			Rectangle[] rects = ScissorStack.Reverse().ToArray();
			Rectangle result = rects[0];
			for (int i = 1; i < rects.Length; i++) {
				Rectangle r = rects[i];
				float x1 = MathF.Max(result.X, r.X);
				float y1 = MathF.Max(result.Y, r.Y);
				float x2 = MathF.Min(result.X + result.Width, r.X + r.Width);
				float y2 = MathF.Min(result.Y + result.Height, r.Y + r.Height);
				float w = MathF.Max(0, x2 - x1);
				float h = MathF.Max(0, y2 - y1);
				result = new Rectangle(x1, y1, w, h);
			}
			return result;
		}

		public static void BeginScissor(float X, float Y, float W, float H) {
			Rectangle newRect = new Rectangle(X, Y, W, H);
			ScissorStack.Push(newRect);
			Rectangle scissorRect = ClipAllRects();
			CurrentScissorRect = scissorRect;
			Raylib.BeginScissorMode((int)scissorRect.X, (int)scissorRect.Y, (int)scissorRect.Width, (int)scissorRect.Height);
			ScissorCount++;
		}

		public static void EndScissor() {
			if (ScissorStack.Count == 0) {
				throw new InvalidOperationException("ScissorManager: EndScissor called without matching BeginScissor");
			}
			ScissorStack.Pop();
			ScissorCount--;
			if (ScissorCount == 0) {
				Raylib.EndScissorMode();
			} else if (ScissorCount > 0) {
				Rectangle scissorRect = ClipAllRects();
				CurrentScissorRect = scissorRect;
				Raylib.BeginScissorMode((int)scissorRect.X, (int)scissorRect.Y, (int)scissorRect.Width, (int)scissorRect.Height);
			} else {
				throw new InvalidOperationException("ScissorManager: EndScissor called without matching BeginScissor");
			}
		}
	}
}
