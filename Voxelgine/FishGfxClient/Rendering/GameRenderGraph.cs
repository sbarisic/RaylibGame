#if WINDOWS
using FishGfx;
using FishGfx.Graphics;
using FishGfx.Graphics.Shadows;
using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.FishGfxClient.Assets;

namespace Voxelgine.FishGfxClient.Rendering;

public sealed class GameRenderGraph : IDisposable
{
	private readonly GraphicsContext graphics;
	private readonly AssetHandle<ShaderProgram> postShader;
	private bool useMsaa;
	private RenderTarget worldTarget;
	private RenderTarget resolvedTarget;
	private Vector2 targetSize;
	private bool disposed;
	private DirectionalShadowRenderer shadowRenderer;
	private DirectionalShadowOptions shadowOptions;
	private GameStateImpl shadowOwner;
	private long shadowGeometryRevision = long.MinValue;
	private long frameIndex;

	public GameRenderGraph(GraphicsContext graphics, GameAssetStore assets, bool enableMsaa)
	{
		this.graphics = graphics ?? throw new ArgumentNullException(nameof(graphics));
		ArgumentNullException.ThrowIfNull(assets);
		useMsaa = enableMsaa;
		postShader = assets.LoadShader(
			"scene-post",
			"data/shaders/fishgfx/scene_post.vert",
			"data/shaders/fishgfx/scene_post.frag"
		);
	}

	public int SampleCount => useMsaa ? 4 : 1;

	public DirectionalShadowDiagnostics ShadowDiagnostics =>
		shadowRenderer?.Diagnostics ?? default;

	public void SetMultisampling(bool enabled)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		if (useMsaa == enabled)
		{
			return;
		}

		useMsaa = enabled;
		DisposeTargets();
		worldTarget = null;
		resolvedTarget = null;
		targetSize = default;
	}

	public void Render(GameStateImpl state, in FrameTiming timing, Vector2 framebufferSize)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(state);
		int width = Math.Max(1, (int)framebufferSize.X);
		int height = Math.Max(1, (int)framebufferSize.Y);
		EnsureTargets(width, height);
		GameStateRenderSettings settings = state.GetRenderSettings(new Vector2(width, height));

		using RenderFrame frame = graphics.BeginFrame();
		frameIndex++;
		DirectionalShadowFrame? shadows = PrepareShadows(frame, state, timing);
		using (RenderPass world = frame.BeginPass(
			worldTarget,
			CreateDescriptor(
				settings.WorldView,
				settings.WorldState,
				settings.ClearColor,
				timing.TotalTime,
				RenderLoadAction.Clear,
				RenderLoadAction.Clear
			)
		))
		{
			state.RenderWorld(world, timing, shadows);
		}

		using (RenderPass viewmodel = frame.BeginPass(
			worldTarget,
			CreateDescriptor(
				settings.ViewmodelView,
				settings.ViewmodelState,
				settings.ClearColor,
				timing.TotalTime,
				RenderLoadAction.Load,
				RenderLoadAction.Clear
			)
		))
		{
			state.RenderViewmodel(viewmodel, timing);
		}

		if (useMsaa)
		{
			frame.ResolveColor(worldTarget, 0, resolvedTarget, 0);
		}

		ShaderProgram shader = postShader.Value;
		shader.SetUniform("uTexture", 0);
		shader.SetUniform("uUseFxaa", useMsaa ? 0 : 1);
		using (RenderPass post = frame.BeginPass(
			graphics.Backbuffer,
			CreateDescriptor(
				CreateScreenView(width, height),
				settings.OverlayState,
				settings.ClearColor,
				timing.TotalTime,
				RenderLoadAction.Clear,
				RenderLoadAction.Clear
			)
		))
		{
			post.DrawTexturedRectangle(
				0,
				0,
				width,
				height,
				texture: resolvedTarget.ColorAttachments[0],
				shader: shader
			);
		}

		using (RenderPass overlay = frame.BeginPass(
			graphics.Backbuffer,
			CreateDescriptor(
				settings.OverlayView,
				settings.OverlayState,
				settings.ClearColor,
				timing.TotalTime,
				RenderLoadAction.Load,
				RenderLoadAction.DontCare
			)
		))
		{
			state.RenderOverlay(overlay, timing);
		}

		frame.Present();
	}

	public void Dispose()
	{
		if (disposed)
		{
			return;
		}

		DisposeTargets();
		shadowRenderer?.Dispose();
		shadowRenderer = null;
		shadowOwner = null;
		worldTarget = null;
		resolvedTarget = null;
		disposed = true;
	}

	private DirectionalShadowFrame? PrepareShadows(
		RenderFrame frame,
		GameStateImpl state,
		in FrameTiming timing)
	{
		GameDirectionalShadowRequest? requestValue = state.GetDirectionalShadowRequest();

		if (!requestValue.HasValue || requestValue.Value.Options.CascadeCount == 0)
		{
			if (shadowOwner != null)
			{
				shadowRenderer?.InvalidateAll();
				shadowOwner = null;
				shadowGeometryRevision = long.MinValue;
			}

			return null;
		}

		GameDirectionalShadowRequest request = requestValue.Value;

		if (!ReferenceEquals(shadowOwner, state))
		{
			shadowRenderer?.InvalidateAll();
			shadowOwner = state;
			shadowGeometryRevision = long.MinValue;
		}

		if (shadowRenderer == null)
		{
			shadowOptions = request.Options;
			shadowRenderer = new DirectionalShadowRenderer(graphics, shadowOptions);
		}
		else if (!OptionsEqual(shadowOptions, request.Options))
		{
			shadowOptions = request.Options;
			shadowRenderer.SetOptions(shadowOptions);
		}

		if (shadowGeometryRevision != request.GeometryRevision)
		{
			shadowGeometryRevision = request.GeometryRevision;
			shadowRenderer.InvalidateGeometry();
		}

		if (request.DynamicActorsChanged)
		{
			shadowRenderer.NotifyDynamicActorsChanged();
		}

		shadowRenderer.GpuProfilingEnabled = request.GpuProfilingEnabled;

		shadowRenderer.Prepare(
			request.ViewCamera,
			request.LightDirection,
			request.Strength,
			frameIndex
		);

		for (int pendingIndex = 0; pendingIndex < shadowRenderer.CascadesNeedingRender.Count; pendingIndex++)
		{
			int cascadeIndex = shadowRenderer.CascadesNeedingRender[pendingIndex];
			DirectionalShadowCascade cascade = shadowRenderer.GetPendingCascade(cascadeIndex);

			using (RenderPass shadowPass = shadowRenderer.BeginCascadePass(frame, cascadeIndex))
			{
				using IDisposable timingScope = shadowRenderer.BeginCascadeTiming(
					shadowPass,
					cascadeIndex
				);
				state.RenderShadowCasters(shadowPass, cascade, timing);
			}

			shadowRenderer.CompleteCascade(cascadeIndex);
		}

		DirectionalShadowFrame shadowFrame = shadowRenderer.CurrentFrame;
		return shadowFrame.Enabled ? shadowFrame : null;
	}

	private static bool OptionsEqual(
		DirectionalShadowOptions left,
		DirectionalShadowOptions right)
	{
		if (ReferenceEquals(left, right))
		{
			return true;
		}

		if (left == null || right == null
			|| left.CascadeCount != right.CascadeCount
			|| left.Resolution != right.Resolution
			|| left.MaximumDistance != right.MaximumDistance
			|| left.SplitLambda != right.SplitLambda
			|| left.CascadeBlendFraction != right.CascadeBlendFraction
			|| left.Filter != right.Filter
			|| left.RasterSlopeBias != right.RasterSlopeBias
			|| left.RasterConstantBias != right.RasterConstantBias
			|| left.UpdateIntervals.Count != right.UpdateIntervals.Count)
		{
			return false;
		}

		for (int index = 0; index < left.UpdateIntervals.Count; index++)
		{
			if (left.UpdateIntervals[index] != right.UpdateIntervals[index])
			{
				return false;
			}
		}

		return true;
	}

	private void EnsureTargets(int width, int height)
	{
		Vector2 size = new(width, height);
		if (worldTarget is not null && targetSize == size)
		{
			return;
		}

		DisposeTargets();
		worldTarget = graphics.CreateRenderTarget(
			new RenderTargetDescriptor(width, height, sampleCount: SampleCount)
		);
		resolvedTarget = useMsaa
			? graphics.CreateRenderTarget(new RenderTargetDescriptor(width, height))
			: worldTarget;
		targetSize = size;
	}

	private void DisposeTargets()
	{
		if (resolvedTarget is not null && !ReferenceEquals(resolvedTarget, worldTarget))
		{
			resolvedTarget.Dispose();
		}

		worldTarget?.Dispose();
	}

	private static RenderPassDescriptor CreateDescriptor(
		RenderView view,
		RenderState state,
		Color clearColor,
		float time,
		RenderLoadAction colorLoad,
		RenderLoadAction depthLoad
	)
	{
		return new RenderPassDescriptor
		{
			View = view,
			State = state,
			ColorLoadAction = colorLoad,
			DepthLoadAction = depthLoad,
			StencilLoadAction = depthLoad,
			ClearColor = clearColor,
			Time = time,
		};
	}

	private static RenderView CreateScreenView(int width, int height)
	{
		Camera camera = new();
		camera.SetOrthogonal(0, 0, width, height);
		return new RenderView(camera);
	}
}
#endif
