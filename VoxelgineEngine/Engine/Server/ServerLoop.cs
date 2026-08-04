using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Voxelgine.Engine.DI;
using Voxelgine.Engine.World.Structures;
using Voxelgine.WorldGeneration;
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
		private readonly string _mapFile;
		private readonly RuntimePaths _runtimePaths;

		private readonly NetServer _server;
		private readonly WorldStreamManager _worldStream;
		private readonly WorldObjectStreamManager _worldObjectStream;
		private readonly IFishLogging _logging;
		private readonly IFishEngineRunner _eng;
		private readonly LerpManager _lerpManager;
		private GameSimulation _simulation;
		private int _worldSeed;
		private WorldArchivePayloadCache _archivePayloadCache;
		private InfrastructureMachineService _infrastructure;
		private HabitatProgressionService _progression;
		private FarmingService _farming;
		private NpcLifeService _npcLife;
		private NpcLifeRecord[] _loadedNpcLife = Array.Empty<NpcLifeRecord>();
		private readonly InventoryTransactionService _inventoryTransactions = new();
		private PersistedMachineIntent[] _loadedMachineIntents = Array.Empty<PersistedMachineIntent>();
		private HabitatMilestone _loadedMilestone;

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
		private readonly Dictionary<int, ServerClientSession> _sessions = new();
		private readonly PlayerDataStore _playerData;
		private ulong _nextPlayerSessionId = 1;

		private float _lastTimeSyncTime;

		/// <summary>
		/// Tracks death time for each dead player. Key = playerId, Value = time of death.
		/// </summary>

		/// <summary>
		/// Duration of the attack animation in seconds, used for animation state broadcasting.
		/// </summary>
		private const float AttackAnimDuration = 0.4f;

		/// <summary>
		/// Tracks the time at which each player's attack animation ends.
		/// Key = playerId, Value = time when the attack animation expires.
		/// </summary>

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

		public ServerLoop(
			RuntimePaths runtimePaths,
			GameLogLevel minimumLogLevel = GameLogLevel.Trace)
		{
			_runtimePaths = runtimePaths ?? throw new ArgumentNullException(nameof(runtimePaths));
			_runtimePaths.CreateDirectories();
			_mapFile = Path.Combine(_runtimePaths.WorldDirectory, "map.bin");
			ServerConfig cfg = new()
			{
				LogFolder = _runtimePaths.LogDirectory,
				LogLevel = minimumLogLevel,
			};
			_logging = new FishLogging(cfg);
			_logging.Init(true);
			_lerpManager = new LerpManager();
			_eng = new ServerEngineRunner(_logging, _lerpManager);
			_playerData = new PlayerDataStore(_runtimePaths.PlayerDirectory, _logging);
			_logging.Log(GameLogLevel.Info, "Startup", $"Server initialized processId={Environment.ProcessId} logLevel={minimumLogLevel} workingDirectory={Environment.CurrentDirectory}");

			_server = new NetServer(_logging);
#if DEBUG
			//_server.PacketLoggingEnabled = true;
#endif
			_simulation = new GameSimulation(_eng);
			_simulation.Entities.PlayerTouchedEntity += OnPlayerTouchedEntity;
			_simulation.Map.BlockChanged += OnFurnitureSupportChanged;
			_worldStream = new WorldStreamManager(_server, _simulation.Map, _logging);
			_worldObjectStream = new WorldObjectStreamManager(_server, _worldStream, _simulation.WorldObjects, _logging, () => CurrentTime);

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
		public void Start(int port, int worldSeed = 666, bool forceRegenerate = false, string worldPlanDirectory = null)
		{
			if (Interlocked.Exchange(ref _startInvoked, 1) != 0)
				throw new InvalidOperationException("A server loop can only be started once.");

			CancellationToken cancellationToken = _stopSource.Token;
			try
			{
				cancellationToken.ThrowIfCancellationRequested();
				_worldSeed = worldSeed;
				_logging.ServerWriteLine("VoxelgineServer - Aurora Falls Dedicated Server");

				string mapDirectory = Path.GetDirectoryName(_mapFile);
				if (!string.IsNullOrEmpty(mapDirectory))
					Directory.CreateDirectory(mapDirectory);

				bool generated = true;
				if (File.Exists(_mapFile) && !forceRegenerate)
				{
					string backup = WorldArchive.MoveIncompatibleFileToBackup(_mapFile);
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
						using (FileStream archiveStream = File.OpenRead(_mapFile))
							archive = WorldArchive.Read(archiveStream, cancellationToken);
						_simulation.Map.ReplaceAllColumns(archive.Columns);
						_simulation.WorldObjects.Restore(archive.WorldObjects);
						_simulation.Furniture.Restore(archive.Furniture);
						_simulation.Tombstones.Restore(archive.Tombstones);
						_simulation.Map.RestoreGeneratedFeatures(archive.Metadata.GeneratedFeatures);
						_archivePayloadCache = archive.PayloadCache;
						_worldStream.SetArchivePayloadCache(_archivePayloadCache);
						_worldSeed = archive.Metadata.WorldSeed;
						PlayerSpawnPosition = archive.Metadata.PlayerSpawn;
						_pickupSpawnPos = archive.Metadata.PickupSpawn;
						_npcSpawnPos = archive.Metadata.NpcSpawn;
						_loadedMachineIntents = archive.Metadata.MachineIntents ?? Array.Empty<PersistedMachineIntent>();
						_loadedMilestone = archive.Metadata.Milestone;
						_loadedNpcLife = archive.NpcLife.ToArray();
						foreach(NpcLifeRecord life in _loadedNpcLife)if(life.NpcId.Kind==StableNpcIdKind.Persistent)_simulation.PersistentEntityIds.Observe(life.NpcId.PersistentEntityId);
						_simulation.DayNight.RestoreAbsoluteGameTime(archive.Metadata.AbsoluteGameHours);
						generated = false;
						_logging.Log(
							GameLogLevel.Info,
							"Persistence",
							$"world-load path={Path.GetFullPath(_mapFile)} columns={archive.Columns.Count} seed={_worldSeed} durationMs={loadTimer.Elapsed.TotalMilliseconds:F1}");
					}
				}

				if (generated)
				{
					_logging.Log(GameLogLevel.Info, "Generation", $"begin seed={worldSeed} size={DefaultWorldWidth}x{DefaultWorldLength}");
					string structureDirectory = Path.Combine(AppContext.BaseDirectory, "data", "world", "structures");
					string ceramicFishPath = Path.Combine(AppContext.BaseDirectory, "data", "world", "ceramic-fish", "village.json");
					Stopwatch structureTimer = Stopwatch.StartNew();
					StructureBlueprintCatalog structureCatalog = StructureBlueprintCatalog.LoadDirectory(structureDirectory);
					CeramicVillageCatalog ceramicFish = CeramicVillageCatalog.Load(ceramicFishPath);
					WorldPlan worldPlan;
					if (!string.IsNullOrWhiteSpace(worldPlanDirectory))
					{
						string catalogHash = WorldPlanMaterializer.ComputeCatalogHash(structureCatalog);
						worldPlan = WorldPlanBundle.LoadAsync(worldPlanDirectory, catalogHash, cancellationToken, ceramicFish.Hash).GetAwaiter().GetResult();
						_worldSeed = worldPlan.Seed;
						_logging.Log(GameLogLevel.Info, "Generation", $"plan-load path={Path.GetFullPath(worldPlanDirectory)} seed={worldPlan.Seed}");
					}
					else
					{
						worldPlan = WorldPlanMaterializer.GeneratePlan(DefaultWorldWidth, DefaultWorldLength, worldSeed, structureCatalog, cancellationToken, ceramicFish: ceramicFish);
					}
					WorldPlanMaterializer.MaterializeAtomically(_simulation.Map, worldPlan, structureCatalog, cancellationToken, ceramicFish);
					PersistWorldPlanSidecar(worldPlan, cancellationToken);
					StructureGenerationTimings timings = _simulation.Map.StructureGenerationTimings;
					_logging.Log(GameLogLevel.Info, "Generation",
						$"structures sites={_simulation.Map.GeneratedFeatures.Sites.Count} routes={_simulation.Map.GeneratedFeatures.Routes.Count} blueprints={structureCatalog.Blueprints.Count} planningMs={timings.SitePlanning.TotalMilliseconds:F1} routesMs={timings.Routes.TotalMilliseconds:F1} stampingMs={timings.Stamping.TotalMilliseconds:F1} worldTotalMs={structureTimer.Elapsed.TotalMilliseconds:F1}");
					_logging.Log(GameLogLevel.Info, "Generation",
						$"ceramic-fish villages={worldPlan.VillageLayouts.Count}/{worldPlan.Villages.Count} empty={worldPlan.VillageFailures.Count} attempts={worldPlan.VillageLayouts.Sum(static layout => layout.Attempts)} topologyChecks={worldPlan.VillageLayouts.Sum(static layout => layout.TopologyChecks)} propagationChecks={worldPlan.VillageLayouts.Sum(static layout => layout.PropagationChecks)}");
					_simulation.Map.ClearPendingChanges();
					ApplyGeneratedSpawnPoints(cancellationToken);
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

				Stopwatch infrastructureTimer = Stopwatch.StartNew();
				_infrastructure = new InfrastructureMachineService(_simulation.Map, _simulation.Map.GeneratedFeatures, _logging);
				_infrastructure.StateChanged += BroadcastInfrastructureState;
				_logging.Log(GameLogLevel.Info, "Infrastructure", $"index-ready blocks={_simulation.Map.InfrastructureBlockCount} machines={_infrastructure.Machines.Count} durationMs={infrastructureTimer.Elapsed.TotalMilliseconds:F1}");
				Stopwatch progressionTimer = Stopwatch.StartNew();
				_progression = new HabitatProgressionService(_simulation.Map, _simulation.Map.GeneratedFeatures, _infrastructure, _logging);
				_infrastructure.RestoreRequestedStates(_loadedMachineIntents.Select(static intent => (intent.Key, intent.RequestedEnabled)));
				_progression.RestoreMilestone(_loadedMilestone);
				_logging.Log(GameLogLevel.Info, "Progression", $"restore milestone={_progression.Milestone} durationMs={progressionTimer.Elapsed.TotalMilliseconds:F1}");
				_farming = new FarmingService(_simulation.Map, _simulation.WorldObjects);
				_farming.RebuildIndex();
				_farming.PlantLostSupport += OnPlantLostSupport;
				RestoreGeneratedPhaseOneMarkers();
				_npcLife = new NpcLifeService(_simulation.Furniture, () => _simulation.Entities.GetAllEntities().OfType<VEntBed>(), _simulation.DayNight.AbsoluteGameHours);
				_npcLife.Restore(_loadedNpcLife, _simulation.DayNight.AbsoluteGameHours);

				// Spawn server-side entities
				Stopwatch entityTimer = Stopwatch.StartNew();
				SpawnEntities();
				ApplyGeneratedBedAssignments();
				foreach ((StableNpcId npcId, PersistentFurnitureKey missingBed) in _npcLife.RepairAssignments())
					_logging.Log(GameLogLevel.Warning,"NpcLife",$"cleared missing bed assignment npc={npcId} bed={missingBed}");
				_logging.Log(GameLogLevel.Info, "Entities", $"initial-spawn durationMs={entityTimer.Elapsed.TotalMilliseconds:F1}");

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
			ProcessPendingItemUses();

			// 5. Kill players who fell out of the world
			CheckPlayerBounds();

			// 6. Update day/night cycle
			_simulation.DayNight.Update(dt);
			_npcLife?.Update(_simulation.DayNight.AbsoluteGameHours,_simulation.DayNight.TimeOfDay,totalTime);

			// 7. Broadcast time sync periodically
			BroadcastTimeSync(totalTime);

			// 8. Update entity simulation
			// Note: Entity UpdateLockstep requires InputMgr; on the server, entities run
			// their own AI and don't process player input, so we pass null.
			_simulation.Entities.UpdateLockstep(totalTime, dt, null);

			// 9. Kill and remove NPCs which fell out of the world
			RemoveFallenNpcs();
			ProcessItemDrops();
			_infrastructure?.Update(maximumNetworks: 4);
			_progression?.Update(_server.ServerTick);
			_farming?.Update(dt);

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

				if (!_sessions.TryGetValue(playerId, out ServerClientSession session) ||
					!session.IsGameplayActive)
					continue;
				InputMgr inputMgr = session.InputManager;
				NetworkInputSource inputSource = session.InputSource;
				ServerCommandQueue commandQueue = session.CommandQueue;

				commandQueue.BeginFrame();
				for (int processed = 0; processed < 4 && commandQueue.TryDequeue(out InputCommand command); processed++)
				{
					InputState state = new();
					command.UnpackKeys(ref state);
					inputSource.SetState(state);
					inputMgr.Tick(CurrentTime);

					player.SetCamAngle(new Vector3(command.CameraAngle.X, command.CameraAngle.Y, 0));
					player.UpdateDirectionVectors();
					session.SelectedHotbarSlot = command.SelectedHotbarSlot;
					session.SelectionCommandTick = command.TickNumber;
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

					Vector3 interactionDirection = player.GetForward();
					if (!float.IsFinite(interactionDirection.X) || interactionDirection.LengthSquared() < 0.0001f)
						interactionDirection = Vector3.UnitZ;
					session.CommandHistory.Record(new SimulatedCommandRecord(
						command.TickNumber,
						command.SelectedHotbarSlot,
						player.Position,
						Vector3.Normalize(interactionDirection),
						IsCommandInputDown(command, InputKey.Click_Left),
						IsCommandInputDown(command, InputKey.Click_Right)));
				}
			}
		}

		private static bool IsCommandInputDown(InputCommand command, InputKey key) =>
			(int)key < 64 && (command.KeysBitmask & (1UL << (int)key)) != 0;

		private void PersistWorldPlanSidecar(WorldPlan plan, CancellationToken cancellationToken)
		{
			string target = Path.Combine(_runtimePaths.WorldDirectory, "world-plan");
			string pending = target + $".install-{Guid.NewGuid():N}";
			string backup = target + $".previous-{Guid.NewGuid():N}";
			bool movedExisting = false;
			try
			{
				WorldPlanBundle.SaveAsync(pending, plan, cancellationToken).GetAwaiter().GetResult();
				if (Directory.Exists(target))
				{
					Directory.Move(target, backup);
					movedExisting = true;
				}
				Directory.Move(pending, target);
				if (movedExisting) Directory.Delete(backup, recursive: true);
				_logging.Log(GameLogLevel.Info, "Generation", $"plan-sidecar path={Path.GetFullPath(target)} seed={plan.Seed}");
			}
			catch
			{
				if (Directory.Exists(pending)) Directory.Delete(pending, recursive: true);
				if (movedExisting && !Directory.Exists(target) && Directory.Exists(backup)) Directory.Move(backup, target);
				throw;
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
				string temporaryPath = _mapFile + ".tmp";
				using (FileStream fileStream = File.Create(temporaryPath))
				{
					_archivePayloadCache = WorldArchive.Write(
						fileStream,
						_simulation.Map,
						new WorldArchiveMetadata(
							_worldSeed,
							PlayerSpawnPosition,
							_pickupSpawnPos,
							_npcSpawnPos,
							_simulation.Map.GeneratedFeatures,
							_infrastructure?.CaptureRequestedStates()
								.Select(static state => new PersistedMachineIntent(state.Key, state.RequestedEnabled))
								.ToArray() ?? Array.Empty<PersistedMachineIntent>(),
							_progression?.Milestone ?? HabitatMilestone.None,
							_simulation.DayNight.AbsoluteGameHours),
						_archivePayloadCache,
						worldObjects: _simulation.WorldObjects.GetAll(),
						furniture: _simulation.Entities.GetAllEntities().Where(static entity=>entity is VEntItemBasket or VEntBed).Select(static entity=>entity is VEntItemBasket basket?basket.CaptureRecord():((VEntBed)entity).CaptureRecord()).ToArray(),
						npcLife: _npcLife?.Capture(),
						tombstones: _simulation.Tombstones.GetAll());
					fileStream.Flush(flushToDisk: true);
				}
				File.Move(temporaryPath, _mapFile, overwrite: true);
				_worldStream.SetArchivePayloadCache(_archivePayloadCache);
				SaveActivePlayers();
				_logging.Log(
					GameLogLevel.Info,
					"Persistence",
					$"world-save path={Path.GetFullPath(_mapFile)} columns={_simulation.Map.ColumnCount} bytes={new FileInfo(_mapFile).Length} durationMs={timer.Elapsed.TotalMilliseconds:F1}");
			}
			catch (Exception ex)
			{
				_logging.Log(GameLogLevel.Error, "Persistence", $"Failed to save world path={Path.GetFullPath(_mapFile)}", ex);
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

		private void ApplyGeneratedSpawnPoints(CancellationToken cancellationToken)
		{
			PlannedMarker? player = _simulation.Map.GeneratedFeatures.FindFirstMarker(StructureMarkerKind.PlayerSpawn);
			PlannedMarker? npc = _simulation.Map.GeneratedFeatures.FindFirstMarker(StructureMarkerKind.NpcSpawn);
			PlannedMarker? loot = _simulation.Map.GeneratedFeatures.FindFirstMarker(StructureMarkerKind.Loot);
			if (player == null)
			{
				FindAndSetSpawnPoints(cancellationToken);
				return;
			}

			PlayerSpawnPosition = ToSpawn(player.Value.Position);
			_npcSpawnPos = npc == null ? PlayerSpawnPosition + new Vector3(2, 0, 2) : ToSpawn(npc.Value.Position);
			_pickupSpawnPos = loot == null ? PlayerSpawnPosition : ToSpawn(loot.Value.Position);
			_logging.ServerWriteLine($"Generated spawn points: Player={PlayerSpawnPosition}, Pickup={_pickupSpawnPos}, NPC={_npcSpawnPos}");
		}

		private static Vector3 ToSpawn(BlockCoordinate coordinate) =>
			new(coordinate.X + 0.5f, coordinate.Y, coordinate.Z + 0.5f);

		public void Dispose()
		{
			if (_disposed)
				return;

			_disposed = true;
			if (_farming != null) _farming.PlantLostSupport -= OnPlantLostSupport;
			_farming?.Dispose();
			_progression?.Dispose();
			if (_infrastructure != null)
				_infrastructure.StateChanged -= BroadcastInfrastructureState;
			_infrastructure?.Dispose();
			Stop();
			_worldObjectStream.Dispose();
			_server.Dispose();
			(_logging as IDisposable)?.Dispose();
			_stopSource.Dispose();
		}

		private void SaveActivePlayers()
		{
			foreach (ServerClientSession session in _sessions.Values)
			{
				if (!session.IsGameplayActive)
					continue;
				_playerData.Save(
					session.PlayerName,
					session.Player.Position,
					session.Player.Health,
					session.Player.GetVelocity(),
					session.Inventory,
					session.SelectedHotbarSlot);
			}
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
			public string LogFolder { get; set; }
			public GameLogLevel LogLevel { get; set; } = GameLogLevel.Trace;
			public void LoadFromJson() { }
		}

		/// <summary>
		/// Minimal <see cref="IFishEngineRunner"/> implementation for the dedicated server.
		/// </summary>
		private class ServerEngineRunner : IFishEngineRunner
		{
			public ServerEngineRunner(IFishLogging logging, ILerpManager lerpManager)
			{
				Logging = logging;
				LerpManager = lerpManager;
			}

			public IFishLogging Logging { get; }
			public ILerpManager LerpManager { get; }
			public int ChunkDrawCalls { get; set; }
			public bool DebugMode { get; set; }
			public float TotalTime { get; set; }
		}
	}
}
