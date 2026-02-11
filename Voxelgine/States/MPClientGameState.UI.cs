using Voxelgine.Engine;
using Raylib_cs;
using System;
using System.Numerics;
using Voxelgine.GUI;
using FishUI;
using FishUI.Controls;

namespace Voxelgine.States
{
	public unsafe partial class MPClientGameState
	{
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
			var windowSize = new Vector2(320, 660);
			_debugMenuWindow = new Window
			{
				Title = "Debug Menu",
				Position = new Vector2(screenW / 2f - windowSize.X / 2f, screenH / 2f - windowSize.Y / 2f),
				Size = windowSize,
				IsResizable = true,
				ResizeHandleSize = 10,
				ShowCloseButton = false,
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
				Size = new Vector2(windowSize.X - 10, windowSize.Y - 10),
				IsTransparent = true,
				Anchor = FishUIAnchor.All
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

			// --- Debug Spawn Buttons (listen server only) ---
			var btnSpawnDoor = new Button
			{
				Text = "Spawn Door",
				Size = new Vector2(140, 36)
			};
			btnSpawnDoor.Clicked += (sender, args) => DebugSpawnDoor();
			stack.AddChild(btnSpawnDoor);

			var btnPlaceCampfire = new Button
			{
				Text = "Place Campfire",
				Size = new Vector2(140, 36)
			};
			btnPlaceCampfire.Clicked += (sender, args) => DebugPlaceCampfire();
			stack.AddChild(btnPlaceCampfire);

			var btnPlaceTorch = new Button
			{
				Text = "Place Torch",
				Size = new Vector2(140, 36)
			};
			btnPlaceTorch.Clicked += (sender, args) => DebugPlaceTorch();
			stack.AddChild(btnPlaceTorch);

			var btnNpcComeHere = new Button
			{
				Text = "NPC Come Here",
				Size = new Vector2(140, 36)
			};
			btnNpcComeHere.Clicked += (sender, args) => SendChatCommand("/comehere");
			stack.AddChild(btnNpcComeHere);

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

		private void DebugSpawnDoor()
		{
			var player = _simulation?.LocalPlayer;
			if (_client == null || !_client.IsConnected || player == null)
				return;

			Vector3 fwd = player.GetForward();
			Vector3 horizontalFwd = new Vector3(fwd.X, 0, fwd.Z);
			if (horizontalFwd.LengthSquared() > 0.001f)
				horizontalFwd = Vector3.Normalize(horizontalFwd);
			else
				horizontalFwd = Vector3.UnitZ;

			Vector3 pos = player.Position + horizontalFwd * 3;
			pos = new Vector3(MathF.Floor(pos.X), MathF.Floor(pos.Y), MathF.Floor(pos.Z));
			Vector3 facing = -horizontalFwd;

			var packet = new DebugSpawnEntityRequestPacket
			{
				EntityType = "VEntSlidingDoor",
				Position = pos,
				FacingDirection = facing,
			};
			_client.Send(packet, true, (float)Raylib.GetTime());
		}

		private void DebugPlaceCampfire()
		{
			var player = _simulation?.LocalPlayer;
			if (_client == null || !_client.IsConnected || player == null)
				return;

			Vector3 fwd = player.GetForward();
			Vector3 pos = player.Position + fwd * 3;
			int x = (int)MathF.Floor(pos.X);
			int y = (int)MathF.Floor(pos.Y);
			int z = (int)MathF.Floor(pos.Z);

			var packet = new DebugPlaceBlockRequestPacket
			{
				X = x,
				Y = y,
				Z = z,
				BlockType = (byte)BlockType.Campfire,
			};
			_client.Send(packet, true, (float)Raylib.GetTime());
		}

		private void DebugPlaceTorch()
		{
			var player = _simulation?.LocalPlayer;
			if (_client == null || !_client.IsConnected || player == null)
				return;

			Vector3 fwd = player.GetForward();
			Vector3 pos = player.Position + fwd * 3;
			int x = (int)MathF.Floor(pos.X);
			int y = (int)MathF.Floor(pos.Y);
			int z = (int)MathF.Floor(pos.Z);

			var packet = new DebugPlaceBlockRequestPacket
			{
				X = x,
				Y = y,
				Z = z,
				BlockType = (byte)BlockType.Torch,
			};
			_client.Send(packet, true, (float)Raylib.GetTime());
		}

		/// <summary>
		/// Sends a chat message to the server. Used by debug buttons to issue /commands.
		/// </summary>
		private void SendChatCommand(string message)
		{
			if (_client == null || !_client.IsConnected)
				return;

			var packet = new ChatMessagePacket
			{
				PlayerId = _client.PlayerId,
				Message = message
			};
			_client.Send(packet, true, (float)Raylib.GetTime());
			_chatToast?.Show($"[Command] {message}", ToastType.Info, ChatMessageDuration);
		}

		private void UpdateHUDInfo()
		{
			if (_hudInfoLabel == null || _simulation == null)
				return;

			_hudInfoLabel.Clear();
			_hudInfoLabel.WriteLine($"Time: {_simulation.DayNight.GetTimeString()} ({_simulation.DayNight.GetPeriodString()})");

			if (_client != null)
				_hudInfoLabel.WriteLine($"Ping: {_client.RoundTripTimeMs}ms | Tick: {_client.LocalTick} | Players: {_simulation.Players.RemotePlayerCount + 1}");
		}

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
	}
}
