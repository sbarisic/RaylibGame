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
			Assert.Contains("[CLIENT] engine message", firstSession);
			Assert.Contains("direct stdout", firstSession);
			Assert.Contains("direct stderr", firstSession);

			using (FishLogging logging = new(new TestConfig(folder)))
			{
				logging.Init();
				logging.WriteLine("next session");
			}

			string secondSession = File.ReadAllText(path);
			Assert.Contains("[CLIENT] next session", secondSession);
			Assert.DoesNotContain("engine message", secondSession);
			Assert.DoesNotContain("direct stdout", secondSession);
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

		public void LoadFromJson()
		{
		}
	}
}
