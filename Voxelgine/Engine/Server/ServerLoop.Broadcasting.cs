using System.Numerics;

namespace Voxelgine.Engine.Server
{
	public partial class ServerLoop
	{
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
				_lastInputTicks.TryGetValue(p.PlayerId, out int lastInputTick);
				snapshot.Players[i] = new WorldSnapshotPacket.PlayerEntry
				{
					PlayerId = p.PlayerId,
					Position = p.Position,
					Velocity = p.GetVelocity(),
					CameraAngle = new Vector2(camAngle.X, camAngle.Y),
					Health = p.Health,
					AnimationState = GetPlayerAnimationState(p),
					LastInputTick = lastInputTick,
				};
			}

			_server.Broadcast(snapshot, false, currentTime);
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
		/// Determines the animation state byte for a player.
		/// 0 = idle, 1 = walk, 2 = attack (recently fired weapon or placed/destroyed block).
		/// </summary>
		private byte GetPlayerAnimationState(Player player)
		{
			// Attack takes priority — check if the player recently performed an action
			if (_playerAttackEndTimes.TryGetValue(player.PlayerId, out float attackEnd) && CurrentTime < attackEnd)
				return 2;

			// Walk vs idle based on XZ velocity
			Vector3 vel = player.GetVelocity();
			float xzSpeed = new Vector2(vel.X, vel.Z).Length();
			return xzSpeed > 0.5f ? (byte)1 : (byte)0;
		}
	}
}
