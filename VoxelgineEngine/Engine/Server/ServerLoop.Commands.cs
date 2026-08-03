using System.Collections.Concurrent;
using System.Numerics;
using Voxelgine.Engine.DI;
using Voxelgine.Graphics;
using Voxelgine.Engine.World.Structures;

namespace Voxelgine.Engine.Server
{
	public partial class ServerLoop
	{
		private sealed record QueuedServerCommand(
			string Name,
			string Arguments,
			Action<ConsoleCommandExecutionResult> Completion);

		private sealed class CommandOutput
		{
			private readonly Action<string> sink;
			private readonly List<string> lines = [];

			public CommandOutput(Action<string> sink = null) => this.sink = sink;

			public bool Success { get; private set; } = true;

			public void WriteLine(string message)
			{
				string line = message ?? string.Empty;
				if (lines.Count < ConsoleCommandCatalog.MaximumResultLines
					&& lines.Sum(static value => value.Length) + line.Length <= ConsoleCommandCatalog.MaximumResultTextLength)
					lines.Add(line.Length <= ConsoleCommandCatalog.MaximumResultLineLength
						? line
						: line[..ConsoleCommandCatalog.MaximumResultLineLength]);
				sink?.Invoke(line);
			}

			public void Reject(string message)
			{
				Success = false;
				WriteLine(message);
			}

			public ConsoleCommandExecutionResult Complete() => new(Success, lines.ToArray());
		}

		private readonly ConcurrentQueue<QueuedServerCommand> _commandQueue = new();

		/// <summary>
		/// Queues a command for execution on the next server tick.
		/// Thread-safe — can be called from any thread (stdin reader, game thread, etc.).
		/// </summary>
		/// <param name="command">The command string to execute (e.g., "kick PlayerName").</param>
		public void ExecuteCommand(string command)
		{
			if (string.IsNullOrWhiteSpace(command))
				return;
			string[] parts = command.Trim().Split(' ', 2, StringSplitOptions.TrimEntries);
			_commandQueue.Enqueue(new QueuedServerCommand(
				parts[0],
				parts.Length > 1 ? parts[1] : string.Empty,
				null));
		}

		/// <summary>Queues an explicitly parsed local-host command and reports its output.</summary>
		public void ExecuteCommand(
			string commandName,
			IReadOnlyList<string> arguments,
			Action<ConsoleCommandExecutionResult> completion)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(commandName);
			ArgumentNullException.ThrowIfNull(arguments);
			ArgumentNullException.ThrowIfNull(completion);
			if (commandName.Length > ConsoleCommandCatalog.MaximumCommandNameLength
				|| arguments.Count > ConsoleCommandCatalog.MaximumArgumentCount
				|| arguments.Any(static argument => argument is null || argument.Length > ConsoleCommandCatalog.MaximumArgumentLength)
				|| commandName.Length + arguments.Sum(static argument => argument.Length) > ConsoleCommandCatalog.MaximumCommandTextLength)
				throw new ArgumentException("The console command exceeds the configured limits.", nameof(arguments));
			_commandQueue.Enqueue(new QueuedServerCommand(
				commandName,
				string.Join(' ', arguments),
				completion));
		}

		/// <summary>
		/// Processes all queued commands. Called once per tick on the server thread.
		/// </summary>
		private void ProcessCommands()
		{
			while (_commandQueue.TryDequeue(out QueuedServerCommand command))
			{
				CommandOutput output = new(_logging.ServerWriteLine);
				try
				{
					ProcessCommand(command.Name, command.Arguments, output);
				}
				catch (Exception ex)
				{
					output.Reject($"Command failed: {ex.Message}");
					_logging.Log(GameLogLevel.Error, "Command", $"Failed command={command.Name}", ex);
				}
				try { command.Completion?.Invoke(output.Complete()); }
				catch (Exception ex) { _logging.Log(GameLogLevel.Error, "Command", "Command completion callback failed.", ex); }
			}
		}

		private void ProcessCommand(string commandName, string args, CommandOutput output)
		{
			string cmd = commandName.ToLowerInvariant();

			switch (cmd)
			{
				case "kick":
					CmdKick(args, output);
					break;

				case "ban":
					CmdBan(args, output);
					break;

				case "say":
					CmdSay(args, output);
					break;

				case "time":
					CmdTime(args, output);
					break;

				case "save":
					CmdSave(output);
					break;

				case "quit":
				case "stop":
					CmdQuit(output);
					break;

				case "status":
					CmdStatus(output);
					break;

				case "players":
					CmdPlayers(output);
					break;

				case "help":
					CmdHelp(output);
					break;

				default:
					output.Reject($"[CMD] Unknown command: {cmd}. Type 'help' for a list of commands.");
					break;
			}
		}

		private void CmdKick(string args, CommandOutput output)
		{
			if (string.IsNullOrEmpty(args))
			{
				output.Reject("[CMD] Usage: kick <player name or id>");
				return;
			}

			var conn = FindConnectionByNameOrId(args);
			if (conn == null)
			{
				output.Reject($"[CMD] Player not found: {args}");
				return;
			}

			output.WriteLine($"[CMD] Kicking player [{conn.PlayerId}] \"{conn.PlayerName}\"...");
			_server.Kick(conn.PlayerId, "Kicked by server", CurrentTime);
		}

		private void CmdBan(string args, CommandOutput output)
		{
			if (string.IsNullOrEmpty(args))
			{
				output.Reject("[CMD] Usage: ban <player name or id>");
				return;
			}

			var conn = FindConnectionByNameOrId(args);
			if (conn == null)
			{
				output.Reject($"[CMD] Player not found: {args}");
				return;
			}

			// Ban is implemented as kick with a ban message.
			// A full ban list (persisted IP/name bans) would require additional infrastructure.
			output.WriteLine($"[CMD] Banning player [{conn.PlayerId}] \"{conn.PlayerName}\"...");
			_server.Kick(conn.PlayerId, "Banned by server", CurrentTime);
		}

		private void CmdSay(string message, CommandOutput output)
		{
			if (string.IsNullOrEmpty(message))
			{
				output.Reject("[CMD] Usage: say <message>");
				return;
			}

			output.WriteLine($"[Server] {message}");

			var chatPacket = new ChatMessagePacket
			{
				PlayerId = -1, // -1 indicates server message
				Message = $"[Server] {message}"
			};
			_server.Broadcast(chatPacket, true, CurrentTime);
		}

		private void CmdTime(string args, CommandOutput output)
		{
			if (string.IsNullOrEmpty(args))
			{
				output.WriteLine($"[CMD] Current time: {_simulation.DayNight.GetTimeString()} ({_simulation.DayNight.GetPeriodString()})");
				return;
			}

			if (!float.TryParse(args, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float hours) || hours < 0 || hours >= 24)
			{
				output.Reject("[CMD] Usage: time <hours> (0-24, e.g., 12.5 for 12:30)");
				return;
			}

			_simulation.DayNight.SetTime(hours);
			output.WriteLine($"[CMD] Time set to {_simulation.DayNight.GetTimeString()} ({_simulation.DayNight.GetPeriodString()})");

			// Broadcast updated time to all clients immediately
			_server.Broadcast(new DayTimeSyncPacket { TimeOfDay = _simulation.DayNight.TimeOfDay }, true, CurrentTime);
		}

		private void CmdSave(CommandOutput output)
		{
			output.WriteLine("[CMD] Saving world...");
			SaveWorld();
		}

		private void CmdQuit(CommandOutput output)
		{
			output.WriteLine("[CMD] Server shutting down...");
			Stop();
		}

		private void CmdStatus(CommandOutput output)
		{
			output.WriteLine($"[CMD] Server status:");
			output.WriteLine($"  Players: {_simulation.Players.Count}/{NetServer.MaxPlayers}");
			output.WriteLine($"  Tick: {_server.ServerTick}");
			output.WriteLine($"  Time: {_simulation.DayNight.GetTimeString()} ({_simulation.DayNight.GetPeriodString()})");
			output.WriteLine($"  Entities: {_simulation.Entities.GetEntityCount()}");
			output.WriteLine($"  Uptime: {CurrentTime:F0}s");
		}

		private void CmdPlayers(CommandOutput output)
		{
			var players = _simulation.Players.GetAllPlayers().ToArray();
			if (players.Length == 0)
			{
				output.WriteLine("[CMD] No players connected.");
				return;
			}

			output.WriteLine($"[CMD] Connected players ({players.Length}/{NetServer.MaxPlayers}):");
			foreach (var player in players)
			{
				var conn = _server.GetConnection(player.PlayerId);
				string name = conn?.PlayerName ?? "Unknown";
				int ping = conn?.RoundTripTimeMs ?? 0;
				string status = player.IsDead ? " [DEAD]" : "";
				output.WriteLine($"  [{player.PlayerId}] \"{name}\" - Pos: ({player.Position.X:F1}, {player.Position.Y:F1}, {player.Position.Z:F1}) - HP: {player.Health:F0} - Ping: {ping}ms{status}");
			}
		}

		private void CmdHelp(CommandOutput output)
		{
			output.WriteLine("[CMD] Server console commands:");
			output.WriteLine("  kick <player>  - Kick a player by name or ID");
			output.WriteLine("  ban <player>   - Ban a player by name or ID");
			output.WriteLine("  say <message>  - Broadcast a server message to all players");
			output.WriteLine("  time [hours]   - Show or set time of day (0-24)");
			output.WriteLine("  save           - Save the world to disk");
			output.WriteLine("  quit / stop    - Save and shut down the server");
			output.WriteLine("  status         - Show server status");
			output.WriteLine("  players        - List connected players");
			output.WriteLine("  help           - Show this help message");
			output.WriteLine("[CMD] Player console commands:");
			output.WriteLine("  comehere       - All NPCs navigate to your position");
			output.WriteLine("  day            - Set time to noon");
			output.WriteLine("  night          - Set time to midnight");
			output.WriteLine("  speak <text>   - All NPCs display a speech bubble");
#if DEBUG
			output.WriteLine("  fog fill ...   - Fill a player-centered fog volume");
			output.WriteLine("  fog clear ...  - Clear a player-centered fog volume");
			output.WriteLine("  give <item> [count] - Add an item to your inventory");
#endif
		}

		/// <summary>
		/// Processes an authoritative command request from a connected player.
		/// </summary>
		private void HandlePlayerCommand(NetConnection connection, ConsoleCommandRequestPacket request)
		{
			CommandOutput output = new();
			try
			{
				if (!ConsoleCommandCatalog.TryGetPlayerCommand(request.CommandName, out ConsoleCommandDefinition definition))
				{
					output.Reject($"Unknown player command: {request.CommandName}.");
				}
				else if (!definition.AcceptsArgumentCount(request.Arguments.Length))
				{
					output.Reject($"Usage: {definition.Usage}");
				}
				else
				{
					string cmd = definition.Name;
					string args = string.Join(' ', request.Arguments);
					_logging.ServerWriteLine($"[CMD] Player [{connection.PlayerId}] \"{connection.PlayerName}\" issued: {cmd} {args}".TrimEnd());
					switch (cmd)
					{
						case "comehere": CmdComeHere(connection, output); break;
						case "day": SetTimeAndNotify(12f, output); break;
						case "night": SetTimeAndNotify(0f, output); break;
						case "speak": CmdSpeak(connection, args, output); break;
						case "machine": CmdMachine(connection, args, output); break;

#if DEBUG
						case "give": CmdGive(connection, args, output); break;
						case "fog": CmdFog(connection, args, output); break;
						case "structure": CmdStructure(connection, args, output); break;
#endif
						default: output.Reject($"Unknown player command: {cmd}."); break;
					}
				}
			}
			catch (Exception exception)
			{
				output.Reject($"Command failed: {exception.Message}");
				_logging.Log(GameLogLevel.Error, "Command", $"Player command failed playerId={connection.PlayerId} command={request.CommandName}", exception);
			}
			ConsoleCommandExecutionResult result = output.Complete();
			_server.SendTo(connection.PlayerId, new ConsoleCommandResultPacket
			{
				RequestId = request.RequestId,
				Success = result.Success,
				Lines = result.Lines.ToArray(),
			}, true, CurrentTime);
		}

		private void CmdMachine(NetConnection connection, string arguments, CommandOutput output)
		{
			if (_infrastructure == null || !_sessions.TryGetValue(connection.PlayerId, out ServerClientSession session))
			{
				output.Reject("Infrastructure is not available.");
				return;
			}
			bool enabled;
			if (string.Equals(arguments, "on", StringComparison.OrdinalIgnoreCase)) enabled = true;
			else if (string.Equals(arguments, "off", StringComparison.OrdinalIgnoreCase)) enabled = false;
			else
			{
				output.Reject("Usage: machine <on|off> while standing near a function block.");
				return;
			}

			Vector3 playerPosition = session.Player.Position;
			InfrastructureMachineSnapshot? nearest = _infrastructure.Machines
				.Select(static value => (InfrastructureMachineSnapshot?)value)
				.Where(value => Vector3.DistanceSquared(playerPosition,
					new Vector3(value.Value.Key.FunctionCoordinate.X + 0.5f, value.Value.Key.FunctionCoordinate.Y + 0.5f, value.Value.Key.FunctionCoordinate.Z + 0.5f)) <= 64f)
				.OrderBy(value => value.Value.GeneratedMarker == null ? 1 : 0)
				.ThenBy(value => Vector3.DistanceSquared(playerPosition,
					new Vector3(value.Value.Key.FunctionCoordinate.X + 0.5f, value.Value.Key.FunctionCoordinate.Y + 0.5f, value.Value.Key.FunctionCoordinate.Z + 0.5f)))
				.FirstOrDefault();
			if (nearest == null)
			{
				output.Reject("No infrastructure function is within eight blocks.");
				return;
			}
			if (enabled && nearest.Value.Key.Function == InfrastructureFunctionKind.GravityAnchor && _progression?.GravityAnchorUnlocked != true)
			{
				output.Reject("The gravity anchor is locked until all three story relays are active.");
				return;
			}

			_infrastructure.SetRequestedEnabled(nearest.Value.Key, enabled);
			output.WriteLine($"{nearest.Value.Key.Function} requested state: {(enabled ? "enabled" : "disabled")}.");
		}

		/// <summary>
		/// Sets the time of day, broadcasts the change to all clients, and notifies the requesting player.
		/// </summary>
		private void SetTimeAndNotify(float hours, CommandOutput output)
		{
			_simulation.DayNight.SetTime(hours);
			_server.Broadcast(new DayTimeSyncPacket { TimeOfDay = _simulation.DayNight.TimeOfDay }, true, CurrentTime);
			output.WriteLine($"Time set to {_simulation.DayNight.GetTimeString()} ({_simulation.DayNight.GetPeriodString()}).");
		}

		/// <summary>
		/// Commands all NPCs in the world to navigate to the player's current position.
		/// </summary>
		private void CmdComeHere(NetConnection connection, CommandOutput output)
		{
			var player = _simulation.Players.GetPlayer(connection.PlayerId);
			if (player == null)
			{
				output.Reject("Could not find your player.");
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

			output.WriteLine($"{count} NPC(s) navigating to your position.");
			}

			/// <summary>
			/// Commands all NPCs to display a speech bubble with the given text.
			/// </summary>
			private void CmdSpeak(NetConnection connection, string text, CommandOutput output)
			{
				if (string.IsNullOrWhiteSpace(text))
				{
					output.Reject("Usage: speak <text>");
					return;
				}

				int count = 0;
				foreach (var entity in _simulation.Entities.GetAllEntities())
				{
					if (entity is VEntNPC npc)
					{
						npc.Speak(text, 5f);
						count++;
					}
				}

				output.WriteLine($"{count} NPC(s) speaking.");
			}

#if DEBUG
			private void CmdGive(NetConnection connection, string arguments, CommandOutput output)
			{
				if (!_sessions.TryGetValue(connection.PlayerId, out ServerClientSession session))
				{
					output.Reject("No active inventory session.");
					return;
				}

				string[] values = arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
				if (values.Length is < 1 or > 2 || !TryResolveItem(values[0], out ItemDefinition item))
				{
					output.Reject("Usage: give <item-id|name> [count]");
					return;
				}

				int requested = 1;
				if (values.Length == 2 && (!int.TryParse(values[1], out requested) || requested <= 0 || requested > 3840))
				{
					output.Reject("Count must be between 1 and 3840.");
					return;
				}

				int granted = session.Inventory.Grant(item.Id, requested);
				SendInventoryState(session, 0, true);
				output.WriteLine(granted == requested
					? $"Granted {granted} x {item.DisplayName}."
					: $"Granted {granted} x {item.DisplayName}; inventory is full.");
			}

			private static bool TryResolveItem(string value, out ItemDefinition item)
			{
				if (ushort.TryParse(value, out ushort numeric) && ItemCatalog.TryGet(new ItemId(numeric), out item))
					return true;

				string normalized = value.Replace("_", string.Empty).Replace("-", string.Empty).Replace(" ", string.Empty);
				foreach (ItemDefinition candidate in ItemCatalog.AllItems)
				{
					string candidateName = candidate.DisplayName.Replace(" ", string.Empty);
					if (string.Equals(candidateName, normalized, StringComparison.OrdinalIgnoreCase))
					{
						item = candidate;
						return true;
					}
				}

				item = null;
				return false;
			}

			private void CmdFog(NetConnection connection, string arguments, CommandOutput output)
			{
				Player player = _simulation.Players.GetPlayer(connection.PlayerId);

				if (player == null)
				{
					output.Reject("Could not find your player.");
					return;
				}

				string[] values = arguments.Split(
					' ',
					StringSplitOptions.RemoveEmptyEntries |
					StringSplitOptions.TrimEntries
				);

				if (values.Length == 0)
				{
					SendFogUsage(output);
					return;
				}

				bool fill = string.Equals(values[0], "fill", StringComparison.OrdinalIgnoreCase);
				bool clear = string.Equals(values[0], "clear", StringComparison.OrdinalIgnoreCase);
				int expectedCount = fill ? 8 : clear ? 4 : -1;

				if (expectedCount < 0 || values.Length != expectedCount
					|| !int.TryParse(values[1], out int radiusX)
					|| !int.TryParse(values[2], out int height)
					|| !int.TryParse(values[3], out int radiusZ)
					|| radiusX < 0 || radiusX > 64
					|| radiusZ < 0 || radiusZ > 64
					|| height <= 0 || height > 96)
				{
					SendFogUsage(output);
					return;
				}

				int centerX = (int)MathF.Floor(player.Position.X);
				int minimumY = (int)MathF.Floor(player.Position.Y);
				int centerZ = (int)MathF.Floor(player.Position.Z);
				int minimumX = centerX - radiusX;
				int minimumZ = centerZ - radiusZ;
				int sizeX = checked(radiusX * 2 + 1);
				int sizeZ = checked(radiusZ * 2 + 1);
				int changed;

				if (clear)
				{
					changed = _simulation.Map.ClearFog(
						minimumX,
						minimumY,
						minimumZ,
						sizeX,
						height,
						sizeZ
					);
				}
				else
				{
					if (!byte.TryParse(values[4], out byte red)
						|| !byte.TryParse(values[5], out byte green)
						|| !byte.TryParse(values[6], out byte blue)
						|| !byte.TryParse(values[7], out byte density))
					{
						SendFogUsage(output);
						return;
					}

					FogVoxel fog = FogVoxel.FromStraight(
						new Rgba32(red, green, blue),
						density
					);
					changed = _simulation.Map.FillFog(
						minimumX,
						minimumY,
						minimumZ,
						sizeX,
						height,
						sizeZ,
						fog
					);
				}

				output.WriteLine(
					$"Fog {values[0].ToLowerInvariant()} changed {changed} cell(s)."
				);
			}

			private void SendFogUsage(CommandOutput output)
			{
				output.Reject("Usage: fog fill <radiusX> <height> <radiusZ> <r> <g> <b> <density> or fog clear <radiusX> <height> <radiusZ>");
			}
#endif

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
