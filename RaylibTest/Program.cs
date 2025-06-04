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
			Window = new GameWindow(1920, 1080, "3D Test");
			GraphicsUtils.Init();
			Scripting.Init();

			MainMenuState = new MainMenuState();
			GameState = new GameState();
			OptionsState = new OptionsState();

			Window.SetState(MainMenuState);

			float Dt = 0;

			while (Window.IsOpen()) {
				Dt = Raylib.GetFrameTime();

				if (Dt != 0 && Dt < 1.5f) {
					Window.Update(Dt);
				}

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

		List<Tuple<Vector3, Vector3>> MarkerList = new List<Tuple<Vector3, Vector3>>();

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

			Ply = new Player("snoutx10k", true);

			Ply.AddOnKeyPressed(KeyboardKey.KEY_F2, () => {
				Console.WriteLine("Compute light!");
				Map.ComputeLighting();
			});

			Ply.AddOnKeyPressed(KeyboardKey.KEY_F3, () => {
				Console.WriteLine("Pos: {0}", Ply.Position);
			});

			Ply.AddOnKeyPressed(KeyboardKey.KEY_F4, () => {
				Console.WriteLine("Clearing records");
				Utils.ClearRaycastRecord();
			});

			Ply.AddOnKeyPressed(KeyboardKey.KEY_F, () => {
				Console.WriteLine("Pew pew!");

				Vector3 Start = Ply.Position;
				Vector3 End = Map.RaycastPos(Start, 10, FPSCamera.GetForward(), out Vector3 Face);

				if (End != Vector3.Zero) {
					MarkerList.Add(new Tuple<Vector3, Vector3>(Start, End));
				}
			});

			Ply.SetPosition(32, 73, 19);
		}

		Vector3 PlyVelocity = Vector3.Zero;

		float ClampToZero(float Num, float ClampHyst) {
			if (Num < 0 && Num > -ClampHyst)
				return 0;

			if (Num > 0 && Num < ClampHyst)
				return 0;

			return Num;
		}

		void UpdatePhysics(float Dt) {
			Ply.UpdatePhysics(Dt);

			float ClampHyst = 0.001f;
			PlyVelocity.X = ClampToZero(PlyVelocity.X, ClampHyst);
			PlyVelocity.Y = ClampToZero(PlyVelocity.Y, ClampHyst);
			PlyVelocity.Z = ClampToZero(PlyVelocity.Z, ClampHyst);

			float VelLen = PlyVelocity.Length();

			float Gravity = 10.5f;
			float MaxPlayerVelocity = 3.6f;
			float MaxPlayerControllableVelocity = 4.0f;
			float MaxPlayerFallVelocity = 6.0f;
			float PlayerJumpVelocity = 4.8f;
			const float PlyMoveSen = 2.2f;

			float PlayerHeight = 1.8f;

			Vector3 TorsoEndPos = Ply.Position + new Vector3(0, -0.8f, 0);
			Vector3 TorsoPos = Ply.Position + new Vector3(0, -1f, 0);

			Vector3 HitTorso = Map.RaycastPos(TorsoEndPos, 0.2f, new Vector3(0, -1f, 0), out Vector3 Face2);
			Vector3 HitFloor = Map.RaycastPos(TorsoPos, PlayerHeight - 1, new Vector3(0, -1f, 0), out Vector3 Face1);

			bool HasHitFloor = HitFloor != Vector3.Zero;
			bool HasHitTorso = HitTorso != Vector3.Zero;

			bool IsBraking = true;

			{
				Vector3 DesiredPos = Vector3.Zero;

				Vector3 Forward = FPSCamera.GetForward();
				Forward.Y = 0;
				Forward = Vector3.Normalize(Forward);

				Vector3 Left = FPSCamera.GetLeft();
				Vector3 Up = FPSCamera.GetUp();

				if (Raylib.IsKeyDown('W'))
					DesiredPos += Forward * PlyMoveSen;
				if (Raylib.IsKeyDown('S'))
					DesiredPos -= Forward * PlyMoveSen;

				if (Raylib.IsKeyDown('A'))
					DesiredPos += Left * PlyMoveSen;
				if (Raylib.IsKeyDown('D'))
					DesiredPos -= Left * PlyMoveSen;

				if (Raylib.IsKeyDown(' ')) {
					//DesiredPos += Up * PlyMoveSen;

					if (HasHitFloor) {
						PlyVelocity += new Vector3(0, PlayerJumpVelocity, 0);
						HasHitFloor = false;
					}
				}

				if (Raylib.IsKeyDown('C'))
					DesiredPos -= Up * PlyMoveSen;

				if (HasHitFloor) {
					PlyVelocity += DesiredPos;
				} else {
					if (PlyVelocity.Length() <= MaxPlayerControllableVelocity) {

						if (!HasHitTorso) {
							PlyVelocity += new Vector3(DesiredPos.X, 0, DesiredPos.Z) * 0.1f;
						}
					}
				}

				if (DesiredPos != Vector3.Zero)
					IsBraking = false;
			}

			Vector2 PlyVelocityH = new Vector2(PlyVelocity.X, PlyVelocity.Z);
			float VelH = PlyVelocityH.Length();
			float VelV = Math.Abs(PlyVelocity.Y);
			float Vel = PlyVelocity.Length();

			if (HasHitFloor) {
				if (VelH > MaxPlayerVelocity) {
					Vector2 NewHorizontal = Vector2.Normalize(PlyVelocityH) * MaxPlayerVelocity;
					PlyVelocity.X = NewHorizontal.X;
					PlyVelocity.Z = NewHorizontal.Y;
				}

				PlyVelocity.Y = 0;

				if (IsBraking)
					PlyVelocity = PlyVelocity * 0.6f;

				Ply.SetPosition(HitFloor + new Vector3(0, PlayerHeight, 0));
			} else {
				//Console.WriteLine("Falling!");



				PlyVelocity = PlyVelocity - new Vector3(0, Gravity * Dt, 0);

				if (VelV > MaxPlayerFallVelocity) {
					if (PlyVelocity.Y < 0)
						PlyVelocity.Y = -MaxPlayerFallVelocity;
					else
						PlyVelocity.Y = MaxPlayerFallVelocity;
				}
			}

			//Console.WriteLine("{0}", PlyVelocity.Length());

			/*bool MoveHits = Map.RaycastPoint(Ply.Position + PlyVelocity * Dt);
			if (MoveHits) {
				PlyVelocity = Vector3.Zero;
			}*/

			if (PlyVelocity != Vector3.Zero) {
				Vector3 HitPos = Map.RaycastPos(Ply.Position, PlyVelocity.Length() * Dt, Vector3.Normalize(PlyVelocity), out Vector3 Face);
				if (HitPos != Vector3.Zero) {

					if (Face.X != 0)
						PlyVelocity.X = 0;

					if (Face.Y != 0)
						PlyVelocity.Y = 0;

					if (Face.Z != 0)
						PlyVelocity.Z = 0;
				}
			}

			if (PlyVelocity != Vector3.Zero)
				Ply.SetPosition(Ply.Position + (PlyVelocity * Dt));
		}

		public override void Update(float Dt) {
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

							Map.SetBlock(X, Y, Z, BlockType.Dirt);
							return true;
						}

						return false;
					});
				}

				if (Middle) {
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

							Map.SetBlock(X, Y, Z, BlockType.Glowstone);
							return true;
						}

						return false;
					});
				}
			}

			UpdatePhysics(Dt);
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

			foreach (var L in MarkerList) {
				Raylib.DrawLine3D(L.Item1, L.Item2, Color.Blue);
			}

			Raylib.DrawLine3D(Vector3.Zero, new Vector3(100, 0, 0), Color.Red);
			Raylib.DrawLine3D(Vector3.Zero, new Vector3(0, 100, 0), Color.Green);
			Raylib.DrawLine3D(Vector3.Zero, new Vector3(0, 0, 100), Color.Blue);

			//Raylib.DrawLine3D(Start, End, Color.White);

		}
	}
}
