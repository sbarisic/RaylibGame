using Voxelgine.Engine;
using Voxelgine.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using Voxelgine.GUI;
using Voxelgine.Engine.DI;
using FishUI;
using FishUI.Controls;
using Voxelgine.Engine.Audio;

namespace Voxelgine.States
{
	/// <summary>
	/// Multiplayer client gameplay state. Connects to a server, receives world data,
	/// and runs the game with networked input sending, client-side prediction,
	/// and remote player rendering.
	/// </summary>
	public unsafe partial class MPClientGameState : GameStateImpl
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
		private IGameAudioSink _snd;
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
		private MultiLineEditbox _chatHistoryBox;
		private NpcSpeechBubbleOverlay _npcSpeechOverlay;
		private readonly ChatHistoryBuffer _chatHistory = new(200);
		private readonly GameplayInputOwnership _inputOwnership = new();
		private readonly NetworkInputSource _neutralInputSource = new();
		private readonly InputMgr _neutralInputManager;

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


		IGameWindow _gameWindow;
		IFishLogging _logging;

		/// <summary>
		/// Returns true when the multiplayer client is actively connected and in-game.
		/// Used by weapon/gameplay code to decide whether to use the multiplayer path.
		/// </summary>
		public bool IsActive => _initialized && _client != null && _client.IsConnected;

		/// <summary>The world chunk map (available after world data is loaded).</summary>
		public ChunkMap Map => _simulation?.Map;

		/// <summary>The entity manager (available after world data is loaded).</summary>
		public EntityManager Entities => _simulation?.Entities;

		public MPClientGameState(IGameWindow window, IFishEngineRunner eng) : base(window, eng)
		{
			_gameWindow = window;
			_logging = eng.DI.GetRequiredService<IFishLogging>();
			_neutralInputManager = new InputMgr(_neutralInputSource);
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
			_gui.InputEnabled = _inputOwnership.UiInputEnabled;
			CreateLoadingUI();

			try
			{
				_client.Connect(host, port, playerName, GetClientTime());
			}
			catch (Exception ex)
			{
				_statusText = "";
				_errorText = $"Failed to connect: {ex.Message}";
				_logging.Log(GameLogLevel.Error, "Network", $"Connection failed endpoint={host}:{port}", ex);
			}
		}

		public override void SwapTo()
		{
			base.SwapTo();
			_inputOwnership.Activate();
			if (_gui is not null)
			{
				_gui.InputEnabled = _inputOwnership.UiInputEnabled;
			}
			ApplyInputOwnership();
			_logging.Log(GameLogLevel.Debug, "Input", $"mode={_inputOwnership.Mode} state=active");
		}

		public override void SwapFrom()
		{
			_inputOwnership.Deactivate();
			if (_gui is not null)
			{
				_gui.InputEnabled = _inputOwnership.UiInputEnabled;
			}
			ApplyInputOwnership();
			base.SwapFrom();
		}

		public override void Tick(float GameTime)
		{
			RecordClientFrameTime(GameTime);
			if (!_connectionLost
				&& !string.IsNullOrEmpty(_errorText)
				&& Window.InMgr.IsInputPressed(InputKey.Esc))
			{
				DisconnectAndReturn("Returning to menu");
				return;
			}

			if (_client == null && !_connectionLost)
				return;

			try
			{
				float currentTime = GetClientTime();

				// Process network (client may still be processing final packets)
				_client?.Tick(currentTime);

				// Handle connection lost overlay input
				if (_connectionLost)
				{
					if (Window.InMgr.IsInputPressed(InputKey.R))
					{
						// Reconnect to the same server
						string host = _serverHost;
						int port = _serverPort;
						string name = _playerName;
						Cleanup();
						Connect(host, port, name);
						return;
					}

					if (Window.InMgr.IsInputPressed(InputKey.Esc))
					{
						// Return to main menu
						Cleanup();
						Window.SetState(Eng.AsClient().MainMenuState);
						return;
					}

					// Keep updating day/night for visual continuity.
					_simulation?.DayNight.Update(GetClientDeltaTime());
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

				float dt = GetClientDeltaTime();

				// Update day/night cycle (client authority is false — time set by server)
				float prevMultiplier = _simulation.DayNight.SkyLightMultiplier;
				_simulation.DayNight.Update(dt);
				if (MathF.Abs(_simulation.DayNight.SkyLightMultiplier - prevMultiplier) > 0.01f)
				{
					_simulation.Map.MarkAllChunksDirty();
				}

				if (_inputOwnership.Mode == GameplayInputMode.Chat)
				{
					if (Window.InMgr.IsInputPressed(InputKey.Enter))
					{
						SubmitChatMessage();
					}
					else if (Window.InMgr.IsInputPressed(InputKey.Esc))
					{
						CloseChatInput();
					}

					TickWorldWithoutGameplayInput();
					return;
				}

				if (Window.InMgr.IsInputPressed(InputKey.F1))
				{
					ToggleDebugMenu();
					TickWorldWithoutGameplayInput();
					return;
				}

				if (_inputOwnership.Mode == GameplayInputMode.DebugMenu)
				{
					TickWorldWithoutGameplayInput();
					return;
				}

				if (Window.InMgr.IsInputPressed(InputKey.Enter))
				{
					OpenChatInput();
					TickWorldWithoutGameplayInput();
					return;
				}

				// Handle ESC — disconnect and return to main menu
				if (Window.InMgr.IsInputPressed(InputKey.Esc))
				{
					DisconnectAndReturn("Player disconnected");
					return;
				}

				// Gameplay hotkeys are unavailable while Chat or DebugMenu owns input.
				if (Window.InMgr.IsInputPressed(InputKey.F5))
					_showNetStats = !_showNetStats;

				if (Window.InMgr.IsInputPressed(InputKey.Tab))
					_showPlayerList = !_showPlayerList;

				// Update game systems
				// Discard block changes logged by server-broadcasted BlockChangePackets
				// (processed during _client.Tick above) so they are not echoed back as
				// client requests in SendPendingBlockChanges. Only changes from local
				// player interaction (TickGUI below) should be sent to the server.
				_simulation.Map.ClearPendingChanges();

				_simulation.Map.Tick();
				(_simulation.LocalPlayer as ClientPlayer)?.Tick(Window.InMgr);
				(_simulation.LocalPlayer as ClientPlayer)?.TickGUI(Window.InMgr, _simulation.Map);
				(_simulation.LocalPlayer as ClientPlayer)?.UpdateGUI();

				// Send pending block changes to server
				SendPendingBlockChanges(GetClientTime());
			}
			catch (Exception ex)
			{
				_logging.Log(GameLogLevel.Error, "Update", "Multiplayer variable update failed.", ex);
			}
		}

		public override void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr)
		{
			if (_connectionLost || _client == null || !_initialized || _client.State != ClientState.Playing)
				return;

			try
			{
				float currentTime = TotalTime;

				// Increment client tick
				_client.LocalTick++;

				InputState simulationInput = CreateSimulationInputState(
					InMgr.State,
					_inputOwnership.Mode
				);
				InputMgr simulationInputManager = InMgr;
				if (_inputOwnership.GameplayInputSuppressed)
				{
					_neutralInputSource.SetState(simulationInput);
					_neutralInputManager.Tick(TotalTime);
					simulationInputManager = _neutralInputManager;
				}

				// Record the same effective input used by local prediction and send it to the server.
				var inputPacket = _inputBuffer.Record(
					_client.LocalTick,
					simulationInput,
					new Vector2(_simulation.LocalPlayer.Camera.CamAngle.X, _simulation.LocalPlayer.Camera.CamAngle.Y)
				);
				_client.Send(inputPacket, false, currentTime);

				// Skip prediction for dead players — server won't process input
				if (!_simulation.LocalPlayer.IsDead)
				{
					// Apply local prediction (same physics as server)
					_simulation.LocalPlayer.UpdatePhysics(
						_simulation.Map,
						_simulation.PhysicsData,
						Dt,
						simulationInputManager
					);

					// Record predicted state
					_prediction.RecordPrediction(
						_client.LocalTick,
						_simulation.LocalPlayer.Position,
						_simulation.LocalPlayer.GetVelocity()
					);
				}

				// Entity AI/physics are server-authoritative; IsAuthority=false skips them.
				// UpdateLockstep is still called for any non-authoritative cleanup.
				_simulation.Entities.UpdateLockstep(TotalTime, Dt, simulationInputManager);
			}
			catch (Exception ex)
			{
				_logging.Log(GameLogLevel.Error, "Update", "Multiplayer fixed update failed.", ex);
			}
		}

		// ======================================= Helper Methods =================================================

		private void OpenChatInput()
		{
			if (!_inputOwnership.OpenChat())
				return;

			if (_chatInputPanel != null)
				_chatInputPanel.Visible = true;
			if (_chatHistoryBox != null)
			{
				_chatHistoryBox.Visible = true;
				_chatHistoryBox.ScrollToEnd();
			}
			if (_chatInputBox != null)
			{
				_chatInputBox.Text = "";
				_gui?.UI.FocusControl(_chatInputBox);
			}
			ApplyInputOwnership();
			_logging.Log(GameLogLevel.Debug, "Input", "mode=Chat cursor=visible focus=chat-input");
		}

		private void CloseChatInput()
		{
			if (_inputOwnership.Mode != GameplayInputMode.Chat)
				return;

			_inputOwnership.CloseOverlay();
			if (_chatInputPanel != null)
				_chatInputPanel.Visible = false;
			if (_chatHistoryBox != null)
				_chatHistoryBox.Visible = false;
			if (_chatInputBox != null)
				_chatInputBox.Text = "";
			_gui?.UI.ClearFocus();
			ApplyInputOwnership();
			_logging.Log(GameLogLevel.Debug, "Input", "mode=Gameplay cursor=captured focus=none reason=chat-closed");
		}

		private void ToggleDebugMenu()
		{
			if (!_inputOwnership.ToggleDebugMenu())
				return;

			if (_debugMenuWindow is not null)
			{
				_debugMenuWindow.Visible = _inputOwnership.Mode == GameplayInputMode.DebugMenu;
				if (_debugMenuWindow.Visible)
					_debugMenuWindow.BringToFront();
			}
			ApplyInputOwnership();
			_logging.Log(GameLogLevel.Debug, "Input", $"mode={_inputOwnership.Mode} cursor={(_inputOwnership.CursorCaptured ? "captured" : "visible")}");
		}

		private void CloseDebugMenu()
		{
			if (_inputOwnership.Mode != GameplayInputMode.DebugMenu)
				return;

			_inputOwnership.CloseOverlay();
			if (_debugMenuWindow is not null)
				_debugMenuWindow.Visible = false;
			_gui?.UI.ClearFocus();
			ApplyInputOwnership();
			_logging.Log(GameLogLevel.Debug, "Input", "mode=Gameplay cursor=captured focus=none reason=debug-closed");
		}

		private void ApplyInputOwnership()
		{
			bool captureCursor = _initialized && _inputOwnership.CursorCaptured;
			if (_simulation?.LocalPlayer is Player player)
				player.CursorDisabled = captureCursor;
			SetCursorCaptured(captureCursor);
		}

		private void TickWorldWithoutGameplayInput()
		{
			_simulation.Map.ClearPendingChanges();
			_simulation.Map.Tick();
			(_simulation.LocalPlayer as ClientPlayer)?.UpdateGUI();
			SendPendingBlockChanges(GetClientTime());
		}

		internal static InputState CreateSimulationInputState(
			InputState source,
			GameplayInputMode mode
		)
		{
			return mode == GameplayInputMode.Gameplay
				? source
				: new InputState { GameTime = source.GameTime };
		}

		private void SubmitChatMessage()
		{
			string message = _chatInputBox?.Text?.Trim();
			CloseChatInput();

			if (string.IsNullOrEmpty(message))
				return;

			// Send all messages (including /commands) to the server via chat.
			// The server intercepts messages starting with / and processes them as player commands.
			if (_client != null && _client.IsConnected)
			{
				var packet = new ChatMessagePacket
				{
					PlayerId = _client.PlayerId,
					Message = message
				};
				_client.Send(packet, true, GetClientTime());

				if (message.StartsWith('/'))
					AppendChatEntry($"[Command] {message}", "Chat");
			}
		}

		private void AppendChatEntry(string text, string category)
		{
			if (string.IsNullOrWhiteSpace(text))
				return;

			_chatToast?.Show(text, ToastType.Info, ChatMessageDuration);
			_chatHistory.Add(text);
			if (_chatHistoryBox != null)
			{
				_chatHistoryBox.Text = _chatHistory.Text;
				_chatHistoryBox.ScrollToEnd();
			}
			_logging.Log(GameLogLevel.Info, category, text);
		}

		private void DisconnectAndReturn(string reason)
		{
			if (_client != null && _client.IsConnected)
			{
				try
				{
					_client.Disconnect(reason, GetClientTime());
				}
				catch (Exception exception)
				{
					_logging.Log(GameLogLevel.Warning, "Network", $"Disconnect send failed reason={reason}", exception);
				}
			}
			Cleanup();
			Window.SetState(Eng.AsClient().MainMenuState);
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

			_simulation?.LocalPlayer?.Dispose();
			DisposeFishGfxVoxelScene();
			_inputBuffer = null;
			_prediction = null;
			_simulation = null;
			_gui?.Dispose();
			_gui = null;
			_snd = null;
			_pendingPlayerJoins.Clear();
			_pendingEntityPackets.Clear();
			_entitySnapshots.Clear();
			_chatHistory.Clear();

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
			_chatHistoryBox = null;
			_npcSpeechOverlay = null;
			_inputOwnership.ResetMode();
			_playerListPanel = null;
			_playerListInfoLabel = null;
			_showPlayerList = false;
		}

		public override void Dispose()
		{
			Cleanup();
		}
	}
}
