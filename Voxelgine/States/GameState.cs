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

namespace RaylibGame.States {
	public unsafe class GameState : GameStateImpl {
		public ChunkMap Map;
		public Player Ply;
		public SoundMgr Snd;

		List<Tuple<Vector3, Vector3>> MarkerList = new();
		GUIManager GUI;
		PhysData PhysicsData;

		EntityManager EntMgr;

		Frustum ViewFrustum;

		const string map_file = "data/map.bin";
		const string player_file = "data/player.bin";

		public GameState(GameWindow window) : base(window) {
			GUI = new GUIManager(window);
			EntMgr = new EntityManager();

			Snd = new SoundMgr();
			Snd.Init();
			PhysicsData = new PhysData();
			Map = new ChunkMap(this);

			VoxEntity BE = new VEntPickup();
			BE.SetPosition(new Vector3(37, 66, 15));
			BE.SetSize(new Vector3(1, 1, 1));
			BE.SetModel("orb_xp/orb_xp.obj");
			EntMgr.Spawn(this, BE);

			VoxEntity NPC = new VEntNPC();
			NPC.SetSize(new Vector3(0.9f, 1.8f, 0.9f));
			NPC.SetPosition(new Vector3(32, 66, 14));
			NPC.SetModel("npc/humanoid.json");
			EntMgr.Spawn(this, NPC);

			if (File.Exists(map_file)) {
				using FileStream FS = File.OpenRead(map_file);
				Map.Read(FS);
			} else {
				Map.GenerateFloatingIsland(64, 64);
			}

			Ply = new Player(GUI, "snoutx10k", true, Snd);
			Ply.InitGUI(window);
			Ply.Init(Map);

			if (File.Exists(player_file)) {
				using (FileStream fs = File.OpenRead(player_file))
				using (BinaryReader reader = new BinaryReader(fs)) {
					Ply.Read(reader);
				}
			} else {
				Ply.SetPosition(32, 73, 19);
			}

		}

		public override void OnResize(GameWindow Window) {
			base.OnResize(Window);
			Ply.RecalcGUI(Window);
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
				File.WriteAllBytes(map_file, ms.ToArray());
			}

			using (FileStream fs = File.Open(player_file, FileMode.Create, FileAccess.Write))
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
			Ply.Tick(Window.InMgr);
			Ply.TickGUI(Window.InMgr, Map);

			if (Window.InMgr.IsInputPressed(InputKey.F5)) {
				SaveGameState();
			}

			Ply.UpdateGUI();
		}

		public override void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr) {
			Ply.UpdatePhysics(Map, PhysicsData, Dt, InMgr);

			EntMgr.UpdateLockstep(TotalTime, Dt, InMgr);
		}

		public override void Draw(float TimeAlpha, ref GameFrameInfo LastFrame, ref GameFrameInfo FInfo) {
			Ply.UpdateFPSCamera(ref FInfo);

			Raylib.ClearBackground(new Color(200, 200, 200));
			Raylib.BeginMode3D(Ply.Cam);

			Shader DefaultShader = ResMgr.GetShader("default");
			Raylib.BeginShaderMode(DefaultShader);


			Draw3D(TimeAlpha, ref LastFrame, ref FInfo);

			Raylib.EndShaderMode();

			Raylib.EndMode3D();

			//FInfo.Cam = Ply.Cam;
			//FInfo.Pos = FPSCamera.Position;
		}

		public Vector3 PlayerCollisionBoxPos;

		void DrawTransparent() {
			Raylib.BeginBlendMode(BlendMode.Alpha);
			Rlgl.DisableDepthMask();

			Map.DrawTransparent(ref ViewFrustum);

			Rlgl.EnableDepthMask();
			Raylib.EndBlendMode();
		}

		void Draw3D(float TimeAlpha, ref GameFrameInfo LastFrame, ref GameFrameInfo CurFame) {
			if (!Ply.FreezeFrustum)
				ViewFrustum = new Frustum(ref Ply.Cam);

			Map.Draw(ref ViewFrustum);
			DrawTransparent();

			EntMgr.Draw3D(TimeAlpha, ref LastFrame);

			Ply.Draw(TimeAlpha, ref LastFrame, ref CurFame);

			if (Program.DebugMode)
				DrawPlayerCollisionBox(PlayerCollisionBoxPos);

			ViewFrustum.Draw();

			foreach (var L in MarkerList)
				Raylib.DrawLine3D(L.Item1, L.Item2, Color.Blue);
			Raylib.DrawLine3D(Vector3.Zero, new Vector3(100, 0, 0), Color.Red);
			Raylib.DrawLine3D(Vector3.Zero, new Vector3(0, 100, 0), Color.Green);
			Raylib.DrawLine3D(Vector3.Zero, new Vector3(0, 0, 100), Color.Blue);
			Utils.DrawRaycastRecord();
		}

		private void DrawPlayerCollisionBox(Vector3 feetPos) {
			float playerRadius = Player.PlayerRadius;
			float playerHeight = Player.PlayerHeight;

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
