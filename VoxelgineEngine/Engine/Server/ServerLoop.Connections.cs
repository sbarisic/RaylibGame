using System.Numerics;
using Voxelgine.Engine.DI;
using Voxelgine.Graphics;

namespace Voxelgine.Engine.Server;

public partial class ServerLoop
{
	private sealed record PendingPlayer(Player Player, ServerInventory Inventory, string Name);

	private void OnClientConnected(NetConnection connection)
	{
		int playerId = connection.PlayerId;
		string playerName = connection.PlayerName;
		_logging.Log(
			GameLogLevel.Info,
			"Connection",
			$"reserved playerId={playerId} name={playerName} endpoint={connection.RemoteEndPoint}");

		Player player = new(_eng, playerId);
		ServerInventory inventory = new();
		if (_playerData.TryLoad(playerName, out Vector3 savedPos, out float savedHealth, out Vector3 savedVel, inventory))
		{
			if (savedHealth <= 0 || !IsSpawnPositionValid(savedPos))
			{
				player.SetPosition(PlayerSpawnPosition);
				player.ResetHealth();
				_logging.Log(GameLogLevel.Warning, "Persistence", $"invalid saved player state playerId={playerId}; using spawn={PlayerSpawnPosition}");
			}
			else
			{
				player.SetPosition(savedPos);
				player.Health = savedHealth;
				player.SetVelocity(savedVel);
			}
		}
		else
		{
			player.SetPosition(PlayerSpawnPosition);
		}

		_pendingPlayers[playerId] = new PendingPlayer(player, inventory, playerName);
		_worldStream.Begin(playerId, player.Position, _worldSeed, CurrentTime);
	}

	private void ActivatePendingPlayer(int playerId)
	{
		if (!_pendingPlayers.Remove(playerId, out PendingPlayer pending))
			return;

		NetConnection connection = _server.GetConnection(playerId);
		if (connection == null || connection.State != ConnectionState.Connected)
			return;

		Player player = pending.Player;
		_playerInventories[playerId] = pending.Inventory;
		NetworkInputSource inputSource = new();
		_playerInputSources[playerId] = inputSource;
		_playerInputMgrs[playerId] = new InputMgr(inputSource);
		_playerCommandQueues[playerId] = new ServerCommandQueue();

		foreach (Player existing in _simulation.Players.GetAllPlayers())
		{
			_server.SendTo(playerId, new PlayerJoinedPacket
			{
				PlayerId = existing.PlayerId,
				PlayerName = GetPlayerName(existing.PlayerId),
				Position = existing.Position,
			}, true, CurrentTime);
		}

		foreach (VoxEntity entity in _simulation.Entities.GetAllEntities())
		{
			_server.SendTo(playerId, BuildEntitySpawnPacket(entity), true, CurrentTime);
			_server.SendTo(playerId, BuildEntitySnapshotPacket(entity), true, CurrentTime);
		}

		_simulation.Players.AddPlayer(playerId, player);
		connection.IsGameplayActive = true;
		_server.BroadcastExcept(playerId, new PlayerJoinedPacket
		{
			PlayerId = playerId,
			PlayerName = pending.Name,
			Position = player.Position,
		}, true, CurrentTime);

		_server.SendTo(playerId, new DayTimeSyncPacket { TimeOfDay = _simulation.DayNight.TimeOfDay }, true, CurrentTime);
		_server.SendTo(playerId, pending.Inventory.CreateFullUpdatePacket(), true, CurrentTime);
		_server.SendTo(playerId, new ClientWorldStartPacket
		{
			StreamId = _worldStream.GetStreamId(playerId),
			ServerTick = _server.ServerTick,
			Health = player.Health,
			PhysicsState = player.CapturePhysicsState(),
		}, true, CurrentTime);

		_logging.Log(
			GameLogLevel.Info,
			"Connection",
			$"activated playerId={playerId} name={pending.Name} position={player.Position} players={_simulation.Players.Count}");
	}

	private void OnClientDisconnected(NetConnection connection, string reason)
	{
		int playerId = connection.PlayerId;
		string playerName = connection.PlayerName;
		_worldStream.Cancel(playerId);

		if (_pendingPlayers.Remove(playerId))
		{
			_logging.Log(
				GameLogLevel.Info,
				"Connection",
				$"loading-disconnect playerId={playerId} name={playerName} reason={reason}");
			return;
		}

		Player player = _simulation.Players.GetPlayer(playerId);
		if (player != null)
		{
			_playerInventories.TryGetValue(playerId, out ServerInventory inventory);
			_playerData.Save(playerName, player.Position, player.Health, player.GetVelocity(), inventory);
		}

		_playerInputMgrs.Remove(playerId);
		_playerInputSources.Remove(playerId);
		_playerCommandQueues.Remove(playerId);
		_respawnTimers.Remove(playerId);
		_playerAttackEndTimes.Remove(playerId);
		_playerInventories.Remove(playerId);
		_simulation.Players.RemovePlayer(playerId);
		_server.Broadcast(new PlayerLeftPacket { PlayerId = playerId }, true, CurrentTime);
		_logging.Log(
			GameLogLevel.Info,
			"Connection",
			$"disconnected playerId={playerId} name={playerName} reason={reason} players={_simulation.Players.Count}");
	}

	private bool IsSpawnPositionValid(Vector3 position)
	{
		if (!float.IsFinite(position.X) || !float.IsFinite(position.Y) || !float.IsFinite(position.Z))
			return false;
		int x = (int)MathF.Floor(position.X);
		int y = (int)MathF.Floor(position.Y);
		int z = (int)MathF.Floor(position.Z);
		return _simulation.Map.IsColumnResident(
			(int)Math.Floor((double)x / Chunk.ChunkSize),
			(int)Math.Floor((double)z / Chunk.ChunkSize)) &&
			_simulation.Map.GetBlock(x, y - 1, z) != BlockType.None &&
			_simulation.Map.GetBlock(x, y, z) == BlockType.None;
	}
}
