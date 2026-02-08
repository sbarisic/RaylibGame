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

					case "--help":
						Console.WriteLine("VoxelgineServer - Aurora Falls Dedicated Server");
						Console.WriteLine("Usage: VoxelgineServer [options]");
						Console.WriteLine("  --port <port>   UDP port to listen on (default: 7777)");
						Console.WriteLine("  --seed <seed>   World generation seed (default: 666)");
						Console.WriteLine("  --force-regen   Force world regeneration even if save file exists");
						Console.WriteLine("  --help          Show this help message");
						return;
				}
			}

			using ServerLoop server = new ServerLoop();

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
				catch (Exception)
				{
					// stdin not available (e.g., redirected/piped with no input)
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
