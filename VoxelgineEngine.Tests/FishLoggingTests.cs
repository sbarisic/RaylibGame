using Voxelgine.Engine;
using Voxelgine.Engine.DI;

namespace VoxelgineEngine.Tests;

public sealed class FishLoggingTests
{
	[Fact]
	public void ConsoleLogCapturesConsoleAndOverwritesPreviousSession()
	{
		string folder = Path.Combine(
			Path.GetTempPath(),
			$"voxelgine-logging-{Guid.NewGuid():N}"
		);
		string path = Path.Combine(folder, "console.log");
		Directory.CreateDirectory(folder);
		File.WriteAllText(path, "stale session");

		try
		{
			using (FishLogging logging = new(new TestConfig(folder)))
			{
				logging.Init();
				logging.WriteLine("engine message");
				Console.WriteLine("direct stdout");
				Console.Error.WriteLine("direct stderr");
			}

			string firstSession = File.ReadAllText(path);
			Assert.DoesNotContain("stale session", firstSession);
			Assert.Contains("[CLIENT][DEBUG][General] engine message", firstSession);
			Assert.Contains("[PROCESS][TRACE][Console] direct stdout", firstSession);
			Assert.Contains("[PROCESS][TRACE][Console] direct stderr", firstSession);
			Assert.All(firstSession.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries),
				line => Assert.Matches(@"^\[\d{2}:\d{2}:\d{2}\.\d{3}\]\[(CLIENT|SERVER|PROCESS)\]\[(TRACE|DEBUG|INFO|WARNING|ERROR|FATAL)\]\[[^]]+\] ", line));

			using (FishLogging logging = new(new TestConfig(folder)))
			{
				logging.Init();
				logging.WriteLine("next session");
			}

			string secondSession = File.ReadAllText(path);
			Assert.Contains("[CLIENT][DEBUG][General] next session", secondSession);
			Assert.DoesNotContain("engine message", secondSession);
			Assert.DoesNotContain("direct stdout", secondSession);
		}
		finally
		{
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public void FiltersSeverityAndPrefixesEveryExceptionLine()
	{
		string folder = Path.Combine(Path.GetTempPath(), $"voxelgine-logging-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try
		{
			using (FishLogging logging = new(new TestConfig(folder) { LogLevel = GameLogLevel.Warning }))
			{
				logging.Init();
				logging.Log(GameLogLevel.Info, "Test", "hidden");
				logging.Log(GameLogLevel.Error, "Persistence", "load failed", new InvalidDataException("bad data"));
			}
			string[] lines = File.ReadAllLines(Path.Combine(folder, "console.log"));
			Assert.DoesNotContain(lines, line => line.Contains("hidden", StringComparison.Ordinal));
			Assert.True(lines.Length >= 2);
			Assert.All(lines, line => Assert.Contains("[ERROR][Persistence]", line));
		}
		finally
		{
			Directory.Delete(folder, recursive: true);
		}
	}

	[Fact]
	public async Task ConcurrentClientAndServerWritesShareOneStructuredFile()
	{
		string folder = Path.Combine(Path.GetTempPath(), $"voxelgine-logging-{Guid.NewGuid():N}");
		Directory.CreateDirectory(folder);
		try
		{
			using FishLogging client = new(new TestConfig(folder));
			using FishLogging server = new(new TestConfig(folder));
			client.Init();
			server.Init(true);
			await Task.WhenAll(
				Task.Run(() => Enumerable.Range(0, 25).ToList().ForEach(i => client.Log(GameLogLevel.Info, "Concurrent", $"client-{i}"))),
				Task.Run(() => Enumerable.Range(0, 25).ToList().ForEach(i => server.Log(GameLogLevel.Info, "Concurrent", $"server-{i}")))
			);
			server.Dispose();
			client.Dispose();

			string[] lines = File.ReadAllLines(Path.Combine(folder, "console.log"));
			Assert.Equal(50, lines.Length);
			Assert.Equal(25, lines.Count(line => line.Contains("[CLIENT][INFO][Concurrent]", StringComparison.Ordinal)));
			Assert.Equal(25, lines.Count(line => line.Contains("[SERVER][INFO][Concurrent]", StringComparison.Ordinal)));
		}
		finally
		{
			Directory.Delete(folder, recursive: true);
		}
	}

	private sealed class TestConfig(string logFolder) : IFishConfig
	{
		public int WindowWidth { get; set; }
		public int WindowHeight { get; set; }
		public string Title { get; set; } = "Logging test";
		public string LogFolder { get; set; } = logFolder;
		public GameLogLevel LogLevel { get; set; } = GameLogLevel.Trace;

		public void LoadFromJson()
		{
		}
	}
}
