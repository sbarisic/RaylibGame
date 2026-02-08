using System.Diagnostics;
using System.IO;
using System.Numerics;
using Voxelgine.Engine.DI;
using Voxelgine.Graphics;
using Voxelgine.States;

namespace Voxelgine.Engine.Server
{
	/// <summary>
	/// Dedicated headless server loop. Owns a <see cref="NetServer"/> and <see cref="GameSimulation"/>,
	/// runs a fixed timestep (66.6 Hz) loop without any rendering or audio.
	/// </summary>
	public partial class ServerLoop : IDisposable
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
		/// Interval between periodic auto-saves in seconds (5 minutes).
		/// </summary>
		private const float AutoSaveInterval = 300f;

		/// <summary>
		/// Default world size for generated worlds.
		/// </summary>
		public const int DefaultWorldWidth = 256;
		public const int DefaultWorldLength = 256;

		/// <summary>
		/// Spawn position for connecting players. Computed from world surface after generation/load.
		/// </summary>
		public Vector3 PlayerSpawnPosition { get; private set; } = new Vector3(128, 66, 128);

		/// <summary>
		/// Spawn positions for server-side entities. Computed from world surface after generation/load.
		/// </summary>
		private Vector3 _pickupSpawnPos = new Vector3(130, 66, 126);
		private Vector3 _npcSpawnPos = new Vector3(126, 66, 128);

		/// <summary>
		/// File path for the persisted server world.
		/// </summary>
		private const string MapFile = "data/map.bin";

		private readonly NetServer _server;
		private readonly WorldTransferManager _worldTransfer;
		private readonly IFishLogging _logging;
		private readonly IFishEngineRunner _eng;
		private readonly FishDI _di;
		private GameSimulation _simulation;
		private int _worldSeed;

		/// <summary>
		/// Interval in seconds between <see cref="DayTimeSyncPacket"/> broadcasts.
		/// </summary>
		private const float TimeSyncInterval = 5f;

		/// <summary>
		/// Maximum distance a player can be from a block position to place/remove.
		/// Slightly larger than client-side reach (20) to account for prediction lag.
		/// </summary>
		private const float MaxBlockReach = 25f;

		/// <summary>
		/// Time in seconds before a dead player respawns.
		/// </summary>
		private const float RespawnDelay = 3f;

		/// <summary>
		/// Per-player input managers. Each player's <see cref="InputMgr"/> is backed by a
		/// <see cref="NetworkInputSource"/> that receives input from the client's <see cref="InputStatePacket"/>.
		/// </summary>
		private readonly Dictionary<int, InputMgr> _playerInputMgrs = new();
			private readonly Dictionary<int, NetworkInputSource> _playerInputSources = new();
			private readonly Dictionary<int, ServerInventory> _playerInventories = new();
			private readonly PlayerDataStore _playerData = new();

		private float _lastTimeSyncTime;

		/// <summary>
		/// Tracks death time for each dead player. Key = playerId, Value = time of death.
		/// </summary>
		private readonly Dictionary<int, float> _respawnTimers = new();

		private volatile bool _running;
		private bool _disposed;
		private float _lastAutoSaveTime;

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
			_di.AddSingleton<IFishDebug, Debug>();
			_di.AddSingleton<IFishLogging, FishLogging>();
			_di.AddSingleton<ILerpManager, LerpManager>();

			_di.Build();
			_di.CreateScope();

			IFishEngineRunner eng = _di.GetRequiredService<IFishEngineRunner>();
			eng.DI = _di;
			_eng = eng;

			ServerConfig cfg = _di.GetRequiredService<ServerConfig>();
			cfg.LogFolder = "server_logs";

			_logging = _di.GetRequiredService<IFishLogging>();
			_logging.Init();

			_server = new NetServer();
			_worldTransfer = new WorldTransferManager(_server);

			_server.OnClientConnected += OnClientConnected;
			_server.OnClientDisconnected += OnClientDisconnected;
			_server.OnPacketReceived += OnPacketReceived;
			_worldTransfer.OnTransferComplete += OnWorldTransferComplete;

			_simulation = new GameSimulation(eng);
		}

		/// <summary>
		/// Starts the server: generates the world, binds the network port, and enters the main loop.
		/// This method blocks until <see cref="Stop"/> is called.
		/// </summary>
		/// <param name="port">UDP port to bind.</param>
		/// <param name="worldSeed">Seed for world generation.</param>
		public void Start(int port, int worldSeed = 666, bool forceRegenerate = false)
		{
			_worldSeed = worldSeed;
			_logging.WriteLine("VoxelgineServer - Aurora Falls Dedicated Server");

			if (File.Exists(MapFile) && !forceRegenerate)
			{
				_logging.WriteLine($"Loading world from '{MapFile}'...");
				using var fileStream = File.OpenRead(MapFile);
				_simulation.Map.Read(fileStream);
				_logging.WriteLine("World loaded from file.");
			}
			else
			{
				if (forceRegenerate && File.Exists(MapFile))
					_logging.WriteLine("Force regeneration requested, ignoring existing world file.");

				_logging.WriteLine($"Generating world (seed: {worldSeed}, size: {DefaultWorldWidth}x{DefaultWorldLength})...");
				_simulation.Map.GenerateFloatingIsland(DefaultWorldWidth, DefaultWorldLength, worldSeed);
				_simulation.Map.ClearPendingChanges();
				_logging.WriteLine("World generation complete.");

				_logging.WriteLine($"Saving world to '{MapFile}'...");
				using var fileStream = File.Create(MapFile);
				_simulation.Map.Write(fileStream);
				_logging.WriteLine("World saved.");
			}

			// Find valid spawn points on the world surface
			FindAndSetSpawnPoints();

			_logging.WriteLine($"Starting server on port {port} (max {NetServer.MaxPlayers} players)...");

			// Spawn server-side entities
			SpawnEntities();

			_server.WorldSeed = worldSeed;
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
			// 0. Process queued admin commands
			ProcessCommands();

			// 1. Process incoming network packets
			_server.Tick(totalTime);

			// 2. Send pending world data fragments to connecting clients
			_worldTransfer.Tick(totalTime);

			// 3. Process player respawns
			ProcessRespawns();

			// 4. Process player input and run authoritative physics
			ProcessPlayerPhysics(dt);

			// 5. Update day/night cycle
			_simulation.DayNight.Update(dt);

			// 6. Broadcast time sync periodically
			BroadcastTimeSync(totalTime);

			// 7. Update entity simulation
			// Note: Entity UpdateLockstep requires InputMgr; on the server, entities run
			// their own AI and don't process player input, so we pass null.
			_simulation.Entities.UpdateLockstep(totalTime, dt, null);

			// 8. Broadcast authoritative player positions to all clients
			BroadcastPlayerSnapshots(totalTime);

			// 9. Broadcast pending block changes to all clients
			BroadcastBlockChanges(totalTime);

			// 10. Broadcast entity snapshots to all clients
			BroadcastEntitySnapshots(totalTime);

			// 11. Periodic auto-save
			if (totalTime - _lastAutoSaveTime >= AutoSaveInterval)
			{
				_lastAutoSaveTime = totalTime;
				SaveWorld();
			}
		}

		private void Shutdown()
		{
			_logging.WriteLine("Shutting down server...");
			SaveWorld();
			_server.Stop(CurrentTime);
			_logging.WriteLine("Server stopped.");
		}

		/// <summary>
		/// Runs authoritative physics for all connected players.
		/// For each player: ticks their <see cref="InputMgr"/> (which reads from <see cref="NetworkInputSource"/>),
		/// updates direction vectors from the camera angle, and runs <see cref="Player.UpdatePhysics"/>.
		/// If no input has been received for a player, the last known input is automatically repeated
		/// because <see cref="NetworkInputSource"/> retains its state between ticks.
		/// </summary>
		private void ProcessPlayerPhysics(float dt)
		{
			ChunkMap map = _simulation.Map;
			PhysData physData = _simulation.PhysicsData;

			foreach (Player player in _simulation.Players.GetAllPlayers())
			{
				int playerId = player.PlayerId;

				if (player.IsDead)
					continue;

				if (!_playerInputMgrs.TryGetValue(playerId, out InputMgr inputMgr))
					continue;

				// Tick the InputMgr so it polls the NetworkInputSource (captures current vs last state)
				inputMgr.Tick(CurrentTime);

				// Update cached direction vectors from the camera angle
				player.UpdateDirectionVectors();

				// Run authoritative Quake-style physics
				player.UpdatePhysics(map, physData, dt, inputMgr);
			}
		}

		/// <summary>
		/// Saves the current world state to <see cref="MapFile"/>.
		/// Called on shutdown and periodically during gameplay.
		/// </summary>
		private void SaveWorld()
		{
			try
			{
				_logging.WriteLine($"Saving world to '{MapFile}'...");
				using var fileStream = File.Create(MapFile);
				_simulation.Map.Write(fileStream);
				_logging.WriteLine("World saved.");
			}
			catch (Exception ex)
			{
				_logging.WriteLine($"ERROR: Failed to save world: {ex.Message}");
			}
		}

		/// <summary>
		/// Serializes the current world state (ChunkMap) into a GZip-compressed byte array.
		/// </summary>
		private byte[] SerializeWorld()
		{
			_logging.WriteLine("SerializeWorld: Serializing world...");
			using var ms = new MemoryStream();
			_simulation.Map.Write(ms);
			byte[] data = ms.ToArray();
			_logging.WriteLine($"SerializeWorld: Produced {data.Length:N0} bytes");
			return data;
		}

		/// <summary>
		/// Gets the display name for a player by querying the NetServer's connection.
		/// Returns empty string if the connection is not found.
		/// </summary>
		private string GetPlayerName(int playerId)
		{
			var conn = _server.GetConnection(playerId);
			return conn?.PlayerName ?? string.Empty;
		}

		/// <summary>
		/// Scans the world surface for valid spawn points and assigns them to the spawn position fields.
		/// Falls back to hardcoded defaults if not enough valid positions are found.
		/// </summary>
		private void FindAndSetSpawnPoints()
		{
			var spawnPoints = _simulation.Map.FindSpawnPoints(3, 5);

			if (spawnPoints.Count >= 1)
				PlayerSpawnPosition = spawnPoints[0];
			if (spawnPoints.Count >= 2)
				_pickupSpawnPos = spawnPoints[1];
			if (spawnPoints.Count >= 3)
				_npcSpawnPos = spawnPoints[2];

			_logging.WriteLine($"Spawn points: Player={PlayerSpawnPosition}, Pickup={_pickupSpawnPos}, NPC={_npcSpawnPos} ({spawnPoints.Count} found)");
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
		/// Presentation properties (MainMenuState, NPCPreviewState) are null —
		/// the server has no rendering or UI.
		/// </summary>
		private class ServerEngineRunner : IFishEngineRunner
		{
			public FishDI DI { get; set; }
			public int ChunkDrawCalls { get; set; }
			public bool DebugMode { get; set; }
			public float TotalTime { get; set; }
			public MainMenuStateFishUI MainMenuState { get; set; }
			public NPCPreviewState NPCPreviewState { get; set; }
			public MultiplayerGameState MultiplayerGameState { get; set; }

			public void Init() { }
		}
	}
}
