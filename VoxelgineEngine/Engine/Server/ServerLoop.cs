using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Voxelgine.Engine.DI;
using Voxelgine.Graphics;

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
		public const int DefaultWorldWidth = 1024;
		public const int DefaultWorldLength = 1024;

		/// <summary>
		/// Spawn position for connecting players. Computed from world surface after generation/load.
		/// </summary>
		public Vector3 PlayerSpawnPosition { get; private set; } = new Vector3(
			DefaultWorldWidth / 2,
			66,
			DefaultWorldLength / 2
		);

		/// <summary>
		/// Spawn positions for server-side entities. Computed from world surface after generation/load.
		/// </summary>
		private Vector3 _pickupSpawnPos = new Vector3(
			DefaultWorldWidth / 2 + 2,
			66,
			DefaultWorldLength / 2 - 2
		);
		private Vector3 _npcSpawnPos = new Vector3(
			DefaultWorldWidth / 2 - 2,
			66,
			DefaultWorldLength / 2
		);

		/// <summary>
		/// File path for the persisted server world.
		/// </summary>
		private const string MapFile = "data/map.bin";

		private readonly NetServer _server;
		private readonly WorldStreamManager _worldStream;
		private readonly IFishLogging _logging;
		private readonly IFishEngineRunner _eng;
		private readonly FishDI _di;
		private GameSimulation _simulation;
		private int _worldSeed;
		private WorldArchivePayloadCache _archivePayloadCache;

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
		private readonly Dictionary<int, ServerCommandQueue> _playerCommandQueues = new();
		private readonly Dictionary<int, ServerInventory> _playerInventories = new();
		private readonly Dictionary<int, PendingPlayer> _pendingPlayers = new();
		private readonly PlayerDataStore _playerData;

		private float _lastTimeSyncTime;

		/// <summary>
		/// Tracks death time for each dead player. Key = playerId, Value = time of death.
		/// </summary>
		private readonly Dictionary<int, float> _respawnTimers = new();

		/// <summary>
		/// Duration of the attack animation in seconds, used for animation state broadcasting.
		/// </summary>
		private const float AttackAnimDuration = 0.4f;

		/// <summary>
		/// Tracks the time at which each player's attack animation ends.
		/// Key = playerId, Value = time when the attack animation expires.
		/// </summary>
		private readonly Dictionary<int, float> _playerAttackEndTimes = new();

		private volatile bool _running;
		private readonly CancellationTokenSource _stopSource = new();
		private readonly TaskCompletionSource<bool> _startupCompletion = new(
			TaskCreationOptions.RunContinuationsAsynchronously
		);
		private int _startInvoked;
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

		/// <summary>
		/// Completes after the UDP socket is listening and the generated or loaded world
		/// is ready for clients. Faults when startup fails and is cancelled when stopped
		/// before readiness.
		/// </summary>
		public Task StartupTask => _startupCompletion.Task;

		public ServerLoop(GameLogLevel minimumLogLevel = GameLogLevel.Trace)
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
			cfg.LogFolder = "data";
			cfg.LogLevel = minimumLogLevel;

			_logging = _di.GetRequiredService<IFishLogging>();
			_logging.Init(true);
			_playerData = new PlayerDataStore("data/players", _logging);
			_logging.Log(GameLogLevel.Info, "Startup", $"Server initialized processId={Environment.ProcessId} logLevel={minimumLogLevel} workingDirectory={Environment.CurrentDirectory}");

			_server = new NetServer(_logging);
#if DEBUG
			//_server.PacketLoggingEnabled = true;
#endif
			_simulation = new GameSimulation(eng);
			_simulation.Entities.PlayerTouchedEntity += OnPlayerTouchedEntity;
			_worldStream = new WorldStreamManager(_server, _simulation.Map, _logging);

			_server.OnClientConnected += OnClientConnected;
			_server.OnClientDisconnected += OnClientDisconnected;
			_server.OnPacketReceived += OnPacketReceived;
			_worldStream.ClientReady += ActivatePendingPlayer;
		}

		/// <summary>
		/// Starts the server: generates the world, binds the network port, and enters the main loop.
		/// This method blocks until <see cref="Stop"/> is called.
		/// </summary>
		/// <param name="port">UDP port to bind.</param>
		/// <param name="worldSeed">Seed for world generation.</param>
		public void Start(int port, int worldSeed = 666, bool forceRegenerate = false)
		{
			if (Interlocked.Exchange(ref _startInvoked, 1) != 0)
				throw new InvalidOperationException("A server loop can only be started once.");

			CancellationToken cancellationToken = _stopSource.Token;
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				_worldSeed = worldSeed;
				_logging.ServerWriteLine("VoxelgineServer - Aurora Falls Dedicated Server");

				string mapDirectory = Path.GetDirectoryName(MapFile);
				if (!string.IsNullOrEmpty(mapDirectory))
					Directory.CreateDirectory(mapDirectory);

				bool generated = true;
				if (File.Exists(MapFile) && !forceRegenerate)
				{
					string backup = WorldArchive.MoveIncompatibleFileToBackup(MapFile);
					if (backup != null)
					{
						_logging.Log(
							GameLogLevel.Warning,
							"Persistence",
							$"incompatible-map backup={Path.GetFullPath(backup)}; regenerating formatVersion={WorldArchive.FormatVersion}");
					}
					else
					{
						Stopwatch loadTimer = Stopwatch.StartNew();
						WorldArchiveReadResult archive;
						using (FileStream archiveStream = File.OpenRead(MapFile))
							archive = WorldArchive.Read(archiveStream, cancellationToken);
						_simulation.Map.ReplaceAllColumns(archive.Columns);
						_archivePayloadCache = archive.PayloadCache;
						_worldStream.SetArchivePayloadCache(_archivePayloadCache);
						_worldSeed = archive.Metadata.WorldSeed;
						PlayerSpawnPosition = archive.Metadata.PlayerSpawn;
						_pickupSpawnPos = archive.Metadata.PickupSpawn;
						_npcSpawnPos = archive.Metadata.NpcSpawn;
						generated = false;
						_logging.Log(
							GameLogLevel.Info,
							"Persistence",
							$"world-load path={Path.GetFullPath(MapFile)} columns={archive.Columns.Count} seed={_worldSeed} durationMs={loadTimer.Elapsed.TotalMilliseconds:F1}");
					}
				}

				if (generated)
				{
					_logging.Log(GameLogLevel.Info, "Generation", $"begin seed={worldSeed} size={DefaultWorldWidth}x{DefaultWorldLength}");
					_simulation.Map.GenerateFloatingIsland(
						DefaultWorldWidth,
						DefaultWorldLength,
						worldSeed,
						cancellationToken);
					_simulation.Map.ClearPendingChanges();
					FindAndSetSpawnPoints(cancellationToken);
					SaveWorld();
				}
				else if (!IsSpawnPositionValid(PlayerSpawnPosition) ||
					!IsSpawnPositionValid(_pickupSpawnPos) ||
					!IsSpawnPositionValid(_npcSpawnPos))
				{
					_logging.Log(GameLogLevel.Warning, "Persistence", "archive spawn positions are invalid; searching deterministically");
					FindAndSetSpawnPoints(cancellationToken);
					SaveWorld();
				}

				_logging.ServerWriteLine($"Starting server on port {port} (max {NetServer.MaxPlayers} players)...");

				// Spawn server-side entities
				SpawnEntities();

				cancellationToken.ThrowIfCancellationRequested();
				_server.WorldSeed = _worldSeed;
				_server.Start(port);
				_running = true;
				if (cancellationToken.IsCancellationRequested)
				{
					_running = false;
					_server.Stop(CurrentTime);
					cancellationToken.ThrowIfCancellationRequested();
				}

				_startupCompletion.TrySetResult(true);
				_logging.ServerWriteLine("Server is running. Press Ctrl+C to stop.");

				RunLoop();
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				_startupCompletion.TrySetCanceled(cancellationToken);
				_logging.Log(GameLogLevel.Info, "Server", "Server startup cancelled.");
			}
			catch (Exception exception)
			{
				_startupCompletion.TrySetException(exception);
				throw;
			}
		}

		/// <summary>
		/// Signals the server loop to stop after the current tick completes.
		/// </summary>
		public void Stop()
		{
			if (!_stopSource.IsCancellationRequested)
				_stopSource.Cancel();
			_running = false;
		}

		public void Log(GameLogLevel level, string category, string message, Exception exception = null)
		{
			_logging.Log(level, category, message, exception);
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
			_worldStream.Tick(totalTime);

			// 3. Process player respawns
			ProcessRespawns();

			// 4. Process player input and run authoritative physics
			ProcessPlayerPhysics(dt);

			// 5. Kill players who fell out of the world
			CheckPlayerBounds();

			// 6. Update day/night cycle
			_simulation.DayNight.Update(dt);

			// 7. Broadcast time sync periodically
			BroadcastTimeSync(totalTime);

			// 8. Update entity simulation
			// Note: Entity UpdateLockstep requires InputMgr; on the server, entities run
			// their own AI and don't process player input, so we pass null.
			_simulation.Entities.UpdateLockstep(totalTime, dt, null);

			// 9. Kill and remove NPCs which fell out of the world
			RemoveFallenNpcs();

			// 10. Broadcast authoritative player positions to all clients
			BroadcastPlayerSnapshots(totalTime);

			// 11. Broadcast pending block changes to all clients
			BroadcastBlockChanges(totalTime);

			// 12. Broadcast entity snapshots to all clients
			BroadcastEntitySnapshots(totalTime);

			// 13. Periodic auto-save
			if (totalTime - _lastAutoSaveTime >= AutoSaveInterval)
			{
				_lastAutoSaveTime = totalTime;
				SaveWorld();
			}
		}

		private void Shutdown()
		{
			_logging.ServerWriteLine("Shutting down server...");
			SaveWorld();
			_server.Stop(CurrentTime);
			_logging.ServerWriteLine("Server stopped.");
		}

		/// <summary>
		/// Runs authoritative physics for all connected players.
		/// For each player: ticks their <see cref="InputMgr"/> (which reads from <see cref="NetworkInputSource"/>),
		/// updates direction vectors from the camera angle, and runs <see cref="Player.UpdatePhysics"/>.
		/// Commands are simulated in exact session-local sequence order. Up to four
		/// contiguous commands are processed for each player per server frame.
		/// </summary>
		private void ProcessPlayerPhysics(float dt)
		{
			PhysData physData = _simulation.PhysicsData;

			foreach (Player player in _simulation.Players.GetAllPlayers())
			{
				int playerId = player.PlayerId;

				if (!_playerInputMgrs.TryGetValue(playerId, out InputMgr inputMgr) ||
					!_playerInputSources.TryGetValue(playerId, out NetworkInputSource inputSource) ||
					!_playerCommandQueues.TryGetValue(playerId, out ServerCommandQueue commandQueue))
					continue;

				commandQueue.BeginFrame();
				for (int processed = 0; processed < 4 && commandQueue.TryDequeue(out InputCommand command); processed++)
				{
					InputState state = new();
					command.UnpackKeys(ref state);
					inputSource.SetState(state);
					inputMgr.Tick(CurrentTime);

					player.SetCamAngle(new Vector3(command.CameraAngle.X, command.CameraAngle.Y, 0));
					player.UpdateDirectionVectors();
					if (player.NoClip != command.NoClip)
					{
						player.NoClip = command.NoClip;
						_logging.Log(
							GameLogLevel.Debug,
							"Physics",
							$"Noclip changed playerId={playerId} enabled={player.NoClip} commandTick={command.TickNumber}"
						);
					}

					if (!player.IsDead)
						player.UpdatePhysics(_simulation.PhysicsWorld, physData, dt, inputMgr);
				}
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
				Stopwatch timer = Stopwatch.StartNew();
				string temporaryPath = MapFile + ".tmp";
				using (FileStream fileStream = File.Create(temporaryPath))
				{
					_archivePayloadCache = WorldArchive.Write(
						fileStream,
						_simulation.Map,
						new WorldArchiveMetadata(_worldSeed, PlayerSpawnPosition, _pickupSpawnPos, _npcSpawnPos),
						_archivePayloadCache);
					fileStream.Flush(flushToDisk: true);
				}
				File.Move(temporaryPath, MapFile, overwrite: true);
				_worldStream.SetArchivePayloadCache(_archivePayloadCache);
				_logging.Log(
					GameLogLevel.Info,
					"Persistence",
					$"world-save path={Path.GetFullPath(MapFile)} columns={_simulation.Map.ColumnCount} bytes={new FileInfo(MapFile).Length} durationMs={timer.Elapsed.TotalMilliseconds:F1}");
			}
			catch (Exception ex)
			{
				_logging.Log(GameLogLevel.Error, "Persistence", $"Failed to save world path={Path.GetFullPath(MapFile)}", ex);
			}
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
		private void FindAndSetSpawnPoints(CancellationToken cancellationToken)
		{
			var spawnPoints = _simulation.Map.FindSpawnPoints(3, 5, cancellationToken);

			if (spawnPoints.Count >= 1)
				PlayerSpawnPosition = spawnPoints[0];
			if (spawnPoints.Count >= 2)
				_pickupSpawnPos = spawnPoints[1];
			if (spawnPoints.Count >= 3)
				_npcSpawnPos = spawnPoints[2];

			_logging.ServerWriteLine($"Spawn points: Player={PlayerSpawnPosition}, Pickup={_pickupSpawnPos}, NPC={_npcSpawnPos} ({spawnPoints.Count} found)");
		}

		public void Dispose()
		{
			if (_disposed)
				return;

			_disposed = true;
			Stop();
			_server.Dispose();
			(_logging as IDisposable)?.Dispose();
			_stopSource.Dispose();
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
			public string LogFolder { get; set; } = "data";
			public GameLogLevel LogLevel { get; set; } = GameLogLevel.Trace;
			public void LoadFromJson() { }
		}

		/// <summary>
		/// Minimal <see cref="IFishEngineRunner"/> implementation for the dedicated server.
		/// </summary>
		private class ServerEngineRunner : IFishEngineRunner
		{
			public FishDI DI { get; set; }
			public int ChunkDrawCalls { get; set; }
			public bool DebugMode { get; set; }
			public float TotalTime { get; set; }

			public void Init() { }
		}
	}
}
