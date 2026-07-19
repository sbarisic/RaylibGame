#if WINDOWS
using System.Numerics;
using FishGfx;
using FishGfx.Graphics;
using Voxelgine.FishGfxClient.Voxels;

namespace Voxelgine.FishGfxClient.Rendering;

internal sealed class LocalFogCompositeCommand : RenderCommand
{
	private readonly Texture sceneColor;
	private readonly Texture sceneDepth;
	private readonly FishGfxFogFrame fog;
	private readonly ShaderProgram shader;
	private readonly Matrix4x4 inverseViewProjection;
	private readonly Vector3 cameraPosition;
	private readonly int width;
	private readonly int height;

	public LocalFogCompositeCommand(
		Texture sceneColor,
		Texture sceneDepth,
		in FishGfxFogFrame fog,
		ShaderProgram shader,
		in RenderView view,
		int width,
		int height)
	{
		this.sceneColor = sceneColor ?? throw new ArgumentNullException(nameof(sceneColor));
		this.sceneDepth = sceneDepth ?? throw new ArgumentNullException(nameof(sceneDepth));
		this.fog = fog;
		this.shader = shader ?? throw new ArgumentNullException(nameof(shader));
		this.width = width;
		this.height = height;
		cameraPosition = view.Position;
		if (!Matrix4x4.Invert(view.View * view.Projection, out inverseViewProjection))
		{
			throw new InvalidOperationException("The world view-projection matrix is not invertible.");
		}
	}

	public override void Execute(RenderPass pass)
	{
		shader.SetUniform("uTexture", 0);
		shader.SetUniform("uSceneDepth", 1);
		shader.SetUniform("uFogVolume", 2);
		shader.SetUniform("uInverseViewProjection", inverseViewProjection);
		shader.SetUniform("uCameraPosition", cameraPosition);
		shader.SetUniform("uFogOrigin", fog.Origin);
		shader.SetUniform("uFogSize", fog.Size);
		shader.SetUniform("uStepLength", fog.StepLength);
		shader.SetUniform("uMaximumSteps", fog.MaximumSteps);
		using IDisposable depthBinding = sceneDepth.Bind(1);
		using IDisposable volumeBinding = fog.Texture.Bind(2);
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
