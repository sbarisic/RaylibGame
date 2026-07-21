using System;
using System.Numerics;

namespace Voxelgine.Engine.AI
{
	public partial class AIRunner
	{
		private void TickIdle(VEntNPC npc, float dt, ref AIStep step)
		{
			if (_phase == StepPhase.Starting)
			{
				// Random wait between Param and Param*2 seconds
				float minTime = MathF.Max(step.Param, 0.5f);
				_timer = minTime + (float)_rng.NextDouble() * minTime;
				_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} IDLE start ({_timer:F1}s)");
				_phase = StepPhase.Running;
			}

			_timer -= dt;
			if (_timer <= 0f)
				Complete(true, ref step);
		}

		private void TickMoveRandom(VEntNPC npc, float dt, ref AIStep step)
		{
			if (_phase == StepPhase.Starting)
			{
				float radius = MathF.Max(step.Param, 3f);
				Vector3? target = FindRandomWalkablePoint(npc, radius);

				if (target == null || !npc.NavigateTo(target.Value))
				{
					_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} MOVE_RANDOM failed (no walkable point or no path)");
					Complete(false, ref step);
					return;
				}

				_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} MOVE_RANDOM start -> ({target.Value.X:F1}, {target.Value.Z:F1})");
				_phase = StepPhase.Running;
			}

			// Check if navigation finished
			if (!npc.IsNavigating)
			{
				_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} MOVE_RANDOM done (reached={npc.HasReachedTarget})");
				Complete(npc.HasReachedTarget, ref step);
			}
		}

		private void TickMoveToPlayer(VEntNPC npc, float dt, ref AIStep step)
		{
			float stopDistance = step.Param2 > 0 ? step.Param2 : PlayerReachDistance;

			if (_phase == StepPhase.Starting)
			{
				Player nearest = FindNearestPlayer(npc, step.Param);
				if (nearest == null)
				{
					_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} MOVE_TO_PLAYER failed (no player in range {step.Param:F0})");
					Complete(false, ref step);
					return;
				}

				// Already within stop distance — succeed immediately
				float currentDist = Vector3.Distance(npc.Position, nearest.Position);
				if (currentDist < stopDistance)
				{
					_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} MOVE_TO_PLAYER already in range ({currentDist:F1} < {stopDistance:F1})");
					Complete(true, ref step);
					return;
				}

				// Navigate toward a point stopDistance away from the player (not their exact position)
				Vector3 dirToNpc = npc.Position - nearest.Position;
				dirToNpc.Y = 0;
				if (dirToNpc.LengthSquared() > 0.001f)
					dirToNpc = Vector3.Normalize(dirToNpc);
				else
					dirToNpc = Vector3.UnitZ;

				Vector3 navTarget = nearest.Position + dirToNpc * (stopDistance - 0.5f);

				if (!npc.NavigateTo(navTarget))
				{
					_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} MOVE_TO_PLAYER failed (no path to player)");
					Complete(false, ref step);
					return;
				}

				_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} MOVE_TO_PLAYER start -> player {nearest.PlayerId} (stop at {stopDistance:F1})");
				_phase = StepPhase.Running;
			}

			// Check if close enough to player or navigation finished
			Player target = FindNearestPlayer(npc, step.Param);
			if (target != null && Vector3.Distance(npc.Position, target.Position) < stopDistance)
			{
				_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} MOVE_TO_PLAYER reached player {target.PlayerId}");
				npc.StopNavigation();
				Complete(true, ref step);
				return;
			}

			if (!npc.IsNavigating)
			{
				_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} MOVE_TO_PLAYER nav ended (reached={npc.HasReachedTarget})");
				Complete(npc.HasReachedTarget, ref step);
			}
		}

		private void TickIsPlayerNearby(VEntNPC npc, ref AIStep step)
		{
			Player nearest = FindNearestPlayer(npc, step.Param);
			_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} IS_PLAYER_NEARBY({step.Param:F0}) -> {(nearest != null ? $"yes (player {nearest.PlayerId})" : "no")}");
			Complete(nearest != null, ref step);
		}

		private void TickLookAtPlayer(VEntNPC npc, ref AIStep step)
		{
			Player nearest = FindNearestPlayer(npc, step.Param);
			if (nearest == null)
			{
				_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} LOOK_AT_PLAYER failed (no player)");
				Complete(false, ref step);
				return;
			}

			Vector3 dir = nearest.Position - npc.Position;
			dir.Y = 0;
			if (dir.LengthSquared() > 0.001f)
				npc.SetLookDirection(Vector3.Normalize(dir));

			_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} LOOK_AT_PLAYER -> player {nearest.PlayerId}");
			Complete(true, ref step);
		}

		private void TickWait(VEntNPC npc, float dt, ref AIStep step)
		{
			if (_phase == StepPhase.Starting)
			{
				_timer = MathF.Max(step.Param, 0.1f);
				_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} WAIT start ({_timer:F1}s)");
				_phase = StepPhase.Running;
			}

			_timer -= dt;
			if (_timer <= 0f)
				Complete(true, ref step);
		}

		private void TickSpeak(VEntNPC npc, float dt, ref AIStep step)
		{
			if (_phase == StepPhase.Starting)
			{
				string text = step.TextParam ?? "";
				float duration = MathF.Max(step.Param, 0.5f);
				npc.Speak(text, duration);
				_timer = duration;
				_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} SPEAK \"{text}\" ({duration:F1}s)");
				_phase = StepPhase.Running;
			}

			_timer -= dt;
			if (_timer <= 0f)
				Complete(true, ref step);
		}

		private void TickAsyncSpeak(VEntNPC npc, ref AIStep step)
		{
			string text = step.TextParam ?? "";
			float duration = MathF.Max(step.Param, 0.5f);
			npc.Speak(text, duration);
			_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} ASYNC_SPEAK \"{text}\" ({duration:F1}s)");
			Complete(true, ref step);
		}

		// ────────────────────── Extended instruction implementations ──────────────────────

		private void TickTargetEntity(VEntNPC npc, ref AIStep step)
		{
			float range = MathF.Max(step.Param, 5f);
			EntityTargetType targetType = (EntityTargetType)(int)step.Param2;

			VEntNPC target = FindNearestEntity(npc, range, targetType);
			if (target == null)
			{
				_targetEntityId = 0;
				_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} TARGET_ENTITY failed (no entity in range {range:F0})");
				Complete(false, ref step);
				return;
			}

			_targetEntityId = target.NetworkId;
			_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} TARGET_ENTITY -> entity {target.NetworkId}");
			Complete(true, ref step);
		}

		private void TickMoveToTarget(VEntNPC npc, float dt, ref AIStep step)
		{
			float stopDistance = step.Param2 > 0 ? step.Param2 : PlayerReachDistance;

			if (_phase == StepPhase.Starting)
			{
				VoxEntity target = GetTargetEntity(npc);
				if (target == null)
				{
					_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} MOVE_TO_TARGET failed (no target)");
					Complete(false, ref step);
					return;
				}

				float currentDist = Vector3.Distance(npc.Position, target.Position);
				if (currentDist < stopDistance)
				{
					Complete(true, ref step);
					return;
				}

				Vector3 dirToNpc = npc.Position - target.Position;
				dirToNpc.Y = 0;
				if (dirToNpc.LengthSquared() > 0.001f)
					dirToNpc = Vector3.Normalize(dirToNpc);
				else
					dirToNpc = Vector3.UnitZ;

				Vector3 navTarget = target.Position + dirToNpc * (stopDistance - 0.5f);

				if (!npc.NavigateTo(navTarget))
				{
					_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} MOVE_TO_TARGET failed (no path)");
					Complete(false, ref step);
					return;
				}

				_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} MOVE_TO_TARGET start -> entity {_targetEntityId} (stop at {stopDistance:F1})");
				_phase = StepPhase.Running;
			}

			VoxEntity runningTarget = GetTargetEntity(npc);
			if (runningTarget != null && Vector3.Distance(npc.Position, runningTarget.Position) < stopDistance)
			{
				npc.StopNavigation();
				Complete(true, ref step);
				return;
			}

			if (!npc.IsNavigating)
				Complete(npc.HasReachedTarget, ref step);
		}

		private void TickLookAtTarget(VEntNPC npc, ref AIStep step)
		{
			VoxEntity target = GetTargetEntity(npc);
			if (target == null)
			{
				Complete(false, ref step);
				return;
			}

			Vector3 dir = target.Position - npc.Position;
			dir.Y = 0;
			if (dir.LengthSquared() > 0.001f)
				npc.SetLookDirection(Vector3.Normalize(dir));

			_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} LOOK_AT_TARGET -> entity {_targetEntityId}");
			Complete(true, ref step);
		}

		private void TickAimAtTarget(VEntNPC npc, ref AIStep step)
		{
			VoxEntity target = GetTargetEntity(npc);
			if (target == null)
			{
				Complete(false, ref step);
				return;
			}

			Vector3 dir = target.Position - npc.Position;
			dir.Y = 0;
			if (dir.LengthSquared() > 0.001f)
				npc.SetLookDirection(Vector3.Normalize(dir));

			_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} AIM_AT_TARGET -> entity {_targetEntityId}");
			Complete(true, ref step);
		}

		private void TickPrimaryAttack(VEntNPC npc, ref AIStep step)
		{
			float damage = MathF.Max(step.Param, 10f);
			float range = step.Param2 > 0 ? step.Param2 : 3f;

			VoxEntity target = GetTargetEntity(npc);
			if (target == null)
			{
				_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} PRIMARY_ATTACK failed (no target)");
				Complete(false, ref step);
				return;
			}

			float dist = Vector3.Distance(npc.Position, target.Position);
			if (dist > range)
			{
				_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} PRIMARY_ATTACK failed (out of range {dist:F1} > {range:F1})");
				Complete(false, ref step);
				return;
			}

			FaceEntity(npc, target);

			if (target is VEntNPC targetNpc)
				targetNpc.OnAttacked(damage);

			_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} PRIMARY_ATTACK hit entity {_targetEntityId} for {damage:F0} damage");
			Complete(true, ref step);
		}

		private void TickSecondaryAttack(VEntNPC npc, ref AIStep step)
		{
			float damage = MathF.Max(step.Param, 5f);
			float range = step.Param2 > 0 ? step.Param2 : 5f;

			VoxEntity target = GetTargetEntity(npc);
			if (target == null)
			{
				Complete(false, ref step);
				return;
			}

			float dist = Vector3.Distance(npc.Position, target.Position);
			if (dist > range)
			{
				Complete(false, ref step);
				return;
			}

			FaceEntity(npc, target);

			if (target is VEntNPC targetNpc)
				targetNpc.OnAttacked(damage);

			_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} SECONDARY_ATTACK hit entity {_targetEntityId} for {damage:F0} damage");
			Complete(true, ref step);
		}

		private void TickMoveToCover(VEntNPC npc, float dt, ref AIStep step)
		{
			if (_phase == StepPhase.Starting)
			{
				float searchRadius = MathF.Max(step.Param, 5f);

				// Determine danger position (target entity or nearest player)
				Vector3? dangerPos = null;
				VoxEntity target = GetTargetEntity(npc);
				if (target != null)
					dangerPos = target.Position;
				else
				{
					Player nearestPlayer = FindNearestPlayer(npc, 20f);
					if (nearestPlayer != null)
						dangerPos = nearestPlayer.Position;
				}

				if (dangerPos == null)
				{
					_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} MOVE_TO_COVER failed (no danger source)");
					Complete(false, ref step);
					return;
				}

				Vector3? coverPos = FindCoverPosition(npc, dangerPos.Value, searchRadius);
				if (coverPos == null || !npc.NavigateTo(coverPos.Value))
				{
					_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} MOVE_TO_COVER failed (no cover found)");
					Complete(false, ref step);
					return;
				}

				_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} MOVE_TO_COVER start");
				_phase = StepPhase.Running;
			}

			if (!npc.IsNavigating)
				Complete(npc.HasReachedTarget, ref step);
		}

		private void TickCrouch(VEntNPC npc, ref AIStep step)
		{
			npc.SetCrouching(true);
			_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} CROUCH");
			Complete(true, ref step);
		}

		private void TickStandUp(VEntNPC npc, ref AIStep step)
		{
			npc.SetCrouching(false);
			_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} STAND_UP");
			Complete(true, ref step);
		}

		private void TickSetMoveMode(VEntNPC npc, ref AIStep step)
		{
			string mode = step.TextParam ?? "walk";
			npc.SetMoveMode(mode);
			_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} SET_MOVE_MODE \"{mode}\"");
			Complete(true, ref step);
		}

		private void TickPlayAnimation(VEntNPC npc, ref AIStep step)
		{
			string animName = step.TextParam ?? "idle";
			float duration = step.Param > 0 ? step.Param : 2f;
			npc.PlayAnimationOverride(animName, duration);
			_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} PLAY_ANIMATION \"{animName}\" ({duration:F1}s)");
			Complete(true, ref step);
		}

		private void TickEquipWeapon(VEntNPC npc, ref AIStep step)
		{
			npc.WeaponEquipped = true;
			_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} EQUIP_WEAPON");
			Complete(true, ref step);
		}

		private void TickUnequipWeapon(VEntNPC npc, ref AIStep step)
		{
			npc.WeaponEquipped = false;
			_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} UNEQUIP_WEAPON");
			Complete(true, ref step);
		}

		private void TickChatMessageContains(VEntNPC npc, ref AIStep step)
		{
			string searchText = step.TextParam ?? "";
			bool found = ConsumeFirstMatchingChatMessage(searchText);

			_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} CHAT_CONTAINS \"{searchText}\" -> {(found ? "yes" : "no")}");
			Complete(found, ref step);
		}

		// ────────────────────── Helpers ──────────────────────

	}
}

