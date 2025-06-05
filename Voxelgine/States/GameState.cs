using RaylibGame.Engine;
using Raylib_cs;
using Voxelgine.Engine;
using Voxelgine.Graphics;
using Voxelgine;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace RaylibGame.States {
	unsafe class GameState : GameStateImpl {
		ChunkMap Map;
		Player Ply;
		SoundMgr Snd;

		List<Tuple<Vector3, Vector3>> MarkerList = new List<Tuple<Vector3, Vector3>>();

		public override void SwapTo() {
			Snd = new SoundMgr();
			Snd.Init();

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

			Ply = new Player("snoutx10k", true, Snd);

			Stopwatch SWatch = Stopwatch.StartNew();

			Ply.AddOnKeyPressed(KeyboardKey.F2, () => {
				Console.WriteLine("Compute light!");
				SWatch.Restart();

				Map.ComputeLighting();

				SWatch.Stop();
				Console.Title = string.Format("> {0} s", SWatch.ElapsedMilliseconds / 1000.0f);
			});

			Ply.AddOnKeyPressed(KeyboardKey.F3, () => {
				Console.WriteLine("Pos: {0}", Ply.Position);
			});

			Ply.AddOnKeyPressed(KeyboardKey.F4, () => {
				Console.WriteLine("Clearing records");
				Utils.ClearRaycastRecord();
			});

			Ply.AddOnKeyPressed(KeyboardKey.F, () => {
				Console.WriteLine("Pew pew!");

				Vector3 Start = Ply.Position;
				Vector3 End = Map.RaycastPos(Start, 10, FPSCamera.GetForward(), out Vector3 Face);

				if (End != Vector3.Zero) {
					MarkerList.Add(new Tuple<Vector3, Vector3>(Start, End));
				}
			});

			Ply.AddOnKeyPressed(KeyboardKey.E, () => {
				Vector3 Start = Ply.Position;
				Vector3 End = Map.RaycastPos(Start, 1.5f, FPSCamera.GetForward(), out Vector3 Face);
				PlacedBlock Blk = Map.GetPlacedBlock((int)End.X, (int)End.Y, (int)End.Z, out Chunk Chk);

				float XU = (float)(End.X - Math.Floor(End.X));
				float YV = (float)(End.Z - Math.Floor(End.Z));

				//Blk.OnBlockActivate?.Invoke(Blk, End, new Vector2(XU, YV));

				if (Blk.Type == BlockType.CraftingTable)
					Console.WriteLine("Craft! {0}, ({1}, {2})", Face, XU, YV);
			});

			Ply.SetPosition(32, 73, 19);
		}

		Vector3 PlyVelocity = Vector3.Zero;
		bool WasLastLegsOnFloor = false;

		public GameState(GameWindow window) : base(window) {
		}

		float ClampToZero(float Num, float ClampHyst) {
			if (Num < 0 && Num > -ClampHyst)
				return 0;

			if (Num > 0 && Num < ClampHyst)
				return 0;

			return Num;
		}

		void UpdatePhysics(float Dt) {
			Ply.UpdatePhysics(Dt);

			if (!Utils.HasRecord())
				Utils.BeginRaycastRecord();

			float ClampHyst = 0.001f;
			PlyVelocity.X = ClampToZero(PlyVelocity.X, ClampHyst);
			PlyVelocity.Y = ClampToZero(PlyVelocity.Y, ClampHyst);
			PlyVelocity.Z = ClampToZero(PlyVelocity.Z, ClampHyst);

			float VelLen = PlyVelocity.Length();

			float Gravity = 10.5f;
			float MaxPlayerVelocity = 3.6f;
			float MaxPlayerControllableVelocity = 4.0f;
			float MaxPlayerFallVelocity = 10.0f;
			float PlayerJumpVelocity = 4.8f;
			const float PlyMoveSen = 2.2f;

			float PlayerHeight = 1.8f;

			//Vector3 TorsoEndPos = Ply.Position + new Vector3(0, -1.2f, 0);
			Vector3 TorsoPos = Ply.Position + new Vector3(0, -1.2f, 0);



			//Vector3 HitTorso = Map.RaycastPos(TorsoEndPos, 0.2f, new Vector3(0, -1f, 0), out Vector3 Face2);
			Vector3 HitFloor = Map.RaycastPos(TorsoPos, 0.6f, new Vector3(0, -1f, 0), out Vector3 Face1);

			bool HasHitFloor = HitFloor != Vector3.Zero;

			if (Face1.Y != 1)
				HasHitFloor = false;
			//bool HasHitTorso = HitTorso != Vector3.Zero;

			if (HasHitFloor) {
				if (!WasLastLegsOnFloor) {
					WasLastLegsOnFloor = true;
					Ply.PhysicsHit(VelLen, false, true, false, false);
				} else if (PlyVelocity.Length() >= (MaxPlayerVelocity / 2)) {
					Ply.PhysicsHit(PlyVelocity.Length(), false, true, true, false);
				}
			} else {
				WasLastLegsOnFloor = false;
			}

			bool IsBraking = true;

			{
				Vector3 DesiredPos = Vector3.Zero;

				Vector3 Forward = FPSCamera.GetForward();
				Forward.Y = 0;
				Forward = Vector3.Normalize(Forward);

				Vector3 Left = FPSCamera.GetLeft();
				Vector3 Up = FPSCamera.GetUp();

				if (Raylib.IsKeyDown(KeyboardKey.W))
					DesiredPos += Forward * PlyMoveSen;
				if (Raylib.IsKeyDown(KeyboardKey.S))
					DesiredPos -= Forward * PlyMoveSen;

				if (Raylib.IsKeyDown(KeyboardKey.A))
					DesiredPos += Left * PlyMoveSen;
				if (Raylib.IsKeyDown(KeyboardKey.D))
					DesiredPos -= Left * PlyMoveSen;

				if (Raylib.IsKeyDown(KeyboardKey.Space)) {
					//DesiredPos += Up * PlyMoveSen;

					if (HasHitFloor) {
						PlyVelocity += new Vector3(0, PlayerJumpVelocity, 0);
						HasHitFloor = false;
						Ply.PhysicsHit(PlyVelocity.Length(), false, false, false, true);
					}
				}

				if (Raylib.IsKeyDown(KeyboardKey.C))
					DesiredPos -= Up * PlyMoveSen;

				if (HasHitFloor) {
					PlyVelocity += DesiredPos;
				} else {
					if (PlyVelocity.Length() <= MaxPlayerControllableVelocity) {

						PlyVelocity += new Vector3(DesiredPos.X, 0, DesiredPos.Z) * 0.1f;
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

				float Factor = (float)Math.Pow(0.1f, Dt);
				PlyVelocity.X = PlyVelocity.X * Factor;
				PlyVelocity.Z = PlyVelocity.Z * Factor;

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

			Vector3 LastTorsoHitPos = Vector3.Zero;

			if (PlyVelocity != Vector3.Zero) {
				for (int i = 0; i < 360; i += 30) {
					Vector3 Dir = new Vector3((float)Math.Sin(Utils.ToRad(i)), 0, (float)Math.Cos(Utils.ToRad(i))) * 0.4f;

					for (int jj = 0; jj < 2; jj++) {
						Vector3 Addr = (jj == 0 ? new Vector3(0, -1.1f, 0) : new Vector3(0, -0.2f, 0));

						Vector3 HitPos = Map.RaycastPos(Ply.Position + Dir + Addr, PlyVelocity.Length() * Dt, Vector3.Normalize(PlyVelocity), out Vector3 Face);
						if (HitPos != Vector3.Zero) {
							LastTorsoHitPos = HitPos;

							if (Face.X != 0) {
								//float DeltaVel = PlyVelocity.Length();

								PlyVelocity.X = 0;

								//DeltaVel = DeltaVel - PlyVelocity.Length();
								//Ply.PhysicsHit(DeltaVel, true, false);
							}

							if (Face.Y != 0) {
								//float DeltaVel = PlyVelocity.Length();

								PlyVelocity.Y = 0;

								//DeltaVel = DeltaVel - PlyVelocity.Length();
								//Ply.PhysicsHit(DeltaVel, false, Face.Y == 1);
							}

							if (Face.Z != 0) {
								//float DeltaVel = PlyVelocity.Length();

								PlyVelocity.Z = 0;

								//DeltaVel = DeltaVel - PlyVelocity.Length();
								//Ply.PhysicsHit(DeltaVel, true, false);
							}
						}
					}
				}

				/*Vector3 HitPos = Map.RaycastPos(Ply.Position, PlyVelocity.Length() * Dt, Vector3.Normalize(PlyVelocity), out Vector3 Face);
				if (HitPos != Vector3.Zero) {

					if (Face.X != 0)
						PlyVelocity.X = 0;

					if (Face.Y != 0)
						PlyVelocity.Y = 0;

					if (Face.Z != 0)
						PlyVelocity.Z = 0;
				}*/
			}

			if (PlyVelocity != Vector3.Zero) {
				Vector3 NewPlyPos = Ply.Position + (PlyVelocity * Dt);



				if (Map.GetBlock(NewPlyPos) == BlockType.None)
					Ply.SetPosition(NewPlyPos);
				else
					PlyVelocity = Vector3.Zero;
			}

			if (PlyVelocity.Y == 0 && !HasHitFloor) {
				if (LastTorsoHitPos != Vector3.Zero) {
					PlyVelocity = PlyVelocity * 0.5f;
					Ply.Parkour(LastTorsoHitPos + new Vector3(0, PlayerHeight, 0));
				}
			}

			Utils.EndRaycastRecord();
		}

		public override void Update(float Dt) {
			if (Raylib.IsKeyPressed(KeyboardKey.Escape)) {
				Window.SetState(Program.MainMenuState);
				return;
			}

			Ply.Update();

			if (Raylib.IsKeyPressed(KeyboardKey.F5)) {
				Console.WriteLine("Saving map!");

				using (MemoryStream MS = new MemoryStream()) {
					Map.Write(MS);
					File.WriteAllBytes("map.bin", MS.ToArray());
				}

				Console.WriteLine("Done!");
			}

			bool Left = Raylib.IsMouseButtonPressed(MouseButton.Left);
			bool Right = Raylib.IsMouseButtonPressed(MouseButton.Right);
			bool Middle = Raylib.IsMouseButtonPressed(MouseButton.Middle);
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

							Map.SetBlock(X, Y, Z, BlockType.Campfire);
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

			Utils.DrawRaycastRecord();

			//Raylib.DrawLine3D(Start, End, Color.White);

		}
	}

}
