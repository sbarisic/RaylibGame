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

namespace RaylibGame.States
{
	public unsafe class GameState : GameStateImpl
	{
		private const string MAP_FILE = "data/map.bin";
		private const string PLAYER_FILE = "data/player.bin";
		private const int ISLAND_SIZE = 64;

		Vector3 PickupPos = new Vector3(37, 66, 15);
		Vector3 NPCPos = new Vector3(32, 66, 14);
		Vector3 PlayerPos = new Vector3(32, 73, 19);

		public ChunkMap Map
		{
			get; private set;
		}

		public Player Ply
		{
			get; private set;
		}

		public SoundMgr Snd
		{
			get; private set;
		}

		public ParticleSystem Particle
		{
			get; private set;
		}

		public Vector3 PlayerCollisionBoxPos;

		private readonly List<Tuple<Vector3, Vector3>> MarkerList = new();
		private readonly FishUIManager GUI;
		private readonly PhysData PhysicsData;
		private readonly EntityManager EntMgr;
		private Frustum ViewFrustum;
		private float _totalTime;

		public GameState(GameWindow window) : base(window)
		{
			GUI = new FishUIManager(window);
			EntMgr = new EntityManager();
			Snd = new SoundMgr();
			PhysicsData = new PhysData();

			// =========================================== Init Systems ==============================================
			Snd.Init();
			Map = new ChunkMap(this);

			Particle = new ParticleSystem();
			Particle.Init(
				(Pt) => Map.Collide(Pt, Vector3.Zero, out Vector3 _),
				(Pt) => Map.GetBlock(Pt)
			);

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
			if (File.Exists(MAP_FILE))
			{
				using var fileStream = File.OpenRead(MAP_FILE);
				Map.Read(fileStream);
			}
			else
			{
				Map.GenerateFloatingIsland(ISLAND_SIZE, ISLAND_SIZE);
			}


			// ====================================== Init player ====================================================
				Ply = new Player(GUI, "snoutx10k", true, Snd);
				Ply.InitGUI(window, GUI);
				Ply.Init(Map);

			if (File.Exists(PLAYER_FILE))
			{
				using var fileStream = File.OpenRead(PLAYER_FILE);
				using var reader = new BinaryReader(fileStream);
				Ply.Read(reader);
			}
			else
			{
				Ply.SetPosition(PlayerPos);
			}
		}


		public override void SwapTo()
		{
			base.SwapTo();
			Raylib.DisableCursor();
		}

		public override void OnResize(GameWindow Window)
		{
			base.OnResize(Window);
			Ply.RecalcGUI(Window);
		}

		public override void Tick(float GameTime)
		{
			Particle.Tick(GameTime);

			// Handle input
			if (Window.InMgr.IsInputPressed(InputKey.Esc))
			{
				Window.SetState(Program.MainMenuState);
				return;
			}

			if (Window.InMgr.IsInputPressed(InputKey.F5))
			{
				SaveGameState();
			}

			// Update game systems
			Map.Tick();
			Ply.Tick(Window.InMgr);
			Ply.TickGUI(Window.InMgr, Map);
			Ply.UpdateGUI();
		}

		public override void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr)
		{
			Ply.UpdatePhysics(Map, PhysicsData, Dt, InMgr);
			EntMgr.UpdateLockstep(TotalTime, Dt, InMgr);
		}

		public override void Draw(float TimeAlpha, ref GameFrameInfo LastFrame, ref GameFrameInfo FInfo)
		{
			Ply.UpdateFPSCamera(ref FInfo);

			Raylib.ClearBackground(new Color(200, 200, 200));
			Raylib.BeginMode3D(Ply.RenderCam); // Use interpolated render camera

			Shader defaultShader = ResMgr.GetShader("default");
			Raylib.BeginShaderMode(defaultShader);

			Draw3D(TimeAlpha, ref LastFrame, ref FInfo);
			//DrawBlockPlacementPreview();

			Raylib.EndShaderMode();


			// Draw wireframe elements without shader (lines don't work well with textured shader)

			/*if (Program.DebugMode) {
				DrawPlayerCollisionBox(PlayerCollisionBoxPos);
				ViewFrustum.Draw();
				DrawDebugLines();
				Utils.DrawRaycastRecord();
			}*/

			Raylib.EndMode3D();
			}

			public override void Draw2D()
			{
				float deltaTime = Raylib.GetFrameTime();
				_totalTime += deltaTime;
				GUI.Tick(deltaTime, _totalTime);
				Raylib.DrawCircleLines(Program.Window.Width / 2, Program.Window.Height / 2, 5, Color.White);
				Raylib.DrawFPS(10, 10);
			}

		private void Draw3D(float TimeAlpha, ref GameFrameInfo LastFrame, ref GameFrameInfo CurrentFrame)
		{
			if (!Ply.FreezeFrustum)
				ViewFrustum = new Frustum(ref Ply.Cam);

			// Draw world geometry
			Map.Draw(ref ViewFrustum);
			DrawTransparent();

			// Draw entities and effects
			EntMgr.Draw3D(TimeAlpha, ref LastFrame);
			
			DrawBlockPlacementPreview();

			Particle.Draw(Ply, ref ViewFrustum);
			Ply.Draw(TimeAlpha, ref LastFrame, ref CurrentFrame);

		}

		private void DrawTransparent()
		{
			// Transparent rendering now handled inside ChunkMap with depth sorting
			Map.DrawTransparent(ref ViewFrustum, Ply.Position);
		}

		private void DrawDebugLines()
		{
			// Draw marker lines
			foreach (var marker in MarkerList)
				Raylib.DrawLine3D(marker.Item1, marker.Item2, Color.Blue);

			// Draw world axes
			Vector3 origin = Vector3.Zero;
			Raylib.DrawLine3D(origin, new Vector3(100, 0, 0), Color.Red);   // X-axis
			Raylib.DrawLine3D(origin, new Vector3(0, 100, 0), Color.Green); // Y-axis  
			Raylib.DrawLine3D(origin, new Vector3(0, 0, 100), Color.Blue);  // Z-axis
		}

		private void DrawPlayerCollisionBox(Vector3 feetPos)
		{
			Vector3 min = new Vector3(feetPos.X - Player.PlayerRadius, feetPos.Y, feetPos.Z - Player.PlayerRadius);
			Vector3 max = new Vector3(feetPos.X + Player.PlayerRadius, feetPos.Y + Player.PlayerHeight, feetPos.Z + Player.PlayerRadius);

			DrawWireframeCube(min, max, Color.Red);
		}

		private static void DrawWireframeCube(Vector3 min, Vector3 max, Color color)
		{
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

		private void DrawBlockPlacementPreview()
		{
			// Get the active inventory item
			InventoryItem activeItem = Ply.GetActiveItem();
			if (activeItem == null)
				return;

			if (!activeItem.IsPlaceableBlock())
				return;

			// Calculate where the block would be placed
			Vector3 start = Ply.Position;
			Vector3 dir = Ply.GetForward();
			const float maxLen = 20f;

			Vector3? placementPos = activeItem.GetBlockPlacementPosition(Map, start, dir, maxLen);
			if (placementPos == null)
				return;

			Vector3 blockPos = placementPos.Value;
			Vector3 center = blockPos + new Vector3(0.5f, 0.5f, 0.5f);

			// Draw smaller textured preview cube inside
			const float previewScale = 0.4f;
			DrawTexturedBlockPreview(center, previewScale, activeItem.BlockIcon);

			// Draw wireframe outline
			Raylib.DrawCubeWiresV(center, Vector3.One, Color.White);
		}

		private void DrawTexturedBlockPreview(Vector3 center, float scale, BlockType blockType)
		{
			// Get the atlas texture
			Texture2D atlas = ResMgr.AtlasTexture;

			// Calculate half size for the preview cube
			float half = scale * 0.8f;

			// Disable backface culling for preview (we want to see all faces)
			Rlgl.DisableBackfaceCulling();

			// Draw each face of the cube with the appropriate texture from the atlas
			DrawBlockFace(atlas, blockType, center, half, new Vector3(0, 1, 0));  // Top
			DrawBlockFace(atlas, blockType, center, half, new Vector3(0, -1, 0)); // Bottom
			DrawBlockFace(atlas, blockType, center, half, new Vector3(1, 0, 0));  // Right
			DrawBlockFace(atlas, blockType, center, half, new Vector3(-1, 0, 0)); // Left
			DrawBlockFace(atlas, blockType, center, half, new Vector3(0, 0, 1));  // Front
			DrawBlockFace(atlas, blockType, center, half, new Vector3(0, 0, -1)); // Back

			// Re-enable backface culling
			Rlgl.EnableBackfaceCulling();
		}

		private void DrawBlockFace(Texture2D atlas, BlockType blockType, Vector3 center, float half, Vector3 faceNormal)
		{
			// Get UV coordinates for this block face
			BlockInfo.GetBlockTexCoords(blockType, faceNormal, out Vector2 uvSize, out Vector2 uvPos);

			// Calculate the 4 corners of this face with correct winding order
			Vector3[] corners = new Vector3[4];
			Vector3 right, up;

			if (faceNormal.Y > 0.5f)
			{
				// Top face
				right = new Vector3(1, 0, 0);
				up = new Vector3(0, 0, -1);
			}
			else if (faceNormal.Y < -0.5f)
			{
				// Bottom face
				right = new Vector3(1, 0, 0);
				up = new Vector3(0, 0, 1);
			}
			else if (faceNormal.X > 0.5f)
			{
				// Right face (+X)
				right = new Vector3(0, 0, -1);
				up = new Vector3(0, 1, 0);
			}
			else if (faceNormal.X < -0.5f)
			{
				// Left face (-X)
				right = new Vector3(0, 0, 1);
				up = new Vector3(0, 1, 0);
			}
			else if (faceNormal.Z > 0.5f)
			{
				// Front face (+Z)
				right = new Vector3(1, 0, 0);
				up = new Vector3(0, 1, 0);
			}
			else
			{
				// Back face (-Z)
				right = new Vector3(-1, 0, 0);
				up = new Vector3(0, 1, 0);
			}

			Vector3 faceCenter = center + faceNormal * half;
			corners[0] = faceCenter - right * half - up * half; // Bottom-left
			corners[1] = faceCenter + right * half - up * half; // Bottom-right
			corners[2] = faceCenter + right * half + up * half; // Top-right
			corners[3] = faceCenter - right * half + up * half; // Top-left

			// Use Rlgl to draw a textured quad
			Rlgl.SetTexture(atlas.Id);
			Rlgl.Begin(DrawMode.Quads);

			// Set color with slight transparency for preview effect
			Rlgl.Color4ub(255, 255, 255, 200);

			// UV coordinates (normalized 0-1)
			float u0 = uvPos.X;
			float v0 = uvPos.Y;
			float u1 = uvPos.X + uvSize.X;
			float v1 = uvPos.Y + uvSize.Y;

			// Quad vertices with texture coordinates (counter-clockwise winding)
			Rlgl.TexCoord2f(u0, v1); Rlgl.Vertex3f(corners[0].X, corners[0].Y, corners[0].Z);
			Rlgl.TexCoord2f(u1, v1); Rlgl.Vertex3f(corners[1].X, corners[1].Y, corners[1].Z);
			Rlgl.TexCoord2f(u1, v0); Rlgl.Vertex3f(corners[2].X, corners[2].Y, corners[2].Z);
			Rlgl.TexCoord2f(u0, v0); Rlgl.Vertex3f(corners[3].X, corners[3].Y, corners[3].Z);

			Rlgl.End();
			Rlgl.SetTexture(0);
		}

		private void SaveGameState()
		{
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
