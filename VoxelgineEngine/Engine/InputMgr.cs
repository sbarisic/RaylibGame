using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine
{
	public enum InputKey : int
	{
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

	public unsafe struct InputState
	{
		public float GameTime;

		public fixed bool KeysDown[(int)InputKey.InputKeyCount];
		public Vector2 MousePos;
		public float MouseWheel;
	}

	public unsafe class InputMgr
	{
		InputState InputState_Cur;
		InputState InputState_Last;
		IInputSource _inputSource;

		/// <summary>
		/// Gets the current input state snapshot.
		/// </summary>
		public InputState State => InputState_Cur;

		public InputMgr(IInputSource inputSource)
		{
			_inputSource = inputSource;
		}

		/// <summary>
		/// Replaces the current input source. Used to switch between local and network input.
		/// </summary>
		public void SetInputSource(IInputSource inputSource)
		{
			_inputSource = inputSource;
		}

		public void Tick(float GameTime)
		{
			InputState_Last = InputState_Cur;
			InputState_Cur = _inputSource.Poll(GameTime);
		}

		public bool IsInputPressed(InputKey K)
		{
			if (!InputState_Last.KeysDown[(int)K] && InputState_Cur.KeysDown[(int)K])
				return true;

			return false;
		}

		public bool IsInputReleased(InputKey K)
		{
			if (InputState_Last.KeysDown[(int)K] && !InputState_Cur.KeysDown[(int)K])
				return true;

			return false;
		}

		public bool IsInputDown(InputKey K)
		{
			return InputState_Cur.KeysDown[(int)K];
		}

		public Vector2 GetMousePos()
		{
			return InputState_Cur.MousePos;
		}

		public float GetMouseWheel()
		{
			return InputState_Cur.MouseWheel;
		}

		public float GetGameTime()
		{
			return InputState_Cur.GameTime;
		}
	}
}
