namespace UnitTest;

using Voxelgine.Engine;
using Voxelgine.FishGfxClient.Voxels;
using Voxelgine.Graphics;

public sealed class LocalFogShaderTests
{
	[Fact]
	public void RaymarchJitterIsSpatiallyStableWithoutTemporalUniform()
	{
		string path = Path.Combine(
			AppContext.BaseDirectory,
			"data",
			"shaders",
			"fishgfx",
			"local_fog.frag"
		);
		string source = File.ReadAllText(path);

		Assert.Contains("stablePixelJitter(gl_FragCoord.xy)", source);
		Assert.DoesNotContain("uniform float uJitter", source);
	}

	[Fact]
	public void FogUploadConvertsAuthoredPremultipliedSrgbToLinearPremultipliedData()
	{
		FogVoxel fog = FogVoxel.FromStraight(new Rgba32(128, 64, 255), 128);
		byte[] output = new byte[4];

		FishGfxFogVolume.WriteLinearPremultipliedFog(output, 0, fog);

		Assert.InRange(output[0], (byte)26, (byte)28);
		Assert.InRange(output[1], (byte)6, (byte)8);
		Assert.Equal((byte)128, output[2]);
		Assert.Equal((byte)128, output[3]);
	}

	[Fact]
	public void EmptyFogUploadClearsEveryChannel()
	{
		byte[] output = { 1, 2, 3, 4 };

		FishGfxFogVolume.WriteLinearPremultipliedFog(output, 0, FogVoxel.Empty);

		Assert.Equal(new byte[4], output);
	}
}
