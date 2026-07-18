#if WINDOWS
using FishGfx;
using FishGfx.Graphics;
using System.Numerics;

namespace Voxelgine.FishGfxClient.Rendering;

public sealed class GameStateRenderSettings
{
	public required RenderView WorldView { get; init; }

	public required RenderView ViewmodelView { get; init; }

	public required RenderView OverlayView { get; init; }

	public RenderState WorldState { get; init; } = RenderState.Default;

	public RenderState ViewmodelState { get; init; } = RenderState.Default;

	public RenderState OverlayState { get; init; } = RenderState.Default with
	{
		CullMode = CullMode.None,
		DepthTestEnabled = false,
		DepthWriteEnabled = false,
	};

	public Color ClearColor { get; init; } = new(150, 150, 150);

	public static GameStateRenderSettings CreateOverlay(Vector2 framebufferSize)
	{
		Vector2 safeSize = new(
			MathF.Max(1, framebufferSize.X),
			MathF.Max(1, framebufferSize.Y)
		);
		Camera camera = new();
		camera.SetOrthogonal(0, 0, safeSize.X, safeSize.Y);
		RenderView view = new(camera);

		return new GameStateRenderSettings
		{
			WorldView = view,
			ViewmodelView = view,
			OverlayView = view,
		};
	}
}
#endif
