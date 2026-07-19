using FishGfx.Graphics;
using FishGfx.Graphics.Shadows;
using System.Numerics;
using Voxelgine.Engine.DI;
using Voxelgine.FishGfxClient.Rendering;
using Voxelgine.FishGfxClient.Voxels;

namespace Voxelgine.Engine;

public abstract class GameStateImpl : IDisposable
{
	protected GameStateImpl(IGameWindow window, IFishEngineRunner engine)
	{
		Window = window ?? throw new ArgumentNullException(nameof(window));
		Eng = engine ?? throw new ArgumentNullException(nameof(engine));
	}

	public IGameWindow Window { get; }

	protected IFishEngineRunner Eng { get; }

	public virtual void SwapTo()
	{
	}

	public virtual void SwapFrom()
	{
	}

	public virtual void OnResize(IGameWindow window)
	{
	}

	/// <summary>Runs once per presented client frame after events are polled.</summary>
	public virtual void Tick(float gameTime)
	{
	}

	/// <summary>Runs zero or more fixed simulation steps per presented frame.</summary>
	public virtual void UpdateLockstep(float totalTime, float deltaTime, InputMgr input)
	{
	}

	/// <summary>
	/// Clears transition state before the window polls events for the next frame.
	/// </summary>
	public virtual void BeginInputFrame()
	{
	}

	/// <summary>
	/// Synchronizes frame-owned state and UI before the render graph starts.
	/// Graphics uploads queued by file watchers have already been processed.
	/// </summary>
	public virtual void BeginFrame(in FrameTiming timing)
	{
	}

	public virtual GameStateRenderSettings GetRenderSettings(Vector2 framebufferSize)
	{
		return GameStateRenderSettings.CreateOverlay(framebufferSize);
	}

	public virtual void RenderWorld(RenderPass pass, in FrameTiming timing)
	{
	}

	public virtual GameDirectionalShadowRequest? GetDirectionalShadowRequest()
	{
		return null;
	}

	public virtual void RenderShadowCasters(
		RenderPass pass,
		in DirectionalShadowCascade cascade,
		in FrameTiming timing)
	{
	}

	public virtual void RenderWorld(
		RenderPass pass,
		in FrameTiming timing,
		DirectionalShadowFrame? shadows)
	{
		RenderWorld(pass, timing);
	}

	public virtual void RenderViewmodel(RenderPass pass, in FrameTiming timing)
	{
	}

	public virtual FishGfxFogFrame? GetLocalFogFrame()
	{
		return null;
	}

	public virtual bool RendererProfilingEnabled => false;

	public virtual void RenderFogDepthOccluders(
		RenderPass pass,
		in FrameTiming timing)
	{
	}

	public virtual void RenderOverlay(RenderPass pass, in FrameTiming timing)
	{
	}

	public virtual void Dispose()
	{
	}
}
