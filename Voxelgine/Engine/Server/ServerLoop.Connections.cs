using System.Numerics;

namespace Voxelgine.Engine.Server
{
	public partial class ServerLoop
	{
		private void OnClientConnected(NetConnection connection)
		{
			int playerId = connection.PlayerId;
			string playerName = connection.PlayerName;

			_logging.WriteLine($"Player connected: [{playerId}] \"{playerName}\" from {connection.RemoteEndPoint}");

			// Create server-side player instance (no GUI, sound, or rendering)
			Player player = new Player(_eng, playerId);
			player.SetPosition(PlayerSpawnPosition);

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
				Position = PlayerSpawnPosition,
			};
			_server.BroadcastExcept(playerId, joinedPacket, true, CurrentTime);

			// Send current time of day to the new client
			_server.SendTo(playerId, new DayTimeSyncPacket { TimeOfDay = _simulation.DayNight.TimeOfDay }, true, CurrentTime);

			// Serialize the world and begin streaming to the new client
			byte[] worldData = SerializeWorld();
			_worldTransfer.BeginTransfer(playerId, worldData);
			int totalFragments = (worldData.Length + WorldTransferManager.FragmentSize - 1) / WorldTransferManager.FragmentSize;
			_logging.WriteLine($"Player [{playerId}] \"{playerName}\" spawned at {PlayerSpawnPosition}. Streaming world ({worldData.Length:N0} bytes, {totalFragments} fragments). Players online: {_simulation.Players.Count}");
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

		private void OnWorldTransferComplete(int playerId)
		{
			string playerName = GetPlayerName(playerId);
			_logging.WriteLine($"World transfer complete for player [{playerId}] \"{playerName}\".");
		}
	}
}
