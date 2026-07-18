using Voxelgine.Engine;

namespace Voxelgine.FishGfxClient.Voxels;

/// <summary>Renderer-independent environmental data sampled at the listener.</summary>
public readonly record struct VoxelEnvironmentSample(
	float OutdoorExposure,
	float DirectSkylight,
	bool IsUnderwater,
	BlockType Material);

/// <summary>Pure normalization helpers shared by runtime sampling and tests.</summary>
public static class VoxelEnvironmentSampling
{
	public const int MaximumSkyLight = 15;

	public static float NormalizeSkyLight(int skyLight)
	{
		return Math.Clamp(skyLight, 0, MaximumSkyLight) / (float)MaximumSkyLight;
	}

	public static float CalculateOutdoorExposure(ReadOnlySpan<byte> skyLightProbes)
	{
		if (skyLightProbes.IsEmpty)
		{
			return 0;
		}

		float total = 0;
		foreach (byte value in skyLightProbes)
		{
			total += NormalizeSkyLight(value);
		}

		return Math.Clamp(total / skyLightProbes.Length, 0, 1);
	}

	public static byte CombineLightLevel(
		int skyLight,
		int blockLight,
		float skyLightMultiplier,
		int minimumAmbientLight)
	{
		if (!float.IsFinite(skyLightMultiplier))
		{
			throw new ArgumentOutOfRangeException(nameof(skyLightMultiplier));
		}

		float adjustedSky = Math.Clamp(skyLight, 0, MaximumSkyLight)
			* Math.Clamp(skyLightMultiplier, 0, 1);
		int combined = Math.Max(
			(int)MathF.Round(adjustedSky),
			Math.Clamp(blockLight, 0, MaximumSkyLight)
		);
		combined = Math.Max(combined, Math.Clamp(minimumAmbientLight, 0, MaximumSkyLight));
		return (byte)combined;
	}
}
