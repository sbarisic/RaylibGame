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

		bool NoClip = false;

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
				Program.DebugMode = !Program.DebugMode;
			});

			Ply.AddOnKeyPressed(KeyboardKey.F4, () => {
				Console.WriteLine("Clearing records");
				Utils.ClearRaycastRecord();
			});

			/*Ply.AddOnKeyPressed(KeyboardKey.B, () => {
				if (Debugger.IsAttached)
					Debugger.Break();
			});*/

			Ply.SetPosition(32, 73, 19);

			Ply.AddOnKeyPressed(KeyboardKey.C, () => {
				NoClip = !NoClip;
				Console.WriteLine($"No-clip mode: {(NoClip ? "ON" : "OFF")}");
			});
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

			SetInvItem(Inventory, 5, BlockType.Glowstone, (ItmBox, Idx) => {
				PlayerSelectedBlockType = BlockType.Glowstone;
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
			InfoLbl.Enabled = false;

			if (Program.DebugMode) {
				InfoLbl.Enabled = true;
				InfoLbl.Clear();
				InfoLbl.WriteLine("Pos: {0:0.00}, {1:0.00}, {2:0.00}", MathF.Round(Ply.Position.X, 2), MathF.Round(Ply.Position.Y, 2), MathF.Round(Ply.Position.Z, 2));
				InfoLbl.WriteLine("Vel: {0:0.000}", MathF.Round(PlyVelocity.Length(), 3));
				InfoLbl.WriteLine("No-clip: {0}", NoClip ? "ON" : "OFF");
			}
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


		// Helper to get the player's feet position
		private Vector3 GetPlayerFeetPosition() {
			return Ply.FeetPosition;
		}

		bool Phys_CollidePlayer(Vector3 Pos, Vector3 ProbeDir, out Vector3 HitNorm) {
			Vector3 feetPos = Ply.FeetPosition;
			bool Res = Phys_CollidePlayerAdvanced(feetPos, ProbeDir, out HitNorm, out Vector3 _, out float _);

			if (!Res) {
				if (Phys_CollidePlayerSingle(feetPos, out HitNorm, out Vector3 _, out float _)) {
					Res = true;
				}
			}

			return Res;
		}

		bool Phys_CollidePlayerAdvanced(Vector3 Pos, Vector3 ProbeDir, out Vector3 HitNorm, out Vector3 HitPoint, out float HitDistance) {
			Vector3 feetPos = Ply.FeetPosition;
			HitNorm = Vector3.Zero;
			HitPoint = Vector3.Zero;
			HitDistance = float.MaxValue;

			Vector3[] PlayerPoints = Phys_PlayerCollisionPointsImproved(feetPos).ToArray();
			bool hasCollision = false;

			foreach (var P in PlayerPoints) {
				if (Map.Collide(P, ProbeDir, out Vector3 tempNorm)) {
					hasCollision = true;
					float distance = Vector3.Distance(feetPos, P);

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
			Vector3 feetPos = Ply.FeetPosition;
			HitDistance = float.MaxValue;
			HitPoint = Vector3.Zero;
			HitNorm = Vector3.Zero;

			Vector3[] PlayerPoints = Phys_PlayerCollisionPointsImproved(feetPos).ToArray();

			if (FeetOnly) {
				float MinY = PlayerPoints.Select(P => P.Y).Min();
				PlayerPoints = PlayerPoints.Where(PP => PP.Y == MinY).ToArray();
			}

			bool hasCollision = false;

			foreach (Vector3 P in PlayerPoints) {
				if (Map.Collide(P, out int X, out int Y, out int Z)) {
					hasCollision = true;
					float distance = Vector3.Distance(feetPos, P);

					if (distance < HitDistance) {
						HitDistance = distance;
						HitNorm = Vector3.Normalize(feetPos - P);
						HitPoint = P;
					}
				}
			}

			return hasCollision;
		}

		IEnumerable<Vector3> Phys_PlayerCollisionPointsImproved(Vector3 feetPos, float Radius = -1, float Height = -1) {
			if (Radius < 0)
				Radius = Player.PlayerRadius;
			if (Height < 0)
				Height = Player.PlayerHeight;
			int RadialDivs = 12;
			int HeightDivs = 4;

			for (int h = 0; h < HeightDivs; h++) {
				float heightRatio = (float)h / (HeightDivs - 1);
				float currentHeight = heightRatio * Height;

				for (int i = 0; i < RadialDivs; i++) {
					float angle = (float)i / RadialDivs * 2.0f * MathF.PI;
					float x = MathF.Cos(angle) * Radius;
					float z = MathF.Sin(angle) * Radius;
					yield return new Vector3(feetPos.X + x, feetPos.Y + currentHeight, feetPos.Z + z);
				}
			}

			for (int h = 0; h < HeightDivs; h++) {
				float heightRatio = (float)h / (HeightDivs - 1);
				float currentHeight = heightRatio * Height;
				yield return new Vector3(feetPos.X, feetPos.Y + currentHeight, feetPos.Z);
			}

			yield return new Vector3(feetPos.X, feetPos.Y + Height, feetPos.Z);
			yield return new Vector3(feetPos.X, feetPos.Y, feetPos.Z);
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

		// Quake/Half-Life 2 style slide-move and step-move collision
		// Attempts to move the player along velocity, sliding along walls and stepping up if possible
		private Vector3 QuakeMoveWithCollision(Vector3 pos, Vector3 velocity, float dt, float stepHeight = 0.5f, int maxSlides = 4) {
			float playerRadius = Player.PlayerRadius;
			float playerHeight = Player.PlayerHeight;
			Vector3 feetPos = Ply.FeetPosition;
			Vector3 move = velocity * dt;
			Vector3 outVel = velocity;
			for (int slide = 0; slide < maxSlides; slide++) {
				Vector3 tryPos = feetPos + move;
				if (!HasBlocksInBounds(
					tryPos - new Vector3(playerRadius, 0, playerRadius),
					tryPos + new Vector3(playerRadius, playerHeight, playerRadius))) {
					feetPos = tryPos;
					break;
				}
				Vector3 stepUp = feetPos + new Vector3(0, stepHeight, 0);
				Vector3 stepTry = stepUp + move;
				if (!HasBlocksInBounds(
					stepTry - new Vector3(playerRadius, 0, playerRadius),
					stepTry + new Vector3(playerRadius, playerHeight, playerRadius))) {
					feetPos = stepTry;
					break;
				}
				Vector3 tryX = new Vector3(feetPos.X + move.X, feetPos.Y, feetPos.Z);
				if (!HasBlocksInBounds(
					tryX - new Vector3(playerRadius, 0, playerRadius),
					tryX + new Vector3(playerRadius, playerHeight, playerRadius))) {
					feetPos = tryX;
					move.Z = 0;
					continue;
				}
				Vector3 tryZ = new Vector3(feetPos.X, feetPos.Y, feetPos.Z + move.Z);
				if (!HasBlocksInBounds(
					tryZ - new Vector3(playerRadius, 0, playerRadius),
					tryZ + new Vector3(playerRadius, playerHeight, playerRadius))) {
					feetPos = tryZ;
					move.X = 0;
					continue;
				}
				outVel = Vector3.Zero;
				break;
			}
			return feetPos + new Vector3(0, Player.PlayerEyeOffset, 0);
		}

		void UpdatePhysics(float Dt) {
			Ply.UpdatePhysics(Dt);

			// Quake/Half-Life 2 style movement constants
			float groundFriction = 8.0f;
			float groundAccel = 50.0f;
			float airFriction = 0.2f;
			float airAccel = 20.0f;
			float maxGroundSpeed = 4.4f;
			float maxAirSpeed = 5.4f;
			float jumpImpulse = 5.2f;
			float gravity = 10.5f;
			float playerHeight = Player.PlayerHeight;
			float playerRadius = Player.PlayerRadius;
			float clampHyst = 0.02f;
			float noClipMoveSpeed = 10.0f;
			float groundEpsilon = 0.02f; // Small offset to start ray inside player
			float groundCheckDist = 0.12f; // Small distance to check below feet

			if (NoClip) {
				// No-clip movement: ignore collisions and physics, move freely
				float moveSpeed = noClipMoveSpeed;
				Vector3 move = Vector3.Zero;
				Vector3 fwd = FPSCamera.GetForward();
				Vector3 lft = FPSCamera.GetLeft();
				Vector3 up = FPSCamera.GetUp();
				if (Raylib.IsKeyDown(KeyboardKey.W))
					move += fwd;
				if (Raylib.IsKeyDown(KeyboardKey.S))
					move -= fwd;
				if (Raylib.IsKeyDown(KeyboardKey.A))
					move += lft;
				if (Raylib.IsKeyDown(KeyboardKey.D))
					move -= lft;
				if (Raylib.IsKeyDown(KeyboardKey.Space))
					move += up;
				if (Raylib.IsKeyDown(KeyboardKey.LeftShift))
					move -= up;
				if (move != Vector3.Zero) {
					move = Vector3.Normalize(move) * moveSpeed * Dt;
					Ply.SetPosition(Ply.Position + move);
				}
				return;
			}

			if (!Utils.HasRecord())
				Utils.BeginRaycastRecord();

			ClampToZero(ref PlyVelocity, clampHyst);

			// Improved floor detection: check all four corners and center, from just above feet to just below
			Vector3 feetPos = Ply.FeetPosition;
			Vector3[] groundCheckPoints = new Vector3[] {
				new Vector3(feetPos.X - playerRadius, feetPos.Y + groundEpsilon, feetPos.Z - playerRadius),
				new Vector3(feetPos.X + playerRadius, feetPos.Y + groundEpsilon, feetPos.Z - playerRadius),
				new Vector3(feetPos.X - playerRadius, feetPos.Y + groundEpsilon, feetPos.Z + playerRadius),
				new Vector3(feetPos.X + playerRadius, feetPos.Y + groundEpsilon, feetPos.Z + playerRadius),
				feetPos + new Vector3(0, groundEpsilon, 0)
			};
			bool OnGround = false;
			Vector3 HitFloor = Vector3.Zero;
			Vector3 Face1 = Vector3.Zero;
			foreach (var pt in groundCheckPoints) {
				Vector3 localFace;
				Vector3 hit = Map.RaycastPos(pt, groundCheckDist, new Vector3(0, -1f, 0), out localFace);
				if (hit != Vector3.Zero && localFace.Y == 1) {
					OnGround = true;
					HitFloor = hit;
					Face1 = localFace;
					break;
				}
			}
			if (!OnGround) {
				// Try a secondary check with velocity factored in
				foreach (var pt in groundCheckPoints) {
					Vector3 TestPoint = pt + PlyVelocity * Dt;
					if (Map.Collide(TestPoint, new Vector3(0, -1, 0), out Vector3 PicNorm)) {
						if (PicNorm.Y > 0) {
							OnGround = true;
							HitFloor = TestPoint;
							Face1 = PicNorm;
							break;
						}
					}
				}
			}

			// Floor hit events
			float VelLen = PlyVelocity.Length();
			if (OnGround) {
				if (!WasLastLegsOnFloor) {
					WasLastLegsOnFloor = true;
					Ply.PhysicsHit(Ply.Position, VelLen, false, true, false, false);
				} else if (VelLen >= (maxGroundSpeed / 2)) {
					Ply.PhysicsHit(HitFloor, VelLen, false, true, true, false);
				}
			} else {
				WasLastLegsOnFloor = false;
			}

			// Get movement input (wishdir)
			Vector3 wishdir = Vector3.Zero;
			Vector3 fwd2 = FPSCamera.GetForward();
			fwd2.Y = 0;
			fwd2 = Vector3.Normalize(fwd2);
			Vector3 lft2 = FPSCamera.GetLeft();
			if (Raylib.IsKeyDown(KeyboardKey.W))
				wishdir += fwd2;
			if (Raylib.IsKeyDown(KeyboardKey.S))
				wishdir -= fwd2;
			if (Raylib.IsKeyDown(KeyboardKey.A))
				wishdir += lft2;
			if (Raylib.IsKeyDown(KeyboardKey.D))
				wishdir -= lft2;
			if (wishdir != Vector3.Zero)
				wishdir = Vector3.Normalize(wishdir);

			// Jump
			if (Raylib.IsKeyDown(KeyboardKey.Space) && OnGround && JumpCounter.ElapsedMilliseconds > 50) {
				JumpCounter.Restart();
				PlyVelocity.Y = jumpImpulse;
				Ply.PhysicsHit(HitFloor, VelLen, false, false, false, true);
				OnGround = false;
			}

			// Friction
			if (OnGround) {
				Vector2 velH = new Vector2(PlyVelocity.X, PlyVelocity.Z);
				float speed = velH.Length();
				if (speed > 0) {
					float drop = speed * groundFriction * Dt;
					float newSpeed = MathF.Max(speed - drop, 0);
					if (newSpeed != speed) {
						newSpeed /= speed;
						PlyVelocity.X *= newSpeed;
						PlyVelocity.Z *= newSpeed;
					}
				}
			} else {
				// Air friction (very low)
				PlyVelocity.X *= (1.0f - airFriction * Dt);
				PlyVelocity.Z *= (1.0f - airFriction * Dt);
			}

			// Acceleration
			if (wishdir != Vector3.Zero) {
				float curSpeed = PlyVelocity.X * wishdir.X + PlyVelocity.Z * wishdir.Z;
				float addSpeed, accel;
				if (OnGround) {
					addSpeed = maxGroundSpeed - curSpeed;
					accel = groundAccel;
				} else {
					addSpeed = maxAirSpeed - curSpeed;
					accel = airAccel;
				}
				if (addSpeed > 0) {
					float accelSpeed = accel * Dt * maxGroundSpeed;
					if (accelSpeed > addSpeed)
						accelSpeed = addSpeed;
					PlyVelocity.X += accelSpeed * wishdir.X;
					PlyVelocity.Z += accelSpeed * wishdir.Z;
				}
			}

			// Gravity
			if (!OnGround) {
				PlyVelocity.Y -= gravity * Dt;
			} else if (PlyVelocity.Y < 0) {
				PlyVelocity.Y = 0;
			}

			// Cap horizontal speed
			Vector2 horizVel = new Vector2(PlyVelocity.X, PlyVelocity.Z);
			float horizSpeed = horizVel.Length();
			float maxSpeed = OnGround ? maxGroundSpeed : maxAirSpeed;
			if (horizSpeed > maxSpeed) {
				float scale = maxSpeed / horizSpeed;
				PlyVelocity.X *= scale;
				PlyVelocity.Z *= scale;
			}

			// Move and collide using Quake-style slide/step
			if (PlyVelocity != Vector3.Zero) {
				Vector3 newPos = QuakeMoveWithCollision(Ply.Position, PlyVelocity, Dt);
				if (newPos != Ply.Position)
					Ply.SetPosition(newPos);
				else
					PlyVelocity = Vector3.Zero;
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

			if (Program.DebugMode) {
				DrawPlayerCollisionBox();
			}

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

		// Draw the player's collision bounding box for debugging
		private void DrawPlayerCollisionBox() {
			float playerRadius = Player.PlayerRadius;
			float playerHeight = Player.PlayerHeight;
			Vector3 feetPos = Ply.FeetPosition;
			Vector3 min = new Vector3(feetPos.X - playerRadius, feetPos.Y, feetPos.Z - playerRadius);
			Vector3 max = new Vector3(feetPos.X + playerRadius, feetPos.Y + playerHeight, feetPos.Z + playerRadius);
			Color color = Color.Red;
			Vector3[] corners = new Vector3[8];
			corners[0] = new Vector3(min.X, min.Y, min.Z);
			corners[1] = new Vector3(max.X, min.Y, min.Z);
			corners[2] = new Vector3(max.X, min.Y, max.Z);
			corners[3] = new Vector3(min.X, min.Y, max.Z);
			corners[4] = new Vector3(min.X, max.Y, min.Z);
			corners[5] = new Vector3(max.X, max.Y, min.Z);
			corners[6] = new Vector3(max.X, max.Y, max.Z);
			corners[7] = new Vector3(min.X, max.Y, max.Z);
			Raylib.DrawLine3D(corners[0], corners[1], color);
			Raylib.DrawLine3D(corners[1], corners[2], color);
			Raylib.DrawLine3D(corners[2], corners[3], color);
			Raylib.DrawLine3D(corners[3], corners[0], color);
			Raylib.DrawLine3D(corners[4], corners[5], color);
			Raylib.DrawLine3D(corners[5], corners[6], color);
			Raylib.DrawLine3D(corners[6], corners[7], color);
			Raylib.DrawLine3D(corners[7], corners[4], color);
			Raylib.DrawLine3D(corners[0], corners[4], color);
			Raylib.DrawLine3D(corners[1], corners[5], color);
			Raylib.DrawLine3D(corners[2], corners[6], color);
			Raylib.DrawLine3D(corners[3], corners[7], color);
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
