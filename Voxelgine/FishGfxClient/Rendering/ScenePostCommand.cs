#if WINDOWS
#nullable enable
using FishGfx;
using FishGfx.Graphics;

namespace Voxelgine.FishGfxClient.Rendering;

internal sealed class ScenePostCommand : RenderCommand
{
	private readonly Texture scene;
	private readonly Texture overlay;
	private readonly ShaderProgram shader;
	private readonly int width;
	private readonly int height;
	private readonly bool useFxaa;

	internal ScenePostCommand(
		Texture scene,
		Texture overlay,
		ShaderProgram shader,
		int width,
		int height,
		bool useFxaa)
	{
		this.scene = scene;
		this.overlay = overlay;
		this.shader = shader;
		this.width = width;
		this.height = height;
		this.useFxaa = useFxaa;
	}

	public override void Execute(RenderPass pass)
	{
		shader.SetUniform("uTexture", 0);
		shader.SetUniform("uOverlayTexture", 1);
		shader.SetUniform("uUseFxaa", useFxaa ? 1 : 0);
		shader.SetUniform("uUseOverlay", overlay == null ? 0 : 1);
		using IDisposable? overlayBinding = overlay?.Bind(1);
		pass.DrawTexturedRectangle(
			0,
			0,
			width,
			height,
			texture: scene,
			shader: shader
		);
	}
}
#endif
