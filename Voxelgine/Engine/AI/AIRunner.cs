using System;
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

		// Tuning
		private const float PlayerReachDistance = 2.5f;

		public AIRunner(AIStep[] program, IFishLogging logging)
		{
			_program = program ?? throw new ArgumentNullException(nameof(program));
			_log = logging;
			if (_program.Length == 0)
				throw new ArgumentException("Program must have at least one step.", nameof(program));
		}

		/// <summary>
		/// Advances the AI program by one tick. Call once per server tick from UpdateLockstep.
		/// </summary>
		public void Tick(VEntNPC npc, float dt)
		{
			if (_program.Length == 0)
				return;

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
			}
		}

		// ────────────────────── Instruction implementations ──────────────────────

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
			if (_phase == StepPhase.Starting)
			{
				Player nearest = FindNearestPlayer(npc, step.Param);
				if (nearest == null)
				{
					_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} MOVE_TO_PLAYER failed (no player in range {step.Param:F0})");
					Complete(false, ref step);
					return;
				}

				if (!npc.NavigateTo(nearest.Position))
				{
					_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} MOVE_TO_PLAYER failed (no path to player)");
					Complete(false, ref step);
					return;
				}

				_log?.WriteLine($"[AI:{npc.NetworkId}] step {_pc} MOVE_TO_PLAYER start -> player {nearest.PlayerId}");
				_phase = StepPhase.Running;
			}

			// Check if close enough to player or navigation finished
			Player target = FindNearestPlayer(npc, step.Param);
			if (target != null && Vector3.Distance(npc.Position, target.Position) < PlayerReachDistance)
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
	}
}
