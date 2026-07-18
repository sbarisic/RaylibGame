using System.Numerics;
using Voxelgine.Engine.DI;
using Voxelgine.Engine.Server;

namespace VoxelgineEngine.Tests;

public sealed class PlayerDataStoreLoggingTests
{
	[Fact]
	public void CorruptPlayerDataProducesSearchableError()
	{
		string folder = Path.Combine(Path.GetTempPath(), $"voxelgine-player-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		File.WriteAllBytes(Path.Combine(folder, "broken.bin"), [1, 2, 3]);
		TestLogging logging = new();
		try
		{
			PlayerDataStore store = new(folder, logging);
			Assert.False(store.TryLoad("broken", out _, out _, out _));
			Assert.Contains(logging.Messages, message => message.Contains("Failed to load player name=broken", StringComparison.Ordinal));
			Assert.Contains(logging.Levels, level => level == GameLogLevel.Error);
		}
		finally
		{
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void PlayerSaveFailureProducesSearchableError()
	{
		string path = Path.Combine(Path.GetTempPath(), $"voxelgine-player-file-{Guid.NewGuid():N}");
		File.WriteAllText(path, "not a directory");
		TestLogging logging = new();
		try
		{
			new PlayerDataStore(path, logging).Save("player", Vector3.Zero, 100, Vector3.Zero);
			Assert.Contains(logging.Messages, message => message.Contains("Failed to save player name=player", StringComparison.Ordinal));
		}
		finally
		{
			File.Delete(path);
		}
	}

	private sealed class TestLogging : IFishLogging
	{
		public List<string> Messages { get; } = new();
		public List<GameLogLevel> Levels { get; } = new();
		public void Init(bool IsServer = false) { }
		public void Log(GameLogLevel level, string category, string message, Exception exception)
		{
			Levels.Add(level);
			Messages.Add(message);
		}
		public void WriteLine(string message) => Messages.Add(message);
		public void ServerWriteLine(string message) => Messages.Add(message);
		public void ClientWriteLine(string message) => Messages.Add(message);
		public void ServerNetworkWriteLine(string message) => Messages.Add(message);
		public void ClientNetworkWriteLine(string message) => Messages.Add(message);
	}
}
