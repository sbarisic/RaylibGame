using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Raylib_cs;
using Voxelgine.Engine.DI;

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
		IFishEngineRunner Eng;

		public InputMgr(IFishEngineRunner Eng)
		{
			this.Eng = Eng;
		}

		public void Tick(float GameTime)
		{
			InputState_Last = InputState_Cur;
			InputState_Cur = new InputState();
			InputState_Cur.GameTime = GameTime;

			// TODO: Input mapping

			InputState_Cur.MousePos = Raylib.GetMousePosition();
			InputState_Cur.MouseWheel = Raylib.GetMouseWheelMove();

			for (int i = 0; i < Eng.DI.GetRequiredService<GameConfig>().MouseButtonDown.Length; i++)
			{
				var KV = Eng.DI.GetRequiredService<GameConfig>().MouseButtonDown[i];
				InputState_Cur.KeysDown[(int)KV.Key] = Raylib.IsMouseButtonDown(KV.Value);
			}

			for (int i = 0; i < Eng.DI.GetRequiredService<GameConfig>().KeyDown.Length; i++)
			{
				var KV = Eng.DI.GetRequiredService<GameConfig>().KeyDown[i];
				InputState_Cur.KeysDown[(int)KV.Key] = Raylib.IsKeyDown(KV.Value);
			}

			for (int i = 0; i < Eng.DI.GetRequiredService<GameConfig>().TwoKeysDown.Length; i++)
			{
				var KV = Eng.DI.GetRequiredService<GameConfig>().TwoKeysDown[i];
				InputState_Cur.KeysDown[(int)KV.Key] = Raylib.IsKeyDown(KV.Value.Key) || Raylib.IsKeyDown(KV.Value.Value);
			}
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
