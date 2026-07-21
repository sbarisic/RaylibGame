using System.Numerics;
using FishUI;
using FishUI.Controls;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;

namespace Voxelgine.States
{
	public unsafe partial class MPClientGameState
	{
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
				CloseDebugMenu();
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

			var chkRendererProfiling = new CheckBox("Renderer Profiling")
			{
				IsChecked = _rendererProfilingEnabled,
				Size = new Vector2(24, 24)
			};
			chkRendererProfiling.OnCheckedChanged += (sender, isChecked) =>
			{
				_rendererProfilingEnabled = isChecked;
				_nextRendererProfileLogTime = 0;

				if (_fishVoxelScene != null)
				{
					_fishVoxelScene.GpuProfilingEnabled = isChecked;
				}
			};
			stack.AddChild(chkRendererProfiling);

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
				IsChecked = Eng.AsClient().MainMenuState?.HostedServer?.Server?.PacketLoggingEnabled ?? false,
				Size = new Vector2(24, 24)
			};
			chkServerPacketLog.OnCheckedChanged += (sender, isChecked) =>
			{
				var server = Eng.AsClient().MainMenuState?.HostedServer?.Server;
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
				CloseDebugMenu();
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
			_client.Send(packet, true, GetClientTime());
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
				BlockType = (ushort)BlockType.Campfire,
			};
			_client.Send(packet, true, GetClientTime());
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
				BlockType = (ushort)BlockType.Torch,
			};
			_client.Send(packet, true, GetClientTime());
		}

		/// <summary>
		/// Sends a chat message to the server. Used by debug buttons to issue /commands.
		/// </summary>
	}
}

