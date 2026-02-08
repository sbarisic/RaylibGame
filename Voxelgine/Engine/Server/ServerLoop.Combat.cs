using System.Numerics;

namespace Voxelgine.Engine.Server
{
	public partial class ServerLoop
	{
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

			// Set attack animation timer for the shooter
			_playerAttackEndTimes[playerId] = CurrentTime + AttackAnimDuration;

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

							// Broadcast kill feed event to all clients
							var killFeedPacket = new KillFeedPacket
							{
								KillerName = GetPlayerName(playerId),
								VictimName = GetPlayerName(hitPlayerId),
								WeaponType = packet.WeaponType,
							};
							_server.Broadcast(killFeedPacket, true, CurrentTime);
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
					player.SetPosition(PlayerSpawnPosition);
					player.SetVelocity(Vector3.Zero);

				_logging.WriteLine($"Player [{playerId}] \"{GetPlayerName(playerId)}\" respawned.");
			}
		}
	}
}
