using FishGfx;
using FishGfx.Graphics;
using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.FishGfxClient.Rendering;

namespace Voxelgine.States;

/// <summary>
/// Exercises a state change requested from BeginFrame. The current frame must
/// finish with this state; the replacement state starts on the following frame.
/// </summary>
internal sealed class FishGfxStateTransitionSmokeState : GameStateImpl
{
	private readonly GameStateImpl replacement;
	private bool transitioned;

	public FishGfxStateTransitionSmokeState(
		IGameWindow window,
		IFishEngineRunner engine,
		GameStateImpl replacement
	) : base(window, engine)
	{
		this.replacement = replacement ?? throw new ArgumentNullException(nameof(replacement));
	}

	public override GameStateRenderSettings GetRenderSettings(Vector2 framebufferSize)
	{
		return GameStateRenderSettings.CreateOverlay(
			new Vector2(Window.Width, Window.Height)
		);
	}

	public override void BeginFrame(in FrameTiming timing)
	{
		if (transitioned)
		{
			return;
		}

		transitioned = true;
		Window.SetState(replacement);
	}

	public override void RenderOverlay(RenderPass pass, in FrameTiming timing)
	{
		pass.FillRectangle(20, 20, 80, 80, Color.White);
	}
}
