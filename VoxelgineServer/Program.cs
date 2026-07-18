using Voxelgine.Engine.Server;

namespace VoxelgineServer
{
	internal class Program
	{
		static void Main(string[] args)
		{
			int port = 7777;
			int seed = 666;
			bool forceRegen = false;
			Voxelgine.Engine.DI.GameLogLevel logLevel = Voxelgine.Engine.DI.GameLogLevel.Trace;

			for (int i = 0; i < args.Length; i++)
			{
				switch (args[i])
				{
					case "--port" when i + 1 < args.Length:
						if (int.TryParse(args[++i], out int p))
							port = p;
						break;

					case "--seed" when i + 1 < args.Length:
						if (int.TryParse(args[++i], out int s))
							seed = s;
						break;

					case "--force-regen":
						forceRegen = true;
						break;

					case "--log-level" when i + 1 < args.Length:
						if (!Enum.TryParse(args[++i], true, out logLevel))
						{
							Console.Error.WriteLine("Invalid --log-level. Use Trace, Debug, Info, Warning, Error, or Fatal.");
							return;
						}
						break;

					case "--help":
						Console.WriteLine("VoxelgineServer - Aurora Falls Dedicated Server");
						Console.WriteLine("Usage: VoxelgineServer [options]");
						Console.WriteLine("  --port <port>   UDP port to listen on (default: 7777)");
						Console.WriteLine("  --seed <seed>   World generation seed (default: 666)");
						Console.WriteLine("  --force-regen   Force world regeneration even if save file exists");
						Console.WriteLine("  --log-level     Minimum console.log level (default: Trace)");
						Console.WriteLine("  --help          Show this help message");
						return;
				}
			}

			using ServerLoop server = new ServerLoop(logLevel);

			Console.CancelKeyPress += (_, e) =>
			{
				e.Cancel = true;
				server.Stop();
			};

			// Start a background thread to read stdin commands
			Thread consoleThread = new Thread(() =>
			{
				try
				{
					while (true)
					{
						string line = Console.ReadLine();
						if (line == null)
							break; // stdin closed
						server.ExecuteCommand(line);
					}
				}
				catch (Exception exception)
				{
					server.Log(Voxelgine.Engine.DI.GameLogLevel.Error, "ConsoleInput", "Console input thread failed.", exception);
				}
			});
			consoleThread.IsBackground = true;
			consoleThread.Name = "ConsoleInput";
			consoleThread.Start();

			// Start blocks until Stop() is called
			server.Start(port, seed, forceRegen);
		}
	}
}
