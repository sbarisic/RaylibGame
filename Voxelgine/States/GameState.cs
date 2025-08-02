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
using Voxelgine.GUI;
using Voxelgine.Engine.Physics;

namespace RaylibGame.States {
	unsafe class GameState : GameStateImpl {
		public ChunkMap Map;
		public Player Ply;
		public SoundMgr Snd;

		List<Tuple<Vector3, Vector3>> MarkerList = new();
		GUIManager GUI;
		GUIInventory Inventory;
		PhysData PhysicsData;
		bool NoClip = false;

		public GameState(GameWindow window) : base(window) {
			GUI = new GUIManager(window);
			InitGUI();
			Snd = new SoundMgr();
			Snd.Init();
			PhysicsData = new PhysData();
			Map = new ChunkMap(this);

			if (File.Exists("map.bin")) {
				using FileStream FS = File.OpenRead("map.bin");
				Map.Read(FS);
			} else {
				Map.GenerateFloatingIsland(64, 64);
			}

			Ply = new Player("snoutx10k", true, Snd);
			if (File.Exists("player.bin"))
			{
				using (FileStream fs = File.OpenRead("player.bin"))
				using (BinaryReader reader = new BinaryReader(fs))
				{
					Ply.Read(reader);
				}
			}
			else
			{
				Ply.SetPosition(32, 73, 19);
			}
			Stopwatch SWatch = Stopwatch.StartNew();

			Ply.AddOnKeyPressed(KeyboardKey.F2, () => {
				Console.WriteLine("Compute light!");
				SWatch.Restart();
				Map.ComputeLighting();
				SWatch.Stop();
				Console.Title = $"> {SWatch.ElapsedMilliseconds / 1000.0f} s";
			});

			Ply.AddOnKeyPressed(KeyboardKey.F3, () => { Program.DebugMode = !Program.DebugMode; });

			Ply.AddOnKeyPressed(KeyboardKey.F4, () => { Console.WriteLine("Clearing records"); Utils.ClearRaycastRecord(); });

			Ply.AddOnKeyPressed(KeyboardKey.C, () => {
				NoClip = !NoClip;
				Console.WriteLine($"No-clip mode: {(NoClip ? "ON" : "OFF")}");
			});
		}

		GUIItemBox Box_Health;
		GUILabel InfoLbl;
		BlockType PlayerSelectedBlockType;
		void InitGUI() {
			Box_Health = new GUIItemBox(GUI);
			Box_Health.Pos = new Vector2(64, Window.Height - 128);
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

			SetInvItem(Inventory, 0, BlockType.Dirt, (ItmBox, Idx) => PlayerSelectedBlockType = BlockType.Dirt);
			SetInvItem(Inventory, 1, BlockType.Stone, (ItmBox, Idx) => PlayerSelectedBlockType = BlockType.Stone);
			SetInvItem(Inventory, 2, BlockType.StoneBrick, (ItmBox, Idx) => PlayerSelectedBlockType = BlockType.StoneBrick);
			SetInvItem(Inventory, 3, BlockType.Bricks, (ItmBox, Idx) => PlayerSelectedBlockType = BlockType.Bricks);
			SetInvItem(Inventory, 4, BlockType.Plank, (ItmBox, Idx) => PlayerSelectedBlockType = BlockType.Plank);
			SetInvItem(Inventory, 5, BlockType.CraftingTable, (ItmBox, Idx) => PlayerSelectedBlockType = BlockType.CraftingTable);
			SetInvItem(Inventory, 6, BlockType.Glowstone, (ItmBox, Idx) => PlayerSelectedBlockType = BlockType.Glowstone);
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
			for (int x = (int)min.X; x <= (int)max.X; x++)
				for (int y = (int)min.Y; y <= (int)max.Y; y++)
					for (int z = (int)min.Z; z <= (int)max.Z; z++)
						if (Map.GetBlock(x, y, z) != BlockType.None)
							return true;
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

		private Vector3 QuakeMoveWithCollision(Vector3 pos, Vector3 velocity, float dt, float stepHeight = 0.5f, int maxSlides = 4) {
			float playerRadius = Player.PlayerRadius;
			float playerHeight = Player.PlayerHeight;
			Vector3 feetPos = Ply.FeetPosition;
			Vector3 move = velocity * dt;
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
				break;
			}
			return feetPos + new Vector3(0, Player.PlayerEyeOffset, 0);
		}

		void UpdatePhysics(float Dt) {
			Ply.UpdatePhysics(Dt);
			float playerHeight = Player.PlayerHeight;
			float playerRadius = Player.PlayerRadius;
			if (NoClip) {
				Vector3 move = Vector3.Zero;
				Vector3 fwd = Ply.GetForward();
				Vector3 lft = Ply.GetLeft();
				Vector3 up = Ply.GetUp();
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
					move = Vector3.Normalize(move) * PhysicsData.NoClipMoveSpeed * Dt;
					Ply.SetPosition(Ply.Position + move);
				}
				return;
			}
			if (!Utils.HasRecord())
				Utils.BeginRaycastRecord();
			ClampToZero(ref PlyVelocity, PhysicsData.ClampHyst);
			Vector3 feetPos = Ply.FeetPosition;
			Vector3[] groundCheckPoints = new Vector3[] {
				new Vector3(feetPos.X - playerRadius, feetPos.Y + PhysicsData.GroundEpsilon, feetPos.Z - playerRadius),
				new Vector3(feetPos.X + playerRadius, feetPos.Y + PhysicsData.GroundEpsilon, feetPos.Z - playerRadius),
				new Vector3(feetPos.X - playerRadius, feetPos.Y + PhysicsData.GroundEpsilon, feetPos.Z + playerRadius),
				new Vector3(feetPos.X + playerRadius, feetPos.Y + PhysicsData.GroundEpsilon, feetPos.Z + playerRadius),
				feetPos + new Vector3(0, PhysicsData.GroundEpsilon, 0)
			};
			bool OnGround = false;
			Vector3 HitFloor = Vector3.Zero;
			foreach (var pt in groundCheckPoints) {
				Vector3 localFace;
				Vector3 hit = Map.RaycastPos(pt, PhysicsData.GroundCheckDist, new Vector3(0, -1f, 0), out localFace);
				if (hit != Vector3.Zero && localFace.Y == 1) {
					OnGround = true;
					HitFloor = hit;
					break;
				}
			}
			if (!OnGround) {
				foreach (var pt in groundCheckPoints) {
					Vector3 TestPoint = pt + PlyVelocity * Dt;
					if (Map.Collide(TestPoint, new Vector3(0, -1, 0), out Vector3 PicNorm)) {
						if (PicNorm.Y > 0) {
							OnGround = true;
							HitFloor = TestPoint;
							break;
						}
					}
				}
			}
			Vector3 wishdir = Vector3.Zero;
			Vector3 fwd2 = Ply.GetForward();
			fwd2.Y = 0;
			fwd2 = Vector3.Normalize(fwd2);
			Vector3 lft2 = Ply.GetLeft();
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
			bool ledgeSafety = OnGround && Raylib.IsKeyDown(KeyboardKey.LeftShift);
			if (ledgeSafety && wishdir != Vector3.Zero) {
				float innerRadius = 0.4f;
				var points = Phys_PlayerCollisionPointsImproved(feetPos, innerRadius, Player.PlayerHeight).ToArray();
				float minY = points.Min(p => p.Y);
				var feetPoints = points.Where(p => Math.Abs(p.Y - minY) < 0.01f).ToArray();
				List<Vector3> supportedPoints = new();
				foreach (var pt in feetPoints) {
					Vector3 groundCheck = pt + new Vector3(0, -0.15f, 0);
					if (Map.GetBlock((int)MathF.Floor(groundCheck.X), (int)MathF.Floor(groundCheck.Y), (int)MathF.Floor(groundCheck.Z)) != BlockType.None) {
						supportedPoints.Add(pt);
					}
				}
				if (supportedPoints.Count == 0) {
					PlyVelocity.X = 0;
					PlyVelocity.Z = 0;
					wishdir = Vector3.Zero;
				} else {
					bool allow = false;
					foreach (var spt in supportedPoints) {
						Vector3 toSupport = Vector3.Normalize(spt - feetPos);
						if (Vector3.Dot(wishdir, toSupport) > 0) {
							allow = true;
							break;
						}
					}
					if (!allow) {
						PlyVelocity.X = 0;
						PlyVelocity.Z = 0;
						wishdir = Vector3.Zero;
					}
				}
			}
			float VelLen = PlyVelocity.Length();
			if (OnGround) {
				if (!WasLastLegsOnFloor) {
					WasLastLegsOnFloor = true;
					Ply.PhysicsHit(Ply.Position, VelLen, false, true, false, false);
				} else if (VelLen >= (PhysicsData.MaxGroundSpeed / 2)) {
					Ply.PhysicsHit(HitFloor, VelLen, false, true, true, false);
				}
			} else {
				WasLastLegsOnFloor = false;
			}
			if (Raylib.IsKeyDown(KeyboardKey.Space) && OnGround && JumpCounter.ElapsedMilliseconds > 50) {
				JumpCounter.Restart();
				PlyVelocity.Y = PhysicsData.JumpImpulse;
				Ply.PhysicsHit(HitFloor, VelLen, false, false, false, true);
				OnGround = false;
			}
			if (OnGround) {
				Vector2 velH = new Vector2(PlyVelocity.X, PlyVelocity.Z);
				float speed = velH.Length();
				if (speed > 0) {
					float drop = speed * PhysicsData.GroundFriction * Dt;
					float newSpeed = MathF.Max(speed - drop, 0);
					if (newSpeed != speed) {
						newSpeed /= speed;
						PlyVelocity.X *= newSpeed;
						PlyVelocity.Z *= newSpeed;
					}
				}
			} else {
				PlyVelocity.X *= (1.0f - PhysicsData.AirFriction * Dt);
				PlyVelocity.Z *= (1.0f - PhysicsData.AirFriction * Dt);
			}
			if (wishdir != Vector3.Zero) {
				float curSpeed = PlyVelocity.X * wishdir.X + PlyVelocity.Z * wishdir.Z;
				float addSpeed, accel;
				float maxGroundSpeed = Raylib.IsKeyDown(KeyboardKey.LeftShift) ? PhysicsData.MaxWalkSpeed : PhysicsData.MaxGroundSpeed;
				if (OnGround) {
					addSpeed = maxGroundSpeed - curSpeed;
					accel = PhysicsData.GroundAccel;
				} else {
					addSpeed = PhysicsData.MaxAirSpeed - curSpeed;
					accel = PhysicsData.AirAccel;
				}
				if (addSpeed > 0) {
					float accelSpeed = accel * Dt * maxGroundSpeed;
					if (accelSpeed > addSpeed)
						accelSpeed = addSpeed;
					PlyVelocity.X += accelSpeed * wishdir.X;
					PlyVelocity.Z += accelSpeed * wishdir.Z;
				}
			}
			if (!OnGround) {
				PlyVelocity.Y -= PhysicsData.Gravity * Dt;
			} else if (PlyVelocity.Y < 0) {
				PlyVelocity.Y = 0;
			}
			if (PlyVelocity.Y > 0) {
				float headEpsilon = 0.02f;
				Vector3 headPos = feetPos + new Vector3(0, playerHeight - headEpsilon, 0);
				Vector3[] headCheckPoints = new Vector3[] {
					new Vector3(headPos.X - playerRadius, headPos.Y, headPos.Z - playerRadius),
					new Vector3(headPos.X + playerRadius, headPos.Y, headPos.Z - playerRadius),
					new Vector3(headPos.X - playerRadius, headPos.Y, headPos.Z + playerRadius),
					new Vector3(headPos.X + playerRadius, headPos.Y, headPos.Z + playerRadius),
					headPos
				};
				foreach (var pt in headCheckPoints) {
					if (Map.GetBlock((int)MathF.Floor(pt.X), (int)MathF.Floor(pt.Y + 0.1f), (int)MathF.Floor(pt.Z)) != BlockType.None) {
						PlyVelocity.Y = 0;
						break;
					}
				}
			}
			Vector2 horizVel = new Vector2(PlyVelocity.X, PlyVelocity.Z);
			float horizSpeed = horizVel.Length();
			float maxSpeed = OnGround ? (Raylib.IsKeyDown(KeyboardKey.LeftShift) ? PhysicsData.MaxWalkSpeed : PhysicsData.MaxGroundSpeed) : PhysicsData.MaxAirSpeed;
			if (horizSpeed > maxSpeed) {
				float scale = maxSpeed / horizSpeed;
				PlyVelocity.X *= scale;
				PlyVelocity.Z *= scale;
			}
			if (PlyVelocity.Y > 0) {
				float headEpsilon = 0.02f;
				Vector3 feetPos2 = Ply.FeetPosition;
				Vector3 headPos = feetPos2 + new Vector3(0, playerHeight - headEpsilon, 0);
				Vector3[] headCheckPoints = new Vector3[] {
					new Vector3(headPos.X - playerRadius, headPos.Y, headPos.Z - playerRadius),
					new Vector3(headPos.X + playerRadius, headPos.Y, headPos.Z - playerRadius),
					new Vector3(headPos.X - playerRadius, headPos.Y, headPos.Z + playerRadius),
					new Vector3(headPos.X + playerRadius, headPos.Y, headPos.Z + playerRadius),
					headPos
				};
				foreach (var pt in headCheckPoints) {
					if (Map.GetBlock((int)MathF.Floor(pt.X), (int)MathF.Floor(pt.Y + 0.1f), (int)MathF.Floor(pt.Z)) != BlockType.None) {
						PlyVelocity.Y = 0;
						break;
					}
				}
			}
			Vector3 newPos = QuakeMoveWithCollision(Ply.Position, PlyVelocity, Dt);
			if (newPos != Ply.Position)
				Ply.SetPosition(newPos);
			else
				PlyVelocity = Vector3.Zero;
			Utils.EndRaycastRecord();
		}

		Stopwatch JumpCounter = Stopwatch.StartNew();

		private void SaveGameState()
		{
			Console.WriteLine("Saving map and player!");
			using (MemoryStream ms = new())
			{
				Map.Write(ms);
				File.WriteAllBytes("map.bin", ms.ToArray());
			}
			using (FileStream fs = File.Open("player.bin", FileMode.Create, FileAccess.Write))
			using (BinaryWriter writer = new BinaryWriter(fs))
			{
				Ply.Write(writer);
			}
			Console.WriteLine("Done!");
		}

		public override void Tick() {
			if (Window.InMgr.IsInputPressed(InputKey.Esc)) {
				Window.SetState(Program.MainMenuState);
				return;
			}
			Map.Tick();
			Ply.Tick();
			if (Window.InMgr.IsInputPressed(InputKey.F5)) {
				SaveGameState();
			}
			bool Left = Window.InMgr.IsInputPressed(InputKey.Click_Left);
			bool Right = Window.InMgr.IsInputPressed(InputKey.Click_Right);
			bool Middle = Window.InMgr.IsInputPressed(InputKey.Click_Middle);
			const float MaxLen = 20;
			float Wheel = Window.InMgr.GetMouseWheel();
			if (Wheel >= 1)
				Inventory.SelectNext();
			else if (Wheel <= -1)
				Inventory.SelectPrevious();
			if ((Left || Right || Middle) && Ply.CursorDisabled) {
				Vector3 Dir = Ply.GetForward();
				Vector3 Start = Ply.Position;
				if (Left) {
					Utils.Raycast(Start, Dir, MaxLen, (X, Y, Z, Face) => {
						if (Map.GetBlock(X, Y, Z) != BlockType.None) {
							Snd.PlayCombo("block_break", Start, Dir, new Vector3(X, Y, Z));
							Map.SetBlock(X, Y, Z, BlockType.None);
							return true;
						}
						return false;
					});
				}
				if (Right) {
					Utils.Raycast(Start, Dir, MaxLen, (X, Y, Z, Face) => {
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
			if (!Ply.CursorDisabled) {
				GUI.Tick();
			} else {
				if (Window.InMgr.IsInputPressed(InputKey.Q))
					Inventory.SelectPrevious();
				if (Window.InMgr.IsInputPressed(InputKey.E)) {
					Vector3 Start = Ply.Position;
					Vector3 End = Map.RaycastPos(Start, 1.5f, Ply.GetForward(), out Vector3 Face);
					if (Face.Y == 1)
						End.Y -= 0.001f;
					PlacedBlock Blk = Map.GetPlacedBlock((int)End.X, (int)End.Y, (int)End.Z, out Chunk Chk);
					if (Blk.Type == BlockType.CraftingTable) {
						Console.WriteLine($"Craft! {Face}, ({End.X - Math.Floor(End.X)}, {End.Z - Math.Floor(End.Z)})");
						return;
					}
					Inventory.SelectNext();
				}
				Inventory.Update();
			}
			UpdateGUI();
		}

		public override void UpdateLockstep(float TotalTime, float Dt) {
			UpdatePhysics(Dt);
		}

		public override void Draw(float TimeAlpha) {
			Raylib.ClearBackground(new Color(200, 200, 200));
			Raylib.BeginMode3D(Ply.Cam);
			Draw3D(TimeAlpha);
			Raylib.EndMode3D();
		}

		void Draw3D(float TimeAlpha) {
			Map.Draw();
			Map.DrawTransparent();
			Ply.Draw();
			if (Program.DebugMode)
				DrawPlayerCollisionBox();
			foreach (var L in MarkerList)
				Raylib.DrawLine3D(L.Item1, L.Item2, Color.Blue);
			Raylib.DrawLine3D(Vector3.Zero, new Vector3(100, 0, 0), Color.Red);
			Raylib.DrawLine3D(Vector3.Zero, new Vector3(0, 100, 0), Color.Green);
			Raylib.DrawLine3D(Vector3.Zero, new Vector3(0, 0, 100), Color.Blue);
			Utils.DrawRaycastRecord();
		}

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
			GUI.Draw();
			Raylib.DrawCircleLines(Program.Window.Width / 2, Program.Window.Height / 2, 5, Color.White);
			Raylib.DrawFPS(10, 10);
		}
	}
}
