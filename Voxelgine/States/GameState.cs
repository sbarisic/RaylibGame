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

			Ply = new Player(GUI, "snoutx10k", true, Snd);
			if (File.Exists("player.bin")) {
				using (FileStream fs = File.OpenRead("player.bin"))
				using (BinaryReader reader = new BinaryReader(fs)) {
					Ply.Read(reader);
				}
			} else {
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
                InfoLbl.WriteLine("Vel: {0:0.000}", MathF.Round(Ply.GetVelocity().Length(), 3));
                InfoLbl.WriteLine("No-clip: {0}", NoClip ? "ON" : "OFF");
                InfoLbl.WriteLine("OnGround: {0}", Ply.GetWasLastLegsOnFloor() ? "YES" : "NO");
            }
        }

		bool HasBlocksInBounds(Vector3 min, Vector3 max) {
			for (int x = (int)min.X; x <= (int)max.X; x++)
				for (int y = (int)min.Y; y <= (int)max.Y; y++)
					for (int z = (int)min.Z; z <= (int)max.Z; z++)
						if (Map.GetBlock(x, y, z) != BlockType.None)
							return true;
			return false;
		}

		private void SaveGameState() {
			Console.WriteLine("Saving map and player!");
			using (MemoryStream ms = new()) {
				Map.Write(ms);
				File.WriteAllBytes("map.bin", ms.ToArray());
			}
			using (FileStream fs = File.Open("player.bin", FileMode.Create, FileAccess.Write))
			using (BinaryWriter writer = new BinaryWriter(fs)) {
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
			Ply.UpdatePhysics(Map, PhysicsData, NoClip, Dt);
		}

		public override void Draw(float TimeAlpha) {
			Ply.UpdateFPSCamera();

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
