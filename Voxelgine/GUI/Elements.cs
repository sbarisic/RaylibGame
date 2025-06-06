using MoonSharp.Interpreter.Interop.LuaStateInterop;

using Raylib_cs;

using RaylibGame.States;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using TextCopy;

using Voxelgine.Engine;

using Windows.Graphics.Printing3D;
using Windows.Media.Core;

namespace Voxelgine.GUI {
	delegate void OnMouseClickedFunc(GUIElement El);

	abstract class GUIElement {
		public Vector2 Pos;
		public Vector2 Size;
		public Vector2 MousePos;

		public virtual bool IsInside(Vector2 Pos2) {
			Rectangle Rect = new Rectangle(Pos, Size);
			return Raylib.CheckCollisionPointRec(Pos2, Rect);
		}

		public abstract void Update(float Dt);

		public abstract void Draw(bool Hovered, bool MouseClicked, bool MouseDown);
	}	
}
