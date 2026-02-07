using System.Diagnostics;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.States;

namespace VoxelgineServer
{
	/// <summary>
	/// Dedicated headless server loop. Owns a <see cref="NetServer"/> and <see cref="GameSimulation"/>,
	/// runs a fixed timestep (66.6 Hz) loop without any rendering or audio.
	/// </summary>
	public class ServerLoop : IDisposable
	{
		/// <summary>
		/// Fixed timestep matching the client (0.015s = 66.6 Hz).
		/// </summary>
		public const float DeltaTime = 0.015f;

		/// <summary>
		/// Maximum frame time to prevent spiral of death on slow ticks.
		/// </summary>
		public const float MaxFrameTime = 0.25f;

		/// <summary>
		/// Default world size for generated worlds.
		/// </summary>
		public const int DefaultWorldWidth = 256;
		public const int DefaultWorldLength = 256;

		private readonly NetServer _server;
		private readonly IFishLogging _logging;
		private readonly FishDI _di;
		private GameSimulation _simulation;

		private volatile bool _running;
		private bool _disposed;

		/// <summary>
		/// The current server time in seconds since start.
		/// </summary>
		public float CurrentTime { get; private set; }

		/// <summary>
		/// The game simulation owned by this server.
		/// </summary>
		public GameSimulation Simulation => _simulation;

		/// <summary>
		/// The network server instance.
		/// </summary>
		public NetServer Server => _server;

		public ServerLoop()
		{
			_di = new FishDI();
			_di.AddSingleton<IFishEngineRunner, ServerEngineRunner>();
			_di.AddSingleton<IFishConfig, ServerConfig>();
			_di.AddSingleton<IFishDebug, Voxelgine.Engine.Debug>();
			_di.AddSingleton<IFishLogging, FishLogging>();
			_di.AddSingleton<ILerpManager, LerpManager>();

			_di.Build();
			_di.CreateScope();

			IFishEngineRunner eng = _di.GetRequiredService<IFishEngineRunner>();
			eng.DI = _di;

			ServerConfig cfg = _di.GetRequiredService<ServerConfig>();
			cfg.LogFolder = "server_logs";

			_logging = _di.GetRequiredService<IFishLogging>();
			_logging.Init();

			_server = new NetServer();

			_server.OnClientConnected += OnClientConnected;
			_server.OnClientDisconnected += OnClientDisconnected;
			_server.OnPacketReceived += OnPacketReceived;

			_simulation = new GameSimulation(eng);
		}

		/// <summary>
		/// Starts the server: generates the world, binds the network port, and enters the main loop.
		/// This method blocks until <see cref="Stop"/> is called.
		/// </summary>
		/// <param name="port">UDP port to bind.</param>
		/// <param name="worldSeed">Seed for world generation.</param>
		public void Start(int port, int worldSeed = 666)
		{
			_logging.WriteLine("VoxelgineServer - Aurora Falls Dedicated Server");
			_logging.WriteLine($"Generating world (seed: {worldSeed})...");

			_simulation.Map.GenerateFloatingIsland(DefaultWorldWidth, DefaultWorldLength, worldSeed);
			_simulation.Map.ClearPendingChanges();

			_logging.WriteLine("World generation complete.");
			_logging.WriteLine($"Starting server on port {port} (max {NetServer.MaxPlayers} players)...");

			_server.Start(port);
			_running = true;

			_logging.WriteLine("Server is running. Press Ctrl+C to stop.");

			RunLoop();
		}

		/// <summary>
		/// Signals the server loop to stop after the current tick completes.
		/// </summary>
		public void Stop()
		{
			_running = false;
		}

		private void RunLoop()
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			float accumulator = 0f;
			float previousTime = 0f;

			while (_running)
			{
				float newTime = (float)stopwatch.Elapsed.TotalSeconds;
				float frameTime = newTime - previousTime;

				if (frameTime > MaxFrameTime)
					frameTime = MaxFrameTime;

				previousTime = newTime;
				accumulator += frameTime;

				while (accumulator >= DeltaTime)
				{
					Tick(CurrentTime, DeltaTime);
					CurrentTime += DeltaTime;
					accumulator -= DeltaTime;
				}

				// Sleep briefly to avoid burning CPU when no ticks are pending
				float remainingTime = DeltaTime - accumulator;
				if (remainingTime > 0.001f)
				{
					Thread.Sleep(1);
				}
			}

			Shutdown();
		}

		private void Tick(float totalTime, float dt)
		{
			// 1. Process incoming network packets
			_server.Tick(totalTime);

			// 2. Update day/night cycle
			_simulation.DayNight.Update(dt);

			// 3. Update entity simulation
			// Note: Entity UpdateLockstep requires InputMgr; on the server, entities run
			// their own AI and don't process player input, so we pass null.
			// Player physics will be handled by Server Player Management (future task).
			_simulation.Entities.UpdateLockstep(totalTime, dt, null);
		}

		private void Shutdown()
		{
			_logging.WriteLine("Shutting down server...");
			_server.Stop(CurrentTime);
			_logging.WriteLine("Server stopped.");
		}

		private void OnClientConnected(NetConnection connection)
		{
			_logging.WriteLine($"Player connected: [{connection.PlayerId}] \"{connection.PlayerName}\" from {connection.RemoteEndPoint}");
		}

		private void OnClientDisconnected(NetConnection connection, string reason)
		{
			_logging.WriteLine($"Player disconnected: [{connection.PlayerId}] \"{connection.PlayerName}\" - {reason}");
		}

		private void OnPacketReceived(NetConnection connection, Packet packet)
		{
			// Game packet handling will be implemented in future tasks:
			// - Server player management (InputState, BlockPlaceRequest, BlockRemoveRequest, WeaponFire, etc.)
		}

		public void Dispose()
		{
			if (_disposed)
				return;

			_disposed = true;
			Stop();
			_server.Dispose();
		}

		/// <summary>
		/// Minimal <see cref="IFishConfig"/> implementation for the dedicated server.
		/// No window, no JSON config file — just logging folder.
		/// </summary>
		private class ServerConfig : IFishConfig
		{
			public int WindowWidth { get; set; } = 0;
			public int WindowHeight { get; set; } = 0;
			public string Title { get; set; } = "VoxelgineServer";
			public string LogFolder { get; set; } = "server_logs";
			public void LoadFromJson() { }
		}

		/// <summary>
		/// Minimal <see cref="IFishEngineRunner"/> implementation for the dedicated server.
		/// Presentation properties (MainMenuState, GameState, NPCPreviewState) are null —
		/// the server has no rendering or UI.
		/// </summary>
		private class ServerEngineRunner : IFishEngineRunner
		{
			public FishDI DI { get; set; }
			public int ChunkDrawCalls { get; set; }
			public bool DebugMode { get; set; }
			public float TotalTime { get; set; }
			public MainMenuStateFishUI MainMenuState { get; set; }
			public GameState GameState { get; set; }
			public NPCPreviewState NPCPreviewState { get; set; }

			public void Init() { }
		}
	}
}
