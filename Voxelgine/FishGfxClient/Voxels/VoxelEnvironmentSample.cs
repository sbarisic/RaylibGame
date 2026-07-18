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
}
