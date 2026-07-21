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
	public partial class AIRunner
	{
		private readonly AIStep[] _program;
		private readonly IFishLogging _log;
		private int _pc;
		private StepPhase _phase;
		private float _timer;
		private static readonly Random _rng = new();

		// Event system
		private readonly Dictionary<AIEvent, int> _eventHandlers = new();
		private readonly Dictionary<AIEvent, int> _eventHandlerEnds = new();
		private readonly Dictionary<AIEvent, float> _eventCooldowns = new();
		private readonly Dictionary<AIEvent, float> _eventCooldownDurations = new();
		private AIEvent? _activeHandlerEvent;

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
		private bool _playerSightPresent;
		private bool _playerInRangePresent;
		private bool _enemyInRangePresent;
		private bool _friendlyInRangePresent;

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

			// Scan program for event handler markers and register their instruction ranges.
			List<(AIEvent Event, int Start)> handlers = new();
			for (int i = 0; i < _program.Length; i++)
			{
				if (_program[i].Instruction == AIInstruction.EventHandler)
				{
					AIEvent evt = (AIEvent)(int)_program[i].Param;
					_eventHandlers[evt] = i;
					_eventCooldownDurations[evt] = MathF.Max(0, _program[i].Param2);
					handlers.Add((evt, i));
				}
			}

			for (int i = 0; i < handlers.Count; i++)
			{
				(AIEvent evt, int start) = handlers[i];
				if (_eventHandlers[evt] != start)
					continue;

				_eventHandlerEnds[evt] = i + 1 < handlers.Count
					? handlers[i + 1].Start
					: _program.Length;
			}
		}

		internal int ProgramCounter => _pc;

		/// <summary>
		/// Raises an event. If a handler is registered for this event, interrupts the current
		/// step and jumps to the handler. Subject to a per-event cooldown.
		/// </summary>
		public void RaiseEvent(AIEvent evt, int npcNetId)
		{
			if (!_eventHandlers.TryGetValue(evt, out int handlerIndex))
				return;

			// The active handler owns its full range until control leaves it.
			if (_activeHandlerEvent == evt)
			{
				_log?.Log(GameLogLevel.Trace, "AI", $"npcId={npcNetId} event={evt} suppressed=active-handler pc={_pc}");
				return;
			}

			// Cooldown to prevent spamming
			if (_eventCooldowns.TryGetValue(evt, out float remaining) && remaining > 0)
			{
				_log?.Log(GameLogLevel.Trace, "AI", $"npcId={npcNetId} event={evt} suppressed=cooldown remaining={remaining:F2}s pc={_pc}");
				return;
			}

			_eventCooldowns[evt] = _eventCooldownDurations.GetValueOrDefault(evt, 1f);
			_log?.Log(GameLogLevel.Debug, "AI", $"npcId={npcNetId} EVENT {evt} handlerStart={handlerIndex} handlerEnd={_eventHandlerEnds[evt]} pc={_pc}");
			_activeHandlerEvent = evt;
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
					bool isPresent = FindNearestPlayer(npc, 15f) != null;
					if (EnteredPresence(ref _playerSightPresent, isPresent))
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
					bool isPresent = FindNearestPlayer(npc, ProximityRange) != null;
					if (EnteredPresence(ref _playerInRangePresent, isPresent))
						RaiseEvent(AIEvent.OnPlayerInRange, npc.NetworkId);
				}

				if (_eventHandlers.ContainsKey(AIEvent.OnEnemyInRange))
				{
					bool isPresent = FindNearestEntity(
						npc,
						ProximityRange,
						EntityTargetType.Enemy
					) != null;
					if (EnteredPresence(ref _enemyInRangePresent, isPresent))
						RaiseEvent(AIEvent.OnEnemyInRange, npc.NetworkId);
				}

				if (_eventHandlers.ContainsKey(AIEvent.OnFriendlyInRange))
				{
					bool isPresent = FindNearestEntity(
						npc,
						ProximityRange,
						EntityTargetType.Friendly
					) != null;
					if (EnteredPresence(ref _friendlyInRangePresent, isPresent))
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
			if (_activeHandlerEvent is AIEvent activeEvent
				&& (!IsInsideHandler(activeEvent, _pc)))
			{
				_activeHandlerEvent = null;
			}
			_phase = StepPhase.Starting;
			_timer = 0f;
			_log?.WriteLine($"[AI] advance {prev} -> {_pc} ({_program[_pc].Instruction})");
		}

		private bool IsInsideHandler(AIEvent evt, int programCounter)
		{
			return _eventHandlers.TryGetValue(evt, out int start)
				&& _eventHandlerEnds.TryGetValue(evt, out int end)
				&& programCounter >= start
				&& programCounter < end;
		}

		internal bool ConsumeFirstMatchingChatMessage(string searchText)
		{
			bool found = false;
			int messageCount = _chatBuffer.Count;
			for (int i = 0; i < messageCount; i++)
			{
				string message = _chatBuffer.Dequeue();
				if (!found && message.Contains(searchText, StringComparison.OrdinalIgnoreCase))
				{
					found = true;
					continue;
				}

				_chatBuffer.Enqueue(message);
			}
			return found;
		}

		internal static bool EnteredPresence(ref bool wasPresent, bool isPresent)
		{
			bool entered = isPresent && !wasPresent;
			wasPresent = isPresent;
			return entered;
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
