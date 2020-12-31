using RaylibSharp;
using RaylibTest.Engine;
using RaylibTest.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace RaylibTest {
	class Program {
		public static GameWindow Window;

		public static GameStateImpl MainMenuState;
		public static GameStateImpl GameState;
		public static GameStateImpl OptionsState;

		static void Main(string[] args) {
			Window = new GameWindow(1366, 768, "3D Test");
			GraphicsUtils.Init();
			Scripting.Init();

			MainMenuState = new MainMenuState();
			GameState = new GameState();
			OptionsState = new OptionsState();

			Window.SetState(MainMenuState);

			while (Window.IsOpen()) {
				Window.Update();
				Window.Draw();
			}
		}
	}

	class MainMenuState : GameStateImpl {
		Camera2D Cam = new Camera2D();

		public override void SwapTo() {
			Cam.zoom = 1;
		}

		public override void Draw() {
			const int BtnWidth = 400;
			const int BtnHeight = 40;
			const int BtnPadding = 10;
			int BtnX = (Program.Window.Width / 2) - (BtnWidth / 2);

			//RL.ClearBackground(new Color(160, 180, 190, 255));
			Raylib.ClearBackground(new Color(200, 200, 200));
			Raylib.BeginMode2D(Cam);

			if (Raygui.GuiButton(new Rectangle(BtnX, 100 + (BtnHeight + BtnPadding) * 0, BtnWidth, BtnHeight), "New Game"))
				Program.Window.SetState(Program.GameState);

			if (Raygui.GuiButton(new Rectangle(BtnX, 100 + (BtnHeight + BtnPadding) * 1, BtnWidth, BtnHeight), "Options"))
				Program.Window.SetState(Program.OptionsState);

			if (Raygui.GuiButton(new Rectangle(BtnX, 100 + (BtnHeight + BtnPadding) * 2, BtnWidth, BtnHeight), "Quit"))
				Program.Window.Close();

			Raylib.EndMode2D();
		}
	}

	class OptionsState : GameStateImpl {
	}

	unsafe class GameState : GameStateImpl {
		ChunkMap Map;
		Player Ply;

		public override void SwapTo() {
			Map = new ChunkMap();
			//Map.LoadFromChunk("data/map0.chunk");

			if (File.Exists("map.bin")) {
				using (FileStream FS = File.OpenRead("map.bin")) {
					Map.Read(FS);
				}
			} else
				Map.GenerateFloatingIsland(64, 64);

			//Map.SetBlock(0, 0, 0, BlockType.Test2);

			/*foreach (var C in Map.GetAllChunks())
				C.Fill((BlockType)Utils.Random(1, 11));*/

			FPSCamera.Position = new Vector3(6, 6, -12);
			Ply = new Player("snoutx10k", true);
		}



		public override void Update() {
			Ply.Update();

			if (Raylib.IsKeyPressed(KeyboardKey.KEY_F5)) {
				Console.WriteLine("Saving map!");

				using (MemoryStream MS = new MemoryStream()) {
					Map.Write(MS);
					File.WriteAllBytes("map.bin", MS.ToArray());
				}

				Console.WriteLine("Done!");
			}

			bool Left = Raylib.IsMouseButtonPressed(MouseButton.MOUSE_LEFT_BUTTON);
			bool Right = Raylib.IsMouseButtonPressed(MouseButton.MOUSE_RIGHT_BUTTON);
			bool Middle = Raylib.IsMouseButtonPressed(MouseButton.MOUSE_MIDDLE_BUTTON);
			const float MaxLen = 20;

			if (Left || Right || Middle) {
				Vector3 Dir = FPSCamera.GetForward();
				Vector3 Start = FPSCamera.Position;
				Vector3 End = FPSCamera.Position + (Dir * MaxLen);

				if (Left) {
					Utils.Raycast(Start, Dir, MaxLen, (X, Y, Z, Face) => {
						if (Map.GetBlock(X, Y, Z) != BlockType.None) {
							Map.SetBlock(X, Y, Z, BlockType.None);
							return true;
						}

						/*if (Map.GetBlock(X, Y, Z) != BlockType.None) {
							X += (int)Face.X;
							Y += (int)Face.Y;
							Z += (int)Face.Z;

							Map.SetBlock(X, Y, Z, BlockType.Test);
							return true;
						}*/

						return false;
					});
				}

				if (Right) {
					Utils.Raycast(Start, Dir, MaxLen, (X, Y, Z, Face) => {
						BlockType CurBlockType = BlockType.None;

						/*if (Chunk.EmitsLight(CurBlockType = Map.GetBlock(X, Y, Z))) {
							Map.SetBlock(X, Y, Z, CurBlockType);
							return true;
						}*/

						if (Map.GetBlock(X, Y, Z) != BlockType.None) {
							X += (int)Face.X;
							Y += (int)Face.Y;
							Z += (int)Face.Z;

							Map.SetBlock(X, Y, Z, BlockType.Sand);
							return true;
						}

						return false;
					});
				}
			}
		}

		public override void Draw() {
			Raylib.ClearBackground(new Color(200, 200, 200));
			Raylib.BeginMode3D(Ply.Cam);
			Draw3D();
			Raylib.EndMode3D();

			Raylib.DrawCircleLines(Program.Window.Width / 2, Program.Window.Height / 2, 5, Color.White);

			Raylib.DrawFPS(10, 10);
		}

		void Draw3D() {

			//Raylib.DrawGrid(100, 1);
			Map.Draw();
			Map.DrawTransparent();

			Ply.Draw();

			Raylib.DrawLine3D(Vector3.Zero, new Vector3(100, 0, 0), Color.Red);
			Raylib.DrawLine3D(Vector3.Zero, new Vector3(0, 100, 0), Color.Green);
			Raylib.DrawLine3D(Vector3.Zero, new Vector3(0, 0, 100), Color.Blue);

			//Raylib.DrawLine3D(Start, End, Color.White);

		}
	}
}
