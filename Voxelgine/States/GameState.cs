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
		GUIInventory Inventory;

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

			Ply.AddOnKeyPressed(KeyboardKey.B, () => {
				if (Debugger.IsAttached)
					Debugger.Break();
			});

			Ply.SetPosition(32, 73, 19);
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
		BlockType PlayerSelectedBlockType;
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


			Inventory = new GUIInventory(GUI);
			Inventory.Pos = GUI.WindowScale(new Vector2(0.5f, 0.9f));
			Inventory.Pos -= new Vector2(Inventory.Size.X / 2, 0);
			GUI.AddElement(Inventory);

			SetInvItem(Inventory, 0, BlockType.Dirt, (ItmBox, Idx) => {
				PlayerSelectedBlockType = BlockType.Dirt;
			});

			SetInvItem(Inventory, 1, BlockType.Stone, (ItmBox, Idx) => {
				PlayerSelectedBlockType = BlockType.Stone;
			});

			SetInvItem(Inventory, 2, BlockType.StoneBrick, (ItmBox, Idx) => {
				PlayerSelectedBlockType = BlockType.StoneBrick;
			});

			SetInvItem(Inventory, 3, BlockType.Bricks, (ItmBox, Idx) => {
				PlayerSelectedBlockType = BlockType.Bricks;
			});

			SetInvItem(Inventory, 4, BlockType.Plank, (ItmBox, Idx) => {
				PlayerSelectedBlockType = BlockType.Plank;
			});

			SetInvItem(Inventory, 5, BlockType.CraftingTable, (ItmBox, Idx) => {
				PlayerSelectedBlockType = BlockType.CraftingTable;
			});

		}

		void SetInvItem(GUIInventory Inventory, int Idx, BlockType BType, Action<GUIItemBox, int> OnClick) {
			GUIItemBox Itm = Inventory.GetItem(Idx);

			BlockInfo.GetBlockTexCoords(BType, new Vector3(0, 1, 0), out Vector2 UVSize, out Vector2 UVPos);
			Itm.SetIcon(ResMgr.AtlasTexture, 0.092f, UVPos, UVSize);

			Itm.OnClickedFunc = (E) => {
				Inventory.SetSelectedIndex(Idx);
				OnClick(E as GUIItemBox, Idx);
			};
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


		bool Phys_CollidePlayer(Vector3 Pos, Vector3 ProbeDir, out Vector3 HitNorm) {
			bool Res = Phys_CollidePlayerAdvanced(Pos, ProbeDir, out HitNorm, out Vector3 _, out float _);

			if (!Res) {
				if (Phys_CollidePlayerSingle(Pos, out HitNorm, out Vector3 _, out float _)) {
					Res = true;
				}
			}

			return Res;
		}

		bool Phys_CollidePlayerAdvanced(Vector3 Pos, Vector3 ProbeDir, out Vector3 HitNorm, out Vector3 HitPoint, out float HitDistance) {
			HitNorm = Vector3.Zero;
			HitPoint = Vector3.Zero;
			HitDistance = float.MaxValue;

			Vector3[] PlayerPoints = Phys_PlayerCollisionPointsImproved(Pos).ToArray();
			bool hasCollision = false;

			foreach (var P in PlayerPoints) {
				if (Map.Collide(P, ProbeDir, out Vector3 tempNorm)) {
					hasCollision = true;
					float distance = Vector3.Distance(Pos, P);

					// Keep track of closest collision  
					if (distance < HitDistance) {
						HitDistance = distance;
						HitNorm = tempNorm;
						HitPoint = P;
					}
				}
			}

			return hasCollision;
		}

		bool Phys_CollidePlayerSingle(Vector3 Pos, out Vector3 HitNorm, out Vector3 HitPoint, out float HitDistance, bool FeetOnly = false) {
			HitDistance = float.MaxValue;
			HitPoint = Vector3.Zero;
			HitNorm = Vector3.Zero;

			Vector3[] PlayerPoints = Phys_PlayerCollisionPointsImproved(Pos).ToArray();

			if (FeetOnly) {
				float MinY = PlayerPoints.Select(P => P.Y).Min();
				PlayerPoints = PlayerPoints.Where(PP => PP.Y == MinY).ToArray();
			}

			bool hasCollision = false;

			foreach (Vector3 P in PlayerPoints) {
				if (Map.Collide(P, out int X, out int Y, out int Z)) {


					hasCollision = true;
					float distance = Vector3.Distance(Pos, P);

					// Keep track of closest collision  
					if (distance < HitDistance) {
						HitDistance = distance;
						HitNorm = Vector3.Normalize(Pos - P);
						HitPoint = P;
					}
				}
			}

			return hasCollision;
		}

		IEnumerable<Vector3> Phys_PlayerCollisionPointsImproved(Vector3 Pos, float Radius = 0.4f, float Height = 1.8f) {
			int RadialDivs = 12; // Increased from 6 for better precision  
			int HeightDivs = 4;  // Multiple height levels for better coverage  

			// Generate cylinder points at multiple heights  
			for (int h = 0; h < HeightDivs; h++) {
				float heightRatio = (float)h / (HeightDivs - 1);
				float currentHeight = -Height + (heightRatio * Height);

				// Generate radial points at this height  
				for (int i = 0; i < RadialDivs; i++) {
					float angle = (float)i / RadialDivs * 2.0f * MathF.PI;
					float x = MathF.Cos(angle) * Radius;
					float z = MathF.Sin(angle) * Radius;

					yield return Pos + new Vector3(x, currentHeight, z);
				}
			}

			// Add center points for better internal collision detection  
			for (int h = 0; h < HeightDivs; h++) {
				float heightRatio = (float)h / (HeightDivs - 1);
				float currentHeight = -Height + (heightRatio * Height);
				yield return Pos + new Vector3(0, currentHeight, 0);
			}

			// Add top and bottom cap points  
			yield return Pos + new Vector3(0, 0, 0);           // Top center  
			yield return Pos + new Vector3(0, -Height, 0);     // Bottom center  
		}


		bool HasBlocksInBounds(Vector3 min, Vector3 max) {
			// Quick check if there are any non-air blocks in the bounding box  
			for (int x = (int)min.X; x <= (int)max.X; x++) {
				for (int y = (int)min.Y; y <= (int)max.Y; y++) {
					for (int z = (int)min.Z; z <= (int)max.Z; z++) {
						if (Map.GetBlock(x, y, z) != BlockType.None) {
							return true;
						}
					}
				}
			}
			return false;
		}

		void ClampToZero(ref Vector3 Vec, float ClampHyst) {
			if (float.IsNaN(Vec.X))
				Vec.X = 0;
			if (float.IsNaN(Vec.Y))
				Vec.Y = 0;
			if (float.IsNaN(Vec.Z))
				Vec.Z = 0;

			Vec.X = ClampToZero(Vec.X, ClampHyst);
			Vec.Y = ClampToZero(Vec.Y, ClampHyst);
			Vec.Z = ClampToZero(Vec.Z, ClampHyst);
		}

		void UpdatePhysics(float Dt) {
			Ply.UpdatePhysics(Dt);

			if (!Utils.HasRecord())
				Utils.BeginRaycastRecord();

			// Physics constants - consolidated for better organization  
			var physicsConfig = new {
				ClampHyst = 0.02f,
				Gravity = 10.5f,
				MaxPlayerVelocity = 3.6f,
				MaxPlayerControllableVelocity = 4.0f,
				MaxPlayerFallVelocity = 10.0f,
				PlayerJumpVelocity = 5.2f,
				PlyMoveSen = 3.2f,
				PlayerHeight = 1.8f
			};

			// Velocity clamping  
			ClampToZero(ref PlyVelocity, physicsConfig.ClampHyst);

			float VelLen = PlyVelocity.Length();

			// Floor detection - simplified to single raycast  
			Vector3 TorsoPos = Ply.Position + new Vector3(0, -1.2f, 0);

			Vector3 HitFloor = Map.RaycastPos(TorsoPos, 0.6f + physicsConfig.ClampHyst, new Vector3(0, -1f, 0), out Vector3 Face1);
			bool HasHitFloor = HitFloor != Vector3.Zero && Face1.Y == 1;

			if (!HasHitFloor) {
				Vector3 TestPoint = TorsoPos - new Vector3(0, 0.6f, 0) + PlyVelocity * Dt;
				if (Map.Collide(TestPoint, new Vector3(0, -1, 0), out Vector3 PicNorm)) {
					HitFloor = TestPoint;

					if (PicNorm.Y > 0)
						HasHitFloor = true;
				}

			}


			// Floor hit events  
			if (HasHitFloor) {
				if (!WasLastLegsOnFloor) {
					WasLastLegsOnFloor = true;
					Ply.PhysicsHit(Ply.Position, VelLen, false, true, false, false);
				} else if (PlyVelocity.Length() >= (physicsConfig.MaxPlayerVelocity / 2)) {
					Ply.PhysicsHit(HitFloor, PlyVelocity.Length(), false, true, true, false);
				}
			} else {
				WasLastLegsOnFloor = false;
			}

			// Movement input handling  
			Vector3 MovementInput = HandleMovementInput(physicsConfig.PlyMoveSen, physicsConfig.PlayerJumpVelocity, ref HasHitFloor, HitFloor);
			bool IsBraking = MovementInput == Vector3.Zero;

			// Apply movement based on ground state  
			if (HasHitFloor) {
				if (Math.Abs(PlyVelocity.X) > MovementInput.X)
					PlyVelocity.X += MovementInput.X;
				else
					PlyVelocity.X = MovementInput.X;

				if (Math.Abs(PlyVelocity.Z) > MovementInput.Z)
					PlyVelocity.Z += MovementInput.Z;
				else
					PlyVelocity.Z = MovementInput.Z;

				if (MovementInput.Y > physicsConfig.ClampHyst) {
					if (PlyVelocity.Y < 0)
						PlyVelocity.Y = MovementInput.Y;
					else
						PlyVelocity.Y += MovementInput.Y;
					//HasHitFloor = false;
				}
			} else {
				PlyVelocity += new Vector3(MovementInput.X, 0, MovementInput.Z) * 0.1f;

				float MaxVel = physicsConfig.MaxPlayerControllableVelocity;
				PlyVelocity.X = Utils.Clamp(PlyVelocity.X, -MaxVel, MaxVel);
				PlyVelocity.Z = Utils.Clamp(PlyVelocity.Z, -MaxVel, MaxVel);

				/*if (PlyVelocity.Length() <= physicsConfig.MaxPlayerControllableVelocity) {
					PlyVelocity += new Vector3(MovementInput.X, 0, MovementInput.Z) * 0.1f;
				}*/
			}

			// Velocity processing and constraints  
			ProcessVelocityConstraints(physicsConfig, HasHitFloor, HitFloor, IsBraking, Dt);

			if (PlyVelocity != Vector3.Zero) {
				/*if (Phys_CollidePlayerSingle(Ply.Position, out Vector3 HitNrm, out Vector3 HitPoint, out float Dist)) {
					Ply.Position = Ply.Position - (HitNrm * Dist);
				}*/

				Vector3 NewPlyPos = Ply.Position + (PlyVelocity * Dt);
				Vector3 MoveDir = Vector3.Normalize(PlyVelocity);
				Vector3 NewPlyVelocity = PlyVelocity;

				if (Phys_CollidePlayer(NewPlyPos, MoveDir, out Vector3 HitNorm)) {
					// Project velocity onto collision surface  
					NewPlyVelocity = Utils.ProjectOnPlane(PlyVelocity, HitNorm, physicsConfig.ClampHyst);

					if (NewPlyVelocity == Vector3.Zero) {
						if (Phys_CollidePlayerSingle(NewPlyPos, out Vector3 HitNrm, out Vector3 HitPt, out float Dst)) {
							NewPlyVelocity = HitNrm * Dst;
						}
					}

					MoveDir = Vector3.Normalize(NewPlyVelocity);

					// Try to move with projected velocity  
					NewPlyPos = Ply.Position + (NewPlyVelocity * Dt);
					if (!Phys_CollidePlayer(NewPlyPos, MoveDir, out Vector3 HitNorm2)) {
						PlyVelocity = NewPlyVelocity;
						Ply.SetPosition(NewPlyPos);
					} else {
						// If still colliding, stop movement  
						PlyVelocity = Vector3.Zero;

						if (Phys_CollidePlayer(Ply.Position, MoveDir, out Vector3 _)) {
							PlyVelocity = Vector3.Zero;
							Ply.SetPosition(Ply.GetPreviousPosition());
						} else {
							PlyVelocity += HitNorm2;
							Ply.SetPosition(Ply.Position);
						}
					}
				} else {
					// No collision, move normally  
					if (Map.GetBlock(NewPlyPos) == BlockType.None) {
						Ply.SetPosition(NewPlyPos);
					} else {

						if (Phys_CollidePlayer(Ply.Position, Vector3.Zero, out Vector3 _)) {
							PlyVelocity = Vector3.Zero;
							Ply.SetPosition(Ply.GetPreviousPosition());
						} else {
							Ply.SetPosition(Ply.Position);
						}
					}
				}
			}

			Utils.EndRaycastRecord();
		}

		// TODO: Move out into a scalable in-game timer event or something
		Stopwatch JumpCounter = Stopwatch.StartNew();

		// Helper method for movement input  
		private Vector3 HandleMovementInput(float moveSensitivity, float jumpVelocity, ref bool hasHitFloor, Vector3 hitFloor) {
			Vector3 DesiredPos = Vector3.Zero;

			Vector3 Forward = FPSCamera.GetForward();
			Forward.Y = 0;
			Forward = Vector3.Normalize(Forward);

			Vector3 Left = FPSCamera.GetLeft();
			Vector3 Up = FPSCamera.GetUp();

			if (Raylib.IsKeyDown(KeyboardKey.W))
				DesiredPos += Forward * moveSensitivity;
			if (Raylib.IsKeyDown(KeyboardKey.S))
				DesiredPos -= Forward * moveSensitivity;
			if (Raylib.IsKeyDown(KeyboardKey.A))
				DesiredPos += Left * moveSensitivity;
			if (Raylib.IsKeyDown(KeyboardKey.D))
				DesiredPos -= Left * moveSensitivity;

			if (Raylib.IsKeyDown(KeyboardKey.Space) && hasHitFloor && JumpCounter.ElapsedMilliseconds > 500) {
				JumpCounter.Restart();

				//PlyVelocity += new Vector3(0, jumpVelocity, 0);
				DesiredPos += new Vector3(0, jumpVelocity, 0);
				Ply.PhysicsHit(hitFloor, PlyVelocity.Length(), false, false, false, true);
				//hasHitFloor = false;
			}

			if (Raylib.IsKeyDown(KeyboardKey.C))
				DesiredPos -= Up * moveSensitivity;

			return DesiredPos;
		}

		// Helper method for velocity constraints  
		private void ProcessVelocityConstraints(dynamic config, bool hasHitFloor, Vector3 hitFloor, bool isBraking, float dt) {
			Vector2 PlyVelocityH = new Vector2(PlyVelocity.X, PlyVelocity.Z);
			float VelH = PlyVelocityH.Length();
			float VelV = Math.Abs(PlyVelocity.Y);

			if (hasHitFloor) {
				if (VelH > config.MaxPlayerVelocity) {
					Vector2 NewHorizontal = Vector2.Normalize(PlyVelocityH) * config.MaxPlayerVelocity;
					PlyVelocity.X = NewHorizontal.X;
					PlyVelocity.Z = NewHorizontal.Y;
				}

				//PlyVelocity.Y = 0;

				if (isBraking)
					PlyVelocity = PlyVelocity * 0.6f;

				Ply.SetPosition(hitFloor + new Vector3(0, config.PlayerHeight, 0));
			} else {
				// Apply gravity  

				if (!hasHitFloor)
					PlyVelocity = PlyVelocity - new Vector3(0, config.Gravity * dt, 0);

				// Air resistance  
				float Factor = (float)Math.Pow(0.1f, dt);
				PlyVelocity.X = PlyVelocity.X * Factor;
				PlyVelocity.Z = PlyVelocity.Z * Factor;

				// Terminal velocity  
				if (VelV > config.MaxPlayerFallVelocity) {
					if (PlyVelocity.Y < 0) {
						PlyVelocity.Y = -config.MaxPlayerFallVelocity;
					} else {
						PlyVelocity.Y = config.MaxPlayerFallVelocity;
					}
				}
			}
		}

		public override void Tick() {
			if (Window.InMgr.IsInputPressed(InputKey.Esc)) {
				Window.SetState(Program.MainMenuState);
				return;
			}

			Map.Tick();
			Ply.Tick();

			if (Window.InMgr.IsInputPressed(InputKey.F5)) {
				Console.WriteLine("Saving map!");

				using (MemoryStream MS = new MemoryStream()) {
					Map.Write(MS);
					File.WriteAllBytes("map.bin", MS.ToArray());
				}

				Console.WriteLine("Done!");
			}

			bool Left = Window.InMgr.IsInputPressed(InputKey.Click_Left);
			bool Right = Window.InMgr.IsInputPressed(InputKey.Click_Right);
			bool Middle = Window.InMgr.IsInputPressed(InputKey.Click_Middle);
			const float MaxLen = 20;

			float Wheel = Window.InMgr.GetMouseWheel();

			if (Wheel >= 1) {
				Inventory.SelectNext();
			} else if (Wheel <= -1) {
				Inventory.SelectPrevious();
			}

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
							Map.SetBlock(X, Y, Z, PlayerSelectedBlockType);
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

			// Update physics goes here

			if (!Ply.CursorDisabled) {
				GUI.Tick();
			} else {
				if (Window.InMgr.IsInputPressed(InputKey.Q)) {
					Inventory.SelectPrevious();
				}

				if (Window.InMgr.IsInputPressed(InputKey.E)) {
					Vector3 Start = Ply.Position;
					Vector3 End = Map.RaycastPos(Start, 1.5f, FPSCamera.GetForward(), out Vector3 Face);

					if (Face.Y == 1)
						End.Y -= 0.001f;

					PlacedBlock Blk = Map.GetPlacedBlock((int)End.X, (int)End.Y, (int)End.Z, out Chunk Chk);

					float XU = (float)(End.X - Math.Floor(End.X));
					float YV = (float)(End.Z - Math.Floor(End.Z));

					//Blk.OnBlockActivate?.Invoke(Blk, End, new Vector2(XU, YV));

					if (Blk.Type == BlockType.CraftingTable) {
						Console.WriteLine("Craft! {0}, ({1}, {2})", Face, XU, YV);
						return;
					}

					Inventory.SelectNext();
				}

				if (Window.InMgr.IsInputPressed(InputKey.F)) {
					Console.WriteLine("Pew pew!");

					Vector3 Start = Ply.Position;
					//Vector3 End = Map.RaycastPos(Start, 10, FPSCamera.GetForward(), out Vector3 Face);

					/*Ray R = new Ray();
					R.Position = Start;
					R.Direction = FPSCamera.GetForward();
					RayCollision Col = Map.RaycastEnt(R);

					Vector3 End = Vector3.Zero;

					if (Col.Hit)
						End = Col.Point;

					if (End != Vector3.Zero) {
						MarkerList.Add(new Tuple<Vector3, Vector3>(Start, End));
					}*/

				}

				Inventory.Update();
			}

			UpdateGUI();
		}

		public override void UpdateLockstep(float TotalTime, float Dt) {
			//Map.UpdateLockstep(TotalTime, Dt);
			UpdatePhysics(Dt);
		}

		public override void Draw(float TimeAlpha) {
			//Raylib.EndBlendMode();

			Raylib.ClearBackground(new Color(200, 200, 200));
			Raylib.BeginMode3D(Ply.Cam);

			Draw3D(TimeAlpha);

			Raylib.EndMode3D();
		}

		void Draw3D(float TimeAlpha) {
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

			// Raylib.DrawTexture(ResMgr.AtlasTexture, 0, 0, Color.White);
			//Raylib.DrawTextureEx(ResMgr.AtlasTexture, Vector2.Zero, 0, 0.01f, Color.White);

			//Raylib.DrawCircle3D(new Vector3(0, 0, 0), 1, new Vector3(0, 1, 0), 0, Color.Pink);

			Utils.DrawRaycastRecord();
			//Raylib.DrawLine3D(Start, End, Color.White);

		}

		public override void Draw2D() {
			//Camera2D GUICam = new Camera2D();
			//Raylib.BeginMode2D(GUICam);
			GUI.Draw();

			Raylib.DrawCircleLines(Program.Window.Width / 2, Program.Window.Height / 2, 5, Color.White);

			//Raylib.EndMode2D();
			Raylib.DrawFPS(10, 10);
		}
	}
}
