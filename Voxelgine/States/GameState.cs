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
		private const string MAP_FILE = "data/map.bin";
		private const string PLAYER_FILE = "data/player.bin";
		private const int ISLAND_SIZE = 64;

		Vector3 PickupPos = new Vector3(37, 66, 15);
		Vector3 NPCPos = new Vector3(32, 66, 14);
		Vector3 PlayerPos = new Vector3(32, 73, 19);

		public ChunkMap Map {
			get; private set;
		}

		public Player Ply {
			get; private set;
		}

		public SoundMgr Snd {
			get; private set;
		}

		public ParticleSystem Particle {
			get; private set;
		}

		public Vector3 PlayerCollisionBoxPos;

		private readonly List<Tuple<Vector3, Vector3>> MarkerList = new();
		private readonly GUIManager GUI;
		private readonly PhysData PhysicsData;
		private readonly EntityManager EntMgr;
		private Frustum ViewFrustum;

		public GameState(GameWindow window) : base(window) {
			GUI = new GUIManager(window);
			EntMgr = new EntityManager();
			Snd = new SoundMgr();
			PhysicsData = new PhysData();

			// =========================================== Init Systems ==============================================
			Snd.Init();
			Map = new ChunkMap(this);

			Particle = new ParticleSystem();
			Particle.Init((Pt) => Map.Collide(Pt, Vector3.Zero, out Vector3 _));

			// =========================================== Create entities ===========================================
			// Create pickup entity
			VEntPickup pickup = new VEntPickup();
			pickup.SetPosition(PickupPos);
			pickup.SetSize(Vector3.One);
			pickup.SetModel("orb_xp/orb_xp.obj");
			EntMgr.Spawn(this, pickup);

			// Create NPC entity  
			VEntNPC npc = new VEntNPC();
			npc.SetSize(new Vector3(0.9f, 1.8f, 0.9f));
			npc.SetPosition(NPCPos);
			npc.SetModel("npc/humanoid.json");
			EntMgr.Spawn(this, npc);

			// ======================================= Create rest of the world ======================================
			if (File.Exists(MAP_FILE)) {
				using var fileStream = File.OpenRead(MAP_FILE);
				Map.Read(fileStream);
			} else {
				Map.GenerateFloatingIsland(ISLAND_SIZE, ISLAND_SIZE);
			}


			// ====================================== Init player ====================================================
			Ply = new Player(GUI, "snoutx10k", true, Snd);
			Ply.InitGUI(window);
			Ply.Init(Map);

			if (File.Exists(PLAYER_FILE)) {
				using var fileStream = File.OpenRead(PLAYER_FILE);
				using var reader = new BinaryReader(fileStream);
				Ply.Read(reader);
			} else {
				Ply.SetPosition(PlayerPos);
			}
		}


		public override void SwapTo() {
			base.SwapTo();
			Raylib.DisableCursor();
		}

		public override void OnResize(GameWindow Window) {
			base.OnResize(Window);
			Ply.RecalcGUI(Window);
		}

		public override void Tick(float GameTime) {
			Particle.Tick(GameTime);

			// Handle input
			if (Window.InMgr.IsInputPressed(InputKey.Esc)) {
				Window.SetState(Program.MainMenuState);
				return;
			}

			if (Window.InMgr.IsInputPressed(InputKey.F5)) {
				SaveGameState();
			}

			// Update game systems
			Map.Tick();
			Ply.Tick(Window.InMgr);
			Ply.TickGUI(Window.InMgr, Map);
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

			Shader defaultShader = ResMgr.GetShader("default");
			Raylib.BeginShaderMode(defaultShader);

			Draw3D(TimeAlpha, ref LastFrame, ref FInfo);

			Raylib.EndShaderMode();
			Raylib.EndMode3D();
		}

		public override void Draw2D() {
			GUI.Draw();
			Raylib.DrawCircleLines(Program.Window.Width / 2, Program.Window.Height / 2, 5, Color.White);
			Raylib.DrawFPS(10, 10);
		}

		private void Draw3D(float TimeAlpha, ref GameFrameInfo LastFrame, ref GameFrameInfo CurrentFrame) {
			if (!Ply.FreezeFrustum)
				ViewFrustum = new Frustum(ref Ply.Cam);

			// Draw world geometry
			Map.Draw(ref ViewFrustum);
			DrawTransparent();

			// Draw entities and effects
			EntMgr.Draw3D(TimeAlpha, ref LastFrame);
			Particle.Draw(Ply, ref ViewFrustum);
			Ply.Draw(TimeAlpha, ref LastFrame, ref CurrentFrame);

			// Draw debug information
			if (Program.DebugMode) {
				DrawPlayerCollisionBox(PlayerCollisionBoxPos);
				ViewFrustum.Draw();
				DrawDebugLines();
				Utils.DrawRaycastRecord();
			}
		}

		private void DrawTransparent() {
			Raylib.BeginBlendMode(BlendMode.Alpha);
			Rlgl.DisableDepthMask();

			Map.DrawTransparent(ref ViewFrustum);

			Rlgl.EnableDepthMask();
			Raylib.EndBlendMode();
		}

		private void DrawDebugLines() {
			// Draw marker lines
			foreach (var marker in MarkerList)
				Raylib.DrawLine3D(marker.Item1, marker.Item2, Color.Blue);

			// Draw world axes
			Vector3 origin = Vector3.Zero;
			Raylib.DrawLine3D(origin, new Vector3(100, 0, 0), Color.Red);   // X-axis
			Raylib.DrawLine3D(origin, new Vector3(0, 100, 0), Color.Green); // Y-axis  
			Raylib.DrawLine3D(origin, new Vector3(0, 0, 100), Color.Blue);  // Z-axis
		}

		private void DrawPlayerCollisionBox(Vector3 feetPos) {
			Vector3 min = new Vector3(feetPos.X - Player.PlayerRadius, feetPos.Y, feetPos.Z - Player.PlayerRadius);
			Vector3 max = new Vector3(feetPos.X + Player.PlayerRadius, feetPos.Y + Player.PlayerHeight, feetPos.Z + Player.PlayerRadius);

			DrawWireframeCube(min, max, Color.Red);
		}

		private static void DrawWireframeCube(Vector3 min, Vector3 max, Color color) {
			// Bottom face
			Raylib.DrawLine3D(new Vector3(min.X, min.Y, min.Z), new Vector3(max.X, min.Y, min.Z), color);
			Raylib.DrawLine3D(new Vector3(max.X, min.Y, min.Z), new Vector3(max.X, min.Y, max.Z), color);
			Raylib.DrawLine3D(new Vector3(max.X, min.Y, max.Z), new Vector3(min.X, min.Y, max.Z), color);
			Raylib.DrawLine3D(new Vector3(min.X, min.Y, max.Z), new Vector3(min.X, min.Y, min.Z), color);

			// Top face
			Raylib.DrawLine3D(new Vector3(min.X, max.Y, min.Z), new Vector3(max.X, max.Y, min.Z), color);
			Raylib.DrawLine3D(new Vector3(max.X, max.Y, min.Z), new Vector3(max.X, max.Y, max.Z), color);
			Raylib.DrawLine3D(new Vector3(max.X, max.Y, max.Z), new Vector3(min.X, max.Y, max.Z), color);
			Raylib.DrawLine3D(new Vector3(min.X, max.Y, max.Z), new Vector3(min.X, max.Y, min.Z), color);

			// Vertical edges
			Raylib.DrawLine3D(new Vector3(min.X, min.Y, min.Z), new Vector3(min.X, max.Y, min.Z), color);
			Raylib.DrawLine3D(new Vector3(max.X, min.Y, min.Z), new Vector3(max.X, max.Y, min.Z), color);
			Raylib.DrawLine3D(new Vector3(max.X, min.Y, max.Z), new Vector3(max.X, max.Y, max.Z), color);
			Raylib.DrawLine3D(new Vector3(min.X, min.Y, max.Z), new Vector3(min.X, max.Y, max.Z), color);
		}

		private void SaveGameState() {
			Console.WriteLine("Saving map and player!");

			// Save map
			using MemoryStream memoryStream = new MemoryStream();
			Map.Write(memoryStream);
			File.WriteAllBytes(MAP_FILE, memoryStream.ToArray());

			// Save player
			using FileStream fileStream = File.Open(PLAYER_FILE, FileMode.Create, FileAccess.Write);
			using BinaryWriter writer = new BinaryWriter(fileStream);
			Ply.Write(writer);

			Console.WriteLine("Save complete!");
		}
	}
}
