using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.FishGfxClient.Voxels;

namespace Voxelgine.FishGfxClient.Effects;

internal enum VoxelFireEmissionKind
{
	Flame,
	Smoke,
}

internal readonly record struct VoxelFireEmission(
	VoxelFireEmitter Emitter,
	VoxelFireEmissionKind Kind);

/// <summary>
/// Frame-rate-independent scheduling for persistent campfire and torch effects.
/// This owns no graphics resources, which keeps emitter behavior testable.
/// </summary>
internal sealed class VoxelFireEmissionScheduler
{
	internal const float MaximumDistance = 64;
	internal const float CampfireFlameInterval = 0.12f;
	internal const float CampfireSmokeInterval = 1.25f;
	internal const float TorchFlameInterval = 0.2f;
	private const int MaximumCatchUpEmissions = 4;

	private readonly Dictionary<VoxelFireEmitter, EmitterTimers> timers = new();
	private readonly HashSet<VoxelFireEmitter> active = new();

	public void Advance(
		float deltaSeconds,
		Vector3 listenerPosition,
		IReadOnlyList<VoxelFireEmitter> emitters,
		List<VoxelFireEmission> output)
	{
		if (!float.IsFinite(deltaSeconds) || deltaSeconds < 0)
		{
			throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
		}
		if (!IsFinite(listenerPosition))
		{
			throw new ArgumentException("Listener position must be finite.", nameof(listenerPosition));
		}
		ArgumentNullException.ThrowIfNull(emitters);
		ArgumentNullException.ThrowIfNull(output);

		output.Clear();
		active.Clear();
		float maximumDistanceSquared = MaximumDistance * MaximumDistance;
		foreach (VoxelFireEmitter emitter in emitters)
		{
			if (!IsFinite(emitter.Position)
				|| Vector3.DistanceSquared(listenerPosition, emitter.Position) > maximumDistanceSquared)
			{
				continue;
			}

			active.Add(emitter);
			if (!timers.TryGetValue(emitter, out EmitterTimers state))
			{
				state = EmitterTimers.StartReady(emitter.Type);
			}

			state.FlameElapsed += deltaSeconds;
			float flameInterval = emitter.Type switch
			{
				BlockType.Campfire => CampfireFlameInterval,
				BlockType.Torch => TorchFlameInterval,
				_ => 0,
			};
			EmitDue(emitter, VoxelFireEmissionKind.Flame, flameInterval, ref state.FlameElapsed, output);

			if (emitter.Type == BlockType.Campfire)
			{
				state.SmokeElapsed += deltaSeconds;
				EmitDue(
					emitter,
					VoxelFireEmissionKind.Smoke,
					CampfireSmokeInterval,
					ref state.SmokeElapsed,
					output
				);
			}

			timers[emitter] = state;
		}

		foreach (VoxelFireEmitter stale in timers.Keys.Where(emitter => !active.Contains(emitter)).ToArray())
		{
			timers.Remove(stale);
		}
	}

	public void Reset()
	{
		timers.Clear();
		active.Clear();
	}

	private static void EmitDue(
		in VoxelFireEmitter emitter,
		VoxelFireEmissionKind kind,
		float interval,
		ref float elapsed,
		List<VoxelFireEmission> output)
	{
		if (interval <= 0)
		{
			return;
		}

		int count = Math.Min((int)(elapsed / interval), MaximumCatchUpEmissions);
		for (int index = 0; index < count; index++)
		{
			output.Add(new VoxelFireEmission(emitter, kind));
		}

		elapsed -= count * interval;
		if (count == MaximumCatchUpEmissions && elapsed >= interval)
		{
			elapsed %= interval;
		}
	}

	private static bool IsFinite(Vector3 value)
	{
		return float.IsFinite(value.X)
			&& float.IsFinite(value.Y)
			&& float.IsFinite(value.Z);
	}

	private struct EmitterTimers
	{
		public float FlameElapsed;
		public float SmokeElapsed;

		public static EmitterTimers StartReady(BlockType type)
		{
			return new EmitterTimers
			{
				FlameElapsed = type == BlockType.Campfire
					? CampfireFlameInterval
					: TorchFlameInterval,
				SmokeElapsed = type == BlockType.Campfire ? CampfireSmokeInterval : 0,
			};
		}
	}
}
