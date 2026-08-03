using FishUI.Controls;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;

namespace Voxelgine.States;

public unsafe partial class MPClientGameState
{
	private readonly List<GameConsoleCommand> registeredConsoleCommands = [];
	private readonly HashSet<uint> pendingConsoleCommandRequests = [];
	private uint nextConsoleCommandRequestId = 1;

	private void RegisterDeveloperConsoleCommands()
	{
		GameConsole console = _gui?.DeveloperConsole;
		if (console is null)
			return;
		IClientEngineRunner client = Eng.AsClient();
		bool includeHost = client.IsCurrentConnectionHosted && client.HostedServer is not null;
		foreach (ConsoleCommandDefinition definition in GetDeveloperConsoleCommands(includeHost))
			RegisterConsoleCommand(
				console,
				definition,
				ConsoleCommandCatalog.TryGetHostCommand(definition.Name, out _)
					? ExecuteHostConsoleCommand
					: ExecutePlayerConsoleCommand);
	}

	internal static IReadOnlyList<ConsoleCommandDefinition> GetDeveloperConsoleCommands(bool includeHost) =>
		includeHost
			? ConsoleCommandCatalog.PlayerCommands.Concat(ConsoleCommandCatalog.HostCommands).ToArray()
			: ConsoleCommandCatalog.PlayerCommands;

	private void RegisterConsoleCommand(GameConsole console, ConsoleCommandDefinition definition, Action<GameConsoleCommandContext> execute)
	{
		registeredConsoleCommands.Add(console.RegisterCommand(
			definition.Name,
			execute,
			definition.Description,
			definition.Usage,
			definition.Aliases));
	}

	private void ExecutePlayerConsoleCommand(GameConsoleCommandContext context)
	{
		if (_client?.IsConnected != true)
		{
			context.Console.WriteLine("Command rejected: not connected to a server.");
			return;
		}
		if (!ConsoleCommandCatalog.TryGetPlayerCommand(context.Command.Name, out ConsoleCommandDefinition definition)
			|| !definition.AcceptsArgumentCount(context.Arguments.Count))
		{
			context.Console.WriteLine($"Usage: {definition?.Usage ?? context.Command.Usage}");
			return;
		}

		uint requestId = AllocateConsoleCommandRequestId();
		pendingConsoleCommandRequests.Add(requestId);
		_client.Send(new ConsoleCommandRequestPacket
		{
			RequestId = requestId,
			CommandName = definition.Name,
			Arguments = context.Arguments.ToArray(),
		}, true, GetClientTime());
	}

	private void ExecuteHostConsoleCommand(GameConsoleCommandContext context)
	{
		IClientEngineRunner client = Eng.AsClient();
		if (!client.IsCurrentConnectionHosted || client.HostedServer is null)
		{
			context.Console.WriteLine("Host command rejected: this is not the owned hosted session.");
			return;
		}
		if (!ConsoleCommandCatalog.TryGetHostCommand(context.Command.Name, out ConsoleCommandDefinition definition)
			|| !definition.AcceptsArgumentCount(context.Arguments.Count))
		{
			context.Console.WriteLine($"Usage: {definition?.Usage ?? context.Command.Usage}");
			return;
		}

		try
		{
			client.HostedServer.ExecuteCommand(
				definition.Name,
				context.Arguments,
				result => WriteConsoleCommandResult(context.Console, result));
		}
		catch (Exception exception)
		{
			context.Console.WriteLine($"Host command failed: {exception.Message}");
		}
	}

	private void HandleConsoleCommandResult(ConsoleCommandResultPacket packet)
	{
		if (!pendingConsoleCommandRequests.Remove(packet.RequestId) || _gui?.DeveloperConsole is not GameConsole console)
			return;
		WriteConsoleCommandResult(console, new ConsoleCommandExecutionResult(packet.Success, packet.Lines));
	}

	private static void WriteConsoleCommandResult(GameConsole console, ConsoleCommandExecutionResult result)
	{
		if (result.Lines.Count == 0)
		{
			console.WriteLine(result.Success ? "Command completed." : "Command failed.");
			return;
		}
		for (int i = 0; i < result.Lines.Count; i++)
			console.WriteLine(!result.Success && i == 0 ? $"Error: {result.Lines[i]}" : result.Lines[i]);
	}

	private uint AllocateConsoleCommandRequestId()
	{
		while (nextConsoleCommandRequestId == 0 || pendingConsoleCommandRequests.Contains(nextConsoleCommandRequestId))
			nextConsoleCommandRequestId++;
		return nextConsoleCommandRequestId++;
	}

	private void UnregisterDeveloperConsoleCommands()
	{
		GameConsole console = _gui?.DeveloperConsole;
		if (console is not null)
			foreach (GameConsoleCommand command in registeredConsoleCommands)
				console.UnregisterCommand(command);
		registeredConsoleCommands.Clear();
		pendingConsoleCommandRequests.Clear();
		nextConsoleCommandRequestId = 1;
	}
}
