using Voxelgine.Engine;
using Raylib_cs;
using Voxelgine.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using FishUI.Controls;

namespace Voxelgine.States
{
	public unsafe partial class MPClientGameState
	{
		// ======================================= Network Event Handlers ==========================================

		private void OnConnected(ConnectAcceptPacket accept)
		{
			_logging.ClientWriteLine($"MPClientGameState: Connected as player {accept.PlayerId}");
			_statusText = "Connected! Loading world...";
		}

		private void OnDisconnected(string reason)
		{
			_logging.ClientWriteLine($"MPClientGameState: Disconnected: {reason}");

			if (_initialized)
			{
				// Was in-game — show "Connection Lost" overlay with reconnect/return options
				_connectionLost = true;
				_disconnectReason = reason;
				Raylib.EnableCursor();
			}
			else
			{
				// Was still connecting/loading — show error on the status screen
				_statusText = "";
				_errorText = $"Disconnected: {reason}";
			}
		}

		private void OnConnectionRejected(string reason)
		{
			_logging.ClientWriteLine($"MPClientGameState: Rejected: {reason}");
			_statusText = "";
			_errorText = $"Connection rejected: {reason}";
		}

		private void OnWorldDataReady(byte[] compressedData)
		{
			_logging.ClientWriteLine($"MPClientGameState: World data received ({compressedData.Length} bytes compressed)");
			_statusText = "Building world...";

			try
			{
				// Create game simulation
				_logging.ClientWriteLine("MPClientGameState: Creating GameSimulation...");
				_simulation = new GameSimulation(Eng);
				_simulation.DayNight.IsAuthority = false; // Server controls time
				_logging.ClientWriteLine("MPClientGameState: GameSimulation created");

				// Load world
				_logging.ClientWriteLine("MPClientGameState: Reading ChunkMap from stream...");
				using (var ms = new MemoryStream(compressedData))
				{
					_simulation.Map.Read(ms);
				}
				_logging.ClientWriteLine("MPClientGameState: ChunkMap loaded successfully");

				_logging.ClientWriteLine("MPClientGameState: Computing lighting...");
				_simulation.Map.ComputeLighting();
				_logging.ClientWriteLine("MPClientGameState: Lighting computed");

				// Create GUI
				_logging.ClientWriteLine("MPClientGameState: Creating gameplay UI...");
				CreateGameplayUI();
				_logging.ClientWriteLine("MPClientGameState: Gameplay UI created");

				// Create sound
				_logging.ClientWriteLine("MPClientGameState: Creating SoundMgr...");
				_snd = new SoundMgr();
				_snd.Init();
				_logging.ClientWriteLine("MPClientGameState: SoundMgr initialized");

				// Create particle system
				_logging.ClientWriteLine("MPClientGameState: Creating ParticleSystem...");
				_particle = new ParticleSystem();
				_particle.Init(
					(pt) => _simulation.Map.Collide(pt, Vector3.Zero, out Vector3 _),
					(pt) => _simulation.Map.GetBlock(pt),
					(pt) => _simulation.Map.GetLightColor(pt)
				);
				_logging.ClientWriteLine("MPClientGameState: ParticleSystem initialized");

				// Create local player
				_logging.ClientWriteLine($"MPClientGameState: Creating Player (id={_client.PlayerId}, name={_playerName})...");
				var ply = new Player(Eng, _gui, _playerName, true, _snd, _client.PlayerId);
				_logging.ClientWriteLine("MPClientGameState: Player created, adding to PlayerManager...");
				_simulation.Players.AddLocalPlayer(_client.PlayerId, ply);

				_logging.ClientWriteLine("MPClientGameState: InitGUI...");
				ply.InitGUI(_gameWindow, _gui);
				_logging.ClientWriteLine("MPClientGameState: InitGUI complete");

				_logging.ClientWriteLine("MPClientGameState: Player.Init...");
				ply.Init(_simulation.Map);
				_logging.ClientWriteLine("MPClientGameState: Player.Init complete");

				ply.OnMenuToggled = (cursorVisible) =>
				{
					if (_debugMenuWindow != null)
					{
						_debugMenuWindow.Visible = cursorVisible;
						if (cursorVisible)
							_debugMenuWindow.BringToFront();
					}
				};

				_logging.ClientWriteLine("MPClientGameState: SetPosition...");
				ply.SetPosition(new Vector3(32, 73, 19)); // Default spawn, server will correct
				_logging.ClientWriteLine("MPClientGameState: SetPosition complete");

				// Set entity manager to non-authoritative (server owns entity state)
				_simulation.Entities.IsAuthority = false;

				// Process any InventoryUpdate packet that arrived before the simulation was created
				if (_pendingInventoryUpdate != null)
				{
					_logging.ClientWriteLine($"MPClientGameState: Replaying buffered InventoryUpdatePacket ({_pendingInventoryUpdate.Slots.Length} slots)...");
					HandleInventoryUpdate(_pendingInventoryUpdate);
					_pendingInventoryUpdate = null;
				}

				// Process any PlayerJoined packets that arrived before the simulation was created
				if (_pendingPlayerJoins.Count > 0)
				{
					_logging.ClientWriteLine($"MPClientGameState: Processing {_pendingPlayerJoins.Count} buffered PlayerJoined packet(s)...");
					foreach (var pending in _pendingPlayerJoins)
					{
						HandlePlayerJoined(pending);
					}
					_pendingPlayerJoins.Clear();
				}

				// Process any entity packets that arrived before the simulation was created
				if (_pendingEntityPackets.Count > 0)
				{
					_logging.ClientWriteLine($"MPClientGameState: Processing {_pendingEntityPackets.Count} buffered entity packet(s)...");
					float replayTime = (float)Raylib.GetTime();
					foreach (var pending in _pendingEntityPackets)
					{
						switch (pending)
						{
							case EntitySpawnPacket spawn:
								HandleEntitySpawn(spawn);
								break;
							case EntityRemovePacket remove:
								HandleEntityRemove(remove);
								break;
							case EntitySnapshotPacket snapshot:
								HandleEntitySnapshot(snapshot, replayTime);
								break;
						}
					}
					_pendingEntityPackets.Clear();
				}

				// Finish loading
				_logging.ClientWriteLine("MPClientGameState: FinishLoading...");
				_client.FinishLoading();
				_initialized = true;
				_statusText = "";
				_errorText = "";

				Raylib.DisableCursor();

				_logging.ClientWriteLine("MPClientGameState: World loaded, entering gameplay");
			}
			catch (Exception ex)
			{
				_logging.ClientWriteLine($"MPClientGameState: Failed to load world: {ex}");
				_statusText = "";
				_errorText = $"Failed to load world: {ex.Message}";
			}
		}

		private void OnWorldTransferFailed(string error)
		{
			_logging.ClientWriteLine($"MPClientGameState: World transfer failed: {error}");
			_statusText = "";
			_errorText = $"World transfer failed: {error}";
		}

		private void OnPacketReceived(Packet packet)
		{
			float currentTime = (float)Raylib.GetTime();

			switch (packet)
			{
				case WorldSnapshotPacket snapshot:
					HandleWorldSnapshot(snapshot, currentTime);
					break;

				case PlayerJoinedPacket joined:
					HandlePlayerJoined(joined);
					break;

				case PlayerLeftPacket left:
					HandlePlayerLeft(left);
					break;

				case DayTimeSyncPacket timeSync:
					if (_simulation != null)
						_simulation.DayNight.SetTime(timeSync.TimeOfDay);
					break;

				case BlockChangePacket blockChange:
					HandleBlockChange(blockChange);
					break;

				case EntitySpawnPacket entitySpawn:
					HandleEntitySpawn(entitySpawn);
					break;

				case EntityRemovePacket entityRemove:
					HandleEntityRemove(entityRemove);
					break;

				case EntitySnapshotPacket entitySnapshot:
					HandleEntitySnapshot(entitySnapshot, currentTime);
					break;

				case EntitySpeechPacket entitySpeech:
					HandleEntitySpeech(entitySpeech);
					break;

				case WeaponFireEffectPacket fireEffect:
					HandleWeaponFireEffect(fireEffect);
					break;

				case PlayerDamagePacket damage:
					HandlePlayerDamage(damage);
					break;

				case InventoryUpdatePacket inventoryUpdate:
					HandleInventoryUpdate(inventoryUpdate);
					break;

				case SoundEventPacket soundEvent:
					HandleSoundEvent(soundEvent);
					break;

				case ChatMessagePacket chatMsg:
					HandleChatMessage(chatMsg);
					break;

				case KillFeedPacket killFeed:
					HandleKillFeed(killFeed);
					break;
			}
		}

		private void HandleWorldSnapshot(WorldSnapshotPacket snapshot, float currentTime)
		{
			if (_simulation == null)
				return;

			foreach (var entry in snapshot.Players)
			{
				if (entry.PlayerId == _client.PlayerId)
				{
					// Sync health from server
					_simulation.LocalPlayer.Health = entry.Health;

					// Skip prediction reconciliation until the server has processed
					// at least one of our InputStatePackets
					if (entry.LastInputTick <= 0)
						continue;

					// Local player — reconciliation using server's last-processed input tick
					bool needsCorrection = _prediction.ProcessServerSnapshot(
						entry.LastInputTick,
						entry.Position,
						entry.Velocity
					);

					if (needsCorrection)
					{
						// Capture pre-correction position for visual smoothing
						Vector3 preCorrection = _simulation.LocalPlayer.Position;

						PredictionReconciler.Reconcile(
							_simulation.LocalPlayer,
							entry.Position,
							entry.Velocity,
							entry.LastInputTick,
							_client.LocalTick,
							_inputBuffer,
							_prediction,
							_simulation.Map,
							_simulation.PhysicsData,
							DeltaTime
						);

						// Compute visual offset: difference between where we were and where we should be.
						// If the error is small (< SnapThreshold), smooth visually instead of hard-snapping.
						Vector3 postCorrection = _simulation.LocalPlayer.Position;
						Vector3 delta = preCorrection - postCorrection;

						if (delta.LengthSquared() < ClientPrediction.SnapThreshold * ClientPrediction.SnapThreshold)
						{
							_correctionSmoothOffset += delta;
						}
						else
						{
							// Large correction (teleport) — snap immediately
							_correctionSmoothOffset = Vector3.Zero;
						}
					}
				}
				else
				{
					// Remote player — apply snapshot for interpolation
					var remote = _simulation.Players.GetRemotePlayer(entry.PlayerId);
					if (remote != null)
					{
						remote.ApplySnapshot(entry.Position, entry.Velocity, entry.CameraAngle, entry.AnimationState, currentTime);
					}
				}
			}
		}

		private void HandlePlayerJoined(PlayerJoinedPacket joined)
		{
			if (joined.PlayerId == _client.PlayerId)
				return; // That's us

			if (_simulation == null)
			{
				// World not loaded yet — buffer for later processing
				_pendingPlayerJoins.Add(joined);
				return;
			}

			_logging.ClientWriteLine($"MPClientGameState: Player joined: {joined.PlayerName} (ID {joined.PlayerId})");

			var remote = new RemotePlayer(joined.PlayerId, joined.PlayerName, Eng);
			remote.SetPosition(joined.Position);
			_simulation.Players.AddRemotePlayer(remote);
		}

		private void HandlePlayerLeft(PlayerLeftPacket left)
		{
			if (_simulation == null)
				return;

			_logging.ClientWriteLine($"MPClientGameState: Player left (ID {left.PlayerId})");
			_simulation.Players.RemoveRemotePlayer(left.PlayerId);
		}

		private void HandleBlockChange(BlockChangePacket blockChange)
		{
			if (_simulation == null)
				return;

			_simulation.Map.SetBlock(blockChange.X, blockChange.Y, blockChange.Z, (BlockType)blockChange.BlockType);
		}

		/// <summary>
		/// Handles an <see cref="EntitySpawnPacket"/> from the server.
		/// Creates the entity locally with the server-assigned network ID.
		/// </summary>
		private void HandleEntitySpawn(EntitySpawnPacket packet)
		{
			if (_simulation == null)
			{
				_pendingEntityPackets.Add(packet);
				return;
			}

			// Don't duplicate if already exists
			if (_simulation.Entities.GetEntityByNetworkId(packet.NetworkId) != null)
				return;

			VoxEntity entity = CreateEntityByType(packet.EntityType);
			if (entity == null)
			{
				_logging.ClientWriteLine($"MPClientGameState: Unknown entity type '{packet.EntityType}'");
				return;
			}

			entity.SetPosition(packet.Position);

			// Read spawn properties (size, model, subclass data)
			if (packet.Properties.Length > 0)
			{
				using var ms = new MemoryStream(packet.Properties);
				using var reader = new BinaryReader(ms);
				entity.ReadSpawnProperties(reader);
			}

			_simulation.Entities.SpawnWithNetworkId(_simulation, entity, packet.NetworkId);
			_logging.ClientWriteLine($"MPClientGameState: Entity spawned: {packet.EntityType} (netId={packet.NetworkId})");
		}

		/// <summary>
		/// Handles an <see cref="EntityRemovePacket"/> from the server.
		/// Removes the entity by network ID.
		/// </summary>
		private void HandleEntityRemove(EntityRemovePacket packet)
		{
			if (_simulation == null)
			{
				_pendingEntityPackets.Add(packet);
				return;
			}

			var removed = _simulation.Entities.Remove(packet.NetworkId);
			_entitySnapshots.Remove(packet.NetworkId);

			if (removed != null)
				_logging.ClientWriteLine($"MPClientGameState: Entity removed (netId={packet.NetworkId})");
		}

		/// <summary>
		/// Handles an <see cref="EntitySnapshotPacket"/> from the server.
		/// Stores the snapshot in the interpolation buffer for smooth rendering.
		/// </summary>
		private void HandleEntitySnapshot(EntitySnapshotPacket packet, float currentTime)
		{
			if (_simulation == null)
			{
				_pendingEntityPackets.Add(packet);
				return;
			}

			var entity = _simulation.Entities.GetEntityByNetworkId(packet.NetworkId);
			if (entity == null)
				return;

			// Add to interpolation buffer
			if (!_entitySnapshots.TryGetValue(packet.NetworkId, out var buffer))
			{
				buffer = new SnapshotBuffer<EntitySnapshot>();
				_entitySnapshots[packet.NetworkId] = buffer;
			}

			buffer.Add(new EntitySnapshot
			{
				Position = packet.Position,
				Velocity = packet.Velocity,
				AnimationState = packet.AnimationState,
			}, currentTime);
		}

		/// <summary>
		/// Handles an <see cref="EntitySpeechPacket"/> from the server.
		/// Sets the speech bubble text and duration on the target NPC.
		/// </summary>
		private void HandleEntitySpeech(EntitySpeechPacket packet)
		{
			if (_simulation == null)
				return;

			var entity = _simulation.Entities.GetEntityByNetworkId(packet.NetworkId);
			if (entity is VEntNPC npc)
			{
				npc.Speak(packet.Text, packet.Duration);
			}
		}

		/// <summary>
		/// Updates entity positions from interpolation buffers. Called each frame.
		/// </summary>
		private void UpdateEntityInterpolation(float currentTime)
		{
			float renderTime = currentTime - EntityInterpolationDelay;

			foreach (var kvp in _entitySnapshots)
			{
				int networkId = kvp.Key;
				var buffer = kvp.Value;

				var entity = _simulation.Entities.GetEntityByNetworkId(networkId);
				if (entity == null)
					continue;

				if (buffer.Sample(renderTime, out var from, out var to, out float t))
				{
					entity.Position = Vector3.Lerp(from.Position, to.Position, t);
					entity.Velocity = Vector3.Lerp(from.Velocity, to.Velocity, t);
				}
				else if (buffer.Count == 1)
				{
					// Only one snapshot — just snap to it
					entity.Position = from.Position;
					entity.Velocity = from.Velocity;
				}

				// Update animation from latest snapshot state
				UpdateEntityAnimation(entity, to.AnimationState, Raylib.GetFrameTime());
			}
		}

		/// <summary>
		/// Applies animation state from the server to an entity.
		/// 0 = idle, 1 = walk, 2 = attack.
		/// </summary>
		private static void UpdateEntityAnimation(VoxEntity entity, byte animationState, float deltaTime)
		{
			if (entity is VEntNPC npc)
			{
				var animator = npc.GetAnimator();
				if (animator != null)
				{
					string targetAnim = animationState switch
					{
						1 => "walk",
						2 => "attack",
						_ => "idle",
					};

					if (animator.CurrentAnimation != targetAnim)
						animator.Play(targetAnim);

					animator.Update(deltaTime);
				}

				// Derive look direction from velocity (server syncs velocity but not look direction)
				Vector3 horizontalVel = new Vector3(entity.Velocity.X, 0, entity.Velocity.Z);
				if (horizontalVel.LengthSquared() > 0.01f)
					npc.SetLookDirection(Vector3.Normalize(horizontalVel));
			}

			// Update cosmetic visuals (rotation) on the client
			entity.UpdateVisuals(deltaTime);
		}

		/// <summary>
		/// Creates a <see cref="VoxEntity"/> instance from a type name string.
		/// </summary>
		private static VoxEntity CreateEntityByType(string entityType)
		{
			return entityType switch
			{
				"VEntNPC" => new VEntNPC(),
				"VEntPickup" => new VEntPickup(),
				"VEntSlidingDoor" => new VEntSlidingDoor(),
				"VEntPlayer" => new VEntPlayer(),
				_ => null,
			};
		}

		/// <summary>
		/// Collects local block changes (from player placing/removing blocks) and sends them
		/// to the server as <see cref="BlockPlaceRequestPacket"/> or <see cref="BlockRemoveRequestPacket"/>.
		/// </summary>
		private void SendPendingBlockChanges(float currentTime)
		{
			if (_simulation == null || _client == null || !_client.IsConnected)
				return;

			var changes = _simulation.Map.GetPendingChanges();
			if (changes.Count == 0)
				return;

			foreach (var change in changes)
			{
				if (change.NewType == BlockType.None)
				{
					var packet = new BlockRemoveRequestPacket
					{
						X = change.X,
						Y = change.Y,
						Z = change.Z,
					};
					_client.Send(packet, true, currentTime);
				}
				else
				{
					var packet = new BlockPlaceRequestPacket
					{
						X = change.X,
						Y = change.Y,
						Z = change.Z,
						BlockType = (byte)change.NewType,
					};
					_client.Send(packet, true, currentTime);
				}
			}

			_simulation.Map.ClearPendingChanges();
		}

		/// <summary>
		/// Sends a <see cref="WeaponFirePacket"/> to the server for authoritative hit resolution.
		/// </summary>
		public void SendWeaponFire(Vector3 origin, Vector3 direction)
		{
			if (_client == null || !_client.IsConnected)
				return;

			var packet = new WeaponFirePacket
			{
				WeaponType = 0,
				AimOrigin = origin,
				AimDirection = direction,
			};
			_client.Send(packet, true, (float)Raylib.GetTime());
		}

		/// <summary>
		/// Spawns predicted fire effects (tracer and hit particles) based on a local client-side raycast.
		/// Called immediately on weapon fire so the local player sees instant visual feedback.
		/// </summary>
		public void SpawnPredictedFireEffects(Vector3 origin, Vector3 direction, float maxRange)
		{
			if (_simulation == null || _particle == null)
				return;

			Vector3 muzzlePos = origin + direction * 0.5f;

			// --- Raycast against world blocks ---
			float worldDist = float.MaxValue;
			Vector3 worldHitPos = Vector3.Zero;
			Vector3 worldHitNormal = Vector3.Zero;

			if (_simulation.Map.RaycastPrecise(origin, maxRange, direction, out Vector3 preciseHitPoint, out Vector3 faceDir))
			{
				worldDist = Vector3.Distance(origin, preciseHitPoint);
				worldHitPos = preciseHitPoint;
				worldHitNormal = faceDir;
			}

			// --- Raycast against entities ---
			RaycastHit entityHit = _simulation.Entities.Raycast(origin, direction, maxRange);

			// --- Determine closest hit ---
			FireHitType hitType = FireHitType.None;
			Vector3 hitPos = origin + direction * maxRange;
			Vector3 hitNormal = -direction;
			float closestDist = maxRange;
			VoxEntity hitEntity = null;

			if (entityHit.Hit && entityHit.Distance < closestDist)
			{
				closestDist = entityHit.Distance;
				hitType = FireHitType.Entity;
				hitPos = entityHit.HitPosition;
				hitNormal = entityHit.HitNormal;
				hitEntity = entityHit.Entity;
			}

			if (worldDist < closestDist)
			{
				closestDist = worldDist;
				hitType = FireHitType.World;
				hitPos = worldHitPos;
				hitNormal = worldHitNormal;
				hitEntity = null;
			}

			// Tracer line from muzzle to hit point
			_particle.SpawnTracer(muzzlePos, hitPos);

			// Hit particles based on predicted hit type
			switch (hitType)
			{
				case FireHitType.Entity:
					if (hitEntity is VEntNPC)
					{
						for (int i = 0; i < 8; i++)
						{
							_particle.SpawnBlood(hitPos, hitNormal * 0.5f, (0.8f + (float)Random.Shared.NextDouble() * 0.4f) * 0.85f);
						}
					}
					else
					{
						for (int i = 0; i < 6; i++)
						{
							float forceFactor = 10.6f;
							float randomUnitFactor = 0.6f;
							if (hitNormal.Y == 0)
							{
								forceFactor *= 2;
								randomUnitFactor = 0.4f;
							}
							Vector3 rndDir = Vector3.Normalize(hitNormal + Utils.GetRandomUnitVector() * randomUnitFactor);
							_particle.SpawnSpark(hitPos, rndDir * forceFactor, Color.White, (float)(Random.Shared.NextDouble() + 0.5));
						}
					}
					break;

				case FireHitType.World:
					for (int i = 0; i < 6; i++)
					{
						float forceFactor = 10.6f;
						float randomUnitFactor = 0.6f;
						if (hitNormal.Y == 0)
						{
							forceFactor *= 2;
							randomUnitFactor = 0.4f;
						}
						Vector3 rndDir = Vector3.Normalize(hitNormal + Utils.GetRandomUnitVector() * randomUnitFactor);
						_particle.SpawnFire(hitPos, rndDir * forceFactor, Color.White, (float)(Random.Shared.NextDouble() + 0.5));
					}
					break;
			}
		}

		/// <summary>
		/// Handles a <see cref="WeaponFireEffectPacket"/> from the server.
		/// Spawns tracer, blood, and spark particles based on the hit result.
		/// </summary>
		private void HandleWeaponFireEffect(WeaponFireEffectPacket packet)
		{
			if (_simulation == null || _particle == null)
				return;

			Vector3 muzzlePos = packet.Origin + packet.Direction * 0.5f;
			bool isLocalPlayer = packet.PlayerId == _client.PlayerId;

			// Play fire sound for other players' shots
			if (!isLocalPlayer && _snd != null)
			{
				_snd.PlayCombo("shoot1", _simulation.LocalPlayer.Position, _simulation.LocalPlayer.GetForward(), packet.Origin);
			}

			// Tracer line from muzzle to hit point (skip for local player — predicted locally)
			if (!isLocalPlayer)
				_particle.SpawnTracer(muzzlePos, packet.HitPosition);

			FireHitType hitType = (FireHitType)packet.HitType;
			switch (hitType)
			{
				case FireHitType.Entity:
					// Always apply authoritative NPC twitch from server
					bool isNpcHit = false;
					if (packet.EntityNetworkId != 0)
					{
						VoxEntity hitEntity = _simulation.Entities.GetEntityByNetworkId(packet.EntityNetworkId);
						if (hitEntity is VEntNPC npc)
						{
							isNpcHit = true;
							npc.TwitchBodyPart("body", packet.Direction);
						}
					}

					// Particles only for remote players (local player predicted them)
					if (!isLocalPlayer)
					{
						if (isNpcHit)
						{
							for (int i = 0; i < 8; i++)
							{
								_particle.SpawnBlood(packet.HitPosition, packet.HitNormal * 0.5f, (0.8f + (float)Random.Shared.NextDouble() * 0.4f) * 0.85f);
							}
						}
						else
						{
							for (int i = 0; i < 6; i++)
							{
								float forceFactor = 10.6f;
								float randomUnitFactor = 0.6f;

								if (packet.HitNormal.Y == 0)
								{
									forceFactor *= 2;
									randomUnitFactor = 0.4f;
								}

								Vector3 rndDir = Vector3.Normalize(packet.HitNormal + Utils.GetRandomUnitVector() * randomUnitFactor);
								_particle.SpawnSpark(packet.HitPosition, rndDir * forceFactor, Color.White, (float)(Random.Shared.NextDouble() + 0.5));
							}
						}
					}
					break;

				case FireHitType.Player:
					// Always show blood for player hits — client cannot predict these
					for (int i = 0; i < 8; i++)
					{
						_particle.SpawnBlood(packet.HitPosition, packet.HitNormal * 0.5f, (0.8f + (float)Random.Shared.NextDouble() * 0.4f) * 0.85f);
					}
					break;

				case FireHitType.World:
					// Particles only for remote players (local player predicted them)
					if (!isLocalPlayer)
					{
						for (int i = 0; i < 6; i++)
						{
							float forceFactor = 10.6f;
							float randomUnitFactor = 0.6f;

							if (packet.HitNormal.Y == 0)
							{
								forceFactor *= 2;
								randomUnitFactor = 0.4f;
							}

							Vector3 rndDir = Vector3.Normalize(packet.HitNormal + Utils.GetRandomUnitVector() * randomUnitFactor);
							_particle.SpawnFire(packet.HitPosition, rndDir * forceFactor, Color.White, (float)(Random.Shared.NextDouble() + 0.5));
						}
					}
					break;
			}
		}

		private void HandleSoundEvent(SoundEventPacket packet)
		{
			if (_simulation == null || _snd == null)
				return;

			// Local player already played the sound on their end
			if (packet.SourcePlayerId == _client.PlayerId)
				return;

			Vector3 ears = _simulation.LocalPlayer.Position;
			Vector3 dir = _simulation.LocalPlayer.GetForward();

			switch ((SoundEventType)packet.EventType)
			{
				case SoundEventType.BlockBreak:
					_snd.PlayCombo("block_break", ears, dir, packet.Position);
					break;

				case SoundEventType.BlockPlace:
					_snd.PlayCombo("block_place", ears, dir, packet.Position);
					break;
			}
		}

		private void HandlePlayerDamage(PlayerDamagePacket packet)
		{
			if (_simulation == null)
				return;

			if (packet.TargetPlayerId == _client.PlayerId)
			{
				_logging.ClientWriteLine($"MPClientGameState: Took {packet.DamageAmount} damage from player [{packet.SourcePlayerId}]. Health: {_simulation.LocalPlayer.Health}");
			}
		}

		private void HandleKillFeed(KillFeedPacket packet)
		{
			string weaponName = packet.WeaponType switch
			{
				0 => "Gun",
				_ => "Gun",
			};

			string text = $"{packet.KillerName} killed {packet.VictimName} with {weaponName}";
			_killFeedToast?.Show(text, ToastType.Error, KillFeedDuration);
		}

		private void HandleChatMessage(ChatMessagePacket packet)
		{
			string senderName;
			if (packet.PlayerId < 0)
			{
				senderName = null;
			}
			else if (packet.PlayerId == _client?.PlayerId)
			{
				senderName = _playerName ?? "You";
			}
			else
			{
				var remote = _simulation?.Players?.GetRemotePlayer(packet.PlayerId);
				senderName = remote?.PlayerName ?? $"Player {packet.PlayerId}";
			}

			string displayText = senderName != null ? $"{senderName}: {packet.Message}" : packet.Message;
			_chatToast?.Show(displayText, ToastType.Info, ChatMessageDuration);
			_logging.ClientWriteLine($"[Chat] {displayText}");
		}

		private void HandleInventoryUpdate(InventoryUpdatePacket packet)
		{
			if (_simulation?.LocalPlayer == null)
			{
				_pendingInventoryUpdate = packet;
				_logging.ClientWriteLine($"MPClientGameState: Buffered InventoryUpdatePacket ({packet.Slots.Length} slots) — simulation not ready");
				return;
			}

			foreach (var slot in packet.Slots)
			{
				InventoryItem item = _simulation.LocalPlayer.GetInventoryItem(slot.SlotIndex);
				if (item != null)
					item.Count = slot.Count;
			}
		}

		private void DrawRemotePlayerNameTags()
		{
			if (_simulation?.LocalPlayer == null || _simulation.Players == null)
				return;

			Camera3D camera = _simulation.LocalPlayer.RenderCam;

			foreach (var remotePlayer in _simulation.Players.GetAllRemotePlayers())
			{
				remotePlayer.DrawNameTag(camera, _simulation.Map);
			}
		}
	}
}
