using System;
using System.Collections.Generic;
using System.Numerics;

using Voxelgine.Engine.DI;

namespace Voxelgine.Engine.AI
{
	/// <summary>
	/// Execution state of the current AI step.
	/// </summary>
	internal enum StepPhase : byte
	{
		/// <summary>Step has not started yet — begin execution on next tick.</summary>
		Starting,

		/// <summary>Step is in progress (e.g., NPC is walking to target).</summary>
		Running,
	}

	/// <summary>
	/// Target type filter for entity targeting instructions.
	/// </summary>
	internal enum EntityTargetType : byte
	{
		/// <summary>Any NPC entity.</summary>
		Any = 0,
		/// <summary>Enemy NPC (different non-zero team).</summary>
		Enemy = 1,
		/// <summary>Friendly NPC (same non-zero team).</summary>
		Friendly = 2,
	}

	/// <summary>
	/// Simple VM that executes an <see cref="AIStep"/> program on an NPC each tick.
	/// Runs server-side only (called from <see cref="VEntNPC.UpdateLockstep"/>).
	/// </summary>
	public class AIRunner
	{
		private readonly AIStep[] _program;
		private readonly IFishLogging _log;
		private int _pc;
		private StepPhase _phase;
		private float _timer;
		private static readonly Random _rng = new();

		// Event system
		private readonly Dictionary<AIEvent, int> _eventHandlers = new();
		private readonly Dictionary<AIEvent, float> _eventCooldowns = new();
		private const float EventCooldownTime = 1f;

		// Tuning
		private const float PlayerReachDistance = 2.5f;
		private const float SightCheckInterval = 0.5f;
		private float _sightCheckTimer;

		// Entity targeting
		private int _targetEntityId;

		// Chat buffer
		private readonly Queue<string> _chatBuffer = new();
		private const int MaxChatBufferSize = 5;

		// Low health tracking
		private bool _lowHealthFired;

		// Range check timers
		private float _rangeCheckTimer;
		private const float RangeCheckInterval = 0.5f;
		private const float ProximityRange = 10f;

		public AIRunner(AIStep[] program, IFishLogging logging)
		{
			_program = program ?? throw new ArgumentNullException(nameof(program));
			_log = logging;
			if (_program.Length == 0)
				throw new ArgumentException("Program must have at least one step.", nameof(program));

			// Scan program for event handler markers and register them
			for (int i = 0; i < _program.Length; i++)
			{
				if (_program[i].Instruction == AIInstruction.EventHandler)
				{
					AIEvent evt = (AIEvent)(int)_program[i].Param;
					_eventHandlers[evt] = i;
				}
			}
		}

		/// <summary>
		/// Raises an event. If a handler is registered for this event, interrupts the current
		/// step and jumps to the handler. Subject to a per-event cooldown.
		/// </summary>
		public void RaiseEvent(AIEvent evt, int npcNetId)
		{
			if (!_eventHandlers.TryGetValue(evt, out int handlerIndex))
				return;

			// Skip if already at or inside this handler
			if (_pc == handlerIndex || _pc == handlerIndex + 1)
				return;

			// Cooldown to prevent spamming
			if (_eventCooldowns.TryGetValue(evt, out float remaining) && remaining > 0)
				return;

			_eventCooldowns[evt] = EventCooldownTime;
			_log?.WriteLine($"[AI:{npcNetId}] EVENT {evt} -> jump to step {handlerIndex}");
			Advance(handlerIndex);
		}

		/// <summary>
		/// Pushes a chat message into the buffer for <see cref="AIInstruction.ChatMessageContains"/> checks.
		/// </summary>
		public void PushChatMessage(string message)
		{
			_chatBuffer.Enqueue(message);
			while (_chatBuffer.Count > MaxChatBufferSize)
				_chatBuffer.Dequeue();
		}

		/// <summary>
		/// Advances the AI program by one tick. Call once per server tick from UpdateLockstep.
		/// </summary>
		public void Tick(VEntNPC npc, float dt)
		{
			if (_program.Length == 0)
				return;

			// Tick event cooldowns
			List<AIEvent> expired = null;
			foreach (var kvp in _eventCooldowns)
			{
				if (kvp.Value > 0)
					_eventCooldowns[kvp.Key] = kvp.Value - dt;
				else
				{
					expired ??= new();
					expired.Add(kvp.Key);
				}
			}
			if (expired != null)
				foreach (var e in expired)
					_eventCooldowns.Remove(e);

			// Periodic sight check
			if (_eventHandlers.ContainsKey(AIEvent.OnPlayerSight))
			{
				_sightCheckTimer -= dt;
				if (_sightCheckTimer <= 0)
				{
					_sightCheckTimer = SightCheckInterval;
					if (FindNearestPlayer(npc, 15f) != null)
						RaiseEvent(AIEvent.OnPlayerSight, npc.NetworkId);
				}
			}

			// Low health check
			if (!npc.IsDead && npc.HealthPercent > 0f && npc.HealthPercent < 0.3f && !_lowHealthFired)
			{
				_lowHealthFired = true;
				RaiseEvent(AIEvent.OnLowHealth, npc.NetworkId);
			}
			else if (npc.HealthPercent >= 0.3f)
			{
				_lowHealthFired = false;
			}

			// Periodic range checks for players and entities
			_rangeCheckTimer -= dt;
			if (_rangeCheckTimer <= 0)
			{
				_rangeCheckTimer = RangeCheckInterval;

				if (_eventHandlers.ContainsKey(AIEvent.OnPlayerInRange))
				{
					if (FindNearestPlayer(npc, ProximityRange) != null)
						RaiseEvent(AIEvent.OnPlayerInRange, npc.NetworkId);
				}

				if (_eventHandlers.ContainsKey(AIEvent.OnEnemyInRange))
				{
					if (FindNearestEntity(npc, ProximityRange, EntityTargetType.Enemy) != null)
						RaiseEvent(AIEvent.OnEnemyInRange, npc.NetworkId);
				}

				if (_eventHandlers.ContainsKey(AIEvent.OnFriendlyInRange))
				{
					if (FindNearestEntity(npc, ProximityRange, EntityTargetType.Friendly) != null)
						RaiseEvent(AIEvent.OnFriendlyInRange, npc.NetworkId);
				}

				// Check if target entity died
				if (_targetEntityId != 0 && _eventHandlers.ContainsKey(AIEvent.OnEnemyKilled))
				{
					VoxEntity target = GetTargetEntity(npc);
					if (target is VEntNPC targetNpc && targetNpc.IsDead)
					{
						RaiseEvent(AIEvent.OnEnemyKilled, npc.NetworkId);
						_targetEntityId = 0;
					}
				}
			}

			AIStep step = _program[_pc];

			switch (step.Instruction)
			{
				case AIInstruction.Idle:
					TickIdle(npc, dt, ref step);
					break;

				case AIInstruction.MoveRandom:
					TickMoveRandom(npc, dt, ref step);
					break;

				case AIInstruction.MoveToPlayer:
					TickMoveToPlayer(npc, dt, ref step);
					break;

				case AIInstruction.IsPlayerNearby:
					TickIsPlayerNearby(npc, ref step);
					break;

				case AIInstruction.LookAtPlayer:
					TickLookAtPlayer(npc, ref step);
					break;

				case AIInstruction.Goto:
					_log?.WriteLine($"[AI:{npc.NetworkId}] GOTO -> step {(int)step.Param}");
					Advance((int)step.Param);
					break;

				case AIInstruction.Wait:
					TickWait(npc, dt, ref step);
					break;

				case AIInstruction.Speak:
					TickSpeak(npc, dt, ref step);
					break;

				case AIInstruction.AsyncSpeak:
					TickAsyncSpeak(npc, ref step);
					break;

				case AIInstruction.EventHandler:
					// Pass-through marker — just advance to next step
					Advance(_pc + 1);
					break;

				// ── Extended instructions ──

				case AIInstruction.TargetEntity:
					TickTargetEntity(npc, ref step);
					break;

				case AIInstruction.MoveToTarget:
					TickMoveToTarget(npc, dt, ref step);
					break;

				case AIInstruction.LookAtTarget:
					TickLookAtTarget(npc, ref step);
					break;

				case AIInstruction.AimAtTarget:
					TickAimAtTarget(npc, ref step);
					break;

				case AIInstruction.PrimaryAttack:
					TickPrimaryAttack(npc, ref step);
					break;

				case AIInstruction.SecondaryAttack:
					TickSecondaryAttack(npc, ref step);
					break;

				case AIInstruction.MoveToCover:
					TickMoveToCover(npc, dt, ref step);
					break;

				case AIInstruction.Crouch:
					TickCrouch(npc, ref step);
					break;

				case AIInstruction.StandUp:
					TickStandUp(npc, ref step);
					break;

				case AIInstruction.SetMoveMode:
					TickSetMoveMode(npc, ref step);
					break;

				case AIInstruction.PlayAnimation:
					TickPlayAnimation(npc, ref step);
					break;

				case AIInstruction.EquipWeapon:
					TickEquipWeapon(npc, ref step);
					break;

				case AIInstruction.UnequipWeapon:
					TickUnequipWeapon(npc, ref step);
					break;

				case AIInstruction.ChatMessageContains:
					TickChatMessageContains(npc, ref step);
					break;
			}
		}

		// ────────────────────── Core instruction implementations ──────────────────────

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
			bool found = false;

			foreach (var msg in _chatBuffer)
			{
				if (msg.Contains(searchText, StringComparison.OrdinalIgnoreCase))
				{
					found = true;
					break;
				}
			}

			_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} CHAT_CONTAINS \"{searchText}\" -> {(found ? "yes" : "no")}");
			Complete(found, ref step);
		}

		// ────────────────────── Helpers ──────────────────────

		private void Complete(bool success, ref AIStep step)
		{
			if (!success && step.OnFailGoto >= 0)
				Advance(step.OnFailGoto);
			else
				Advance(_pc + 1);
		}

		private void Advance(int target)
		{
			int prev = _pc;
			_pc = target % _program.Length;
			_phase = StepPhase.Starting;
			_timer = 0f;
			_log?.WriteLine($"[AI] advance {prev} -> {_pc} ({_program[_pc].Instruction})");
		}

		private static void FaceEntity(VEntNPC npc, VoxEntity target)
		{
			Vector3 dir = target.Position - npc.Position;
			dir.Y = 0;
			if (dir.LengthSquared() > 0.001f)
				npc.SetLookDirection(Vector3.Normalize(dir));
		}

		private VoxEntity GetTargetEntity(VEntNPC npc)
		{
			if (_targetEntityId == 0) return null;
			var entMgr = npc.GetEntityManager();
			return entMgr?.GetEntityByNetworkId(_targetEntityId);
		}

		private static Player FindNearestPlayer(VEntNPC npc, float maxRadius)
		{
			GameSimulation sim = npc.GetSimulation();
			if (sim == null)
				return null;

			float maxDistSq = maxRadius * maxRadius;
			Player nearest = null;
			float nearestDistSq = float.MaxValue;

			foreach (Player player in sim.Players.GetAllPlayers())
			{
				if (player.IsDead)
					continue;

				float distSq = Vector3.DistanceSquared(npc.Position, player.Position);
				if (distSq < maxDistSq && distSq < nearestDistSq)
				{
					nearest = player;
					nearestDistSq = distSq;
				}
			}

			return nearest;
		}

		private static VEntNPC FindNearestEntity(VEntNPC npc, float maxRadius, EntityTargetType targetType = EntityTargetType.Any)
		{
			var entMgr = npc.GetEntityManager();
			if (entMgr == null) return null;

			float maxDistSq = maxRadius * maxRadius;
			VEntNPC nearest = null;
			float nearestDistSq = float.MaxValue;

			foreach (var ent in entMgr.GetAllEntities())
			{
				if (ent is not VEntNPC otherNpc || otherNpc == npc || otherNpc.IsDead)
					continue;

				// Team filtering
				if (targetType == EntityTargetType.Enemy && (npc.Team == 0 || otherNpc.Team == 0 || otherNpc.Team == npc.Team))
					continue;
				if (targetType == EntityTargetType.Friendly && (npc.Team == 0 || otherNpc.Team != npc.Team))
					continue;

				float distSq = Vector3.DistanceSquared(npc.Position, otherNpc.Position);
				if (distSq < maxDistSq && distSq < nearestDistSq)
				{
					nearest = otherNpc;
					nearestDistSq = distSq;
				}
			}

			return nearest;
		}

		private static Vector3? FindRandomWalkablePoint(VEntNPC npc, float radius)
		{
			GameSimulation sim = npc.GetSimulation();
			if (sim == null)
				return null;

			int currentY = (int)MathF.Floor(npc.Position.Y);

			for (int attempt = 0; attempt < 10; attempt++)
			{
				float angle = (float)(_rng.NextDouble() * Math.PI * 2);
				float dist = 3f + (float)(_rng.NextDouble() * (radius - 3f));

				int targetX = (int)MathF.Floor(npc.Position.X + MathF.Cos(angle) * dist);
				int targetZ = (int)MathF.Floor(npc.Position.Z + MathF.Sin(angle) * dist);

				bool groundSolid = sim.Map.IsSolid(targetX, currentY - 1, targetZ);
				bool feetClear = !sim.Map.IsSolid(targetX, currentY, targetZ);
				bool headClear = !sim.Map.IsSolid(targetX, currentY + 1, targetZ);

				if (groundSolid && feetClear && headClear)
					return new Vector3(targetX + 0.5f, currentY, targetZ + 0.5f);
			}

			return null;
		}

		private static Vector3? FindCoverPosition(VEntNPC npc, Vector3 dangerPos, float searchRadius)
		{
			GameSimulation sim = npc.GetSimulation();
			if (sim == null) return null;

			int currentY = (int)MathF.Floor(npc.Position.Y);
			Vector3? bestCover = null;
			float bestDistSq = float.MaxValue;

			for (int attempt = 0; attempt < 16; attempt++)
			{
				float angle = (float)(_rng.NextDouble() * Math.PI * 2);
				float dist = 3f + (float)(_rng.NextDouble() * (searchRadius - 3f));

				int targetX = (int)MathF.Floor(npc.Position.X + MathF.Cos(angle) * dist);
				int targetZ = (int)MathF.Floor(npc.Position.Z + MathF.Sin(angle) * dist);

				bool groundSolid = sim.Map.IsSolid(targetX, currentY - 1, targetZ);
				bool feetClear = !sim.Map.IsSolid(targetX, currentY, targetZ);
				bool headClear = !sim.Map.IsSolid(targetX, currentY + 1, targetZ);

				if (!groundSolid || !feetClear || !headClear)
					continue;

				Vector3 candidatePos = new Vector3(targetX + 0.5f, currentY, targetZ + 0.5f);

				// Check for cover: solid block between candidate and danger direction
				Vector3 toDanger = dangerPos - candidatePos;
				toDanger.Y = 0;
				if (toDanger.LengthSquared() < 0.001f) continue;
				toDanger = Vector3.Normalize(toDanger);

				int coverX = (int)MathF.Floor(candidatePos.X + toDanger.X * 1.5f);
				int coverZ = (int)MathF.Floor(candidatePos.Z + toDanger.Z * 1.5f);

				if (sim.Map.IsSolid(coverX, currentY, coverZ))
				{
					float distSq = Vector3.DistanceSquared(npc.Position, candidatePos);
					if (distSq < bestDistSq)
					{
						bestCover = candidatePos;
						bestDistSq = distSq;
					}
				}
			}

			return bestCover;
		}
	}
}
