using System.Numerics;

namespace Voxelgine.Engine.Server
{
	public partial class ServerLoop
	{
		private void OnClientConnected(NetConnection connection)
		{
			int playerId = connection.PlayerId;
			string playerName = connection.PlayerName;

			_logging.ServerWriteLine($"Player connected: [{playerId}] \"{playerName}\" from {connection.RemoteEndPoint}");

			// Create server-side player instance (no GUI, sound, or rendering)
			Player player = new Player(_eng, playerId);

			// Create server-side inventory for this player
			var inventory = new ServerInventory();

			// Restore saved player state (position, health, velocity, inventory) if available
			if (_playerData.TryLoad(playerName, out Vector3 savedPos, out float savedHealth, out Vector3 savedVel, inventory))
			{
				player.SetPosition(savedPos);
				player.Health = savedHealth;
				player.SetVelocity(savedVel);
				_logging.ServerWriteLine($"Player [{playerId}] \"{playerName}\" restored from saved data (pos={savedPos}, health={savedHealth}).");
			}
			else
			{
				player.SetPosition(PlayerSpawnPosition);
			}

			_playerInventories[playerId] = inventory;

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
				Position = player.Position,
			};
			_server.BroadcastExcept(playerId, joinedPacket, true, CurrentTime);

			// Send current time of day to the new client
			_server.SendTo(playerId, new DayTimeSyncPacket { TimeOfDay = _simulation.DayNight.TimeOfDay }, true, CurrentTime);

			// Send the player's inventory state
			_server.SendTo(playerId, inventory.CreateFullUpdatePacket(), true, CurrentTime);

			// Serialize the world and begin streaming to the new client
			byte[] worldData = SerializeWorld();
			_worldTransfer.BeginTransfer(playerId, worldData);
			int totalFragments = (worldData.Length + WorldTransferManager.FragmentSize - 1) / WorldTransferManager.FragmentSize;
			_logging.ServerWriteLine($"Player [{playerId}] \"{playerName}\" spawned at {player.Position}. Streaming world ({worldData.Length:N0} bytes, {totalFragments} fragments). Players online: {_simulation.Players.Count}");
		}

		private void OnClientDisconnected(NetConnection connection, string reason)
		{
			int playerId = connection.PlayerId;
			string playerName = connection.PlayerName;

			_logging.ServerWriteLine($"Player disconnected: [{playerId}] \"{playerName}\" - {reason}");

			// Cancel any in-progress world transfer
			_worldTransfer.CancelTransfer(playerId);

			// Save player state before removal
			var player = _simulation.Players.GetPlayer(playerId);
			if (player != null)
			{
				_playerInventories.TryGetValue(playerId, out var inventory);
				_playerData.Save(playerName, player.Position, player.Health, player.GetVelocity(), inventory);
				_logging.ServerWriteLine($"Player [{playerId}] \"{playerName}\" state saved.");
			}

			// Clean up per-player input pipeline
			_playerInputMgrs.Remove(playerId);
			_playerInputSources.Remove(playerId);

			// Clean up respawn timer, attack timer, inventory, and input tick tracking
			_respawnTimers.Remove(playerId);
			_playerAttackEndTimes.Remove(playerId);
			_playerInventories.Remove(playerId);
			_lastInputTicks.Remove(playerId);

			// Remove from simulation
			_simulation.Players.RemovePlayer(playerId);

			// Broadcast PlayerLeft to remaining clients
			var leftPacket = new PlayerLeftPacket
			{
				PlayerId = playerId,
			};
			_server.Broadcast(leftPacket, true, CurrentTime);

			_logging.ServerWriteLine($"Player [{playerId}] \"{playerName}\" removed. Players online: {_simulation.Players.Count}");
		}

		private void OnWorldTransferComplete(int playerId)
		{
			string playerName = GetPlayerName(playerId);
			_logging.ServerWriteLine($"World transfer complete for player [{playerId}] \"{playerName}\".");

			// Re-send inventory after world transfer — the initial InventoryUpdate sent during
			// connect may have arrived before the client created its simulation and been dropped.
			if (_playerInventories.TryGetValue(playerId, out var inventory))
			{
				_server.SendTo(playerId, inventory.CreateFullUpdatePacket(), true, CurrentTime);
				_logging.ServerWriteLine($"Player [{playerId}] \"{playerName}\" inventory re-sent after world transfer.");
			}
		}
	}
}
