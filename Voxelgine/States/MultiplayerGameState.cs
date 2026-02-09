using Voxelgine.Engine;
using Raylib_cs;
using Voxelgine.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Voxelgine.GUI;
using Voxelgine.Engine.DI;
using FishUI;
using FishUI.Controls;

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

		// Connection lost overlay
		private bool _connectionLost;
		private string _disconnectReason = "";

		// Network statistics HUD
		private bool _showNetStats;

		// Player list overlay
		private bool _showPlayerList;

		// Kill feed duration for toast notifications
		private const float KillFeedDuration = 5f;

		// Chat message display duration
		private const float ChatMessageDuration = 10f;
		private const int MaxChatMessages = 10;

		// FishUI HUD controls — Loading screen
		private Label _loadingStatusLabel;
		private Label _loadingErrorLabel;
		private Label _loadingHintLabel;
		private ProgressBar _loadingProgressBar;

		// FishUI HUD controls — In-game
		private FishUIInfoLabel _hudInfoLabel;
		private BarGauge _healthBar;
		private Label _healthLabel;
		private ToastNotification _killFeedToast;
		private Panel _netStatsPanel;
		private FishUIInfoLabel _netStatsInfoLabel;
		private Panel _deathOverlayPanel;
		private Label _deathTitleLabel;
		private Label _deathSubtitleLabel;
		private Panel _connectionLostPanel;
		private Label _connectionLostTitleLabel;
		private Label _connectionLostReasonLabel;
		private Label _connectionLostReconnectLabel;
		private Label _connectionLostMenuLabel;
		private Window _debugMenuWindow;

		// FishUI HUD controls — Connection status
		private Label _connectionStatusLabel;

		// FishUI HUD controls — Player list
		private Panel _playerListPanel;
		private FishUIInfoLabel _playerListInfoLabel;

		// FishUI HUD controls — Chat
		private ToastNotification _chatToast;
		private Panel _chatInputPanel;
		private Textbox _chatInputBox;
		private bool _chatOpen;

		// Buffer for PlayerJoined packets received before simulation is created
		private readonly List<PlayerJoinedPacket> _pendingPlayerJoins = new List<PlayerJoinedPacket>();

		// Buffer for entity packets received before simulation is created
		private readonly List<Packet> _pendingEntityPackets = new List<Packet>();

		// Buffer for inventory update packets received before simulation is created
		private InventoryUpdatePacket _pendingInventoryUpdate;

		// Visual correction smoothing — accumulated position offset from reconciliation
		// corrections, blended toward zero each frame to avoid visual jitter/snapping.
		private Vector3 _correctionSmoothOffset;
		private const float CorrectionSmoothRate = 12f; // Higher = faster convergence

		// Entity interpolation buffers keyed by network ID
		private readonly Dictionary<int, SnapshotBuffer<EntitySnapshot>> _entitySnapshots = new Dictionary<int, SnapshotBuffer<EntitySnapshot>>();

		/// <summary>Interpolation delay for remote entities, matching remote players.</summary>
		private const float EntityInterpolationDelay = 0.1f;

		/// <summary>Snapshot data for entity interpolation.</summary>
		private struct EntitySnapshot
		{
			public Vector3 Position;
			public Vector3 Velocity;
			public byte AnimationState;
		}

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

		/// <summary>
		/// Returns true when the multiplayer client is actively connected and in-game.
		/// Used by weapon/gameplay code to decide whether to use the multiplayer path.
		/// </summary>
		public bool IsActive => _initialized && _client != null && _client.IsConnected;

		/// <summary>The world chunk map (available after world data is loaded).</summary>
		public ChunkMap Map => _simulation?.Map;

		/// <summary>The particle system for visual effects.</summary>
		public ParticleSystem Particle => _particle;

		/// <summary>The entity manager (available after world data is loaded).</summary>
		public EntityManager Entities => _simulation?.Entities;

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

			_client = new NetClient(_logging);
#if DEBUG
			//_client.PacketLoggingEnabled = true;
#endif
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

			// Create FishUI for loading screen
			_gui = new FishUIManager(_gameWindow, _logging);
			CreateLoadingUI();

			try
			{
				_client.Connect(host, port, playerName, (float)Raylib.GetTime());
			}
			catch (Exception ex)
			{
				_statusText = "";
				_errorText = $"Failed to connect: {ex.Message}";
				_logging.ClientWriteLine($"MultiplayerGameState: Connection failed: {ex.Message}");
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
			if (_client == null && !_connectionLost)
				return;

			try
			{
				float currentTime = (float)Raylib.GetTime();

				// Process network (client may still be processing final packets)
				_client?.Tick(currentTime);

				// Handle connection lost overlay input
				if (_connectionLost)
				{
					if (Raylib.IsKeyPressed(KeyboardKey.R))
					{
						// Reconnect to the same server
						string host = _serverHost;
						int port = _serverPort;
						string name = _playerName;
						Cleanup();
						Connect(host, port, name);
						return;
					}

					if (Raylib.IsKeyPressed(KeyboardKey.Escape))
					{
						// Return to main menu
						Cleanup();
						Window.SetState(Eng.MainMenuState);
						return;
					}

					// Keep updating particles and day/night for visual continuity
					_simulation?.DayNight.Update(Raylib.GetFrameTime());
					_particle?.Tick(GameTime);
					return;
				}

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

				// Toggle network statistics overlay
				if (Raylib.IsKeyPressed(KeyboardKey.F5))
					_showNetStats = !_showNetStats;

				// Toggle player list overlay
				if (Raylib.IsKeyPressed(KeyboardKey.Tab))
					_showPlayerList = !_showPlayerList;

				// Chat input handling
				if (_chatOpen)
				{
					if (Raylib.IsKeyPressed(KeyboardKey.Enter))
					{
						SubmitChatMessage();
					}
					else if (Raylib.IsKeyPressed(KeyboardKey.Escape))
					{
						CloseChatInput();
					}
					return; // Consume all other input while chat is open
				}

				if (Raylib.IsKeyPressed(KeyboardKey.Enter))
				{
					OpenChatInput();
					return;
				}

				// Handle ESC — disconnect and return to main menu
				if (Window.InMgr.IsInputPressed(InputKey.Esc))
				{
					DisconnectAndReturn("Player disconnected");
					return;
				}

				// Update game systems
				// Discard block changes logged by server-broadcasted BlockChangePackets
				// (processed during _client.Tick above) so they are not echoed back as
				// client requests in SendPendingBlockChanges. Only changes from local
				// player interaction (TickGUI below) should be sent to the server.
				_simulation.Map.ClearPendingChanges();

				_simulation.Map.Tick();
				_simulation.LocalPlayer.Tick(Window.InMgr);
				_simulation.LocalPlayer.TickGUI(Window.InMgr, _simulation.Map);
				_simulation.LocalPlayer.UpdateGUI();

				// Send pending block changes to server
				SendPendingBlockChanges((float)Raylib.GetTime());
			}
			catch (Exception ex)
			{
				_logging.ClientWriteLine($"MultiplayerGameState: Tick exception: {ex}");
			}
		}

		public override void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr)
		{
			if (_connectionLost || _client == null || !_initialized || _client.State != ClientState.Playing)
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

				// Skip prediction for dead players — server won't process input
				if (!_simulation.LocalPlayer.IsDead)
				{
					// Apply local prediction (same physics as server)
					_simulation.LocalPlayer.UpdatePhysics(_simulation.Map, _simulation.PhysicsData, Dt, InMgr);

					// Record predicted state
						_prediction.RecordPrediction(
							_client.LocalTick,
							_simulation.LocalPlayer.Position,
							_simulation.LocalPlayer.GetVelocity()
						);
					}

					// Entity AI/physics are server-authoritative; IsAuthority=false skips them.
				// UpdateLockstep is still called for any non-authoritative cleanup.
				_simulation.Entities.UpdateLockstep(TotalTime, Dt, InMgr);
			}
			catch (Exception ex)
			{
				_logging.ClientWriteLine($"MultiplayerGameState: UpdateLockstep exception: {ex}");
			}
		}

		public override void Draw(float TimeAlpha, ref GameFrameInfo LastFrame, ref GameFrameInfo FInfo)
		{
			if (!_initialized || _simulation?.LocalPlayer == null)
				return;

			try
			{
				_simulation.LocalPlayer.UpdateFPSCamera(ref FInfo);

				// Sync render camera from physics camera
				_simulation.LocalPlayer.RenderCam = _simulation.LocalPlayer.Cam;

				// Decay correction smooth offset toward zero
				float dt = Raylib.GetFrameTime();
				if (_correctionSmoothOffset.LengthSquared() > 0.0001f)
				{
					_correctionSmoothOffset *= MathF.Max(0f, 1f - CorrectionSmoothRate * dt);
				}
				else
				{
					_correctionSmoothOffset = Vector3.Zero;
				}

				// Apply smooth offset to render camera (physics position is unaffected)
				if (_correctionSmoothOffset != Vector3.Zero)
				{
					var cam = _simulation.LocalPlayer.RenderCam;
					cam.Position += _correctionSmoothOffset;
					cam.Target += _correctionSmoothOffset;
					_simulation.LocalPlayer.RenderCam = cam;
				}

				// Populate frame info for GameWindow interpolation
				FInfo.Empty = false;
				FInfo.Pos = _simulation.LocalPlayer.RenderCam.Position;
				FInfo.Cam = _simulation.LocalPlayer.RenderCam;
				FInfo.CamAngle = _simulation.LocalPlayer.GetCamAngle();
				FInfo.FeetPosition = _simulation.LocalPlayer.FeetPosition + _correctionSmoothOffset;
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

					// Play footstep sounds for remote players based on velocity detection
					if (_snd != null && remotePlayer.TryPlayFootstep())
					{
						_snd.PlayCombo("walk", _simulation.LocalPlayer.Position, _simulation.LocalPlayer.GetForward(), remotePlayer.Position);
					}
				}

				// Update entity interpolation from server snapshots
				UpdateEntityInterpolation(currentTime);

				Raylib.ClearBackground(_simulation.DayNight.SkyColor);

				CalculateCelestialPositions();
				DrawCelestialBodies();

				Raylib.BeginMode3D(_simulation.LocalPlayer.RenderCam);

				Shader defaultShader = ResMgr.GetShader("default");
				Raylib.BeginShaderMode(defaultShader);

				Draw3D(TimeAlpha, ref LastFrame, ref FInfo);

				Raylib.EndShaderMode();

				Raylib.EndMode3D();

				// Draw viewmodel overlay AFTER EndMode3D so the 2D texture is not
				// projected through the 3D camera (which would misplace it on screen).
				_simulation.LocalPlayer.DrawViewModelOverlay();
			}
			catch (Exception ex)
			{
				_logging.ClientWriteLine($"MultiplayerGameState: Draw exception: {ex}");
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
					// Loading screen
					Raylib.ClearBackground(new Color(30, 30, 40, 255));

					// Update loading UI state
					if (_loadingStatusLabel != null)
						_loadingStatusLabel.Text = _statusText ?? "";

					bool hasError = !string.IsNullOrEmpty(_errorText);
					if (_loadingErrorLabel != null)
					{
						_loadingErrorLabel.Text = _errorText ?? "";
						_loadingErrorLabel.Visible = hasError;
					}
					if (_loadingHintLabel != null)
						_loadingHintLabel.Visible = hasError;

					if (_client?.State == ClientState.Loading && _loadingProgressBar != null)
					{
						_loadingProgressBar.Visible = true;
						_loadingProgressBar.Value = _client.WorldReceiver.Progress;
					}
					else if (_loadingProgressBar != null)
					{
						_loadingProgressBar.Visible = false;
					}

					if (hasError && Raylib.IsKeyPressed(KeyboardKey.Escape))
					{
						DisconnectAndReturn("Cancelled");
						return;
					}

					_gui?.Tick(deltaTime, _totalTime);
					return;
				}

				// In-game HUD
				DrawUnderwaterOverlay();

				// Crosshair and FPS (Raylib primitives)
				Raylib.DrawCircleLines(_gameWindow.Width / 2, _gameWindow.Height / 2, 5, Color.White);
				Raylib.DrawFPS(10, 10);

				// Remote player name tags (3D projection, stays as Raylib)
				DrawRemotePlayerNameTags();

				// Update FishUI HUD state
				UpdateHUDInfo();
				UpdateHealthBar();
				UpdateConnectionStatus();

				// Network stats panel visibility
				if (_netStatsPanel != null)
					_netStatsPanel.Visible = _showNetStats;
				if (_showNetStats)
					UpdateNetStats();

				// Player list panel visibility
				if (_playerListPanel != null)
					_playerListPanel.Visible = _showPlayerList;
				if (_showPlayerList)
					UpdatePlayerList();

				// Death overlay visibility
				if (_deathOverlayPanel != null)
					_deathOverlayPanel.Visible = _simulation?.LocalPlayer != null && _simulation.LocalPlayer.IsDead;

				// Connection lost overlay
				if (_connectionLostPanel != null)
				{
					_connectionLostPanel.Visible = _connectionLost;
					if (_connectionLost && _connectionLostReasonLabel != null)
						_connectionLostReasonLabel.Text = _disconnectReason ?? "";
				}

				// FishUI draws all HUD controls (inventory, health, kill feed, overlays)
				_gui.Tick(deltaTime, _totalTime);
			}
			catch (Exception ex)
			{
				_logging.ClientWriteLine($"MultiplayerGameState: Draw2D exception: {ex}");
			}
		}

		// ======================================= Network Event Handlers ==========================================

		private void OnConnected(ConnectAcceptPacket accept)
		{
			_logging.ClientWriteLine($"MultiplayerGameState: Connected as player {accept.PlayerId}");
			_statusText = "Connected! Loading world...";
		}

		private void OnDisconnected(string reason)
		{
			_logging.ClientWriteLine($"MultiplayerGameState: Disconnected: {reason}");

			if (_initialized)
			{
				// Was in-game — show "Connection Lost" overlay with reconnect/return options
				_connectionLost = true;
				_disconnectReason = reason;
				Raylib.EnableCursor();
			}
			else
			{
				// Was still connecting/loading — show error on the status screen
				_statusText = "";
				_errorText = $"Disconnected: {reason}";
			}
		}

		private void OnConnectionRejected(string reason)
		{
			_logging.ClientWriteLine($"MultiplayerGameState: Rejected: {reason}");
			_statusText = "";
			_errorText = $"Connection rejected: {reason}";
		}

		private void OnWorldDataReady(byte[] compressedData)
		{
			_logging.ClientWriteLine($"MultiplayerGameState: World data received ({compressedData.Length} bytes compressed)");
			_statusText = "Building world...";

			try
			{
				// Create game simulation
				_logging.ClientWriteLine("MultiplayerGameState: Creating GameSimulation...");
				_simulation = new GameSimulation(Eng);
				_simulation.DayNight.IsAuthority = false; // Server controls time
				_logging.ClientWriteLine("MultiplayerGameState: GameSimulation created");

				// Load world
				_logging.ClientWriteLine("MultiplayerGameState: Reading ChunkMap from stream...");
				using (var ms = new MemoryStream(compressedData))
				{
					_simulation.Map.Read(ms);
				}
				_logging.ClientWriteLine("MultiplayerGameState: ChunkMap loaded successfully");

				_logging.ClientWriteLine("MultiplayerGameState: Computing lighting...");
				_simulation.Map.ComputeLighting();
				_logging.ClientWriteLine("MultiplayerGameState: Lighting computed");

				// Create GUI
				_logging.ClientWriteLine("MultiplayerGameState: Creating gameplay UI...");
				CreateGameplayUI();
				_logging.ClientWriteLine("MultiplayerGameState: Gameplay UI created");

				// Create sound
				_logging.ClientWriteLine("MultiplayerGameState: Creating SoundMgr...");
				_snd = new SoundMgr();
				_snd.Init();
				_logging.ClientWriteLine("MultiplayerGameState: SoundMgr initialized");

				// Create particle system
				_logging.ClientWriteLine("MultiplayerGameState: Creating ParticleSystem...");
				_particle = new ParticleSystem();
				_particle.Init(
					(pt) => _simulation.Map.Collide(pt, Vector3.Zero, out Vector3 _),
					(pt) => _simulation.Map.GetBlock(pt),
					(pt) => _simulation.Map.GetLightColor(pt)
				);
				_logging.ClientWriteLine("MultiplayerGameState: ParticleSystem initialized");

				// Create local player
				_logging.ClientWriteLine($"MultiplayerGameState: Creating Player (id={_client.PlayerId}, name={_playerName})...");
				var ply = new Player(Eng, _gui, _playerName, true, _snd, _client.PlayerId);
				_logging.ClientWriteLine("MultiplayerGameState: Player created, adding to PlayerManager...");
				_simulation.Players.AddLocalPlayer(_client.PlayerId, ply);

				_logging.ClientWriteLine("MultiplayerGameState: InitGUI...");
				ply.InitGUI(_gameWindow, _gui);
				_logging.ClientWriteLine("MultiplayerGameState: InitGUI complete");

				_logging.ClientWriteLine("MultiplayerGameState: Player.Init...");
				ply.Init(_simulation.Map);
				_logging.ClientWriteLine("MultiplayerGameState: Player.Init complete");

				ply.OnMenuToggled = (cursorVisible) =>
				{
					if (_debugMenuWindow != null)
					{
						_debugMenuWindow.Visible = cursorVisible;
						if (cursorVisible)
							_debugMenuWindow.BringToFront();
					}
				};

				_logging.ClientWriteLine("MultiplayerGameState: SetPosition...");
				ply.SetPosition(new Vector3(32, 73, 19)); // Default spawn, server will correct
				_logging.ClientWriteLine("MultiplayerGameState: SetPosition complete");

				// Set entity manager to non-authoritative (server owns entity state)
				_simulation.Entities.IsAuthority = false;

				// Process any InventoryUpdate packet that arrived before the simulation was created
				if (_pendingInventoryUpdate != null)
				{
					_logging.ClientWriteLine($"MultiplayerGameState: Replaying buffered InventoryUpdatePacket ({_pendingInventoryUpdate.Slots.Length} slots)...");
					HandleInventoryUpdate(_pendingInventoryUpdate);
					_pendingInventoryUpdate = null;
				}

				// Process any PlayerJoined packets that arrived before the simulation was created
				if (_pendingPlayerJoins.Count > 0)
				{
					_logging.ClientWriteLine($"MultiplayerGameState: Processing {_pendingPlayerJoins.Count} buffered PlayerJoined packet(s)...");
					foreach (var pending in _pendingPlayerJoins)
					{
						HandlePlayerJoined(pending);
					}
					_pendingPlayerJoins.Clear();
				}

				// Process any entity packets that arrived before the simulation was created
				if (_pendingEntityPackets.Count > 0)
				{
					_logging.ClientWriteLine($"MultiplayerGameState: Processing {_pendingEntityPackets.Count} buffered entity packet(s)...");
					float replayTime = (float)Raylib.GetTime();
					foreach (var pending in _pendingEntityPackets)
					{
						switch (pending)
						{
							case EntitySpawnPacket spawn:
								HandleEntitySpawn(spawn);
								break;
							case EntityRemovePacket remove:
								HandleEntityRemove(remove);
								break;
							case EntitySnapshotPacket snapshot:
								HandleEntitySnapshot(snapshot, replayTime);
								break;
						}
					}
					_pendingEntityPackets.Clear();
				}

				// Finish loading
				_logging.ClientWriteLine("MultiplayerGameState: FinishLoading...");
				_client.FinishLoading();
				_initialized = true;
				_statusText = "";
				_errorText = "";

				Raylib.DisableCursor();

				_logging.ClientWriteLine("MultiplayerGameState: World loaded, entering gameplay");
			}
			catch (Exception ex)
			{
				_logging.ClientWriteLine($"MultiplayerGameState: Failed to load world: {ex}");
				_statusText = "";
				_errorText = $"Failed to load world: {ex.Message}";
			}
		}

		private void OnWorldTransferFailed(string error)
		{
			_logging.ClientWriteLine($"MultiplayerGameState: World transfer failed: {error}");
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

				case BlockChangePacket blockChange:
					HandleBlockChange(blockChange);
					break;

				case EntitySpawnPacket entitySpawn:
					HandleEntitySpawn(entitySpawn);
					break;

				case EntityRemovePacket entityRemove:
					HandleEntityRemove(entityRemove);
					break;

				case EntitySnapshotPacket entitySnapshot:
					HandleEntitySnapshot(entitySnapshot, currentTime);
					break;

				case WeaponFireEffectPacket fireEffect:
					HandleWeaponFireEffect(fireEffect);
					break;

				case PlayerDamagePacket damage:
					HandlePlayerDamage(damage);
					break;

				case InventoryUpdatePacket inventoryUpdate:
					HandleInventoryUpdate(inventoryUpdate);
					break;

				case SoundEventPacket soundEvent:
					HandleSoundEvent(soundEvent);
					break;

				case ChatMessagePacket chatMsg:
					HandleChatMessage(chatMsg);
					break;

				case KillFeedPacket killFeed:
					HandleKillFeed(killFeed);
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
					// Sync health from server
					_simulation.LocalPlayer.Health = entry.Health;

					// Skip prediction reconciliation until the server has processed
					// at least one of our InputStatePackets
					if (entry.LastInputTick <= 0)
						continue;

					// Local player — reconciliation using server's last-processed input tick
					bool needsCorrection = _prediction.ProcessServerSnapshot(
						entry.LastInputTick,
						entry.Position,
						entry.Velocity
					);

					if (needsCorrection)
					{
						// Capture pre-correction position for visual smoothing
						Vector3 preCorrection = _simulation.LocalPlayer.Position;

						PredictionReconciler.Reconcile(
							_simulation.LocalPlayer,
							entry.Position,
							entry.Velocity,
							entry.LastInputTick,
							_client.LocalTick,
							_inputBuffer,
							_prediction,
							_simulation.Map,
							_simulation.PhysicsData,
							DeltaTime
						);

						// Compute visual offset: difference between where we were and where we should be.
						// If the error is small (< SnapThreshold), smooth visually instead of hard-snapping.
						Vector3 postCorrection = _simulation.LocalPlayer.Position;
						Vector3 delta = preCorrection - postCorrection;

						if (delta.LengthSquared() < ClientPrediction.SnapThreshold * ClientPrediction.SnapThreshold)
						{
							_correctionSmoothOffset += delta;
						}
						else
						{
							// Large correction (teleport) — snap immediately
							_correctionSmoothOffset = Vector3.Zero;
						}
					}
				}
				else
				{
					// Remote player — apply snapshot for interpolation
					var remote = _simulation.Players.GetRemotePlayer(entry.PlayerId);
					if (remote != null)
					{
						remote.ApplySnapshot(entry.Position, entry.Velocity, entry.CameraAngle, entry.AnimationState, currentTime);
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

			_logging.ClientWriteLine($"MultiplayerGameState: Player joined: {joined.PlayerName} (ID {joined.PlayerId})");

			var remote = new RemotePlayer(joined.PlayerId, joined.PlayerName, Eng);
			remote.SetPosition(joined.Position);
			_simulation.Players.AddRemotePlayer(remote);
		}

		private void HandlePlayerLeft(PlayerLeftPacket left)
		{
			if (_simulation == null)
				return;

			_logging.ClientWriteLine($"MultiplayerGameState: Player left (ID {left.PlayerId})");
			_simulation.Players.RemoveRemotePlayer(left.PlayerId);
		}

		private void HandleBlockChange(BlockChangePacket blockChange)
		{
			if (_simulation == null)
				return;

			_simulation.Map.SetBlock(blockChange.X, blockChange.Y, blockChange.Z, (BlockType)blockChange.BlockType);
		}

		/// <summary>
		/// Handles an <see cref="EntitySpawnPacket"/> from the server.
		/// Creates the entity locally with the server-assigned network ID.
		/// </summary>
		private void HandleEntitySpawn(EntitySpawnPacket packet)
		{
			if (_simulation == null)
			{
				_pendingEntityPackets.Add(packet);
				return;
			}

			// Don't duplicate if already exists
			if (_simulation.Entities.GetEntityByNetworkId(packet.NetworkId) != null)
				return;

			VoxEntity entity = CreateEntityByType(packet.EntityType);
			if (entity == null)
			{
				_logging.ClientWriteLine($"MultiplayerGameState: Unknown entity type '{packet.EntityType}'");
				return;
			}

			entity.SetPosition(packet.Position);

			// Read spawn properties (size, model, subclass data)
			if (packet.Properties.Length > 0)
			{
				using var ms = new MemoryStream(packet.Properties);
				using var reader = new BinaryReader(ms);
				entity.ReadSpawnProperties(reader);
			}

			_simulation.Entities.SpawnWithNetworkId(_simulation, entity, packet.NetworkId);
			_logging.ClientWriteLine($"MultiplayerGameState: Entity spawned: {packet.EntityType} (netId={packet.NetworkId})");
		}

		/// <summary>
		/// Handles an <see cref="EntityRemovePacket"/> from the server.
		/// Removes the entity by network ID.
		/// </summary>
		private void HandleEntityRemove(EntityRemovePacket packet)
		{
			if (_simulation == null)
			{
				_pendingEntityPackets.Add(packet);
				return;
			}

			var removed = _simulation.Entities.Remove(packet.NetworkId);
			_entitySnapshots.Remove(packet.NetworkId);

			if (removed != null)
				_logging.ClientWriteLine($"MultiplayerGameState: Entity removed (netId={packet.NetworkId})");
		}

		/// <summary>
		/// Handles an <see cref="EntitySnapshotPacket"/> from the server.
		/// Stores the snapshot in the interpolation buffer for smooth rendering.
		/// </summary>
		private void HandleEntitySnapshot(EntitySnapshotPacket packet, float currentTime)
		{
			if (_simulation == null)
			{
				_pendingEntityPackets.Add(packet);
				return;
			}

			var entity = _simulation.Entities.GetEntityByNetworkId(packet.NetworkId);
			if (entity == null)
				return;

			// Add to interpolation buffer
			if (!_entitySnapshots.TryGetValue(packet.NetworkId, out var buffer))
			{
				buffer = new SnapshotBuffer<EntitySnapshot>();
				_entitySnapshots[packet.NetworkId] = buffer;
			}

			buffer.Add(new EntitySnapshot
			{
				Position = packet.Position,
				Velocity = packet.Velocity,
				AnimationState = packet.AnimationState,
			}, currentTime);
		}

		/// <summary>
		/// Updates entity positions from interpolation buffers. Called each frame.
		/// </summary>
		private void UpdateEntityInterpolation(float currentTime)
		{
			float renderTime = currentTime - EntityInterpolationDelay;

			foreach (var kvp in _entitySnapshots)
			{
				int networkId = kvp.Key;
				var buffer = kvp.Value;

				var entity = _simulation.Entities.GetEntityByNetworkId(networkId);
				if (entity == null)
					continue;

				if (buffer.Sample(renderTime, out var from, out var to, out float t))
				{
					entity.Position = Vector3.Lerp(from.Position, to.Position, t);
					entity.Velocity = Vector3.Lerp(from.Velocity, to.Velocity, t);
				}
				else if (buffer.Count == 1)
				{
					// Only one snapshot — just snap to it
					entity.Position = from.Position;
					entity.Velocity = from.Velocity;
				}

				// Update animation from latest snapshot state
				UpdateEntityAnimation(entity, to.AnimationState, Raylib.GetFrameTime());
			}
		}

		/// <summary>
		/// Applies animation state from the server to an entity.
		/// 0 = idle, 1 = walk, 2 = attack.
		/// </summary>
		private static void UpdateEntityAnimation(VoxEntity entity, byte animationState, float deltaTime)
		{
			if (entity is VEntNPC npc)
			{
				var animator = npc.GetAnimator();
				if (animator != null)
				{
					string targetAnim = animationState switch
					{
						1 => "walk",
						2 => "attack",
						_ => "idle",
					};

					if (animator.CurrentAnimation != targetAnim)
						animator.Play(targetAnim);

					animator.Update(deltaTime);
				}
			}

			// Update cosmetic visuals (rotation) on the client
			entity.UpdateVisuals(deltaTime);
		}

		/// <summary>
		/// Creates a <see cref="VoxEntity"/> instance from a type name string.
		/// </summary>
		private static VoxEntity CreateEntityByType(string entityType)
		{
			return entityType switch
			{
				"VEntNPC" => new VEntNPC(),
				"VEntPickup" => new VEntPickup(),
				"VEntSlidingDoor" => new VEntSlidingDoor(),
				"VEntPlayer" => new VEntPlayer(),
				_ => null,
			};
		}

		/// <summary>
		/// Collects local block changes (from player placing/removing blocks) and sends them
		/// to the server as <see cref="BlockPlaceRequestPacket"/> or <see cref="BlockRemoveRequestPacket"/>.
		/// The local change is kept as optimistic client prediction; the server will validate
		/// and broadcast authoritative <see cref="BlockChangePacket"/>s to all clients.
		/// </summary>
		private void SendPendingBlockChanges(float currentTime)
		{
			if (_simulation == null || _client == null || !_client.IsConnected)
				return;

			var changes = _simulation.Map.GetPendingChanges();
			if (changes.Count == 0)
				return;

			foreach (var change in changes)
			{
				if (change.NewType == BlockType.None)
				{
					var packet = new BlockRemoveRequestPacket
					{
						X = change.X,
						Y = change.Y,
						Z = change.Z,
					};
					_client.Send(packet, true, currentTime);
				}
				else
				{
					var packet = new BlockPlaceRequestPacket
					{
						X = change.X,
						Y = change.Y,
						Z = change.Z,
						BlockType = (byte)change.NewType,
					};
					_client.Send(packet, true, currentTime);
				}
			}

			_simulation.Map.ClearPendingChanges();
		}

		/// <summary>
		/// Sends a <see cref="WeaponFirePacket"/> to the server for authoritative hit resolution.
		/// Called by <see cref="WeaponGun.OnLeftClick"/> in multiplayer mode.
		/// </summary>
		public void SendWeaponFire(Vector3 origin, Vector3 direction)
		{
			if (_client == null || !_client.IsConnected)
				return;

			var packet = new WeaponFirePacket
			{
				WeaponType = 0,
				AimOrigin = origin,
				AimDirection = direction,
			};
			_client.Send(packet, true, (float)Raylib.GetTime());
		}

		/// <summary>
		/// Spawns predicted fire effects (tracer and hit particles) based on a local client-side raycast.
		/// Called immediately on weapon fire so the local player sees instant visual feedback.
		/// The server's authoritative <see cref="WeaponFireEffectPacket"/> suppresses duplicate visuals for the local player.
		/// </summary>
		public void SpawnPredictedFireEffects(Vector3 origin, Vector3 direction, float maxRange)
		{
			if (_simulation == null || _particle == null)
				return;

			Vector3 muzzlePos = origin + direction * 0.5f;

			// --- Raycast against world blocks ---
			float worldDist = float.MaxValue;
			Vector3 worldHitPos = Vector3.Zero;
			Vector3 worldHitNormal = Vector3.Zero;

			if (_simulation.Map.RaycastPrecise(origin, maxRange, direction, out Vector3 preciseHitPoint, out Vector3 faceDir))
			{
				worldDist = Vector3.Distance(origin, preciseHitPoint);
				worldHitPos = preciseHitPoint;
				worldHitNormal = faceDir;
			}

			// --- Raycast against entities ---
			RaycastHit entityHit = _simulation.Entities.Raycast(origin, direction, maxRange);

			// --- Determine closest hit ---
			FireHitType hitType = FireHitType.None;
			Vector3 hitPos = origin + direction * maxRange;
			Vector3 hitNormal = -direction;
			float closestDist = maxRange;
			VoxEntity hitEntity = null;

			if (entityHit.Hit && entityHit.Distance < closestDist)
			{
				closestDist = entityHit.Distance;
				hitType = FireHitType.Entity;
				hitPos = entityHit.HitPosition;
				hitNormal = entityHit.HitNormal;
				hitEntity = entityHit.Entity;
			}

			if (worldDist < closestDist)
			{
				closestDist = worldDist;
				hitType = FireHitType.World;
				hitPos = worldHitPos;
				hitNormal = worldHitNormal;
				hitEntity = null;
			}

			// Tracer line from muzzle to hit point
			_particle.SpawnTracer(muzzlePos, hitPos);

			// Hit particles based on predicted hit type
			switch (hitType)
			{
				case FireHitType.Entity:
					if (hitEntity is VEntNPC)
					{
						for (int i = 0; i < 8; i++)
						{
							_particle.SpawnBlood(hitPos, hitNormal * 0.5f, (0.8f + (float)Random.Shared.NextDouble() * 0.4f) * 0.85f);
						}
					}
					else
					{
						for (int i = 0; i < 6; i++)
						{
							float forceFactor = 10.6f;
							float randomUnitFactor = 0.6f;
							if (hitNormal.Y == 0)
							{
								forceFactor *= 2;
								randomUnitFactor = 0.4f;
							}
							Vector3 rndDir = Vector3.Normalize(hitNormal + Utils.GetRandomUnitVector() * randomUnitFactor);
							_particle.SpawnSpark(hitPos, rndDir * forceFactor, Color.White, (float)(Random.Shared.NextDouble() + 0.5));
						}
					}
					break;

				case FireHitType.World:
					for (int i = 0; i < 6; i++)
					{
						float forceFactor = 10.6f;
						float randomUnitFactor = 0.6f;
						if (hitNormal.Y == 0)
						{
							forceFactor *= 2;
							randomUnitFactor = 0.4f;
						}
						Vector3 rndDir = Vector3.Normalize(hitNormal + Utils.GetRandomUnitVector() * randomUnitFactor);
						_particle.SpawnFire(hitPos, rndDir * forceFactor, Color.White, (float)(Random.Shared.NextDouble() + 0.5));
					}
					break;
			}
		}

		/// <summary>
		/// Handles a <see cref="WeaponFireEffectPacket"/> from the server.
		/// Spawns tracer, blood, and spark particles based on the hit result.
		/// For the local player, tracer and hit particles are suppressed (already predicted locally);
		/// only authoritative entity effects (NPC twitch) and unpredictable player-hit blood are applied.
		/// For other players, full effects and fire sound are played.
		/// </summary>
		private void HandleWeaponFireEffect(WeaponFireEffectPacket packet)
		{
			if (_simulation == null || _particle == null)
				return;

			Vector3 muzzlePos = packet.Origin + packet.Direction * 0.5f;
			bool isLocalPlayer = packet.PlayerId == _client.PlayerId;

			// Play fire sound for other players' shots
			if (!isLocalPlayer && _snd != null)
			{
				_snd.PlayCombo("shoot1", _simulation.LocalPlayer.Position, _simulation.LocalPlayer.GetForward(), packet.Origin);
			}

			// Tracer line from muzzle to hit point (skip for local player — predicted locally)
			if (!isLocalPlayer)
				_particle.SpawnTracer(muzzlePos, packet.HitPosition);

			FireHitType hitType = (FireHitType)packet.HitType;
			switch (hitType)
			{
				case FireHitType.Entity:
					// Always apply authoritative NPC twitch from server
					bool isNpcHit = false;
					if (packet.EntityNetworkId != 0)
					{
						VoxEntity hitEntity = _simulation.Entities.GetEntityByNetworkId(packet.EntityNetworkId);
						if (hitEntity is VEntNPC npc)
						{
							isNpcHit = true;
							npc.TwitchBodyPart("body", packet.Direction);
						}
					}

					// Particles only for remote players (local player predicted them)
					if (!isLocalPlayer)
					{
						if (isNpcHit)
						{
							for (int i = 0; i < 8; i++)
							{
								_particle.SpawnBlood(packet.HitPosition, packet.HitNormal * 0.5f, (0.8f + (float)Random.Shared.NextDouble() * 0.4f) * 0.85f);
							}
						}
						else
						{
							for (int i = 0; i < 6; i++)
							{
								float forceFactor = 10.6f;
								float randomUnitFactor = 0.6f;

								if (packet.HitNormal.Y == 0)
								{
									forceFactor *= 2;
									randomUnitFactor = 0.4f;
								}

								Vector3 rndDir = Vector3.Normalize(packet.HitNormal + Utils.GetRandomUnitVector() * randomUnitFactor);
								_particle.SpawnSpark(packet.HitPosition, rndDir * forceFactor, Color.White, (float)(Random.Shared.NextDouble() + 0.5));
							}
						}
					}
					break;

				case FireHitType.Player:
					// Always show blood for player hits — client cannot predict these
					for (int i = 0; i < 8; i++)
					{
						_particle.SpawnBlood(packet.HitPosition, packet.HitNormal * 0.5f, (0.8f + (float)Random.Shared.NextDouble() * 0.4f) * 0.85f);
					}
					break;

				case FireHitType.World:
					// Particles only for remote players (local player predicted them)
					if (!isLocalPlayer)
					{
						for (int i = 0; i < 6; i++)
						{
							float forceFactor = 10.6f;
							float randomUnitFactor = 0.6f;

							if (packet.HitNormal.Y == 0)
							{
								forceFactor *= 2;
								randomUnitFactor = 0.4f;
							}

							Vector3 rndDir = Vector3.Normalize(packet.HitNormal + Utils.GetRandomUnitVector() * randomUnitFactor);
							_particle.SpawnFire(packet.HitPosition, rndDir * forceFactor, Color.White, (float)(Random.Shared.NextDouble() + 0.5));
						}
					}
					break;
			}
		}

		/// <summary>
		/// Handles a <see cref="SoundEventPacket"/> from the server.
		/// Plays the appropriate sound at the event position. Skips events caused by the local player
		/// (the local player already played the sound optimistically on input).
		/// </summary>
		private void HandleSoundEvent(SoundEventPacket packet)
		{
			if (_simulation == null || _snd == null)
				return;

			// Local player already played the sound on their end
			if (packet.SourcePlayerId == _client.PlayerId)
				return;

			Vector3 ears = _simulation.LocalPlayer.Position;
			Vector3 dir = _simulation.LocalPlayer.GetForward();

			switch ((SoundEventType)packet.EventType)
			{
				case SoundEventType.BlockBreak:
					_snd.PlayCombo("block_break", ears, dir, packet.Position);
					break;

				case SoundEventType.BlockPlace:
					_snd.PlayCombo("block_place", ears, dir, packet.Position);
					break;
			}
		}

		/// <summary>
		/// Handles a <see cref="PlayerDamagePacket"/> from the server.
		/// Updates local player health and logs damage events.
		/// </summary>
		private void HandlePlayerDamage(PlayerDamagePacket packet)
		{
			if (_simulation == null)
				return;

			if (packet.TargetPlayerId == _client.PlayerId)
			{
				// Local player took damage — health is synced via WorldSnapshot
				_logging.ClientWriteLine($"MultiplayerGameState: Took {packet.DamageAmount} damage from player [{packet.SourcePlayerId}]. Health: {_simulation.LocalPlayer.Health}");
			}
		}

		/// <summary>
		/// Handles a <see cref="KillFeedPacket"/> from the server.
		/// Adds a kill event entry to the on-screen kill feed.
		/// </summary>
		private void HandleKillFeed(KillFeedPacket packet)
		{
			string weaponName = packet.WeaponType switch
			{
				0 => "Gun",
				_ => "Gun",
			};

			string text = $"{packet.KillerName} killed {packet.VictimName} with {weaponName}";
			_killFeedToast?.Show(text, ToastType.Error, KillFeedDuration);
		}

		/// <summary>
		/// Handles a <see cref="ChatMessagePacket"/> from the server.
		/// Resolves the sender name and displays the message in the chat toast.
		/// </summary>
		private void HandleChatMessage(ChatMessagePacket packet)
		{
			string senderName;
			if (packet.PlayerId < 0)
			{
				// Server message — already formatted
				senderName = null;
			}
			else if (packet.PlayerId == _client?.PlayerId)
			{
				senderName = _playerName ?? "You";
			}
			else
			{
				var remote = _simulation?.Players?.GetRemotePlayer(packet.PlayerId);
				senderName = remote?.PlayerName ?? $"Player {packet.PlayerId}";
			}

			string displayText = senderName != null ? $"{senderName}: {packet.Message}" : packet.Message;
			_chatToast?.Show(displayText, ToastType.Info, ChatMessageDuration);
			_logging.ClientWriteLine($"[Chat] {displayText}");
		}

		/// <summary>
		/// Handles an <see cref="InventoryUpdatePacket"/> from the server.
		/// Updates local player inventory item counts to match the server-authoritative state.
		/// </summary>
		private void HandleInventoryUpdate(InventoryUpdatePacket packet)
		{
			if (_simulation?.LocalPlayer == null)
			{
				// Simulation not created yet — buffer for replay after world load
				_pendingInventoryUpdate = packet;
				_logging.ClientWriteLine($"MultiplayerGameState: Buffered InventoryUpdatePacket ({packet.Slots.Length} slots) — simulation not ready");
				return;
			}

			foreach (var slot in packet.Slots)
			{
				InventoryItem item = _simulation.LocalPlayer.GetInventoryItem(slot.SlotIndex);
				if (item != null)
					item.Count = slot.Count;
			}
		}

		/// <summary>
		/// Draws name tags above remote players as billboard text in screen space.
		/// </summary>
		private void DrawRemotePlayerNameTags()
		{
			if (_simulation?.LocalPlayer == null || _simulation.Players == null)
				return;

			Camera3D camera = _simulation.LocalPlayer.RenderCam;

			foreach (var remotePlayer in _simulation.Players.GetAllRemotePlayers())
			{
				remotePlayer.DrawNameTag(camera, _simulation.Map);
			}
		}

		// ======================================= Helper Methods =================================================

		private void OpenChatInput()
		{
			_chatOpen = true;
			if (_chatInputPanel != null)
				_chatInputPanel.Visible = true;
			if (_chatInputBox != null)
				_chatInputBox.Text = "";
			Raylib.EnableCursor();
		}

		private void CloseChatInput()
		{
			_chatOpen = false;
			if (_chatInputPanel != null)
				_chatInputPanel.Visible = false;
			if (_chatInputBox != null)
				_chatInputBox.Text = "";
			Raylib.DisableCursor();
		}

		private void SubmitChatMessage()
		{
			string message = _chatInputBox?.Text?.Trim();
			CloseChatInput();

			if (string.IsNullOrEmpty(message))
				return;

			// Handle /commands — route to hosted server if available
			if (message.StartsWith('/'))
			{
				string command = message.Substring(1);
				var server = Eng.MainMenuState?.HostedServer;
				if (server != null)
				{
					server.ExecuteCommand(command);
					_chatToast?.Show($"[Command] /{command}", ToastType.Info, ChatMessageDuration);
				}
				else
				{
					_chatToast?.Show("Commands are only available on the host.", ToastType.Warning, ChatMessageDuration);
				}
				return;
			}

			// Send chat message to server
			if (_client != null && _client.IsConnected)
			{
				var packet = new ChatMessagePacket
				{
					PlayerId = _client.PlayerId,
					Message = message
				};
				_client.Send(packet, true, (float)Raylib.GetTime());
			}
		}


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
			_connectionLost = false;
			_disconnectReason = "";
			_showNetStats = false;
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
			_pendingEntityPackets.Clear();
			_entitySnapshots.Clear();

			// FishUI controls are owned by _gui; null the references
			_loadingStatusLabel = null;
			_loadingErrorLabel = null;
			_loadingHintLabel = null;
			_loadingProgressBar = null;
			_hudInfoLabel = null;
			_healthBar = null;
			_healthLabel = null;
			_killFeedToast = null;
			_netStatsPanel = null;
			_netStatsInfoLabel = null;
			_deathOverlayPanel = null;
			_deathTitleLabel = null;
			_deathSubtitleLabel = null;
			_connectionLostPanel = null;
			_connectionLostTitleLabel = null;
			_connectionLostReasonLabel = null;
			_connectionLostReconnectLabel = null;
			_connectionLostMenuLabel = null;
			_debugMenuWindow = null;
			_connectionStatusLabel = null;
			_chatToast = null;
			_chatInputPanel = null;
			_chatInputBox = null;
			_chatOpen = false;
			_playerListPanel = null;
			_playerListInfoLabel = null;
			_showPlayerList = false;
		}

		// ======================================= FishUI Setup =================================================

		public override void OnResize(GameWindow window)
		{
			base.OnResize(window);
			_gui?.OnResize(window.Width, window.Height);
			PositionHUDControls(window.Width, window.Height);
		}

		/// <summary>
		/// Creates FishUI controls for the loading/connecting screen.
		/// Called from <see cref="Connect"/> before any network activity.
		/// </summary>
		private void CreateLoadingUI()
		{
			int screenW = _gameWindow.Width;
			int screenH = _gameWindow.Height;

			_loadingStatusLabel = new Label
			{
				Text = "",
				Position = new Vector2(screenW / 2f - 200, screenH / 2f - 30),
				Size = new Vector2(400, 30),
				Alignment = Align.Center,
			};
			_gui.AddControl(_loadingStatusLabel);

			_loadingErrorLabel = new Label
			{
				Text = "",
				Position = new Vector2(screenW / 2f - 200, screenH / 2f + 10),
				Size = new Vector2(400, 24),
				Alignment = Align.Center,
				Visible = false,
			};
			_loadingErrorLabel.SetColorOverride("Text", new FishColor(255, 60, 60, 255));
			_gui.AddControl(_loadingErrorLabel);

			_loadingHintLabel = new Label
			{
				Text = "Press ESC to return to menu",
				Position = new Vector2(screenW / 2f - 200, screenH / 2f + 50),
				Size = new Vector2(400, 22),
				Alignment = Align.Center,
				Visible = false,
			};
			_loadingHintLabel.SetColorOverride("Text", new FishColor(150, 150, 150, 255));
			_gui.AddControl(_loadingHintLabel);

			_loadingProgressBar = new ProgressBar
			{
				Value = 0f,
				Position = new Vector2(screenW / 2f - 150, screenH / 2f + 20),
				Size = new Vector2(300, 20),
				Visible = false,
			};
			_gui.AddControl(_loadingProgressBar);
		}

		/// <summary>
		/// Removes loading screen FishUI controls and creates gameplay HUD controls.
		/// Called from <see cref="OnWorldDataReady"/> after world loading completes.
		/// </summary>
		private void CreateGameplayUI()
		{
			// Remove loading controls
			if (_loadingStatusLabel != null) { _gui.RemoveControl(_loadingStatusLabel); _loadingStatusLabel = null; }
			if (_loadingErrorLabel != null) { _gui.RemoveControl(_loadingErrorLabel); _loadingErrorLabel = null; }
			if (_loadingHintLabel != null) { _gui.RemoveControl(_loadingHintLabel); _loadingHintLabel = null; }
			if (_loadingProgressBar != null) { _gui.RemoveControl(_loadingProgressBar); _loadingProgressBar = null; }

			int screenW = _gameWindow.Width;
			int screenH = _gameWindow.Height;

			// HUD info label (time + net info) — top-left, below FPS counter
			_hudInfoLabel = new FishUIInfoLabel
			{
				Position = new Vector2(10, 30),
				Size = new Vector2(400, 50),
				TextColor = FishColor.White,
			};
			_gui.AddControl(_hudInfoLabel);

			// Connection status indicator — top-right corner, small ping display
			_connectionStatusLabel = new Label
			{
				Text = "",
				Position = new Vector2(screenW - 120, 10),
				Size = new Vector2(110, 22),
				Alignment = Align.Right,
			};
			_gui.AddControl(_connectionStatusLabel);

			// Health bar — bottom-center, fuel-style zones (red-yellow-green)
			int barW = 200;
			int barH = 20;
			_healthBar = new BarGauge(0, 100)
			{
				Value = 100,
				Position = new Vector2(screenW / 2f - barW / 2f, screenH - 42),
				Size = new Vector2(barW, barH),
				ShowValue = false,
			};
			_healthBar.SetupFuelZones();
			_gui.AddControl(_healthBar);

			// Health text label overlaying the bar
			_healthLabel = new Label
			{
				Text = "100 / 100",
				Position = new Vector2(screenW / 2f - barW / 2f, screenH - 42),
				Size = new Vector2(barW, barH),
				Alignment = Align.Center,
			};
			_gui.AddControl(_healthLabel);

			// Kill feed — toast notifications in the top-right corner
			_killFeedToast = new ToastNotification
			{
				MaxToasts = 8,
				DefaultDuration = KillFeedDuration,
			};
			_killFeedToast.TextColor = new FishColor(255, 70, 70, 255);
			_gui.AddControl(_killFeedToast);

			// Chat messages — toast notifications anchored to bottom-left
			_chatToast = new ToastNotification
			{
				MaxToasts = MaxChatMessages,
				DefaultDuration = ChatMessageDuration,
				Position = new Vector2(10, screenH - 370),
				Size = new Vector2(400, 180),
			};
			_chatToast.TextColor = FishColor.White;
			_gui.AddControl(_chatToast);

			// Chat input panel — bottom-left, hidden by default
			_chatInputPanel = new Panel
			{
				Position = new Vector2(10, screenH - 186),
				Size = new Vector2(400, 30),
				Visible = false,
			};
			_chatInputPanel.Opacity = 0.85f;

			_chatInputBox = new Textbox
			{
				Placeholder = "Type a message...",
				Position = new Vector2(2, 2),
				Size = new Vector2(396, 26),
			};
			_chatInputPanel.AddChild(_chatInputBox);
			_gui.AddControl(_chatInputPanel);

			// Network stats panel — top-left, below HUD info (toggled with F5)
			_netStatsInfoLabel = new FishUIInfoLabel
				{
					Position = new Vector2(4, 4),
					Size = new Vector2(270, 200),
					TextColor = FishColor.Black,
					DrawOutline = false,
				};
			_netStatsPanel = new Panel
				{
					Position = new Vector2(screenW - 286, 66),
					Size = new Vector2(280, 210),
					Variant = PanelVariant.Dark,
					Visible = false,
				};
			_netStatsPanel.Opacity = 0.85f;
			_netStatsPanel.AddChild(_netStatsInfoLabel);
			_gui.AddControl(_netStatsPanel);

			// Player list panel — center screen, toggled with Tab
			_playerListInfoLabel = new FishUIInfoLabel
			{
				Position = new Vector2(4, 4),
				Size = new Vector2(282, 240),
				TextColor = FishColor.White,
				DrawOutline = false,
			};
			_playerListPanel = new Panel
			{
				Position = new Vector2(screenW / 2f - 145, screenH / 2f - 130),
				Size = new Vector2(290, 250),
				Variant = PanelVariant.Dark,
				Visible = false,
			};
			_playerListPanel.Opacity = 0.85f;
			_playerListPanel.AddChild(_playerListInfoLabel);
			_gui.AddControl(_playerListPanel);

			// Death overlay — full-screen red tint with centered text
			_deathOverlayPanel = new Panel
			{
				Position = Vector2.Zero,
				Size = new Vector2(screenW, screenH),
				Visible = false,
			};
			_deathOverlayPanel.SetColorOverride("Background", new FishColor(100, 0, 0, 140));

			_deathTitleLabel = new Label
			{
				Text = "YOU DIED",
				Position = new Vector2(0, screenH / 2f - 50),
				Size = new Vector2(screenW, 60),
				Alignment = Align.Center,
			};
			_deathTitleLabel.SetColorOverride("Text", new FishColor(220, 30, 30, 255));
			_deathOverlayPanel.AddChild(_deathTitleLabel);

			_deathSubtitleLabel = new Label
			{
				Text = "Respawning...",
				Position = new Vector2(0, screenH / 2f + 20),
				Size = new Vector2(screenW, 30),
				Alignment = Align.Center,
			};
			_deathSubtitleLabel.SetColorOverride("Text", new FishColor(200, 200, 200, 200));
			_deathOverlayPanel.AddChild(_deathSubtitleLabel);

			_gui.AddControl(_deathOverlayPanel);

			// Connection lost overlay — full-screen dark tint with reconnect options
			_connectionLostPanel = new Panel
			{
				Position = Vector2.Zero,
				Size = new Vector2(screenW, screenH),
				Visible = false,
			};
			_connectionLostPanel.SetColorOverride("Background", new FishColor(0, 0, 0, 160));

			_connectionLostTitleLabel = new Label
			{
				Text = "CONNECTION LOST",
				Position = new Vector2(0, screenH / 2f - 80),
				Size = new Vector2(screenW, 50),
				Alignment = Align.Center,
			};
			_connectionLostTitleLabel.SetColorOverride("Text", new FishColor(255, 80, 80, 255));
			_connectionLostPanel.AddChild(_connectionLostTitleLabel);

			_connectionLostReasonLabel = new Label
			{
				Text = "",
				Position = new Vector2(0, screenH / 2f - 20),
				Size = new Vector2(screenW, 30),
				Alignment = Align.Center,
			};
			_connectionLostReasonLabel.SetColorOverride("Text", new FishColor(200, 200, 200, 220));
			_connectionLostPanel.AddChild(_connectionLostReasonLabel);

			_connectionLostReconnectLabel = new Label
			{
				Text = "Press [R] to Reconnect",
				Position = new Vector2(0, screenH / 2f + 30),
				Size = new Vector2(screenW, 26),
				Alignment = Align.Center,
			};
			_connectionLostReconnectLabel.SetColorOverride("Text", new FishColor(100, 255, 100, 220));
			_connectionLostPanel.AddChild(_connectionLostReconnectLabel);

			_connectionLostMenuLabel = new Label
			{
				Text = "Press [ESC] to Return to Menu",
				Position = new Vector2(0, screenH / 2f + 60),
				Size = new Vector2(screenW, 26),
				Alignment = Align.Center,
			};
			_connectionLostMenuLabel.SetColorOverride("Text", new FishColor(200, 200, 200, 200));
			_connectionLostPanel.AddChild(_connectionLostMenuLabel);

			_gui.AddControl(_connectionLostPanel);

			// Debug menu — toggled with F1
			CreateDebugMenu(screenW, screenH);
		}

		private void CreateDebugMenu(int screenW, int screenH)
		{
			var windowSize = new Vector2(320, 540);
			_debugMenuWindow = new Window
			{
				Title = "Debug Menu",
				Position = new Vector2(screenW / 2f - windowSize.X / 2f, screenH / 2f - windowSize.Y / 2f),
				Size = windowSize,
				IsResizable = false,
				ShowCloseButton = true,
				Visible = false
			};

			_debugMenuWindow.OnClosed += (window) =>
			{
				_debugMenuWindow.Visible = false;
				_simulation?.LocalPlayer?.ToggleMouse(false);
			};

			var stack = new StackLayout
			{
				Orientation = StackOrientation.Vertical,
				Spacing = 10,
				Position = new Vector2(10, 10),
				Size = new Vector2(windowSize.X - 40, windowSize.Y - 100),
				IsTransparent = true
			};

			// Debug mode (F3 wireframe/debug rendering)
			var chkDebugMode = new CheckBox("Debug Mode (F3)")
			{
				IsChecked = Eng.DebugMode,
				Size = new Vector2(24, 24)
			};
			chkDebugMode.OnCheckedChanged += (sender, isChecked) => { Eng.DebugMode = isChecked; };
			stack.AddChild(chkDebugMode);

			// Network statistics overlay (F5)
			var chkNetStats = new CheckBox("Network Stats (F5)")
			{
				IsChecked = _showNetStats,
				Size = new Vector2(24, 24)
			};
			chkNetStats.OnCheckedChanged += (sender, isChecked) => { _showNetStats = isChecked; };
			stack.AddChild(chkNetStats);

			// Client packet logging
			var chkClientPacketLog = new CheckBox("Client Packet Logging")
			{
				IsChecked = _client?.PacketLoggingEnabled ?? false,
				Size = new Vector2(24, 24)
			};
			chkClientPacketLog.OnCheckedChanged += (sender, isChecked) =>
			{
				if (_client != null) _client.PacketLoggingEnabled = isChecked;
			};
			stack.AddChild(chkClientPacketLog);

			// Server packet logging (only when hosting)
			var chkServerPacketLog = new CheckBox("Server Packet Logging")
			{
				IsChecked = Eng.MainMenuState?.HostedServer?.Server?.PacketLoggingEnabled ?? false,
				Size = new Vector2(24, 24)
			};
			chkServerPacketLog.OnCheckedChanged += (sender, isChecked) =>
			{
				var server = Eng.MainMenuState?.HostedServer?.Server;
				if (server != null) server.PacketLoggingEnabled = isChecked;
			};
			stack.AddChild(chkServerPacketLog);

			// --- Network Simulation ---
			var chkNetSim = new CheckBox("Network Simulation")
			{
				IsChecked = _client?.NetSimulation?.Enabled ?? false,
				Size = new Vector2(24, 24)
			};
			chkNetSim.OnCheckedChanged += (sender, isChecked) =>
			{
				if (_client?.NetSimulation != null) _client.NetSimulation.Enabled = isChecked;
			};
			stack.AddChild(chkNetSim);

			var lblLatency = new Label("Latency (ms): 0")
			{
				Size = new Vector2(260, 20),
				Alignment = Align.Left,
			};
			stack.AddChild(lblLatency);

			var sldLatency = new Slider
			{
				MinValue = 0,
				MaxValue = 500,
				Value = 0,
				Step = 10,
				Size = new Vector2(260, 24),
			};
			sldLatency.OnValueChanged += (slider, val) =>
			{
				int ms = (int)val;
				if (_client?.NetSimulation != null) _client.NetSimulation.LatencyMs = ms;
				lblLatency.Text = $"Latency (ms): {ms}";
			};
			stack.AddChild(sldLatency);

			var lblLoss = new Label("Packet Loss (%): 0")
			{
				Size = new Vector2(260, 20),
				Alignment = Align.Left,
			};
			stack.AddChild(lblLoss);

			var sldLoss = new Slider
			{
				MinValue = 0,
				MaxValue = 100,
				Value = 0,
				Step = 5,
				Size = new Vector2(260, 24),
			};
			sldLoss.OnValueChanged += (slider, val) =>
			{
				int pct = (int)val;
				if (_client?.NetSimulation != null) _client.NetSimulation.PacketLossPercent = pct;
				lblLoss.Text = $"Packet Loss (%): {pct}";
			};
			stack.AddChild(sldLoss);

			var lblJitter = new Label("Jitter (ms): 0")
			{
				Size = new Vector2(260, 20),
				Alignment = Align.Left,
			};
			stack.AddChild(lblJitter);

			var sldJitter = new Slider
			{
				MinValue = 0,
				MaxValue = 200,
				Value = 0,
				Step = 5,
				Size = new Vector2(260, 24),
			};
			sldJitter.OnValueChanged += (slider, val) =>
			{
				int ms = (int)val;
				if (_client?.NetSimulation != null) _client.NetSimulation.JitterMs = ms;
				lblJitter.Text = $"Jitter (ms): {ms}";
			};
			stack.AddChild(sldJitter);

			var btnClose = new Button
			{
				Text = "Close",
				Size = new Vector2(140, 36)
			};
			btnClose.Clicked += (sender, args) =>
			{
				_debugMenuWindow.Visible = false;
				_simulation?.LocalPlayer?.ToggleMouse(false);
			};
			stack.AddChild(btnClose);

			_debugMenuWindow.AddChild(stack);
			_gui.AddControl(_debugMenuWindow);
		}

		/// <summary>
		/// Updates the HUD info label with time of day and network stats.
		/// </summary>
		private void UpdateHUDInfo()
		{
			if (_hudInfoLabel == null || _simulation == null)
				return;

			_hudInfoLabel.Clear();
			_hudInfoLabel.WriteLine($"Time: {_simulation.DayNight.GetTimeString()} ({_simulation.DayNight.GetPeriodString()})");

			if (_client != null)
				_hudInfoLabel.WriteLine($"Ping: {_client.RoundTripTimeMs}ms | Tick: {_client.LocalTick} | Players: {_simulation.Players.RemotePlayerCount + 1}");
		}

		/// <summary>
		/// Updates the health bar gauge and label from local player state.
		/// </summary>
		private void UpdateHealthBar()
		{
			if (_healthBar == null || _simulation?.LocalPlayer == null)
				return;

			float health = _simulation.LocalPlayer.Health;
			float maxHealth = _simulation.LocalPlayer.MaxHealth;
			_healthBar.Value = health;

			if (_healthLabel != null)
					_healthLabel.Text = $"{(int)health} / {(int)maxHealth}";
			}

			/// <summary>
			/// Updates the connection status indicator label with ping and color coding.
			/// Green (≤50ms), Yellow (51–150ms), Red (>150ms).
			/// Shows "Reconnecting..." when no data has been received for over 3 seconds.
			/// </summary>
			private void UpdateConnectionStatus()
			{
				if (_connectionStatusLabel == null || _client == null)
					return;

				float currentTime = (float)Raylib.GetTime();
				float timeSinceReceive = _client.TimeSinceLastReceive(currentTime);

				if (timeSinceReceive > 3f)
				{
					_connectionStatusLabel.Text = "Reconnecting...";
					_connectionStatusLabel.SetColorOverride("Text", new FishColor(255, 80, 80, 255));
					return;
				}

				int ping = _client.RoundTripTimeMs;
				_connectionStatusLabel.Text = $"{ping} ms";

				if (ping <= 50)
					_connectionStatusLabel.SetColorOverride("Text", new FishColor(80, 255, 80, 255));
				else if (ping <= 150)
					_connectionStatusLabel.SetColorOverride("Text", new FishColor(255, 220, 50, 255));
				else
					_connectionStatusLabel.SetColorOverride("Text", new FishColor(255, 80, 80, 255));
			}

			/// <summary>
			/// Updates the network statistics overlay label with current diagnostic data.
			/// </summary>
		private void UpdateNetStats()
		{
			if (_netStatsInfoLabel == null)
				return;

			_netStatsInfoLabel.Clear();
			_netStatsInfoLabel.WriteLine("Network Stats (F5)");

			int ping = _client?.RoundTripTimeMs ?? 0;
			_netStatsInfoLabel.WriteLine($"Ping: {ping} ms");

			var bw = _client?.Bandwidth;
			if (bw != null)
			{
				float kbIn = bw.BytesReceivedPerSec / 1024f;
				float kbOut = bw.BytesSentPerSec / 1024f;
				_netStatsInfoLabel.WriteLine($"In:  {kbIn:F1} KB/s");
				_netStatsInfoLabel.WriteLine($"Out: {kbOut:F1} KB/s");
			}
			else
			{
				_netStatsInfoLabel.WriteLine("In:  -- KB/s");
				_netStatsInfoLabel.WriteLine("Out: -- KB/s");
			}

			int tick = _client?.LocalTick ?? 0;
			_netStatsInfoLabel.WriteLine($"Client Tick: {tick}");

			int playerCount = (_simulation?.Players?.RemotePlayerCount ?? 0) + 1;
			_netStatsInfoLabel.WriteLine($"Players: {playerCount}");

			if (_prediction != null)
			{
				_netStatsInfoLabel.WriteLine($"Reconciliations: {_prediction.ReconciliationCount}");
				_netStatsInfoLabel.WriteLine($"Last Correction: {_prediction.LastCorrectionDistance:F3}");
			}
			else
			{
				_netStatsInfoLabel.WriteLine("Reconciliations: --");
				_netStatsInfoLabel.WriteLine("Last Correction: --");
			}

			int remoteCount = _simulation?.Players?.RemotePlayerCount ?? 0;
			int entityBufCount = _entitySnapshots.Count;
			_netStatsInfoLabel.WriteLine($"Interp Buffers: {remoteCount} players, {entityBufCount} entities");
		}

		/// <summary>
		/// Updates the player list overlay with all connected players.
		/// </summary>
		private void UpdatePlayerList()
		{
			if (_playerListInfoLabel == null || _simulation == null)
				return;

			_playerListInfoLabel.Clear();

			int playerCount = (_simulation.Players?.RemotePlayerCount ?? 0) + 1;
			_playerListInfoLabel.WriteLine($"Players ({playerCount})");
			_playerListInfoLabel.WriteLine("---");

			// Local player
			int localPing = _client?.RoundTripTimeMs ?? 0;
			_playerListInfoLabel.WriteLine($"  {_playerName} (you)  {localPing} ms");

			// Remote players
			if (_simulation.Players != null)
			{
				foreach (var remote in _simulation.Players.GetAllRemotePlayers())
				{
					_playerListInfoLabel.WriteLine($"  {remote.PlayerName}");
				}
			}
		}

		/// <summary>
		/// Repositions all FishUI HUD controls when the window is resized.
		/// </summary>
		private void PositionHUDControls(int screenW, int screenH)
		{
			// Loading screen controls
			if (_loadingStatusLabel != null)
				_loadingStatusLabel.Position = new Vector2(screenW / 2f - 200, screenH / 2f - 30);
			if (_loadingErrorLabel != null)
				_loadingErrorLabel.Position = new Vector2(screenW / 2f - 200, screenH / 2f + 10);
			if (_loadingHintLabel != null)
				_loadingHintLabel.Position = new Vector2(screenW / 2f - 200, screenH / 2f + 50);
			if (_loadingProgressBar != null)
				_loadingProgressBar.Position = new Vector2(screenW / 2f - 150, screenH / 2f + 20);

			// Health bar
			int barW = 200;
			int barH = 20;
			if (_healthBar != null)
				_healthBar.Position = new Vector2(screenW / 2f - barW / 2f, screenH - 42);
			if (_healthLabel != null)
				_healthLabel.Position = new Vector2(screenW / 2f - barW / 2f, screenH - 42);

			// Death overlay
			if (_deathOverlayPanel != null)
			{
				_deathOverlayPanel.Size = new Vector2(screenW, screenH);
				if (_deathTitleLabel != null)
				{
					_deathTitleLabel.Position = new Vector2(0, screenH / 2f - 50);
					_deathTitleLabel.Size = new Vector2(screenW, 60);
				}
				if (_deathSubtitleLabel != null)
				{
					_deathSubtitleLabel.Position = new Vector2(0, screenH / 2f + 20);
					_deathSubtitleLabel.Size = new Vector2(screenW, 30);
				}
			}

			// Connection lost overlay
			if (_connectionLostPanel != null)
			{
				_connectionLostPanel.Size = new Vector2(screenW, screenH);
				if (_connectionLostTitleLabel != null)
				{
					_connectionLostTitleLabel.Position = new Vector2(0, screenH / 2f - 80);
					_connectionLostTitleLabel.Size = new Vector2(screenW, 50);
				}
				if (_connectionLostReasonLabel != null)
				{
					_connectionLostReasonLabel.Position = new Vector2(0, screenH / 2f - 20);
					_connectionLostReasonLabel.Size = new Vector2(screenW, 30);
				}
				if (_connectionLostReconnectLabel != null)
				{
					_connectionLostReconnectLabel.Position = new Vector2(0, screenH / 2f + 30);
					_connectionLostReconnectLabel.Size = new Vector2(screenW, 26);
				}
				if (_connectionLostMenuLabel != null)
				{
					_connectionLostMenuLabel.Position = new Vector2(0, screenH / 2f + 60);
					_connectionLostMenuLabel.Size = new Vector2(screenW, 26);
				}
			}

			// Debug menu — center on resize
			if (_debugMenuWindow != null)
			{
				var sz = _debugMenuWindow.Size;
				_debugMenuWindow.Position = new Vector2(screenW / 2f - sz.X / 2f, screenH / 2f - sz.Y / 2f);
			}

			// Connection status indicator — top-right
			if (_connectionStatusLabel != null)
				_connectionStatusLabel.Position = new Vector2(screenW - 120, 10);

			// Chat toast — bottom-left above input
			if (_chatToast != null)
				_chatToast.Position = new Vector2(10, screenH - 370);

			// Chat input panel — bottom-left
			if (_chatInputPanel != null)
				_chatInputPanel.Position = new Vector2(10, screenH - 186);

			// Player list panel — centered
			if (_playerListPanel != null)
				_playerListPanel.Position = new Vector2(screenW / 2f - 145, screenH / 2f - 130);
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
