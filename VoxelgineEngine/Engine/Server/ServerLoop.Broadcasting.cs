using System.Numerics;

namespace Voxelgine.Engine.Server
{
	public partial class ServerLoop
	{
		/// <summary>
		/// Cached last-sent entity snapshot state for delta suppression.
		/// </summary>
		private struct LastEntitySnapshot
		{
			public Vector3 Position;
			public Vector3 Velocity;
			public byte AnimationState;
		}

		/// <summary>
		/// Tracks last-sent snapshot per entity NetworkId. Entries for entities that no longer
		/// exist are pruned each broadcast pass.
		/// </summary>
		private readonly Dictionary<int, LastEntitySnapshot> _lastEntitySnapshots = new();

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
		/// Broadcasts <see cref="EntitySnapshotPacket"/> for entities whose state has changed since
		/// the last broadcast. Skips entities with identical position, velocity, and animation state
		/// to reduce bandwidth for stationary entities (closed doors, idle pickups, etc.).
		/// </summary>
		private void BroadcastEntitySnapshots(float currentTime)
		{
			// Collect live entity IDs to prune stale entries afterwards
			int liveCount = 0;

			foreach (VoxEntity entity in _simulation.Entities.GetAllEntities())
				{
					liveCount++;
					int netId = entity.NetworkId;
					byte animState = GetEntityAnimationState(entity);

					// Broadcast speech changes for NPCs
					if (entity is VEntNPC npc && npc.IsSpeechDirty)
					{
						npc.ConsumeSpeechDirty();
						_server.Broadcast(new EntitySpeechPacket
						{
							NetworkId = netId,
							Text = npc.SpeechText,
							Duration = npc.SpeechDuration,
						}, true, currentTime);
					}

					// Skip if state hasn't changed since last broadcast
				if (_lastEntitySnapshots.TryGetValue(netId, out var last) &&
					last.Position == entity.Position &&
					last.Velocity == entity.Velocity &&
					last.AnimationState == animState)
				{
					continue;
				}

				var packet = new EntitySnapshotPacket
				{
					NetworkId = netId,
					Position = entity.Position,
					Velocity = entity.Velocity,
					AnimationState = animState,
				};
				_server.Broadcast(packet, false, currentTime);

				_lastEntitySnapshots[netId] = new LastEntitySnapshot
				{
					Position = entity.Position,
					Velocity = entity.Velocity,
					AnimationState = animState,
				};
			}

			// Prune entries for entities that no longer exist
			if (_lastEntitySnapshots.Count > liveCount)
			{
				var staleIds = new List<int>();
				foreach (int id in _lastEntitySnapshots.Keys)
				{
					if (_simulation.Entities.GetEntityByNetworkId(id) == null)
						staleIds.Add(id);
				}
				foreach (int id in staleIds)
					_lastEntitySnapshots.Remove(id);
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
