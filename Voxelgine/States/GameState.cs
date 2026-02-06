using Voxelgine.Engine;
using Raylib_cs;
using Voxelgine.Graphics;
using Voxelgine;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using Voxelgine.GUI;
using FishUI.Controls;
using Voxelgine.Engine.DI;

namespace Voxelgine.States
{
	/// <summary>
	/// Main gameplay state handling the voxel world, player, entities, and rendering.
	/// Manages the game loop including physics updates, input handling, and drawing.
	/// </summary>
	/// <remarks>
	/// GameState owns the ChunkMap (world), Player, EntityManager, ParticleSystem, and GUI.
	/// It handles save/load functionality and coordinates between all game systems.
	/// </remarks>
	public unsafe class GameState : GameStateImpl
	{
		private const string MAP_FILE = "data/map.bin";
		private const string PLAYER_FILE = "data/player.bin";
		private const int ISLAND_SIZE = 64;

		Vector3 PickupPos = new Vector3(37, 66, 15);
		Vector3 NPCPos = new Vector3(32, 66, 14);
		Vector3 PlayerPos = new Vector3(32, 73, 19);

		/// <summary>The voxel world chunk manager.</summary>
		public ChunkMap Map
		{
			get; private set;
		}

		/// <summary>Manages all players in the game.</summary>
		public PlayerManager Players
		{
			get; private set;
		}

		/// <summary>Convenience property to get the local player instance.</summary>
		public Player LocalPlayer => Players.LocalPlayer;

		/// <summary>Positional audio manager.</summary>
		public SoundMgr Snd
		{
			get; private set;
		}

		/// <summary>Particle effect system.</summary>
		public ParticleSystem Particle
		{
			get; private set;
		}

		/// <summary>Day/night cycle manager.</summary>
		public DayNightCycle DayNight
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

		/// <summary>Gets the entity manager for this game state.</summary>
		public EntityManager Entities => EntMgr;

		// Water overlay
		private Texture2D? _waterOverlayTexture;
		private bool _waterOverlayLoaded;

		// Sun/Moon textures
		private Texture2D? _sunTexture;
		private Texture2D? _moonTexture;
		private bool _celestialTexturesLoaded;

		// Sun/Moon screen positions (calculated in Draw3D, rendered in Draw2D)
		private Vector2 _sunScreenPos;
		private bool _sunVisible;
		private float _sunScreenSize;
		private Vector2 _moonScreenPos;
		private bool _moonVisible;
		private float _moonScreenSize;

		// Debug menu
		private Window _debugMenu;
		private CheckBox _debugModeCheckbox;
		private CheckBox _fullbrightCheckbox;
		private CheckBox _noclipCheckbox;

		// Test NPC reference
		private VEntNPC _testNpc;

		IGameWindow _gameWindow;
		IFishLogging Logging;

		public GameState(IGameWindow window, IFishEngineRunner eng) : base(window, eng)
		{
			_gameWindow = window;
			Logging = eng.DI.GetRequiredService<IFishLogging>();

			GUI = new FishUIManager(window, Logging);
			EntMgr = new EntityManager(window, eng);
			Snd = new SoundMgr();
			PhysicsData = new PhysData();
			DayNight = new DayNightCycle();

			// =========================================== Init Systems ==============================================
			Snd.Init();
			Map = new ChunkMap(eng);

			Particle = new ParticleSystem();
			Particle.Init(
				(Pt) => Map.Collide(Pt, Vector3.Zero, out Vector3 _),
				(Pt) => Map.GetBlock(Pt),
				(Pt) => Map.GetLightColor(Pt)
			);

			// =========================================== Create entities ===========================================
			// Create pickup entity
			VEntPickup pickup = new VEntPickup();
			pickup.SetPosition(PickupPos);
			pickup.SetSize(Vector3.One);
			pickup.SetModel("orb_xp/orb_xp.obj");
			EntMgr.Spawn(this, pickup);

			// Create NPC entity  
			_testNpc = new VEntNPC();
			_testNpc.SetSize(new Vector3(0.9f, 1.8f, 0.9f));
			_testNpc.SetPosition(NPCPos);
			_testNpc.SetModel("npc/humanoid.json");
			EntMgr.Spawn(this, _testNpc);

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

			// Initialize pathfinding for NPCs (must be after map is loaded)
			_testNpc.InitPathfinding(Map);

			Map.ComputeLighting();


			// ====================================== Init player ====================================================
			Players = new PlayerManager();
			var ply = new Player(Eng, GUI, "snoutx10k", true, Snd, 0);
			Players.AddLocalPlayer(0, ply);
			ply.InitGUI(window, GUI);
			ply.Init(Map);

			// Connect menu toggle event
			ply.OnMenuToggled = (cursorDisabled) =>
			{
				//Console.WriteLine($"OnMenuToggled - {cursorDisabled}");

				if (cursorDisabled)
				{
					_debugMenu.Visible = true;
					ToggleDebugMenu();
				}
				else if (!_debugMenu.Visible)
				{
					_debugMenu.Visible = true;
				}
			};

			if (File.Exists(PLAYER_FILE))
			{
				using var fileStream = File.OpenRead(PLAYER_FILE);
				using var reader = new BinaryReader(fileStream);
				ply.Read(reader);
			}
			else
			{
				ply.SetPosition(PlayerPos);
			}

			CreateDebugMenu();
		}

		private void CreateDebugMenu()
		{
			IGameWindow Window = Eng.DI.GetRequiredService<IGameWindow>();

			var menuSize = new Vector2(340, 560);
			var menuPos = new Vector2(
				(Window.Width - menuSize.X) / 2,
				(Window.Height - menuSize.Y) / 2
			);

			_debugMenu = new Window
			{
				Title = "Debug Menu (F1 to close)",
				Position = menuPos,
				Size = menuSize,
				IsResizable = false,
				ShowCloseButton = true,
				Visible = false
			};

			_debugMenu.OnClosed += (w) =>
			{
				_debugMenu.Visible = false;
				LocalPlayer.ToggleMouse(false); // Re-lock cursor
			};

			var stack = new StackLayout
			{
				Orientation = StackOrientation.Vertical,
				Spacing = 10,
				Position = new Vector2(10, 10),
				Size = new Vector2(menuSize.X - 40, menuSize.Y - 80),
				IsTransparent = true
			};

			// Debug mode toggle with label
			var debugLabel = new Label
			{
				Text = "Debug Mode (F3)",
				Size = new Vector2(200, 24)
			};
			stack.AddChild(debugLabel);

			_debugModeCheckbox = new CheckBox
			{
				IsChecked = Eng.DebugMode,
				Size = new Vector2(24, 24)
			};
			_debugModeCheckbox.OnCheckedChanged += (sender, isChecked) =>
			{
				Eng.DebugMode = isChecked;
			};
			stack.AddChild(_debugModeCheckbox);

			// Fullbright mode toggle
			var fullbrightLabel = new Label
			{
				Text = "Fullbright Mode",
				Size = new Vector2(200, 24)
			};
			stack.AddChild(fullbrightLabel);

			_fullbrightCheckbox = new CheckBox
			{
				IsChecked = BlockLight.FullbrightMode,
				Size = new Vector2(24, 24)
			};
			_fullbrightCheckbox.OnCheckedChanged += (sender, isChecked) =>
			{
				BlockLight.FullbrightMode = isChecked;
				// Force chunk mesh rebuild to apply new lighting
				Map.MarkAllChunksDirty();
			};
			stack.AddChild(_fullbrightCheckbox);

			// NOCLIP mode toggle (fly mode)
			var noclipLabel = new Label
			{
				Text = "NOCLIP Mode (Fly)",
				Size = new Vector2(200, 24)
			};
			stack.AddChild(noclipLabel);

			_noclipCheckbox = new CheckBox
			{
				IsChecked = LocalPlayer.NoClip,
				Size = new Vector2(24, 24)
			};
			_noclipCheckbox.OnCheckedChanged += (sender, isChecked) =>
			{
				LocalPlayer.NoClip = isChecked;
			};
			stack.AddChild(_noclipCheckbox);

			// Day/Night cycle controls
			var timeLabel = new Label
			{
				Text = "Time of Day:",
				Size = new Vector2(200, 24)
			};
			stack.AddChild(timeLabel);

			var timeButtonsLayout = new StackLayout
			{
				Orientation = StackOrientation.Horizontal,
				Spacing = 5,
				Size = new Vector2(280, 35),
				IsTransparent = true
			};

			var btnDawn = new Button { Text = "Dawn", Size = new Vector2(65, 32) };
			btnDawn.Clicked += (s, e) => { DayNight.SetTime(6f); Map.MarkAllChunksDirty(); };
			timeButtonsLayout.AddChild(btnDawn);

			var btnNoon = new Button { Text = "Noon", Size = new Vector2(65, 32) };
			btnNoon.Clicked += (s, e) => { DayNight.SetTime(12f); Map.MarkAllChunksDirty(); };
			timeButtonsLayout.AddChild(btnNoon);

			var btnDusk = new Button { Text = "Dusk", Size = new Vector2(65, 32) };
			btnDusk.Clicked += (s, e) => { DayNight.SetTime(18f); Map.MarkAllChunksDirty(); };
			timeButtonsLayout.AddChild(btnDusk);

			var btnNight = new Button { Text = "Night", Size = new Vector2(65, 32) };
			btnNight.Clicked += (s, e) => { DayNight.SetTime(0f); Map.MarkAllChunksDirty(); };
			timeButtonsLayout.AddChild(btnNight);

			stack.AddChild(timeButtonsLayout);
			stack.AddChild(_fullbrightCheckbox);

			// Save game button
			var btnSave = new Button
			{
				Text = "Save Game",
				Size = new Vector2(280, 40)
			};
			btnSave.Clicked += (sender, args) =>
			{
				SaveGame();
				Logging.WriteLine("Game saved!");
			};
			stack.AddChild(btnSave);

			// Load game button
			var btnLoad = new Button
			{
				Text = "Load Game",
				Size = new Vector2(280, 40)
			};
			btnLoad.Clicked += (sender, args) =>
			{
				LoadGame();
				Logging.WriteLine("Game loaded!");
			};
			stack.AddChild(btnLoad);

			// Regenerate world button
			var btnRegen = new Button
			{
				Text = "Regenerate World",
				Size = new Vector2(280, 40)
			};
			btnRegen.Clicked += (sender, args) =>
			{
				Map.GenerateFloatingIsland(ISLAND_SIZE, ISLAND_SIZE);
				LocalPlayer.SetPosition(PlayerPos);
				Logging.WriteLine("World regenerated!");
			};
			stack.AddChild(btnRegen);

			// NPC navigate to player button
			var btnNpcNavigate = new Button
			{
				Text = "NPC Navigate to Player",
				Size = new Vector2(280, 40)
			};
			btnNpcNavigate.Clicked += (sender, args) =>
			{
				if (_testNpc != null)
				{
					bool pathFound = _testNpc.NavigateTo(LocalPlayer.Position);
					Logging.WriteLine(pathFound ? "NPC navigating to player!" : "NPC could not find path to player!");
				}
			};
			stack.AddChild(btnNpcNavigate);

			/*// Export animations button
			var btnExportAnims = new Button
			{
				Text = "Export Default Animations",
				Size = new Vector2(280, 40)
			};
			btnExportAnims.Clicked += (sender, args) =>
			{
				NPCAnimations.ExportAllDefaults(Logging);
			};
			stack.AddChild(btnExportAnims);*/

			// Return to main menu button
			var btnMainMenu = new Button
			{
				Text = "Main Menu",
				Size = new Vector2(280, 40)
			};
			btnMainMenu.Clicked += (sender, args) =>
			{
				_debugMenu.Visible = false;
				Window.SetState(Eng.MainMenuState);
			};
			stack.AddChild(btnMainMenu);

			_debugMenu.AddChild(stack);
			GUI.AddControl(_debugMenu);
		}

		private void SaveGame()
		{
			// Save map
			using (var fileStream = File.Create(MAP_FILE))
			{
				Map.Write(fileStream);
			}
			// Save player
			using (var fileStream = File.Create(PLAYER_FILE))
			using (var writer = new BinaryWriter(fileStream))
			{
				LocalPlayer.Write(writer);
			}
		}

		private void LoadGame()
		{
			if (File.Exists(MAP_FILE))
			{
				using var fileStream = File.OpenRead(MAP_FILE);
				Map.Read(fileStream);
			}
			if (File.Exists(PLAYER_FILE))
			{
				using var fileStream = File.OpenRead(PLAYER_FILE);
				using var reader = new BinaryReader(fileStream);
				LocalPlayer.Read(reader);
			}
		}

		public void ToggleDebugMenu()
		{
			_debugMenu.Visible = !_debugMenu.Visible;
			if (_debugMenu.Visible)
			{
				_debugModeCheckbox.IsChecked = Eng.DebugMode;
				_debugMenu.BringToFront();
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
			LocalPlayer.RecalcGUI(Window);
		}

		public override void Tick(float GameTime)
		{
			float dt = Raylib.GetFrameTime();

			// Update day/night cycle
			float prevMultiplier = DayNight.SkyLightMultiplier;
			DayNight.Update(dt);

			// Mark chunks dirty if sky light changed significantly (threshold to avoid constant rebuilds)
			if (MathF.Abs(DayNight.SkyLightMultiplier - prevMultiplier) > 0.01f)
			{
				Map.MarkAllChunksDirty();
			}

			Particle.Tick(GameTime);

			// Handle input
			if (Window.InMgr.IsInputPressed(InputKey.Esc))
			{
				Window.SetState(Eng.MainMenuState);
				return;
			}

			if (Window.InMgr.IsInputPressed(InputKey.F5))
			{
				SaveGameState();
			}

			// Update game systems
			Map.Tick();
			LocalPlayer.Tick(Window.InMgr);
			LocalPlayer.TickGUI(Window.InMgr, Map);
			LocalPlayer.UpdateGUI();
		}

		public override void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr)
		{
			LocalPlayer.UpdatePhysics(Map, PhysicsData, Dt, InMgr);
			EntMgr.UpdateLockstep(TotalTime, Dt, InMgr);
		}

		public override void Draw(float TimeAlpha, ref GameFrameInfo LastFrame, ref GameFrameInfo FInfo)
		{
			LocalPlayer.UpdateFPSCamera(ref FInfo);

			// Use sky color from day/night cycle
			Raylib.ClearBackground(DayNight.SkyColor);

			// Draw celestial bodies in 2D before 3D scene (so they appear behind everything)
			CalculateCelestialPositions();
			DrawCelestialBodies();

			Raylib.BeginMode3D(LocalPlayer.RenderCam); // Use interpolated render camera

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

			// Draw water overlay if player camera is submerged
			DrawUnderwaterOverlay();

			GUI.Tick(deltaTime, _totalTime);
			Raylib.DrawCircleLines(Eng.DI.GetRequiredService<IGameWindow>().Width / 2, Eng.DI.GetRequiredService<IGameWindow>().Height / 2, 5, Color.White);
			Raylib.DrawFPS(10, 10);

			// Draw time of day
			string timeStr = $"Time: {DayNight.GetTimeString()} ({DayNight.GetPeriodString()})";
			Raylib.DrawText(timeStr, 10, 30, 20, Color.White);
		}

		/// <summary>
		/// Draws sun and moon textures in screen space at their calculated positions.
		/// </summary>
		private void DrawCelestialBodies()
		{
			// Draw sun if visible
			if (_sunVisible && _sunTexture.HasValue)
			{
				Texture2D tex = _sunTexture.Value;
				float halfSize = _sunScreenSize / 2f;

				// Center the texture on the screen position
				Rectangle srcRect = new Rectangle(0, 0, tex.Width, tex.Height);
				Rectangle destRect = new Rectangle(
					_sunScreenPos.X - halfSize,
					_sunScreenPos.Y - halfSize,
					_sunScreenSize,
					_sunScreenSize
				);

				Raylib.DrawTexturePro(tex, srcRect, destRect, Vector2.Zero, 0f, DayNight.SunColor);
			}

			// Draw moon if visible
			if (_moonVisible && _moonTexture.HasValue)
			{
				Texture2D tex = _moonTexture.Value;
				float halfSize = _moonScreenSize / 2f;

				// Moon has a cool white/blue tint
				Color moonColor = new Color(220, 230, 255, 255);

				Rectangle srcRect = new Rectangle(0, 0, tex.Width, tex.Height);
				Rectangle destRect = new Rectangle(
					_moonScreenPos.X - halfSize,
					_moonScreenPos.Y - halfSize,
					_moonScreenSize,
					_moonScreenSize
				);

				Raylib.DrawTexturePro(tex, srcRect, destRect, Vector2.Zero, 0f, moonColor);
			}
		}

		private void DrawUnderwaterOverlay()
		{
			// Check if player's eye position is inside a water block
			BlockType blockAtCamera = Map.GetBlock(LocalPlayer.Position);
			if (blockAtCamera != BlockType.Water)
				return;

			// Try to load the water overlay texture once
			if (!_waterOverlayLoaded)
			{
				_waterOverlayLoaded = true;
				try
				{
					_waterOverlayTexture = ResMgr.GetTexture("overlay_water.png", TextureFilter.Bilinear);
				}
				catch
				{
					// Texture not found, will use fallback color overlay
					_waterOverlayTexture = null;
				}
			}

			int screenWidth = Eng.DI.GetRequiredService<IGameWindow>().Width;
			int screenHeight = Eng.DI.GetRequiredService<IGameWindow>().Height;

			if (_waterOverlayTexture.HasValue && _waterOverlayTexture.Value.Id != 0)
			{
				// Draw the water overlay texture scaled to fill the screen
				Texture2D tex = _waterOverlayTexture.Value;
				Rectangle srcRect = new Rectangle(0, 0, tex.Width, tex.Height);
				Rectangle destRect = new Rectangle(0, 0, screenWidth, screenHeight);
				Raylib.DrawTexturePro(tex, srcRect, destRect, Vector2.Zero, 0f, Color.White);
			}
			else
			{
				// Fallback: draw a semi-transparent blue overlay
				Color waterColor = new Color(30, 80, 150, 120);
				Raylib.DrawRectangle(0, 0, screenWidth, screenHeight, waterColor);
			}
		}

		private void Draw3D(float TimeAlpha, ref GameFrameInfo LastFrame, ref GameFrameInfo CurrentFrame)
		{
			if (!LocalPlayer.FreezeFrustum)
				ViewFrustum = new Frustum(Eng, ref LocalPlayer.Cam);

			// Draw world geometry
			Map.Draw(ref ViewFrustum);

			// Draw entities and effects
			EntMgr.Draw3D(TimeAlpha, ref LastFrame);

			DrawBlockPlacementPreview();


			// Draw transparent blocks last for proper alpha blending
			DrawTransparent();

			// Draw particles BEFORE transparent blocks so they appear behind glass/water
			Particle.Draw(LocalPlayer, ref ViewFrustum);

			LocalPlayer.Draw(TimeAlpha, ref LastFrame, ref CurrentFrame);

		}

		/// <summary>
		/// Loads sun and moon textures if not already loaded.
		/// </summary>
		private void LoadCelestialTextures()
		{
			if (_celestialTexturesLoaded)
				return;

			_celestialTexturesLoaded = true;
			try
			{
				_sunTexture = ResMgr.GetTexture("sun.png", TextureFilter.Bilinear);
			}
			catch
			{
				_sunTexture = null;
			}

			try
			{
				_moonTexture = ResMgr.GetTexture("moon.png", TextureFilter.Bilinear);
			}
			catch
			{
				_moonTexture = null;
			}
		}

		/// <summary>
		/// Calculates screen positions for sun and moon based on day/night cycle.
		/// Actual rendering happens in Draw2D for proper screen-space drawing.
		/// </summary>
		private void CalculateCelestialPositions()
		{
			LoadCelestialTextures();

			const float CelestialDistance = 100f; // Distance for world position calculation
			const float BaseSunScreenSize = 128f;
			const float BaseMoonScreenSize = 96f;

			_sunVisible = false;
			_moonVisible = false;

			// Calculate sun screen position if above horizon
			if (DayNight.SunElevation > 0 && _sunTexture.HasValue)
			{
				Vector3 sunDir = DayNight.GetSunDirection();
				Vector3 sunWorldPos = LocalPlayer.RenderCam.Position + sunDir * CelestialDistance;

				// Check if sun is in front of camera
				Vector3 toSun = Vector3.Normalize(sunWorldPos - LocalPlayer.RenderCam.Position);
				Vector3 camForward = Vector3.Normalize(LocalPlayer.RenderCam.Target - LocalPlayer.RenderCam.Position);
				float dot = Vector3.Dot(toSun, camForward);

				if (dot > 0) // Sun is in front of camera
				{
					_sunScreenPos = Raylib.GetWorldToScreen(sunWorldPos, LocalPlayer.RenderCam);

					// Sun size (larger when near horizon for dramatic effect)
					float horizonScale = 1f + (1f - Math.Min(1f, DayNight.SunElevation / 0.5f)) * 0.3f;
					_sunScreenSize = BaseSunScreenSize * horizonScale;
					_sunVisible = true;
				}
			}

			// Calculate moon screen position if sun is below horizon (night time)
			if (DayNight.SunElevation <= 0 && _moonTexture.HasValue)
			{
				// Moon is opposite to sun position, elevated in night sky
				float moonElevation = MathF.Abs(DayNight.SunElevation) + 0.3f;
				moonElevation = MathF.Min(moonElevation, MathF.PI / 3f);

				float moonAzimuth = DayNight.SunAzimuth + MathF.PI;

				float cosElev = MathF.Cos(moonElevation);
				Vector3 moonDir = new Vector3(
					cosElev * MathF.Cos(moonAzimuth),
					MathF.Sin(moonElevation),
					cosElev * MathF.Sin(moonAzimuth)
				);

				Vector3 moonWorldPos = LocalPlayer.RenderCam.Position + moonDir * CelestialDistance;

				// Check if moon is in front of camera
				Vector3 toMoon = Vector3.Normalize(moonWorldPos - LocalPlayer.RenderCam.Position);
				Vector3 camForward = Vector3.Normalize(LocalPlayer.RenderCam.Target - LocalPlayer.RenderCam.Position);
				float dot = Vector3.Dot(toMoon, camForward);

				if (dot > 0) // Moon is in front of camera
				{
					_moonScreenPos = Raylib.GetWorldToScreen(moonWorldPos, LocalPlayer.RenderCam);
					_moonScreenSize = BaseMoonScreenSize;
					_moonVisible = true;
				}
			}
		}

		private void DrawTransparent()
		{
			// Transparent rendering now handled inside ChunkMap with depth sorting
			Map.DrawTransparent(ref ViewFrustum, LocalPlayer.Position);
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
			InventoryItem activeItem = LocalPlayer.GetActiveItem();
			if (activeItem == null)
				return;

			if (!activeItem.IsPlaceableBlock())
				return;

			// Calculate where the block would be placed
			Vector3 start = LocalPlayer.Position;
			Vector3 dir = LocalPlayer.GetForward();
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
			Logging.WriteLine("Saving map and player!");

			// Save map
			using MemoryStream memoryStream = new MemoryStream();
			Map.Write(memoryStream);
			File.WriteAllBytes(MAP_FILE, memoryStream.ToArray());

			// Save player
			using FileStream fileStream = File.Open(PLAYER_FILE, FileMode.Create, FileAccess.Write);
			using BinaryWriter writer = new BinaryWriter(fileStream);
			LocalPlayer.Write(writer);

			Logging.WriteLine("Save complete!");
		}
	}
}
