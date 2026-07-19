namespace UnitTest;

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
}
