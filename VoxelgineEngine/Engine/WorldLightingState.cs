namespace Voxelgine.Engine;

public readonly record struct WorldLightingState(
	float SkyLightMultiplier,
	byte AmbientLight,
	bool Fullbright,
	long Revision);

/// <summary>Calculates immutable lighting snapshots without owning simulation state.</summary>
public sealed class WorldLightingService
{
	public WorldLightingState Calculate(
		in WorldLightingState current,
		in DayNightLightingState dayNightState)
	{
		return new WorldLightingState(
			dayNightState.SkyLightMultiplier,
			dayNightState.AmbientLight,
			current.Fullbright,
			current.Revision + 1);
	}
}
