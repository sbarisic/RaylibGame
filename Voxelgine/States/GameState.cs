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
using Voxelgine.GUI;
using System.ComponentModel.Design;

namespace RaylibGame.States {
	unsafe class GameState : GameStateImpl {
		public ChunkMap Map;
		public Player Ply;
		public SoundMgr Snd;

		List<Tuple<Vector3, Vector3>> MarkerList = new List<Tuple<Vector3, Vector3>>();

		GUIManager GUI;

		public GameState(GameWindow window) : base(window) {
			GUI = new GUIManager(window);
			InitGUI();

			Snd = new SoundMgr();
			Snd.Init();

			Map = new ChunkMap(this);
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
				//Vector3 End = Map.RaycastPos(Start, 10, FPSCamera.GetForward(), out Vector3 Face);

				Ray R = new Ray();
				R.Position = Start;
				R.Direction = FPSCamera.GetForward();
				RayCollision Col = Map.RaycastEnt(R);

				Vector3 End = Vector3.Zero;

				if (Col.Hit)
					End = Col.Point;

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

			/*VoxCollision.Raycast(Ply.Position, Vector3.Normalize(new Vector3(0.5f, -0.8f, 0.2f)), 20,
				GetBlockImpl,
				(X, Y, Z, PBlock, Norm) => {
					Map.SetBlock((int)X, (int)Y, (int)Z, BlockType.Bricks);

					if (PBlock.Type == BlockType.None)
						return false;


					return true;
				});*/
		}

		bool GetBlockImpl(float X, float Y, float Z, int Counter, out PlacedBlock B) {
			if (Counter > 32) {
				B = null;
				return false;
			}

			B = Map.GetPlacedBlock((int)X, (int)Y, (int)Z, out Chunk Chk);
			return true;
		}

		GUIElement AddButton(string Txt, OnMouseClickedFunc OnClick) {

			GUIButton Btn = new GUIButton(GUI);
			Btn.Pos = GUI.WindowScale(new Vector2(0.1f, 0.1f));
			Btn.Size = new Vector2(180, 45);
			Btn.Text = Txt;
			Btn.OnClickedFunc = OnClick;
			GUI.AddElement(Btn);

			return Btn;

		}

		GUIItemBox Box_Health;
		GUILabel InfoLbl;

		void InitGUI() {
			/*GUIIconBar Bar_Health = new GUIIconBar(GUI, IconBarStyle.Hearts, 10, 2.0f);
			Bar_Health.Pos = new Vector2(100, Window.Height - 80);
			Bar_Health.TxtOffset = new Vector2(0, -10);
			Bar_Health.Txt = "Helth";
			GUI.AddElement(Bar_Health);*/

			Box_Health = new GUIItemBox(GUI);
			Box_Health.Pos = new Vector2(64, Window.Height - 64 - 64);
			Box_Health.Text = "100";
			Box_Health.SetIcon(ResMgr.GetTexture("items/heart_full.png"), 3);
			GUI.AddElement(Box_Health);

			InfoLbl = new GUILabel(GUI);
			InfoLbl.Pos = new Vector2(16, 40);
			InfoLbl.Size = new Vector2(300, 250);
			InfoLbl.Clear();
			InfoLbl.WriteLine("Hello World!");
			GUI.AddElement(InfoLbl);

			List<GUIElement> Els = new List<GUIElement>();

			Els.Add(AddButton("Func 1", (E) => { }));
			Els.Add(AddButton("Func 2", (E) => { }));
			Els.Add(AddButton("Func 3", (E) => { }));
			Els.Add(AddButton("Func 4", (E) => { }));

			GUI.CenterVertical(new Vector2(Window.Width - 180, 10), new Vector2(180, 400), new Vector2(10, 0), 1, Els.ToArray());
		}

		void UpdateGUI() {
			InfoLbl.Clear();
			InfoLbl.WriteLine("Pos: {0}, {1}, {2}", (int)Ply.Position.X, (int)Ply.Position.Y, (int)Ply.Position.Z);
			InfoLbl.WriteLine("Vel: {0}", MathF.Round(PlyVelocity.Length(), 2));
		}


		Vector3 PlyVelocity = Vector3.Zero;
		bool WasLastLegsOnFloor = false;

		float ClampToZero(float Num, float ClampHyst) {
			if (Num < 0 && Num > -ClampHyst)
				return 0;

			if (Num > 0 && Num < ClampHyst)
				return 0;

			return Num;
		}

		IEnumerable<Vector3> Phys_RingPoints(Vector3 RingPos, float Radius = 0.4f) {
			for (int i = 0; i < 360; i += 20) {
				float X = MathF.Sin(Utils.ToRad(i)) * Radius;
				float Y = MathF.Cos(Utils.ToRad(i)) * Radius;
				Vector3 Offset = new Vector3(X, 0, Y);

				yield return RingPos + Offset;
			}
		}

		Mesh PlayerColMesh;
		bool HasPlayerColMesh;

		IEnumerable<Vector3> Phys_PlayerCollisionPoints(Vector3 Pos, float Radius = 0.4f, float Height = 1.8f) {
			int Divs = 6;

			if (!HasPlayerColMesh) {
				PlayerColMesh = Raylib.GenMeshCylinder(Radius, Height + 0.1f, Divs);
				HasPlayerColMesh = true;
			}

			if (HasPlayerColMesh) {
				//yield return Pos - new Vector3(0, Height, 0);

				for (int i = 0; i < PlayerColMesh.VertexCount; i++) {
					yield return PlayerColMesh.VerticesAs<Vector3>()[i] + Pos - new Vector3(0, Height - 0.1f, 0);
				}
			}
		}

		/*bool Phys_CollideFeet(Vector3 FeetPos, out Vector3 HitNorm, float Radius = 0.5f) {
			HitNorm = Vector3.Zero;

			foreach (Vector3 P in Phys_RingPoints(FeetPos, Radius)) {
				if (Map.Collide(P, out HitNorm))
					return true;
			}

			return false;
		}*/

		bool Phys_CollidePlayer(Vector3 Pos, Vector3 ProbeDir, out Vector3 HitNorm) {
			Vector3[] PlayerPoints = Phys_PlayerCollisionPoints(Pos).ToArray();

			foreach (var P in PlayerPoints) {
				//Utils.AddRaycastRecord(P, P, Color.Yellow);
				if (Map.Collide(P, ProbeDir, out HitNorm))
					return true;
			}

			HitNorm = Vector3.Zero;
			return false;
		}

		RayCollision Phys_RaycastPlayer(Vector3 Pos, Vector3 Dir, float Dist) {
			//return Map.Collide(new Ray(Pos, Dir));

			List<Vector3> TestPoints = Phys_RingPoints(Pos).ToList();
			TestPoints.Add(Pos);

			List<RayCollision> Cols = new List<RayCollision>();

			foreach (Vector3 TP in TestPoints) {
				Ray R = new Ray(Pos, Dir);
				RayCollision C = Map.Collide(R, Dist);

				if (C.Hit) {
					Cols.Add(C);
				}
			}

			if (Cols.Count > 0) {
				return Cols.OrderBy((C) => C.Distance).First();
			}

			return new RayCollision() { Hit = false };
		}

		void UpdatePhysics_Old(float Dt) {
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
					Ply.PhysicsHit(Ply.Position, VelLen, false, true, false, false);
				} else if (PlyVelocity.Length() >= (MaxPlayerVelocity / 2)) {
					Ply.PhysicsHit(HitFloor, PlyVelocity.Length(), false, true, true, false);
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
						Ply.PhysicsHit(HitFloor, PlyVelocity.Length(), false, false, false, true);
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

			Vector3 LastTorsoHitPos = Vector3.Zero;

			if (PlyVelocity != Vector3.Zero) {
				for (int i = 0; i < 360; i += 30) {
					Vector3 Dir = new Vector3((float)Math.Sin(Utils.ToRad(i)), 0, (float)Math.Cos(Utils.ToRad(i))) * 0.4f;

					for (int jj = 0; jj < 2; jj++) {
						Vector3 Addr = (jj == 0 ? new Vector3(0, -1.1f, 0) : new Vector3(0, -0.2f, 0));

						Vector3 HitPos = Map.RaycastPos(Ply.Position + Dir + Addr, PlyVelocity.Length() * Dt, Vector3.Normalize(PlyVelocity), out Vector3 Face);
						if (HitPos != Vector3.Zero) {
							LastTorsoHitPos = HitPos;

							PlyVelocity = Utils.ProjectOnPlane(PlyVelocity, Face);
						}
					}
				}

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

		void UpdatePhysics(float Dt) {
			Ply.UpdatePhysics(Dt);

			if (!Utils.HasRecord())
				Utils.BeginRaycastRecord();

			float ClampHyst = 0.0001f;
			PlyVelocity.X = ClampToZero(PlyVelocity.X, ClampHyst);
			PlyVelocity.Y = ClampToZero(PlyVelocity.Y, ClampHyst);
			PlyVelocity.Z = ClampToZero(PlyVelocity.Z, ClampHyst);

			float VelLen = PlyVelocity.Length();

			float MaxVelocity = 10.0f;
			if (VelLen > MaxVelocity) {
				PlyVelocity = PlyVelocity * (MaxVelocity / VelLen);
			}

			float Gravity = 10.5f;
			float PlayerHeight = 1.8f;
			float MaxPlayerVelocity = 4.0f;
			float MaxPlayerControllableVelocity = 5.0f;
			float MaxPlayerFallVelocity = 10.0f;
			float PlayerJumpVelocity = 4.8f;
			const float PlyMoveSen = 2.2f;

			if (PlyVelocity != Vector3.Zero) {
				Vector3 NewPlyPos = Ply.Position + (PlyVelocity * Dt);
				Vector3 MoveDir = Vector3.Normalize(NewPlyPos - Ply.Position);

				if (!Phys_CollidePlayer(NewPlyPos, MoveDir, out Vector3 HitNorm1))
					Ply.SetPosition(NewPlyPos);
				else {
					bool Done = false;

					for (int i = 0; i < 3; i++) {
						//Vector3 Delta = NewPlyPos - Ply.Position;
						//RayCollision Col = Phys_RaycastPlayer(Ply.Position, Vector3.Normalize(Delta), Delta.Length());

						if (Phys_CollidePlayer(NewPlyPos, MoveDir, out Vector3 HitNorm2)) {
							PlyVelocity = Utils.ProjectOnPlane(PlyVelocity, HitNorm2);
							NewPlyPos = Ply.Position + (PlyVelocity * Dt);

							if (!Phys_CollidePlayer(NewPlyPos, MoveDir, out Vector3 HitNorm3)) {
								Ply.SetPosition(NewPlyPos);
								Done = true;
								break;
							}
						}
					}

					if (!Done) {
						PlyVelocity = Vector3.Zero;
					}
				}

				float Factor = (float)Math.Pow(0.1f, Dt);
				PlyVelocity.X = PlyVelocity.X * Factor;
				PlyVelocity.Y = PlyVelocity.Y * Factor;
				PlyVelocity.Z = PlyVelocity.Z * Factor;
			}


			/*///Vector3 TorsoEndPos = Ply.Position + new Vector3(0, -1.2f, 0);
			Vector3 TorsoPos = Ply.Position + new Vector3(0, -1.2f, 0);



			/Vector3 HitTorso = Map.RaycastPos(TorsoEndPos, 0.2f, new Vector3(0, -1f, 0), out Vector3 Face2);
			Vector3 HitFloor = Map.RaycastPos(TorsoPos, 0.6f, new Vector3(0, -1f, 0), out Vector3 Face1);

			bool HasHitFloor = HitFloor != Vector3.Zero;

			if (Face1.Y != 1)
				HasHitFloor = false;
			//bool HasHitTorso = HitTorso != Vector3.Zero;*/



			{ // Player movement velocity
				Vector3 MovementVelocity = Vector3.Zero;

				Vector3 Forward = FPSCamera.GetForward();
				//Forward.Y = 0;
				//Forward = Vector3.Normalize(Forward);

				Vector3 Left = FPSCamera.GetLeft();
				Vector3 Up = FPSCamera.GetUp();

				if (Raylib.IsKeyDown(KeyboardKey.W))
					MovementVelocity += Forward * PlyMoveSen;
				if (Raylib.IsKeyDown(KeyboardKey.S))
					MovementVelocity -= Forward * PlyMoveSen;
				if (Raylib.IsKeyDown(KeyboardKey.A))
					MovementVelocity += Left * PlyMoveSen;
				if (Raylib.IsKeyDown(KeyboardKey.D))
					MovementVelocity -= Left * PlyMoveSen;

				if (Raylib.IsKeyDown(KeyboardKey.Space)) {
					MovementVelocity += Up * PlyMoveSen;

					//PlyVelocity += new Vector3(0, PlayerJumpVelocity, 0);
					//Ply.PhysicsHit(HitFloor, PlyVelocity.Length(), false, false, false, true);
				}

				if (Raylib.IsKeyDown(KeyboardKey.C))
					MovementVelocity -= Up * PlyMoveSen;

				PlyVelocity += MovementVelocity;
			}

			//Vector2 PlyVelocityH = new Vector2(PlyVelocity.X, PlyVelocity.Z);
			//float VelH = PlyVelocityH.Length();
			//float VelV = Math.Abs(PlyVelocity.Y);
			//float Vel = PlyVelocity.Length();



			Utils.EndRaycastRecord();
		}

		public override void Update(float Dt) {
			if (Raylib.IsKeyPressed(KeyboardKey.Escape)) {
				Window.SetState(Program.MainMenuState);
				return;
			}

			Map.Update(Dt);
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

			if ((Left || Right || Middle) && Ply.CursorDisabled) {
				Vector3 Dir = FPSCamera.GetForward();
				Vector3 Start = FPSCamera.Position;
				Vector3 End = FPSCamera.Position + (Dir * MaxLen);

				if (Left) {
					Utils.Raycast(Start, Dir, MaxLen, (X, Y, Z, Face) => {
						if (Map.GetBlock(X, Y, Z) != BlockType.None) {

							Snd.PlayCombo("block_break", Start, Dir, new Vector3(X, Y, Z));
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

							Snd.PlayCombo("block_place", Start, Dir, new Vector3(X, Y, Z));
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

							Snd.PlayCombo("block_place", Start, Dir, new Vector3(X, Y, Z));
							Map.SetBlock(X, Y, Z, BlockType.Campfire);
							return true;
						}

						return false;
					});
				}
			}

			UpdatePhysics(Dt);

			if (!Ply.CursorDisabled) {
				GUI.Update(Dt);
			}

			UpdateGUI();
		}

		public override void Draw() {
			Raylib.EndBlendMode();

			Raylib.ClearBackground(new Color(200, 200, 200));
			Raylib.BeginMode3D(Ply.Cam);
			Draw3D();
			Raylib.EndMode3D();

			//Camera2D GUICam = new Camera2D();
			//Raylib.BeginMode2D(GUICam);
			GUI.Draw();

			Raylib.DrawCircleLines(Program.Window.Width / 2, Program.Window.Height / 2, 5, Color.White);

			//Raylib.EndMode2D();
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
