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
		G,
		C,
		V,
		T,
		M,
		K,
		I,
		O,
		P,

		Tab,
		Space,

		F1,
		F2,
		F3,
		F4,
		F5,

		Num0,
		Num1,
		Num2,
		Num3,
		Num4,
		Num5,
		Num6,
		Num7,
		Num8,
		Num9,

		Up,
		Down,
		Left,
		Right,
		Enter,

		Esc,
		Ctrl,
		Alt,
		Shift,

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
			InputState_Cur.KeysDown[(int)InputKey.G] = Raylib.IsKeyDown(KeyboardKey.G);
			InputState_Cur.KeysDown[(int)InputKey.C] = Raylib.IsKeyDown(KeyboardKey.C);
			InputState_Cur.KeysDown[(int)InputKey.V] = Raylib.IsKeyDown(KeyboardKey.V);
			InputState_Cur.KeysDown[(int)InputKey.T] = Raylib.IsKeyDown(KeyboardKey.T);
			InputState_Cur.KeysDown[(int)InputKey.M] = Raylib.IsKeyDown(KeyboardKey.M);
			InputState_Cur.KeysDown[(int)InputKey.K] = Raylib.IsKeyDown(KeyboardKey.K);
			InputState_Cur.KeysDown[(int)InputKey.I] = Raylib.IsKeyDown(KeyboardKey.I);
			InputState_Cur.KeysDown[(int)InputKey.O] = Raylib.IsKeyDown(KeyboardKey.O);
			InputState_Cur.KeysDown[(int)InputKey.P] = Raylib.IsKeyDown(KeyboardKey.P);
			InputState_Cur.KeysDown[(int)InputKey.Tab] = Raylib.IsKeyDown(KeyboardKey.Tab);

			InputState_Cur.KeysDown[(int)InputKey.Space] = Raylib.IsKeyDown(KeyboardKey.Space);

			InputState_Cur.KeysDown[(int)InputKey.F1] = Raylib.IsKeyDown(KeyboardKey.F1);
			InputState_Cur.KeysDown[(int)InputKey.F2] = Raylib.IsKeyDown(KeyboardKey.F2);
			InputState_Cur.KeysDown[(int)InputKey.F3] = Raylib.IsKeyDown(KeyboardKey.F3);
			InputState_Cur.KeysDown[(int)InputKey.F4] = Raylib.IsKeyDown(KeyboardKey.F4);
			InputState_Cur.KeysDown[(int)InputKey.F5] = Raylib.IsKeyDown(KeyboardKey.F5);

			InputState_Cur.KeysDown[(int)InputKey.Num0] = Raylib.IsKeyDown(KeyboardKey.Zero) || Raylib.IsKeyDown(KeyboardKey.Kp0);
			InputState_Cur.KeysDown[(int)InputKey.Num1] = Raylib.IsKeyDown(KeyboardKey.One) || Raylib.IsKeyDown(KeyboardKey.Kp1);
			InputState_Cur.KeysDown[(int)InputKey.Num2] = Raylib.IsKeyDown(KeyboardKey.Two) || Raylib.IsKeyDown(KeyboardKey.Kp2);
			InputState_Cur.KeysDown[(int)InputKey.Num3] = Raylib.IsKeyDown(KeyboardKey.Three) || Raylib.IsKeyDown(KeyboardKey.Kp3);
			InputState_Cur.KeysDown[(int)InputKey.Num4] = Raylib.IsKeyDown(KeyboardKey.Four) || Raylib.IsKeyDown(KeyboardKey.Kp4);
			InputState_Cur.KeysDown[(int)InputKey.Num5] = Raylib.IsKeyDown(KeyboardKey.Five) || Raylib.IsKeyDown(KeyboardKey.Kp5);
			InputState_Cur.KeysDown[(int)InputKey.Num6] = Raylib.IsKeyDown(KeyboardKey.Six) || Raylib.IsKeyDown(KeyboardKey.Kp6);
			InputState_Cur.KeysDown[(int)InputKey.Num7] = Raylib.IsKeyDown(KeyboardKey.Seven) || Raylib.IsKeyDown(KeyboardKey.Kp7);
			InputState_Cur.KeysDown[(int)InputKey.Num8] = Raylib.IsKeyDown(KeyboardKey.Eight) || Raylib.IsKeyDown(KeyboardKey.Kp8);
			InputState_Cur.KeysDown[(int)InputKey.Num9] = Raylib.IsKeyDown(KeyboardKey.Nine) || Raylib.IsKeyDown(KeyboardKey.Kp9);

			InputState_Cur.KeysDown[(int)InputKey.Up] = Raylib.IsKeyDown(KeyboardKey.Up);
			InputState_Cur.KeysDown[(int)InputKey.Down] = Raylib.IsKeyDown(KeyboardKey.Down);
			InputState_Cur.KeysDown[(int)InputKey.Left] = Raylib.IsKeyDown(KeyboardKey.Left);
			InputState_Cur.KeysDown[(int)InputKey.Right] = Raylib.IsKeyDown(KeyboardKey.Right);
			InputState_Cur.KeysDown[(int)InputKey.Enter] = Raylib.IsKeyDown(KeyboardKey.Enter) || Raylib.IsKeyDown(KeyboardKey.KpEnter);

			InputState_Cur.KeysDown[(int)InputKey.Esc] = Raylib.IsKeyDown(KeyboardKey.Escape);
			InputState_Cur.KeysDown[(int)InputKey.Ctrl] = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
			InputState_Cur.KeysDown[(int)InputKey.Alt] = Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt);
			InputState_Cur.KeysDown[(int)InputKey.Shift] = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);
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
