namespace Voxelgine.Engine;

public sealed record ConsoleCommandDefinition(
	string Name,
	string Description,
	string Usage,
	int MinimumArguments,
	int MaximumArguments,
	params string[] Aliases)
{
	public bool AcceptsArgumentCount(int count) => count >= MinimumArguments && count <= MaximumArguments;
}

public static class ConsoleCommandCatalog
{
	public const int MaximumCommandNameLength = 32;
	public const int MaximumArgumentCount = 16;
	public const int MaximumArgumentLength = 512;
	public const int MaximumCommandTextLength = 4096;
	public const int MaximumResultLines = 64;
	public const int MaximumResultLineLength = 1024;
	public const int MaximumResultTextLength = 16 * 1024;

	private static readonly ConsoleCommandDefinition[] playerCommands = CreatePlayerCommands();
	private static readonly ConsoleCommandDefinition[] hostCommands =
	[
		new("say", "Broadcasts a server message.", "say <message>", 1, MaximumArgumentCount),
		new("time", "Shows or sets the time of day.", "time [hours]", 0, 1),
		new("save", "Saves the hosted world.", "save", 0, 0),
		new("status", "Shows hosted-server status.", "status", 0, 0),
		new("players", "Lists connected players.", "players", 0, 0),
		new("stop", "Stops the hosted server.", "stop", 0, 0, "quit"),
	];

	public static IReadOnlyList<ConsoleCommandDefinition> PlayerCommands => playerCommands;

	public static IReadOnlyList<ConsoleCommandDefinition> HostCommands => hostCommands;

	public static bool TryGetPlayerCommand(string name, out ConsoleCommandDefinition definition) =>
		TryGet(playerCommands, name, out definition);

	public static bool TryGetHostCommand(string name, out ConsoleCommandDefinition definition) =>
		TryGet(hostCommands, name, out definition);

	private static ConsoleCommandDefinition[] CreatePlayerCommands()
	{
		List<ConsoleCommandDefinition> commands =
		[
			new("comehere", "Calls every NPC to your position.", "comehere", 0, 0),
			new("day", "Sets the world time to noon.", "day", 0, 0),
			new("night", "Sets the world time to midnight.", "night", 0, 0),
			new("speak", "Makes every NPC display text.", "speak <text>", 1, MaximumArgumentCount),
			new("machine", "Changes the nearest machine request state.", "machine <on|off>", 1, 1),
		];
#if DEBUG
		commands.Add(new("give", "Adds an item to your inventory.", "give <item-id|name> [count]", 1, 2));
		commands.Add(new("fog", "Fills or clears a player-centered fog volume.", "fog fill <radiusX> <height> <radiusZ> <r> <g> <b> <density> | fog clear <radiusX> <height> <radiusZ>", 4, 8));
		commands.Add(new("structure", "Authors and exports structure selections.", "structure pos1|pos2|anchor|marker|export ...", 1, MaximumArgumentCount));
#endif
		return commands.ToArray();
	}

	private static bool TryGet(IEnumerable<ConsoleCommandDefinition> commands, string name, out ConsoleCommandDefinition definition)
	{
		definition = commands.FirstOrDefault(command =>
			string.Equals(command.Name, name, StringComparison.OrdinalIgnoreCase)
			|| command.Aliases.Any(alias => string.Equals(alias, name, StringComparison.OrdinalIgnoreCase)));
		return definition is not null;
	}
}

public sealed record ConsoleCommandExecutionResult(bool Success, IReadOnlyList<string> Lines);
