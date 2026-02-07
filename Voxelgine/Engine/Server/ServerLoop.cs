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
		public const int DefaultWorldWidth = 32;
		public const int DefaultWorldLength = 32;

		/// <summary>
		/// Default spawn position for connecting players (center of island, above surface).
		/// </summary>
		public static readonly Vector3 DefaultSpawnPosition = new Vector3(30, 66, 24);

		/// <summary>
		/// Spawn positions for server-side entities.
		/// </summary>
		private static readonly Vector3 PickupSpawnPos = new Vector3(37, 66, 15);
		private static readonly Vector3 NPCSpawnPos = new Vector3(32, 66, 14);

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

		private float _lastTimeSyncTime;

		/// <summary>
		/// Tracks death time for each dead player. Key = playerId, Value = time of death.
		/// </summary>
		private readonly Dictionary<int, float> _respawnTimers = new();

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
		public void Start(int port, int worldSeed = 666)
		{
			_worldSeed = worldSeed;
			_logging.WriteLine("VoxelgineServer - Aurora Falls Dedicated Server");

			if (File.Exists(MapFile))
			{
				_logging.WriteLine($"Loading world from '{MapFile}'...");
				using var fileStream = File.OpenRead(MapFile);
				_simulation.Map.Read(fileStream);
				_logging.WriteLine("World loaded from file.");
			}
			else
			{
				_logging.WriteLine($"Generating world (seed: {worldSeed}, size: {DefaultWorldWidth}x{DefaultWorldLength})...");
				_simulation.Map.GenerateFloatingIsland(DefaultWorldWidth, DefaultWorldLength, worldSeed);
				_simulation.Map.ClearPendingChanges();
				_logging.WriteLine("World generation complete.");

				_logging.WriteLine($"Saving world to '{MapFile}'...");
				using var fileStream = File.Create(MapFile);
				_simulation.Map.Write(fileStream);
				_logging.WriteLine("World saved.");
			}
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
		}

		private void Shutdown()
		{
			_logging.WriteLine("Shutting down server...");
			_server.Stop(CurrentTime);
			_logging.WriteLine("Server stopped.");
		}

		/// <summary>
		/// Spawns the initial server-side entities (matching the single-player world setup).
		/// </summary>
		private void SpawnEntities()
		{
			// Spawn pickup entity (use SetModelName to avoid Raylib GPU calls on headless server)
			var pickup = new VEntPickup();
			pickup.SetPosition(PickupSpawnPos);
			pickup.SetSize(Vector3.One);
			pickup.SetModelName("orb_xp/orb_xp.obj");
			_simulation.Entities.Spawn(_simulation, pickup);

			// Spawn NPC entity (use SetModelName to avoid Raylib GPU calls on headless server)
			var npc = new VEntNPC();
			npc.SetSize(new Vector3(0.9f, 1.8f, 0.9f));
			npc.SetPosition(NPCSpawnPos);
			npc.SetModelName("npc/humanoid.json");
			_simulation.Entities.Spawn(_simulation, npc);
			npc.InitPathfinding(_simulation.Map);

			_logging.WriteLine($"Spawned {_simulation.Entities.GetEntityCount()} entities.");
		}

		/// <summary>
		/// Broadcasts <see cref="EntitySnapshotPacket"/> for all entities to all clients.
		/// Sent unreliably at tick rate for client-side entity interpolation.
		/// </summary>
		private void BroadcastEntitySnapshots(float currentTime)
		{
			foreach (VoxEntity entity in _simulation.Entities.GetAllEntities())
			{
				var packet = new EntitySnapshotPacket
				{
					NetworkId = entity.NetworkId,
					Position = entity.Position,
					Velocity = entity.Velocity,
					AnimationState = GetEntityAnimationState(entity),
				};
				_server.Broadcast(packet, false, currentTime);
			}
		}

		/// <summary>
		/// Gets a compact animation state byte for an entity.
		/// 0 = idle, 1 = walk, 2 = attack.
		/// </summary>
		private static byte GetEntityAnimationState(VoxEntity entity)
		{
			if (entity is VEntNPC npc)
			{
				var animator = npc.GetAnimator();
				if (animator != null)
				{
					return animator.CurrentAnimation switch
					{
						"walk" => 1,
						"attack" => 2,
						_ => 0,
					};
				}
			}
			return 0;
		}

		/// <summary>
		/// Builds an <see cref="EntitySpawnPacket"/> from an existing entity.
		/// Serializes the entity's spawn properties (size, model, subclass data) into the Properties byte array.
		/// </summary>
		private static EntitySpawnPacket BuildEntitySpawnPacket(VoxEntity entity)
		{
			byte[] properties;
			using (var ms = new MemoryStream())
			using (var writer = new BinaryWriter(ms))
			{
				entity.WriteSpawnProperties(writer);
				properties = ms.ToArray();
			}

			return new EntitySpawnPacket
			{
				EntityType = entity.EntityTypeName,
				NetworkId = entity.NetworkId,
				Position = entity.Position,
				Properties = properties,
			};
		}

		private void OnClientConnected(NetConnection connection)
		{
			int playerId = connection.PlayerId;
			string playerName = connection.PlayerName;

			_logging.WriteLine($"Player connected: [{playerId}] \"{playerName}\" from {connection.RemoteEndPoint}");

			// Create server-side player instance (no GUI, sound, or rendering)
			Player player = new Player(_eng, playerId);
			player.SetPosition(DefaultSpawnPosition);

			// Create per-player input pipeline: NetworkInputSource → InputMgr
			var inputSource = new NetworkInputSource();
			var inputMgr = new InputMgr(inputSource);
			_playerInputSources[playerId] = inputSource;
			_playerInputMgrs[playerId] = inputMgr;

			// Send PlayerJoined for all existing players to the new client
			foreach (Player existing in _simulation.Players.GetAllPlayers())
			{
				var existingJoined = new PlayerJoinedPacket
				{
					PlayerId = existing.PlayerId,
					PlayerName = GetPlayerName(existing.PlayerId),
					Position = existing.Position,
				};
				_server.SendTo(playerId, existingJoined, true, CurrentTime);
			}

			// Send EntitySpawn for all existing entities to the new client
			foreach (VoxEntity entity in _simulation.Entities.GetAllEntities())
			{
				var spawnPacket = BuildEntitySpawnPacket(entity);
				_server.SendTo(playerId, spawnPacket, true, CurrentTime);
			}

			// Add the new player to the simulation
			_simulation.Players.AddPlayer(playerId, player);

			// Broadcast PlayerJoined for the new player to all other clients
			var joinedPacket = new PlayerJoinedPacket
			{
				PlayerId = playerId,
				PlayerName = playerName,
				Position = DefaultSpawnPosition,
			};
			_server.BroadcastExcept(playerId, joinedPacket, true, CurrentTime);

			// Send current time of day to the new client
			_server.SendTo(playerId, new DayTimeSyncPacket { TimeOfDay = _simulation.DayNight.TimeOfDay }, true, CurrentTime);

			// Serialize the world and begin streaming to the new client
			byte[] worldData = SerializeWorld();
			_worldTransfer.BeginTransfer(playerId, worldData);
			int totalFragments = (worldData.Length + WorldTransferManager.FragmentSize - 1) / WorldTransferManager.FragmentSize;
			_logging.WriteLine($"Player [{playerId}] \"{playerName}\" spawned at {DefaultSpawnPosition}. Streaming world ({worldData.Length:N0} bytes, {totalFragments} fragments). Players online: {_simulation.Players.Count}");
		}

		private void OnClientDisconnected(NetConnection connection, string reason)
		{
			int playerId = connection.PlayerId;
			string playerName = connection.PlayerName;

			_logging.WriteLine($"Player disconnected: [{playerId}] \"{playerName}\" - {reason}");

			// Cancel any in-progress world transfer
			_worldTransfer.CancelTransfer(playerId);

			// Clean up per-player input pipeline
			_playerInputMgrs.Remove(playerId);
			_playerInputSources.Remove(playerId);

			// Clean up respawn timer
			_respawnTimers.Remove(playerId);

			// Remove from simulation
			_simulation.Players.RemovePlayer(playerId);

			// Broadcast PlayerLeft to remaining clients
			var leftPacket = new PlayerLeftPacket
			{
				PlayerId = playerId,
			};
			_server.Broadcast(leftPacket, true, CurrentTime);

			_logging.WriteLine($"Player [{playerId}] \"{playerName}\" removed. Players online: {_simulation.Players.Count}");
		}

		private void OnPacketReceived(NetConnection connection, Packet packet)
		{
			switch (packet)
			{
				case InputStatePacket inputPacket:
					HandleInputState(connection, inputPacket);
					break;

				case BlockPlaceRequestPacket placeReq:
					HandleBlockPlaceRequest(connection, placeReq);
					break;

				case BlockRemoveRequestPacket removeReq:
					HandleBlockRemoveRequest(connection, removeReq);
					break;

				case WeaponFirePacket weaponFire:
					HandleWeaponFire(connection, weaponFire);
					break;
			}
		}

		/// <summary>
		/// Handles an <see cref="InputStatePacket"/> from a client.
		/// Unpacks the key bitmask into an <see cref="InputState"/>, sets the camera angle,
		/// and feeds the state into the player's <see cref="NetworkInputSource"/>.
		/// </summary>
		private unsafe void HandleInputState(NetConnection connection, InputStatePacket inputPacket)
		{
			int playerId = connection.PlayerId;

			if (!_playerInputSources.TryGetValue(playerId, out var inputSource))
				return;

			Player player = _simulation.Players.GetPlayer(playerId);
			if (player == null)
				return;

			// Unpack key bitmask into InputState
			InputState state = new InputState();
			inputPacket.UnpackKeys(ref state);
			state.MouseWheel = inputPacket.MouseWheel;

			// Feed the input into the player's NetworkInputSource
			inputSource.SetState(state);

			// Set the camera angle from the packet (Vector2 yaw/pitch → Vector3 with Z=0)
			player.SetCamAngle(new Vector3(inputPacket.CameraAngle.X, inputPacket.CameraAngle.Y, 0));
		}

		/// <summary>
		/// Handles a <see cref="BlockPlaceRequestPacket"/> from a client.
		/// Validates that the player is within reach, then applies the block change to the ChunkMap.
		/// The change is automatically logged by <see cref="ChunkMap.SetPlacedBlock"/> and
		/// will be broadcast to all clients in <see cref="BroadcastBlockChanges"/>.
		/// </summary>
		private void HandleBlockPlaceRequest(NetConnection connection, BlockPlaceRequestPacket packet)
		{
			int playerId = connection.PlayerId;
			Player player = _simulation.Players.GetPlayer(playerId);
			if (player == null)
				return;

			Vector3 blockCenter = new Vector3(packet.X + 0.5f, packet.Y + 0.5f, packet.Z + 0.5f);
			float distance = Vector3.Distance(player.Position, blockCenter);
			if (distance > MaxBlockReach)
				return;

			_simulation.Map.SetBlock(packet.X, packet.Y, packet.Z, (BlockType)packet.BlockType);
		}

		/// <summary>
		/// Handles a <see cref="BlockRemoveRequestPacket"/> from a client.
		/// Validates that the player is within reach, then removes the block from the ChunkMap.
		/// </summary>
		private void HandleBlockRemoveRequest(NetConnection connection, BlockRemoveRequestPacket packet)
		{
			int playerId = connection.PlayerId;
			Player player = _simulation.Players.GetPlayer(playerId);
			if (player == null)
				return;

			Vector3 blockCenter = new Vector3(packet.X + 0.5f, packet.Y + 0.5f, packet.Z + 0.5f);
			float distance = Vector3.Distance(player.Position, blockCenter);
			if (distance > MaxBlockReach)
				return;

			_simulation.Map.SetBlock(packet.X, packet.Y, packet.Z, BlockType.None);
		}

		/// <summary>
		/// Maximum weapon fire range. Slightly larger than client-side to account for prediction.
		/// </summary>
		private const float MaxWeaponRange = 25f;

		/// <summary>
		/// Damage dealt per weapon hit.
		/// </summary>
		private const float WeaponDamage = 25f;

		/// <summary>
		/// Handles a <see cref="WeaponFirePacket"/> from a client.
		/// Performs server-authoritative raycast against world blocks, entities, and other players.
		/// Broadcasts the resolved <see cref="WeaponFireEffectPacket"/> to all clients.
		/// </summary>
		private void HandleWeaponFire(NetConnection connection, WeaponFirePacket packet)
		{
			int playerId = connection.PlayerId;
			Player player = _simulation.Players.GetPlayer(playerId);
			if (player == null)
				return;

			Vector3 origin = packet.AimOrigin;
			Vector3 direction = packet.AimDirection;

			// Validate direction is normalized (prevent malicious packets)
			float dirLen = direction.Length();
			if (dirLen < 0.9f || dirLen > 1.1f)
				return;
			direction = Vector3.Normalize(direction);

			// Validate origin is near the player (anti-cheat: origin should be at eye position)
			if (Vector3.Distance(origin, player.Position) > 3f)
				return;

			// --- Raycast against world blocks ---
			Vector3 worldHitPos = Vector3.Zero;
			Vector3 worldHitNormal = Vector3.Zero;
			float worldDist = float.MaxValue;

			if (_simulation.Map.RaycastPrecise(origin, MaxWeaponRange, direction, out Vector3 preciseHitPoint, out Vector3 faceDir))
			{
				worldDist = Vector3.Distance(origin, preciseHitPoint);
				worldHitPos = preciseHitPoint;
				worldHitNormal = faceDir;
			}

			// --- Raycast against entities ---
			RaycastHit entityHit = _simulation.Entities.Raycast(origin, direction, MaxWeaponRange);

			// --- Raycast against other players ---
			RaycastHit playerHit = RaycastPlayers(origin, direction, MaxWeaponRange, playerId);

			// --- Determine closest hit ---
			byte hitType = (byte)FireHitType.None;
			Vector3 hitPos = origin + direction * MaxWeaponRange;
			Vector3 hitNormal = -direction;
			int hitEntityNetworkId = 0;
			float closestDist = MaxWeaponRange;

			if (entityHit.Hit && entityHit.Distance < closestDist)
			{
				closestDist = entityHit.Distance;
				hitType = (byte)FireHitType.Entity;
				hitPos = entityHit.HitPosition;
				hitNormal = entityHit.HitNormal;
				hitEntityNetworkId = entityHit.Entity?.NetworkId ?? 0;
			}

			int hitPlayerId = -1;

			if (playerHit.Hit && playerHit.Distance < closestDist)
			{
				closestDist = playerHit.Distance;
				hitType = (byte)FireHitType.Player;
				hitPos = playerHit.HitPosition;
				hitNormal = playerHit.HitNormal;
				hitEntityNetworkId = 0;
				hitPlayerId = playerHit.HitPlayerId;
			}

			if (worldDist < closestDist)
			{
				closestDist = worldDist;
				hitType = (byte)FireHitType.World;
				hitPos = worldHitPos;
				hitNormal = worldHitNormal;
				hitEntityNetworkId = 0;
				hitPlayerId = -1;
			}

			// Apply damage if a player was hit
			if (hitPlayerId >= 0)
			{
				Player hitPlayer = _simulation.Players.GetPlayer(hitPlayerId);
				if (hitPlayer != null && !hitPlayer.IsDead)
				{
					float damage = WeaponDamage;
					hitPlayer.TakeDamage(damage);

					// Broadcast damage notification
					var damagePacket = new PlayerDamagePacket
					{
						TargetPlayerId = hitPlayerId,
						DamageAmount = damage,
						SourcePlayerId = playerId,
					};
					_server.Broadcast(damagePacket, true, CurrentTime);

					if (hitPlayer.IsDead)
					{
						_logging.WriteLine($"Player [{hitPlayerId}] \"{GetPlayerName(hitPlayerId)}\" killed by [{playerId}] \"{GetPlayerName(playerId)}\"");
						_respawnTimers[hitPlayerId] = CurrentTime;
					}
				}
			}

			// Broadcast fire effect to all clients
			var effectPacket = new WeaponFireEffectPacket
			{
				PlayerId = playerId,
				WeaponType = packet.WeaponType,
				Origin = origin,
				Direction = direction,
				HitPosition = hitPos,
				HitNormal = hitNormal,
				HitType = hitType,
				EntityNetworkId = hitEntityNetworkId,
				HitPlayerId = hitPlayerId,
			};
			_server.Broadcast(effectPacket, true, CurrentTime);
		}

		/// <summary>
		/// Raycasts against all player AABBs except the shooter.
		/// Returns the closest player hit, or <see cref="RaycastHit.None"/>.
		/// </summary>
		private RaycastHit RaycastPlayers(Vector3 origin, Vector3 direction, float maxDistance, int excludePlayerId)
		{
			RaycastHit closestHit = RaycastHit.None;
			float closestDist = maxDistance;

			foreach (Player player in _simulation.Players.GetAllPlayers())
			{
				if (player.PlayerId == excludePlayerId)
					continue;

				if (player.IsDead)
					continue;

				AABB playerAABB = PhysicsUtils.CreatePlayerAABB(player.Position);

				if (RayMath.RayIntersectsAABB(origin, direction, playerAABB, closestDist, out float dist, out Vector3 normal))
				{
					if (dist < closestDist)
					{
						closestDist = dist;
						closestHit = new RaycastHit
						{
							Hit = true,
							Entity = null,
							Distance = dist,
							HitPosition = origin + direction * dist,
							HitNormal = normal,
							HitPlayerId = player.PlayerId,
						};
					}
				}
			}

			return closestHit;
		}

		/// <summary>
		/// Collects pending block changes from the ChunkMap and broadcasts them to all clients.
		/// Called once per tick after physics and entity updates.
		/// </summary>
		private void BroadcastBlockChanges(float currentTime)
		{
			var changes = _simulation.Map.GetPendingChanges();
			if (changes.Count == 0)
				return;

			foreach (var change in changes)
			{
				var packet = new BlockChangePacket
				{
					X = change.X,
					Y = change.Y,
					Z = change.Z,
					BlockType = (byte)change.NewType,
				};
				_server.Broadcast(packet, true, currentTime);
			}

			_simulation.Map.ClearPendingChanges();
		}

		/// <summary>
		/// Checks all dead players and respawns those whose respawn timer has expired.
		/// Resets health, teleports to spawn point, clears velocity, and logs the respawn.
		/// </summary>
		private void ProcessRespawns()
		{
			if (_respawnTimers.Count == 0)
				return;

			List<int> toRespawn = null;
			foreach (var kvp in _respawnTimers)
			{
				if (CurrentTime - kvp.Value >= RespawnDelay)
				{
					toRespawn ??= new List<int>();
					toRespawn.Add(kvp.Key);
				}
			}

			if (toRespawn == null)
				return;

			foreach (int playerId in toRespawn)
			{
				_respawnTimers.Remove(playerId);

				Player player = _simulation.Players.GetPlayer(playerId);
				if (player == null)
					continue;

				player.ResetHealth();
				player.SetPosition(DefaultSpawnPosition);
				player.SetVelocity(Vector3.Zero);

				_logging.WriteLine($"Player [{playerId}] \"{GetPlayerName(playerId)}\" respawned.");
			}
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
		/// Broadcasts a <see cref="WorldSnapshotPacket"/> containing all player positions to all clients.
		/// Sent unreliably at tick rate for remote player interpolation and local player reconciliation.
		/// </summary>
		private void BroadcastPlayerSnapshots(float currentTime)
		{
			var allPlayers = _simulation.Players.GetAllPlayers().ToArray();
			if (allPlayers.Length == 0)
				return;

			var snapshot = new WorldSnapshotPacket
			{
				TickNumber = _server.ServerTick,
				Players = new WorldSnapshotPacket.PlayerEntry[allPlayers.Length],
			};

			for (int i = 0; i < allPlayers.Length; i++)
			{
				Player p = allPlayers[i];
				Vector3 camAngle = p.GetCamAngle();
				snapshot.Players[i] = new WorldSnapshotPacket.PlayerEntry
				{
					PlayerId = p.PlayerId,
					Position = p.Position,
					Velocity = p.GetVelocity(),
					CameraAngle = new Vector2(camAngle.X, camAngle.Y),
					Health = p.Health,
				};
			}

			_server.Broadcast(snapshot, false, currentTime);
		}

		/// <summary>
		/// Broadcasts a <see cref="DayTimeSyncPacket"/> to all clients at a fixed interval.
		/// Keeps clients' day/night cycle synchronized with the server.
		/// </summary>
		private void BroadcastTimeSync(float currentTime)
		{
			if (currentTime - _lastTimeSyncTime < TimeSyncInterval)
				return;

			_lastTimeSyncTime = currentTime;

			var packet = new DayTimeSyncPacket
			{
				TimeOfDay = _simulation.DayNight.TimeOfDay,
			};

			_server.Broadcast(packet, true, currentTime);
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

		private void OnWorldTransferComplete(int playerId)
		{
			string playerName = GetPlayerName(playerId);
			_logging.WriteLine($"World transfer complete for player [{playerId}] \"{playerName}\".");
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
