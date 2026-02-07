using Voxelgine.Engine;
using Raylib_cs;
using Voxelgine.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Voxelgine.GUI;
using Voxelgine.Engine.DI;

namespace Voxelgine.States
{
	/// <summary>
	/// Multiplayer client gameplay state. Connects to a server, receives world data,
	/// and runs the game with networked input sending, client-side prediction,
	/// and remote player rendering.
	/// </summary>
	public unsafe class MultiplayerGameState : GameStateImpl
	{
		private const int DefaultPort = 7777;
		private const float DeltaTime = 0.015f;

		// Networking
		private NetClient _client;
		private ClientInputBuffer _inputBuffer;
		private ClientPrediction _prediction;
		private string _serverHost;
		private int _serverPort;
		private string _playerName;

		// Game simulation (created after world data is received)
		private GameSimulation _simulation;
		private FishUIManager _gui;
		private SoundMgr _snd;
		private ParticleSystem _particle;
		private Frustum _viewFrustum;
		private float _totalTime;

		// Status
		private string _statusText = "";
		private string _errorText = "";
		private bool _initialized;

		// Buffer for PlayerJoined packets received before simulation is created
		private readonly List<PlayerJoinedPacket> _pendingPlayerJoins = new List<PlayerJoinedPacket>();

		// Water overlay
		private Texture2D? _waterOverlayTexture;
		private bool _waterOverlayLoaded;

		// Sun/Moon textures
		private Texture2D? _sunTexture;
		private Texture2D? _moonTexture;
		private bool _celestialTexturesLoaded;
		private Vector2 _sunScreenPos;
		private bool _sunVisible;
		private float _sunScreenSize;
		private Vector2 _moonScreenPos;
		private bool _moonVisible;
		private float _moonScreenSize;

		IGameWindow _gameWindow;
		IFishLogging _logging;

		public MultiplayerGameState(IGameWindow window, IFishEngineRunner eng) : base(window, eng)
		{
			_gameWindow = window;
			_logging = eng.DI.GetRequiredService<IFishLogging>();
		}

		/// <summary>
		/// Initiates a connection to a multiplayer server.
		/// </summary>
		public void Connect(string host, int port, string playerName)
		{
			_serverHost = host;
			_serverPort = port;
			_playerName = playerName;

			Cleanup();

			_client = new NetClient();
			_inputBuffer = new ClientInputBuffer();
			_prediction = new ClientPrediction();

			// Wire up events
			_client.OnConnected += OnConnected;
			_client.OnDisconnected += OnDisconnected;
			_client.OnConnectionRejected += OnConnectionRejected;
			_client.OnWorldDataReady += OnWorldDataReady;
			_client.OnWorldTransferFailed += OnWorldTransferFailed;
			_client.OnPacketReceived += OnPacketReceived;

			_statusText = $"Connecting to {host}:{port}...";
			_errorText = "";

			try
			{
				_client.Connect(host, port, playerName, (float)Raylib.GetTime());
			}
			catch (Exception ex)
			{
				_statusText = "";
				_errorText = $"Failed to connect: {ex.Message}";
				_logging.WriteLine($"MultiplayerGameState: Connection failed: {ex.Message}");
			}
		}

		public override void SwapTo()
		{
			base.SwapTo();

			if (_initialized)
			{
				Raylib.DisableCursor();
			}
			else
			{
				Raylib.EnableCursor();
			}
		}

		public override void Tick(float GameTime)
		{
			if (_client == null)
				return;

			try
			{
				float currentTime = (float)Raylib.GetTime();

				// Process network
				_client.Tick(currentTime);

				if (!_initialized)
				{
					// Update loading progress
					if (_client.State == ClientState.Loading)
					{
						var wr = _client.WorldReceiver;
						if (wr.TotalFragments > 0)
							_statusText = $"Loading world... {wr.FragmentsReceived}/{wr.TotalFragments}";
						else
							_statusText = "Loading world...";
					}
					return;
				}

				float dt = Raylib.GetFrameTime();

				// Update day/night cycle (client authority is false — time set by server)
				float prevMultiplier = _simulation.DayNight.SkyLightMultiplier;
				_simulation.DayNight.Update(dt);
				if (MathF.Abs(_simulation.DayNight.SkyLightMultiplier - prevMultiplier) > 0.01f)
				{
					_simulation.Map.MarkAllChunksDirty();
				}

				_particle?.Tick(GameTime);

				// Handle ESC — disconnect and return to main menu
				if (Window.InMgr.IsInputPressed(InputKey.Esc))
				{
					DisconnectAndReturn("Player disconnected");
					return;
				}

				// Update game systems
				_simulation.Map.Tick();
				_simulation.LocalPlayer.Tick(Window.InMgr);
				_simulation.LocalPlayer.TickGUI(Window.InMgr, _simulation.Map);
				_simulation.LocalPlayer.UpdateGUI();
			}
			catch (Exception ex)
			{
				_logging.WriteLine($"MultiplayerGameState: Tick exception: {ex}");
			}
		}

		public override void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr)
		{
			if (_client == null || !_initialized || _client.State != ClientState.Playing)
				return;

			try
			{
				float currentTime = (float)Raylib.GetTime();

				// Increment client tick
				_client.LocalTick++;

				// Record input and send to server
				var inputPacket = _inputBuffer.Record(
					_client.LocalTick,
					InMgr.State,
					new Vector2(_simulation.LocalPlayer.Camera.CamAngle.X, _simulation.LocalPlayer.Camera.CamAngle.Y)
				);
				_client.Send(inputPacket, false, currentTime);

				// Apply local prediction (same physics as server)
				_simulation.LocalPlayer.UpdatePhysics(_simulation.Map, _simulation.PhysicsData, Dt, InMgr);

				// Record predicted state
				_prediction.RecordPrediction(
					_client.LocalTick,
					_simulation.LocalPlayer.Position,
					_simulation.LocalPlayer.GetVelocity()
				);

				// Update entities
				_simulation.Entities.UpdateLockstep(TotalTime, Dt, InMgr);
			}
			catch (Exception ex)
			{
				_logging.WriteLine($"MultiplayerGameState: UpdateLockstep exception: {ex}");
			}
		}

		public override void Draw(float TimeAlpha, ref GameFrameInfo LastFrame, ref GameFrameInfo FInfo)
		{
			if (!_initialized || _simulation?.LocalPlayer == null)
				return;

			try
			{
				_simulation.LocalPlayer.UpdateFPSCamera(ref FInfo);

				// Sync render camera from physics camera (GameWindow interpolation only handles single-player GameState)
				_simulation.LocalPlayer.RenderCam = _simulation.LocalPlayer.Cam;

				// Populate frame info for GameWindow interpolation
				FInfo.Empty = false;
				FInfo.Pos = _simulation.LocalPlayer.Camera.Position;
				FInfo.Cam = _simulation.LocalPlayer.Cam;
				FInfo.CamAngle = _simulation.LocalPlayer.GetCamAngle();
				FInfo.FeetPosition = _simulation.LocalPlayer.FeetPosition;
				FInfo.ViewModelOffset = _simulation.LocalPlayer.ViewMdl.ViewModelOffset;
				FInfo.ViewModelRot = _simulation.LocalPlayer.ViewMdl.VMRot;

				// Apply interpolation if we have a previous frame
				if (!LastFrame.Empty)
				{
					GameFrameInfo interp = FInfo.Interpolate(LastFrame, TimeAlpha);
					_simulation.LocalPlayer.RenderCam = interp.Cam;
					_simulation.LocalPlayer.ViewMdl.ViewModelOffset = interp.ViewModelOffset;
					_simulation.LocalPlayer.ViewMdl.VMRot = interp.ViewModelRot;
				}

				// Update remote player interpolation
				float frameTime = Raylib.GetFrameTime();
				float currentTime = (float)Raylib.GetTime();
				foreach (var remotePlayer in _simulation.Players.GetAllRemotePlayers())
				{
					remotePlayer.Update(currentTime, frameTime);
				}

				Raylib.ClearBackground(_simulation.DayNight.SkyColor);

				CalculateCelestialPositions();
				DrawCelestialBodies();

				Raylib.BeginMode3D(_simulation.LocalPlayer.RenderCam);

				Shader defaultShader = ResMgr.GetShader("default");
				Raylib.BeginShaderMode(defaultShader);

				Draw3D(TimeAlpha, ref LastFrame, ref FInfo);

				Raylib.EndShaderMode();

				Raylib.EndMode3D();
			}
			catch (Exception ex)
			{
				_logging.WriteLine($"MultiplayerGameState: Draw exception: {ex}");
			}
		}

		private void Draw3D(float TimeAlpha, ref GameFrameInfo LastFrame, ref GameFrameInfo CurrentFrame)
		{
			if (!_simulation.LocalPlayer.FreezeFrustum)
				_viewFrustum = new Frustum(Eng, ref _simulation.LocalPlayer.Cam);

			_simulation.Map.Draw(ref _viewFrustum);
			_simulation.Entities.Draw3D(TimeAlpha, ref LastFrame);

			// Draw remote players
			foreach (var remotePlayer in _simulation.Players.GetAllRemotePlayers())
			{
				remotePlayer.Draw3D();
			}

			DrawBlockPlacementPreview();
			DrawTransparent();

			_particle?.Draw(_simulation.LocalPlayer, ref _viewFrustum);
			_simulation.LocalPlayer.Draw(TimeAlpha, ref LastFrame, ref CurrentFrame);
		}

		public override void Draw2D()
		{
		  try
		  {
			float deltaTime = Raylib.GetFrameTime();
			_totalTime += deltaTime;

			if (!_initialized)
			{
				// Draw connection/loading status screen
				Raylib.ClearBackground(new Color(30, 30, 40, 255));

				int screenW = _gameWindow.Width;
				int screenH = _gameWindow.Height;

				if (!string.IsNullOrEmpty(_statusText))
				{
					int textW = Raylib.MeasureText(_statusText, 24);
					Raylib.DrawText(_statusText, (screenW - textW) / 2, screenH / 2 - 20, 24, Color.White);
				}

				if (!string.IsNullOrEmpty(_errorText))
				{
					int textW = Raylib.MeasureText(_errorText, 20);
					Raylib.DrawText(_errorText, (screenW - textW) / 2, screenH / 2 + 20, 20, Color.Red);

					string backText = "Press ESC to return to menu";
					int backW = Raylib.MeasureText(backText, 18);
					Raylib.DrawText(backText, (screenW - backW) / 2, screenH / 2 + 60, 18, Color.Gray);

					if (Raylib.IsKeyPressed(KeyboardKey.Escape))
					{
						DisconnectAndReturn("Cancelled");
					}
				}

				// Show loading progress bar
				if (_client?.State == ClientState.Loading)
				{
					var wr = _client.WorldReceiver;
					float progress = wr.Progress;
					int barW = 300;
					int barH = 20;
					int barX = (screenW - barW) / 2;
					int barY = screenH / 2 + 30;

					Raylib.DrawRectangle(barX, barY, barW, barH, Color.DarkGray);
					Raylib.DrawRectangle(barX, barY, (int)(barW * progress), barH, Color.Green);
					Raylib.DrawRectangleLines(barX, barY, barW, barH, Color.White);
				}

				return;
			}

			// In-game HUD
			DrawUnderwaterOverlay();

			_gui.Tick(deltaTime, _totalTime);
			Raylib.DrawCircleLines(_gameWindow.Width / 2, _gameWindow.Height / 2, 5, Color.White);
			Raylib.DrawFPS(10, 10);

			// Time of day
			string timeStr = $"Time: {_simulation.DayNight.GetTimeString()} ({_simulation.DayNight.GetPeriodString()})";
			Raylib.DrawText(timeStr, 10, 30, 20, Color.White);

			// Network info
			string netInfo = $"Ping: {_client.RoundTripTimeMs}ms | Tick: {_client.LocalTick} | Players: {_simulation.Players.RemotePlayerCount + 1}";
			Raylib.DrawText(netInfo, 10, 50, 16, Color.LightGray);
		  }
		  catch (Exception ex)
		  {
			_logging.WriteLine($"MultiplayerGameState: Draw2D exception: {ex}");
		  }
		}

		// ======================================= Network Event Handlers ==========================================

		private void OnConnected(ConnectAcceptPacket accept)
		{
			_logging.WriteLine($"MultiplayerGameState: Connected as player {accept.PlayerId}");
			_statusText = "Connected! Loading world...";
		}

		private void OnDisconnected(string reason)
		{
			_logging.WriteLine($"MultiplayerGameState: Disconnected: {reason}");
			_statusText = "";
			_errorText = $"Disconnected: {reason}";

			if (_initialized)
			{
				// Was in-game, go back to menu
				_initialized = false;
				Window.SetState(Eng.MainMenuState);
			}
		}

		private void OnConnectionRejected(string reason)
		{
			_logging.WriteLine($"MultiplayerGameState: Rejected: {reason}");
			_statusText = "";
			_errorText = $"Connection rejected: {reason}";
		}

		private void OnWorldDataReady(byte[] compressedData)
		{
			_logging.WriteLine($"MultiplayerGameState: World data received ({compressedData.Length} bytes compressed)");
			_statusText = "Building world...";

			try
			{
				// Create game simulation
				_logging.WriteLine("MultiplayerGameState: Creating GameSimulation...");
				_simulation = new GameSimulation(Eng);
				_simulation.DayNight.IsAuthority = false; // Server controls time
				_logging.WriteLine("MultiplayerGameState: GameSimulation created");

				// Load world
				_logging.WriteLine("MultiplayerGameState: Reading ChunkMap from stream...");
				using (var ms = new MemoryStream(compressedData))
				{
					_simulation.Map.Read(ms);
				}
				_logging.WriteLine("MultiplayerGameState: ChunkMap loaded successfully");

				_logging.WriteLine("MultiplayerGameState: Computing lighting...");
				_simulation.Map.ComputeLighting();
				_logging.WriteLine("MultiplayerGameState: Lighting computed");

				// Create GUI
				_logging.WriteLine("MultiplayerGameState: Creating FishUIManager...");
				_gui = new FishUIManager(_gameWindow, _logging);
				_logging.WriteLine("MultiplayerGameState: FishUIManager created");

				// Create sound
				_logging.WriteLine("MultiplayerGameState: Creating SoundMgr...");
				_snd = new SoundMgr();
				_snd.Init();
				_logging.WriteLine("MultiplayerGameState: SoundMgr initialized");

				// Create particle system
				_logging.WriteLine("MultiplayerGameState: Creating ParticleSystem...");
				_particle = new ParticleSystem();
				_particle.Init(
					(pt) => _simulation.Map.Collide(pt, Vector3.Zero, out Vector3 _),
					(pt) => _simulation.Map.GetBlock(pt),
					(pt) => _simulation.Map.GetLightColor(pt)
				);
				_logging.WriteLine("MultiplayerGameState: ParticleSystem initialized");

				// Create local player
				_logging.WriteLine($"MultiplayerGameState: Creating Player (id={_client.PlayerId}, name={_playerName})...");
				var ply = new Player(Eng, _gui, _playerName, true, _snd, _client.PlayerId);
				_logging.WriteLine("MultiplayerGameState: Player created, adding to PlayerManager...");
				_simulation.Players.AddLocalPlayer(_client.PlayerId, ply);

				_logging.WriteLine("MultiplayerGameState: InitGUI...");
				ply.InitGUI(_gameWindow, _gui);
				_logging.WriteLine("MultiplayerGameState: InitGUI complete");

				_logging.WriteLine("MultiplayerGameState: Player.Init...");
				ply.Init(_simulation.Map);
				_logging.WriteLine("MultiplayerGameState: Player.Init complete");

				_logging.WriteLine("MultiplayerGameState: SetPosition...");
				ply.SetPosition(new Vector3(32, 73, 19)); // Default spawn, server will correct
				_logging.WriteLine("MultiplayerGameState: SetPosition complete");

				// Process any PlayerJoined packets that arrived before the simulation was created
					if (_pendingPlayerJoins.Count > 0)
					{
						_logging.WriteLine($"MultiplayerGameState: Processing {_pendingPlayerJoins.Count} buffered PlayerJoined packet(s)...");
						foreach (var pending in _pendingPlayerJoins)
						{
							HandlePlayerJoined(pending);
						}
						_pendingPlayerJoins.Clear();
					}

					// Finish loading
					_logging.WriteLine("MultiplayerGameState: FinishLoading...");
					_client.FinishLoading();
					_initialized = true;
					_statusText = "";
					_errorText = "";

				Raylib.DisableCursor();

				_logging.WriteLine("MultiplayerGameState: World loaded, entering gameplay");
			}
			catch (Exception ex)
			{
				_logging.WriteLine($"MultiplayerGameState: Failed to load world: {ex}");
				_statusText = "";
				_errorText = $"Failed to load world: {ex.Message}";
			}
		}

		private void OnWorldTransferFailed(string error)
		{
			_logging.WriteLine($"MultiplayerGameState: World transfer failed: {error}");
			_statusText = "";
			_errorText = $"World transfer failed: {error}";
		}

		private void OnPacketReceived(Packet packet)
		{
			float currentTime = (float)Raylib.GetTime();

			switch (packet)
			{
				case WorldSnapshotPacket snapshot:
					HandleWorldSnapshot(snapshot, currentTime);
					break;

				case PlayerJoinedPacket joined:
					HandlePlayerJoined(joined);
					break;

				case PlayerLeftPacket left:
					HandlePlayerLeft(left);
					break;

				case DayTimeSyncPacket timeSync:
					if (_simulation != null)
						_simulation.DayNight.SetTime(timeSync.TimeOfDay);
					break;
			}
		}

		private void HandleWorldSnapshot(WorldSnapshotPacket snapshot, float currentTime)
		{
			if (_simulation == null)
				return;

			foreach (var entry in snapshot.Players)
			{
				if (entry.PlayerId == _client.PlayerId)
				{
					// Local player — reconciliation
					bool needsCorrection = _prediction.ProcessServerSnapshot(
						snapshot.TickNumber,
						entry.Position,
						entry.Velocity
					);

					if (needsCorrection)
					{
						PredictionReconciler.Reconcile(
							_simulation.LocalPlayer,
							entry.Position,
							entry.Velocity,
							snapshot.TickNumber,
							_client.LocalTick,
							_inputBuffer,
							_prediction,
							_simulation.Map,
							_simulation.PhysicsData,
							DeltaTime
						);
					}
				}
				else
				{
					// Remote player — apply snapshot for interpolation
					var remote = _simulation.Players.GetRemotePlayer(entry.PlayerId);
					if (remote != null)
					{
						remote.ApplySnapshot(entry.Position, entry.Velocity, entry.CameraAngle, currentTime);
					}
				}
			}
		}

		private void HandlePlayerJoined(PlayerJoinedPacket joined)
		{
			if (joined.PlayerId == _client.PlayerId)
				return; // That's us

			if (_simulation == null)
			{
				// World not loaded yet — buffer for later processing
				_pendingPlayerJoins.Add(joined);
				return;
			}

			_logging.WriteLine($"MultiplayerGameState: Player joined: {joined.PlayerName} (ID {joined.PlayerId})");

			var remote = new RemotePlayer(joined.PlayerId, joined.PlayerName, Eng);
			remote.SetPosition(joined.Position);
			_simulation.Players.AddRemotePlayer(remote);
		}

		private void HandlePlayerLeft(PlayerLeftPacket left)
		{
			if (_simulation == null)
				return;

			_logging.WriteLine($"MultiplayerGameState: Player left (ID {left.PlayerId})");
			_simulation.Players.RemoveRemotePlayer(left.PlayerId);
		}

		// ======================================= Helper Methods =================================================

		private void DisconnectAndReturn(string reason)
		{
			if (_client != null && _client.IsConnected)
			{
				try { _client.Disconnect(reason, (float)Raylib.GetTime()); } catch { }
			}
			Cleanup();
			Window.SetState(Eng.MainMenuState);
		}

		private void Cleanup()
		{
			_initialized = false;
			_statusText = "";
			_errorText = "";

			if (_client != null)
			{
				_client.OnConnected -= OnConnected;
				_client.OnDisconnected -= OnDisconnected;
				_client.OnConnectionRejected -= OnConnectionRejected;
				_client.OnWorldDataReady -= OnWorldDataReady;
				_client.OnWorldTransferFailed -= OnWorldTransferFailed;
				_client.OnPacketReceived -= OnPacketReceived;
				_client.Dispose();
				_client = null;
			}

			_inputBuffer = null;
			_prediction = null;
			_simulation = null;
			_gui = null;
			_snd = null;
			_particle = null;
			_pendingPlayerJoins.Clear();
		}

		// ====================================== Rendering Helpers ===============================================

		private void DrawTransparent()
		{
			if (_simulation?.LocalPlayer != null)
				_simulation.Map.DrawTransparent(ref _viewFrustum, _simulation.LocalPlayer.Position);
		}

		private void DrawBlockPlacementPreview()
		{
			if (_simulation?.LocalPlayer == null)
				return;

			InventoryItem activeItem = _simulation.LocalPlayer.GetActiveItem();
			if (activeItem == null || !activeItem.IsPlaceableBlock())
				return;

			Vector3 start = _simulation.LocalPlayer.Position;
			Vector3 dir = _simulation.LocalPlayer.GetForward();
			const float maxLen = 20f;

			Vector3? placementPos = activeItem.GetBlockPlacementPosition(_simulation.Map, start, dir, maxLen);
			if (placementPos == null)
				return;

			Vector3 center = placementPos.Value + new Vector3(0.5f, 0.5f, 0.5f);
			Raylib.DrawCubeWiresV(center, Vector3.One, Color.White);
		}

		private void DrawUnderwaterOverlay()
		{
			if (_simulation?.LocalPlayer == null)
				return;

			BlockType blockAtCamera = _simulation.Map.GetBlock(_simulation.LocalPlayer.Position);
			if (blockAtCamera != BlockType.Water)
				return;

			if (!_waterOverlayLoaded)
			{
				_waterOverlayLoaded = true;
				try
				{
					_waterOverlayTexture = ResMgr.GetTexture("overlay_water.png", TextureFilter.Bilinear);
				}
				catch
				{
					_waterOverlayTexture = null;
				}
			}

			int screenWidth = _gameWindow.Width;
			int screenHeight = _gameWindow.Height;

			if (_waterOverlayTexture.HasValue && _waterOverlayTexture.Value.Id != 0)
			{
				Texture2D tex = _waterOverlayTexture.Value;
				Rectangle srcRect = new Rectangle(0, 0, tex.Width, tex.Height);
				Rectangle destRect = new Rectangle(0, 0, screenWidth, screenHeight);
				Raylib.DrawTexturePro(tex, srcRect, destRect, Vector2.Zero, 0f, Color.White);
			}
			else
			{
				Color waterColor = new Color(30, 80, 150, 120);
				Raylib.DrawRectangle(0, 0, screenWidth, screenHeight, waterColor);
			}
		}

		private void LoadCelestialTextures()
		{
			if (_celestialTexturesLoaded)
				return;

			_celestialTexturesLoaded = true;
			try { _sunTexture = ResMgr.GetTexture("sun.png", TextureFilter.Bilinear); }
			catch { _sunTexture = null; }

			try { _moonTexture = ResMgr.GetTexture("moon.png", TextureFilter.Bilinear); }
			catch { _moonTexture = null; }
		}

		private void CalculateCelestialPositions()
		{
			if (_simulation?.LocalPlayer == null)
				return;

			LoadCelestialTextures();

			const float CelestialDistance = 100f;
			const float BaseSunScreenSize = 128f;
			const float BaseMoonScreenSize = 96f;

			_sunVisible = false;
			_moonVisible = false;

			var dayNight = _simulation.DayNight;
			var renderCam = _simulation.LocalPlayer.RenderCam;

			// Calculate sun screen position if above horizon
			if (dayNight.SunElevation > 0 && _sunTexture.HasValue)
			{
				Vector3 sunDir = dayNight.GetSunDirection();
				Vector3 sunWorldPos = renderCam.Position + sunDir * CelestialDistance;

				Vector3 toSun = Vector3.Normalize(sunWorldPos - renderCam.Position);
				Vector3 camForward = Vector3.Normalize(renderCam.Target - renderCam.Position);
				float dot = Vector3.Dot(toSun, camForward);

				if (dot > 0)
				{
					_sunScreenPos = Raylib.GetWorldToScreen(sunWorldPos, renderCam);
					float horizonScale = 1f + (1f - Math.Min(1f, dayNight.SunElevation / 0.5f)) * 0.3f;
					_sunScreenSize = BaseSunScreenSize * horizonScale;
					_sunVisible = true;
				}
			}

			// Calculate moon screen position if sun is below horizon
			if (dayNight.SunElevation <= 0 && _moonTexture.HasValue)
			{
				float moonElevation = MathF.Abs(dayNight.SunElevation) + 0.3f;
				moonElevation = MathF.Min(moonElevation, MathF.PI / 3f);

				float moonAzimuth = dayNight.SunAzimuth + MathF.PI;

				float cosElev = MathF.Cos(moonElevation);
				Vector3 moonDir = new Vector3(
					cosElev * MathF.Cos(moonAzimuth),
					MathF.Sin(moonElevation),
					cosElev * MathF.Sin(moonAzimuth)
				);

				Vector3 moonWorldPos = renderCam.Position + moonDir * CelestialDistance;

				Vector3 toMoon = Vector3.Normalize(moonWorldPos - renderCam.Position);
				Vector3 camForward = Vector3.Normalize(renderCam.Target - renderCam.Position);
				float dot = Vector3.Dot(toMoon, camForward);

				if (dot > 0)
				{
					_moonScreenPos = Raylib.GetWorldToScreen(moonWorldPos, renderCam);
					_moonScreenSize = BaseMoonScreenSize;
					_moonVisible = true;
				}
			}
		}

		private void DrawCelestialBodies()
		{
			if (_sunVisible && _sunTexture.HasValue)
			{
				Texture2D tex = _sunTexture.Value;
				float halfSize = _sunScreenSize / 2f;
				Rectangle srcRect = new Rectangle(0, 0, tex.Width, tex.Height);
				Rectangle destRect = new Rectangle(_sunScreenPos.X - halfSize, _sunScreenPos.Y - halfSize, _sunScreenSize, _sunScreenSize);
				Raylib.DrawTexturePro(tex, srcRect, destRect, Vector2.Zero, 0f, _simulation.DayNight.SunColor);
			}

			if (_moonVisible && _moonTexture.HasValue)
			{
				Texture2D tex = _moonTexture.Value;
				float halfSize = _moonScreenSize / 2f;
				Color moonColor = new Color(220, 230, 255, 255);
				Rectangle srcRect = new Rectangle(0, 0, tex.Width, tex.Height);
				Rectangle destRect = new Rectangle(_moonScreenPos.X - halfSize, _moonScreenPos.Y - halfSize, _moonScreenSize, _moonScreenSize);
				Raylib.DrawTexturePro(tex, srcRect, destRect, Vector2.Zero, 0f, moonColor);
			}
		}
	}
}
