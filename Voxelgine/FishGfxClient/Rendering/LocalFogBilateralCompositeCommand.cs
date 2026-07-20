#if WINDOWS
using FishGfx;
using FishGfx.Graphics;

namespace Voxelgine.FishGfxClient.Rendering;

internal sealed class LocalFogBilateralCompositeCommand : RenderCommand
{
	private readonly Texture sceneColor;
	private readonly Texture sceneDepth;
	private readonly Texture fogTexture;
	private readonly ShaderProgram shader;
	private readonly int width;
	private readonly int height;

	internal LocalFogBilateralCompositeCommand(
		Texture sceneColor,
		Texture sceneDepth,
		Texture fogTexture,
		ShaderProgram shader,
		int width,
		int height)
	{
		this.sceneColor = sceneColor;
		this.sceneDepth = sceneDepth;
		this.fogTexture = fogTexture;
		this.shader = shader;
		this.width = width;
		this.height = height;
	}

	public override void Execute(RenderPass pass)
	{
		shader.SetUniform("uTexture", 0);
		shader.SetUniform("uSceneDepth", 1);
		shader.SetUniform("uFogTexture", 2);
		shader.SetUniform("uFogResolution", new System.Numerics.Vector2(
			fogTexture.Width,
			fogTexture.Height
		));
		using IDisposable depth = sceneDepth.Bind(1);
		using IDisposable fog = fogTexture.Bind(2);
		pass.DrawTexturedRectangle(
			0,
			0,
			width,
			height,
			texture: sceneColor,
			shader: shader
		);
	}
}
#endif
