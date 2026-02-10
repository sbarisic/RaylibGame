using System.Collections.Concurrent;

namespace Voxelgine.Engine.Server
{
	public partial class ServerLoop
	{
		private readonly ConcurrentQueue<string> _commandQueue = new();

		/// <summary>
		/// Queues a command for execution on the next server tick.
		/// Thread-safe — can be called from any thread (stdin reader, game thread, etc.).
		/// </summary>
		/// <param name="command">The command string to execute (e.g., "kick PlayerName").</param>
		public void ExecuteCommand(string command)
		{
			if (!string.IsNullOrWhiteSpace(command))
				_commandQueue.Enqueue(command.Trim());
		}

		/// <summary>
		/// Processes all queued commands. Called once per tick on the server thread.
		/// </summary>
		private void ProcessCommands()
		{
			while (_commandQueue.TryDequeue(out string command))
			{
				try
				{
					ProcessCommand(command);
				}
				catch (Exception ex)
				{
					_logging.ServerWriteLine($"[CMD] Error executing '{command}': {ex.Message}");
				}
			}
		}

		private void ProcessCommand(string command)
		{
			// Split into command name and arguments
			string[] parts = command.Split(' ', 2, StringSplitOptions.TrimEntries);
			string cmd = parts[0].ToLowerInvariant();
			string args = parts.Length > 1 ? parts[1] : string.Empty;

			switch (cmd)
			{
				case "kick":
					CmdKick(args);
					break;

				case "ban":
					CmdBan(args);
					break;

				case "say":
					CmdSay(args);
					break;

				case "time":
					CmdTime(args);
					break;

				case "save":
					CmdSave();
					break;

				case "quit":
				case "stop":
					CmdQuit();
					break;

				case "status":
					CmdStatus();
					break;

				case "players":
					CmdPlayers();
					break;

				case "help":
					CmdHelp();
					break;

				default:
					_logging.ServerWriteLine($"[CMD] Unknown command: {cmd}. Type 'help' for a list of commands.");
					break;
			}
		}

		private void CmdKick(string args)
		{
			if (string.IsNullOrEmpty(args))
			{
				_logging.ServerWriteLine("[CMD] Usage: kick <player name or id>");
				return;
			}

			var conn = FindConnectionByNameOrId(args);
			if (conn == null)
			{
				_logging.ServerWriteLine($"[CMD] Player not found: {args}");
				return;
			}

			_logging.ServerWriteLine($"[CMD] Kicking player [{conn.PlayerId}] \"{conn.PlayerName}\"...");
			_server.Kick(conn.PlayerId, "Kicked by server", CurrentTime);
		}

		private void CmdBan(string args)
		{
			if (string.IsNullOrEmpty(args))
			{
				_logging.ServerWriteLine("[CMD] Usage: ban <player name or id>");
				return;
			}

			var conn = FindConnectionByNameOrId(args);
			if (conn == null)
			{
				_logging.ServerWriteLine($"[CMD] Player not found: {args}");
				return;
			}

			// Ban is implemented as kick with a ban message.
			// A full ban list (persisted IP/name bans) would require additional infrastructure.
			_logging.ServerWriteLine($"[CMD] Banning player [{conn.PlayerId}] \"{conn.PlayerName}\"...");
			_server.Kick(conn.PlayerId, "Banned by server", CurrentTime);
		}

		private void CmdSay(string message)
		{
			if (string.IsNullOrEmpty(message))
			{
				_logging.ServerWriteLine("[CMD] Usage: say <message>");
				return;
			}

			_logging.ServerWriteLine($"[Server] {message}");

			var chatPacket = new ChatMessagePacket
			{
				PlayerId = -1, // -1 indicates server message
				Message = $"[Server] {message}"
			};
			_server.Broadcast(chatPacket, true, CurrentTime);
		}

		private void CmdTime(string args)
		{
			if (string.IsNullOrEmpty(args))
			{
				_logging.ServerWriteLine($"[CMD] Current time: {_simulation.DayNight.GetTimeString()} ({_simulation.DayNight.GetPeriodString()})");
				return;
			}

			if (!float.TryParse(args, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float hours) || hours < 0 || hours >= 24)
			{
				_logging.ServerWriteLine("[CMD] Usage: time <hours> (0-24, e.g., 12.5 for 12:30)");
				return;
			}

			_simulation.DayNight.SetTime(hours);
			_logging.ServerWriteLine($"[CMD] Time set to {_simulation.DayNight.GetTimeString()} ({_simulation.DayNight.GetPeriodString()})");

			// Broadcast updated time to all clients immediately
			_server.Broadcast(new DayTimeSyncPacket { TimeOfDay = _simulation.DayNight.TimeOfDay }, true, CurrentTime);
		}

		private void CmdSave()
		{
			_logging.ServerWriteLine("[CMD] Saving world...");
			SaveWorld();
		}

		private void CmdQuit()
		{
			_logging.ServerWriteLine("[CMD] Server shutting down...");
			Stop();
		}

		private void CmdStatus()
		{
			_logging.ServerWriteLine($"[CMD] Server status:");
			_logging.ServerWriteLine($"  Players: {_simulation.Players.Count}/{NetServer.MaxPlayers}");
			_logging.ServerWriteLine($"  Tick: {_server.ServerTick}");
			_logging.ServerWriteLine($"  Time: {_simulation.DayNight.GetTimeString()} ({_simulation.DayNight.GetPeriodString()})");
			_logging.ServerWriteLine($"  Entities: {_simulation.Entities.GetEntityCount()}");
			_logging.ServerWriteLine($"  Uptime: {CurrentTime:F0}s");
		}

		private void CmdPlayers()
		{
			var players = _simulation.Players.GetAllPlayers().ToArray();
			if (players.Length == 0)
			{
				_logging.ServerWriteLine("[CMD] No players connected.");
				return;
			}

			_logging.ServerWriteLine($"[CMD] Connected players ({players.Length}/{NetServer.MaxPlayers}):");
			foreach (var player in players)
			{
				var conn = _server.GetConnection(player.PlayerId);
				string name = conn?.PlayerName ?? "Unknown";
				int ping = conn?.RoundTripTimeMs ?? 0;
				string status = player.IsDead ? " [DEAD]" : "";
				_logging.ServerWriteLine($"  [{player.PlayerId}] \"{name}\" - Pos: ({player.Position.X:F1}, {player.Position.Y:F1}, {player.Position.Z:F1}) - HP: {player.Health:F0} - Ping: {ping}ms{status}");
			}
		}

		private void CmdHelp()
		{
			_logging.ServerWriteLine("[CMD] Server console commands:");
			_logging.ServerWriteLine("  kick <player>  - Kick a player by name or ID");
			_logging.ServerWriteLine("  ban <player>   - Ban a player by name or ID");
			_logging.ServerWriteLine("  say <message>  - Broadcast a server message to all players");
			_logging.ServerWriteLine("  time [hours]   - Show or set time of day (0-24)");
			_logging.ServerWriteLine("  save           - Save the world to disk");
			_logging.ServerWriteLine("  quit / stop    - Save and shut down the server");
			_logging.ServerWriteLine("  status         - Show server status");
			_logging.ServerWriteLine("  players        - List connected players");
			_logging.ServerWriteLine("  help           - Show this help message");
			_logging.ServerWriteLine("[CMD] Player chat commands (usable by any player via /command):");
			_logging.ServerWriteLine("  /comehere      - All NPCs navigate to your position");
			_logging.ServerWriteLine("  /day           - Set time to noon");
			_logging.ServerWriteLine("  /night         - Set time to midnight");
		}

		/// <summary>
		/// Processes a command sent by a player via chat (e.g., /comehere).
		/// Player commands are separate from server console commands — any connected player can use them.
		/// </summary>
		private void HandlePlayerCommand(NetConnection connection, string command)
		{
			string[] parts = command.Split(' ', 2, StringSplitOptions.TrimEntries);
			string cmd = parts[0].ToLowerInvariant();
			string args = parts.Length > 1 ? parts[1] : string.Empty;

			_logging.ServerWriteLine($"[CMD] Player [{connection.PlayerId}] \"{connection.PlayerName}\" issued: /{command}");

			switch (cmd)
			{
				case "comehere":
					CmdComeHere(connection);
					break;

				case "day":
					SetTimeAndNotify(connection.PlayerId, 12f);
					break;

				case "night":
					SetTimeAndNotify(connection.PlayerId, 0f);
					break;

				default:
					SendServerMessageTo(connection.PlayerId, $"Unknown command: /{cmd}. Try /comehere, /day, /night");
					break;
			}
		}

		/// <summary>
		/// Sets the time of day, broadcasts the change to all clients, and notifies the requesting player.
		/// </summary>
		private void SetTimeAndNotify(int playerId, float hours)
		{
			_simulation.DayNight.SetTime(hours);
			_server.Broadcast(new DayTimeSyncPacket { TimeOfDay = _simulation.DayNight.TimeOfDay }, true, CurrentTime);
			SendServerMessageTo(playerId, $"Time set to {_simulation.DayNight.GetTimeString()} ({_simulation.DayNight.GetPeriodString()}).");
		}

		/// <summary>
		/// Commands all NPCs in the world to navigate to the player's current position.
		/// </summary>
		private void CmdComeHere(NetConnection connection)
		{
			var player = _simulation.Players.GetPlayer(connection.PlayerId);
			if (player == null)
			{
				SendServerMessageTo(connection.PlayerId, "Could not find your player.");
				return;
			}

			int count = 0;
			foreach (var entity in _simulation.Entities.GetAllEntities())
			{
				if (entity is VEntNPC npc)
				{
					npc.NavigateTo(player.Position);
					count++;
				}
			}

			SendServerMessageTo(connection.PlayerId, $"{count} NPC(s) navigating to your position.");
		}

		/// <summary>
		/// Sends a server message to a specific player via chat.
		/// </summary>
		private void SendServerMessageTo(int playerId, string message)
		{
			_server.SendTo(playerId, new ChatMessagePacket
			{
				PlayerId = -1,
				Message = $"[Server] {message}"
			}, true, CurrentTime);
		}

		/// <summary>
		/// Finds a connection by player name (case-insensitive) or player ID string.
		/// </summary>
		private NetConnection FindConnectionByNameOrId(string nameOrId)
		{
			// Try parsing as player ID first
			if (int.TryParse(nameOrId, out int playerId))
			{
				var conn = _server.GetConnection(playerId);
				if (conn != null)
					return conn;
			}

			// Search by name (case-insensitive)
			foreach (var conn in _server.GetConnections())
			{
				if (conn.State == ConnectionState.Connected &&
					string.Equals(conn.PlayerName, nameOrId, StringComparison.OrdinalIgnoreCase))
				{
					return conn;
				}
			}

			return null;
		}
	}
}
