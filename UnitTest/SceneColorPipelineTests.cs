namespace UnitTest;

public sealed class SceneColorPipelineTests
{
	[Fact]
	public void ScenePostEncodesLinearSceneExactlyOnceBeforeDisplay()
	{
		string source = ReadShader("scene_post.frag");

		Assert.Contains("vec3 linearToSrgb(vec3 color)", source);
		Assert.Contains("vec3 displayColor = linearToSrgb", source);
		Assert.Contains("float perceptualLuma(vec3 linearColor)", source);
		Assert.DoesNotContain("pow(color, vec3(1.0 / 2.2))", source);
	}

	[Fact]
	public void LocalFogCompositesAlreadyLinearPremultipliedSamples()
	{
		string source = ReadShader("local_fog.frag");

		Assert.Contains("vec3 sourceColor = sampleValue.rgb / density;", source);
		Assert.DoesNotContain("srgbToLinear", source);
	}

	private static string ReadShader(string name)
	{
		return File.ReadAllText(Path.Combine(
			AppContext.BaseDirectory,
			"data",
			"shaders",
			"fishgfx",
			name
		));
	}
}
