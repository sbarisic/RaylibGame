using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Raylib_cs;

namespace Voxelgine.Engine {
	public enum InputKey : int {
		None = 0,

		Click_Left,
		Click_Right,
		Click_Middle,

		W,
		A,
		S,
		D,

		Q,
		E,

		R,
		F,
		C,

		Space,

		F1,
		F2,
		F3,
		F4,
		F5,

		Esc,
		Ctrl,
		Alt,

		InputKeyCount
	}

	public unsafe struct InputState {
		public fixed bool KeysDown[(int)InputKey.InputKeyCount];
		public Vector2 MousePos;
		public float MouseWheel;
	}

	unsafe class InputMgr {
		InputState InputState_Cur;
		InputState InputState_Last;

		public void Tick() {
			InputState_Last = InputState_Cur;
			InputState_Cur = new InputState();

			// TODO: Input mapping

			InputState_Cur.MousePos = Raylib.GetMousePosition();
			InputState_Cur.MouseWheel = Raylib.GetMouseWheelMove();

			InputState_Cur.KeysDown[(int)InputKey.Click_Left] = Raylib.IsMouseButtonDown(MouseButton.Left);
			InputState_Cur.KeysDown[(int)InputKey.Click_Right] = Raylib.IsMouseButtonDown(MouseButton.Right);
			InputState_Cur.KeysDown[(int)InputKey.Click_Middle] = Raylib.IsMouseButtonDown(MouseButton.Middle);

			InputState_Cur.KeysDown[(int)InputKey.W] = Raylib.IsKeyDown(KeyboardKey.W);
			InputState_Cur.KeysDown[(int)InputKey.A] = Raylib.IsKeyDown(KeyboardKey.A);
			InputState_Cur.KeysDown[(int)InputKey.S] = Raylib.IsKeyDown(KeyboardKey.S);
			InputState_Cur.KeysDown[(int)InputKey.D] = Raylib.IsKeyDown(KeyboardKey.D);

			InputState_Cur.KeysDown[(int)InputKey.Q] = Raylib.IsKeyDown(KeyboardKey.Q);
			InputState_Cur.KeysDown[(int)InputKey.E] = Raylib.IsKeyDown(KeyboardKey.E);

			InputState_Cur.KeysDown[(int)InputKey.R] = Raylib.IsKeyDown(KeyboardKey.R);
			InputState_Cur.KeysDown[(int)InputKey.F] = Raylib.IsKeyDown(KeyboardKey.F);
			InputState_Cur.KeysDown[(int)InputKey.C] = Raylib.IsKeyDown(KeyboardKey.C);

			InputState_Cur.KeysDown[(int)InputKey.Space] = Raylib.IsKeyDown(KeyboardKey.Space);

			InputState_Cur.KeysDown[(int)InputKey.F1] = Raylib.IsKeyDown(KeyboardKey.F1);
			InputState_Cur.KeysDown[(int)InputKey.F2] = Raylib.IsKeyDown(KeyboardKey.F2);
			InputState_Cur.KeysDown[(int)InputKey.F3] = Raylib.IsKeyDown(KeyboardKey.F3);
			InputState_Cur.KeysDown[(int)InputKey.F4] = Raylib.IsKeyDown(KeyboardKey.F4);
			InputState_Cur.KeysDown[(int)InputKey.F5] = Raylib.IsKeyDown(KeyboardKey.F5);

			InputState_Cur.KeysDown[(int)InputKey.Esc] = Raylib.IsKeyDown(KeyboardKey.Escape);
			InputState_Cur.KeysDown[(int)InputKey.Ctrl] = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
			InputState_Cur.KeysDown[(int)InputKey.Alt] = Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt);
		}

		public bool IsInputPressed(InputKey K) {
			if (!InputState_Last.KeysDown[(int)K] && InputState_Cur.KeysDown[(int)K])
				return true;

			return false;
		}

		public bool IsInputReleased(InputKey K) {
			if (InputState_Last.KeysDown[(int)K] && !InputState_Cur.KeysDown[(int)K])
				return true;

			return false;
		}

		public bool IsInputDown(InputKey K) {
			return InputState_Cur.KeysDown[(int)K];
		}

		public Vector2 GetMousePos() {
			return InputState_Cur.MousePos;
		}

		public float GetMouseWheel() {
			return InputState_Cur.MouseWheel;
		}
	}
}
