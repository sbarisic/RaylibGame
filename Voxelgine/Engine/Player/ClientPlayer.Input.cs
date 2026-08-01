using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Voxelgine.Engine.DI;
using Voxelgine.Graphics;

#if WINDOWS
using Voxelgine.FishGfxClient;
#endif

namespace Voxelgine.Engine
{
	public unsafe partial class ClientPlayer
	{
		Dictionary<InputKey, Action<OnKeyPressedEventArg>> OnKeyFuncs = new Dictionary<InputKey, Action<OnKeyPressedEventArg>>();
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
				Logging.Log(
					GameLogLevel.Debug,
					"Physics",
					$"Noclip requested playerId={PlayerId} enabled={NoClip}"
				);
			});

			AddOnKeyPressed(InputKey.Num1, _ => SetSelectedInventoryIndex(0));
			AddOnKeyPressed(InputKey.Num2, _ => SetSelectedInventoryIndex(1));
			AddOnKeyPressed(InputKey.Num3, _ => SetSelectedInventoryIndex(2));
			AddOnKeyPressed(InputKey.Num4, _ => SetSelectedInventoryIndex(3));
			AddOnKeyPressed(InputKey.Num5, _ => SetSelectedInventoryIndex(4));
			AddOnKeyPressed(InputKey.Num6, _ => SetSelectedInventoryIndex(5));
			AddOnKeyPressed(InputKey.Num7, _ => SetSelectedInventoryIndex(6));
			AddOnKeyPressed(InputKey.Num8, _ => SetSelectedInventoryIndex(7));
			AddOnKeyPressed(InputKey.Num9, _ => SetSelectedInventoryIndex(8));
			AddOnKeyPressed(InputKey.Num0, _ => SetSelectedInventoryIndex(9));

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
			bool capture = Enable ?? !CursorDisabled;
			IFishGfxGameWindow window = (IFishGfxGameWindow)Eng.AsClient().Window;
			window.RenderWindow.CaptureCursor = capture;
			window.RenderWindow.ShowCursor = !capture;
			CursorDisabled = capture;
		}

		public void Tick(InputMgr InMgr)
		{
			Camera.Update(CursorDisabled, ref Cam, InMgr.GetMousePos());
			UpdateDirectionVectors();

			// Use InputMgr for F1
				if (InMgr.IsInputPressed(InputKey.F1))
				{
					ToggleMouse();
					OnMenuToggled?.Invoke(!CursorDisabled); // true when cursor is now visible (menu open)
				}

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
