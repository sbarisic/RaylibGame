using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.GUI;

#if WINDOWS
using FishGfx.Graphics;
using FishGfx.Graphics.Shadows;
using Voxelgine.FishGfxClient.Entities;
using Voxelgine.FishGfxClient.Rendering;
using Voxelgine.FishGfxClient.Voxels;
using FishColor = FishGfx.Color;
#endif

namespace Voxelgine.States;

public unsafe partial class MPClientGameState
{
	public override void BeginInputFrame()
	{
		_gui?.BeginInputFrame();
	}

	public override GameStateRenderSettings GetRenderSettings(Vector2 framebufferSize)
	{
		Vector2 logicalSize = new(
			Math.Max(1, Window.Width),
			Math.Max(1, Window.Height)
		);
		GameStateRenderSettings overlay = GameStateRenderSettings.CreateOverlay(logicalSize);
		FishColor clearColor = new(30, 30, 40);

		if ((_initialized && _simulation?.LocalPlayer is not null) || _fishVoxelScene is not null)
		{
			ConfigureFishCamera(_fishWorldCamera, _fishCameraState, framebufferSize);
			if (_simulation != null)
			{
				var sky = _simulation.DayNight.SkyColor;
				clearColor = new FishColor(sky.R, sky.G, sky.B, sky.A);
			}
		}
		else
		{
			_fishWorldCamera.Position = new Vector3(0, 0, -1);
			_fishWorldCamera.CameraUpNormal = Vector3.UnitY;
			_fishWorldCamera.LookAt(Vector3.Zero);
			_fishWorldCamera.SetPerspective(framebufferSize, MathF.PI / 2, 0.05f, 512);
		}

		return new GameStateRenderSettings
		{
			WorldView = new RenderView(_fishWorldCamera),
			ViewmodelView = new RenderView(_fishWorldCamera),
			OverlayView = overlay.OverlayView,
			ClearColor = clearColor,
		};
	}

	public override GameDirectionalShadowRequest? GetDirectionalShadowRequest()
	{
		if (_fishVoxelScene is null || _simulation?.DayNight is null)
		{
			return null;
		}

		GameConfig config = Eng.DI.GetRequiredService<GameConfig>();
		DirectionalShadowOptions options = CreateShadowOptions(
			config.SunShadowQuality,
			config.MaxChunkDrawDistance
		);

		if (options.CascadeCount == 0)
		{
			return null;
		}

		float normalizedDaylight = Math.Clamp(
			(_simulation.DayNight.SkyLightMultiplier - 0.15f) / 0.85f,
			0,
			1
		);
		float strength = _simulation.DayNight.SunColor.A == 0
			? 0
			: SmoothStep(0.02f, 0.12f, normalizedDaylight);
		long actorRevision = CalculateShadowActorRevision();
		bool dynamicActorsChanged = actorRevision != _lastShadowActorRevision;
		_lastShadowActorRevision = actorRevision;

		_shadowInvalidations.Clear();
		_fishVoxelScene.Renderer.DrainShadowInvalidations(_shadowInvalidations);
		return new GameDirectionalShadowRequest(
			_fishWorldCamera,
			_fishVoxelScene.Renderer.SunSettings.Direction,
			strength,
			options,
			_fishVoxelScene.GeometryRevision,
			_shadowInvalidations,
			dynamicActorsChanged,
			_rendererProfilingEnabled
		);
	}

	private long CalculateShadowActorRevision()
	{
		HashCode hash = new();
		bool animated = false;
		foreach (RemotePlayer player in _simulation.Players.GetAllRemotePlayers())
		{
			hash.Add(player.PlayerId);
			AddQuantizedVector(ref hash, player.Position);
			hash.Add(player.CurrentAnimationState);
			animated = true;
		}

		foreach (VoxEntity entity in _simulation.Entities.GetAllEntities())
		{
			if (entity is not (VEntNPC or VEntSlidingDoor or VEntPickup))
			{
				continue;
			}

			hash.Add(entity.NetworkId);
			AddQuantizedVector(ref hash, entity.Position + entity.PresentationOffset);
			switch (entity)
			{
				case VEntNPC npc:
					hash.Add(npc.CurrentAnimationName, StringComparer.Ordinal);
					AddQuantizedVector(ref hash, npc.LookDirection);
					animated = true;
					break;
				case VEntSlidingDoor door:
					hash.Add((int)door.State);
					hash.Add((int)MathF.Round(door.OpenAmount * 1024));
					break;
				case VEntPickup:
					animated |= entity.IsRotating;
					break;
			}
		}

		// Animation-only pose changes are deliberately quantized to 30 Hz.
		if (animated)
		{
			hash.Add((long)MathF.Floor(Eng.TotalTime * 30f));
		}

		return hash.ToHashCode();
	}

	private static void AddQuantizedVector(ref HashCode hash, Vector3 value)
	{
		const float scale = 1024f;
		hash.Add((int)MathF.Round(value.X * scale));
		hash.Add((int)MathF.Round(value.Y * scale));
		hash.Add((int)MathF.Round(value.Z * scale));
	}

	public override void RenderShadowCasters(
		RenderPass pass,
		in DirectionalShadowCascade cascade,
		in FrameTiming timing)
	{
		if (_fishVoxelScene is null)
		{
			return;
		}

		_fishVoxelScene.Renderer.RenderShadowCasters(pass, cascade);
	}

	public override void RenderDynamicShadowCasters(
		RenderPass pass,
		in DirectionalShadowCascade cascade,
		in FrameTiming timing)
	{
		if (_initialized)
		{
			DrawFishGfxActorShadowCasters(pass, cascade);
		}
	}

	public override void RenderWorld(
		RenderPass pass,
		in FrameTiming timing,
		DirectionalShadowFrame? shadows)
	{
		if (_fishVoxelScene is null)
		{
			return;
		}

		_fishRenderQueue.BeginFrame();
		if (_initialized && _simulation?.LocalPlayer is not null)
			_fishCelestial?.Render(pass, _fishCameraState, _simulation.DayNight);
		_fishVoxelScene.Enqueue(_fishRenderQueue, _fishWorldCamera, shadows);
		pass.Execute(_fishRenderQueue, RenderQueueBucket.Opaque);
		if (_initialized)
		{
			DrawFishGfxGameplayGeometry(
				pass,
				new EntityWorldLighting(_fishVoxelScene.Renderer.SunSettings, shadows)
			);
		}
		pass.Execute(_fishRenderQueue, RenderQueueBucket.Transparent);
		if (_initialized)
		{
			_fishParticles?.Render(
				pass,
				_fishCameraState.Position,
				_fishCameraState.Target,
				_fishCameraState.Up);
		}
	}

	public override void RenderViewmodel(RenderPass pass, in FrameTiming timing)
	{
		(_simulation?.LocalPlayer as ClientPlayer)?.RenderFishGfxViewModel(pass);
	}

	public override FishGfxFogFrame? GetLocalFogFrame()
	{
		return _fishVoxelScene?.FogVolume.CurrentFrame;
	}

	public override bool RendererProfilingEnabled => _rendererProfilingEnabled;

	public override void RenderFogDepthOccluders(
		RenderPass pass,
		in FrameTiming timing)
	{
		_fishVoxelScene?.Renderer.RenderFogDepthOccluders(
			pass,
			_fishWorldCamera,
			_fishVoxelScene.MaxChunkDrawDistance
		);
	}

	public override void RenderOverlay(RenderPass pass, in FrameTiming timing)
	{
		UpdateNpcSpeechOverlay(timing.TotalTime);
		if (_initialized && _simulation?.LocalPlayer is not null)
		{
			if (_simulation.Map.GetBlock(_fishCameraState.Position) == BlockType.Water)
			{
				pass.FillRectangle(
					0,
					0,
					Window.Width,
					Window.Height,
					new FishColor(30, 80, 150, 105)
				);
			}

			pass.DrawCircle(
				new Vector2(Window.Width * 0.5f, Window.Height * 0.5f),
				5,
				1,
				FishColor.White
			);
		}

		_gui?.Render(pass, timing.DeltaTime, timing.TotalTime);
	}

}
