using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Voxelgine.Graphics;

namespace Voxelgine.Engine
{
	public unsafe partial class Player
	{
		Dictionary<InputKey, Action<OnKeyPressedEventArg>> OnKeyFuncs = new Dictionary<InputKey, Action<OnKeyPressedEventArg>>();
		Stopwatch JumpCounter = Stopwatch.StartNew();

		public void Init(ChunkMap Map)
		{
			Stopwatch SWatch = Stopwatch.StartNew();

			AddOnKeyPressed(InputKey.F2, (E) =>
			{
				Logging.WriteLine("Compute light!");
				SWatch.Restart();
				Map.ComputeLighting();
				SWatch.Stop();
				Logging.WriteLine($"> {SWatch.ElapsedMilliseconds / 1000.0f} s");
			});

			AddOnKeyPressed(InputKey.F3, (E) => { Eng.DebugMode = !Eng.DebugMode; });

			AddOnKeyPressed(InputKey.F4, (E) => { Logging.WriteLine("Clearing records"); Utils.ClearRaycastRecord(); });

			AddOnKeyPressed(InputKey.C, (E) =>
			{
				NoClip = !NoClip;
				Logging.WriteLine($"No-clip mode: {(NoClip ? "ON" : "OFF")}");
			});

			AddOnKeyPressed(InputKey.Num1, (K) => { Inventory?.SetSelectedIndex(0); });
			AddOnKeyPressed(InputKey.Num2, (K) => { Inventory?.SetSelectedIndex(1); });
			AddOnKeyPressed(InputKey.Num3, (K) => { Inventory?.SetSelectedIndex(2); });
			AddOnKeyPressed(InputKey.Num4, (K) => { Inventory?.SetSelectedIndex(3); });
			AddOnKeyPressed(InputKey.Num5, (K) => { Inventory?.SetSelectedIndex(4); });
			AddOnKeyPressed(InputKey.Num6, (K) => { Inventory?.SetSelectedIndex(5); });
			AddOnKeyPressed(InputKey.Num7, (K) => { Inventory?.SetSelectedIndex(6); });
			AddOnKeyPressed(InputKey.Num8, (K) => { Inventory?.SetSelectedIndex(7); });
			AddOnKeyPressed(InputKey.Num9, (K) => { Inventory?.SetSelectedIndex(8); });

			AddOnKeyPressed(InputKey.I, (K) =>
			{
				if (Eng.DebugMode)
				{
					FreezeFrustum = !FreezeFrustum;
				}
			});
		}

		public void ToggleMouse(bool? Enable = null)
		{
			if (Enable != null)
				CursorDisabled = !Enable.Value;

			if (CursorDisabled)
				Raylib_cs.Raylib.EnableCursor();
			else
			{
				Raylib_cs.Raylib.DisableCursor();

				Vector2 MPos = Camera.GetPreviousMousePos();
				Raylib_cs.Raylib.SetMousePosition((int)MPos.X, (int)MPos.Y);
			}

			CursorDisabled = !CursorDisabled;
		}

		public void Tick(InputMgr InMgr)
		{
			ActiveSelection?.Tick(ViewMdl, InMgr);
			Camera.Update(CursorDisabled, ref Cam, InMgr.GetMousePos());
			UpdateDirectionVectors();

			// Use InputMgr for F1
				if (InMgr.IsInputPressed(InputKey.F1))
				{
					ToggleMouse();
					OnMenuToggled?.Invoke(!CursorDisabled); // true when cursor is now visible (menu open)
				}

			// Keep OnKeyFuncs using Raylib for now (as they are mapped to KeyboardKey)
			foreach (var KV in OnKeyFuncs)
			{
				if (InMgr.IsInputPressed(KV.Key))
					KV.Value(new OnKeyPressedEventArg(KV.Key));
			}

			Position = Camera.Position;

			ViewMdl.Update(this);
		}

		public void AddOnKeyPressed(InputKey K, Action<OnKeyPressedEventArg> Act)
		{
			OnKeyFuncs.Add(K, Act);
		}
	}
}
