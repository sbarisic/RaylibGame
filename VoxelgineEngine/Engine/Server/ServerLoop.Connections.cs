using System.Numerics;
using Voxelgine.Engine.DI;
using Voxelgine.Graphics;

namespace Voxelgine.Engine.Server;

public partial class ServerLoop
{
	private void OnClientConnected(NetConnection connection)
	{
		int playerId = connection.PlayerId;
		_logging.Log(GameLogLevel.Info, "Connection", $"reserved playerId={playerId} name={connection.PlayerName} endpoint={connection.RemoteEndPoint}");

		Player player = new(_eng, playerId);
		var inventory = new PlayerInventory();
		byte selectedHotbarSlot = 0;
		if (_playerData.TryLoad(
			connection.PlayerName,
			out Vector3 savedPosition,
			out float savedHealth,
			out Vector3 savedVelocity,
			inventory,
			out selectedHotbarSlot))
		{
			if (savedHealth <= 0 || !IsSpawnPositionValid(savedPosition))
			{
				player.SetPosition(PlayerSpawnPosition);
				player.ResetHealth();
				_logging.Log(GameLogLevel.Warning, "Persistence", $"invalid saved player state playerId={playerId}; using spawn={PlayerSpawnPosition}");
			}
			else
			{
				player.SetPosition(savedPosition);
				player.Health = savedHealth;
				player.SetVelocity(savedVelocity);
			}
		}
		else
		{
			player.SetPosition(PlayerSpawnPosition);
		}

		var session = new ServerClientSession(
			new PlayerSessionId(_nextPlayerSessionId++),
			connection,
			player,
			inventory)
		{
			SelectedHotbarSlot = selectedHotbarSlot,
		};
		_sessions.Add(playerId, session);
		_worldStream.Begin(playerId, player.Position, _worldSeed, CurrentTime);
	}

	private void ActivatePendingPlayer(int playerId)
	{
		if (!_sessions.TryGetValue(playerId, out ServerClientSession session) || session.IsGameplayActive)
			return;

		NetConnection connection = session.Connection;
		if (connection.State != ConnectionState.Connected)
			return;

		Player player = session.Player;
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
		session.IsGameplayActive = true;
		connection.IsGameplayActive = true;
		_server.BroadcastExcept(playerId, new PlayerJoinedPacket
		{
			PlayerId = playerId,
			PlayerName = session.PlayerName,
			Position = player.Position,
		}, true, CurrentTime);

		_server.SendTo(playerId, new DayTimeSyncPacket { TimeOfDay = _simulation.DayNight.TimeOfDay }, true, CurrentTime);
		SendInventoryState(session, 0, true);
		_server.SendTo(playerId, new ClientWorldStartPacket
		{
			StreamId = _worldStream.GetStreamId(playerId),
			ServerTick = _server.ServerTick,
			Health = player.Health,
			PhysicsState = player.CapturePhysicsState(),
		}, true, CurrentTime);

		_logging.Log(GameLogLevel.Info, "Connection", $"activated playerId={playerId} name={session.PlayerName} position={player.Position} players={_simulation.Players.Count}");
	}

	private void OnClientDisconnected(NetConnection connection, string reason)
	{
		int playerId = connection.PlayerId;
		_worldStream.Cancel(playerId);
		if (!_sessions.Remove(playerId, out ServerClientSession session))
			return;

		if (!session.IsGameplayActive)
		{
			_logging.Log(GameLogLevel.Info, "Connection", $"loading-disconnect playerId={playerId} name={session.PlayerName} reason={reason}");
			return;
		}

		ResolveCursorForDisconnect(session.Inventory);
		_playerData.Save(
			session.PlayerName,
			session.Player.Position,
			session.Player.Health,
			session.Player.GetVelocity(),
			session.Inventory,
			session.SelectedHotbarSlot);
		session.ClearTransientState();
		_simulation.Players.RemovePlayer(playerId);
		_server.Broadcast(new PlayerLeftPacket { PlayerId = playerId }, true, CurrentTime);
		_logging.Log(GameLogLevel.Info, "Connection", $"disconnected playerId={playerId} name={session.PlayerName} reason={reason} players={_simulation.Players.Count}");
	}

	private static void ResolveCursorForDisconnect(PlayerInventory inventory)
	{
		if (!inventory.Cursor.IsEmpty)
			inventory.ApplyClick(InventoryActionKind.CancelCursor, PlayerInventory.NoCursorOrigin);
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
