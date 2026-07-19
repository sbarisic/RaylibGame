using FishGfx.Graphics;
using FishGfx.Graphics.Shadows;
using FishGfx.Voxels;
using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.FishGfxClient;
using Voxelgine.FishGfxClient.Effects;
using Voxelgine.FishGfxClient.Entities;
using Voxelgine.FishGfxClient.Rendering;
using Voxelgine.FishGfxClient.Viewmodel;
using Voxelgine.FishGfxClient.Voxels;
using Voxelgine.Graphics;
using Voxelgine.GUI;

namespace Voxelgine.States;

/// <summary>
/// Deterministic real-window acceptance scene for the game-owned FishGfx path.
/// It exercises voxel mirroring/meshing, transparent materials, entity JSON and
/// OBJ models, particles, celestial assets, and viewmodel asset registration.
/// </summary>
internal sealed class FishGfxGameplaySmokeState : GameStateImpl
{
	private readonly IFishGfxGameWindow window;
	private readonly ChunkMap map = new();
	private readonly DayNightCycle dayNight = new() { IsPaused = true };
	private readonly RenderQueue renderQueue = new();
	private readonly FishGfx.Graphics.Camera camera = new();
	private readonly FishGfxVoxelScene voxelScene;
	private readonly FishGfxCelestialLayer celestial;
	private readonly FishGfxGameplayParticles particles;
	private readonly FishGfxEntityRenderAssets entityAssets;
	private readonly FishGfxNpcRenderAdapter npc;
	private readonly FishGfxSlidingDoorRenderAdapter door;
	private readonly FishGfxPickupRenderAdapter pickup;
	private readonly FishGfxViewModelRenderer viewmodelAssetProbe;
	private readonly FishUIManager gui;
	private readonly NpcSpeechBubbleOverlay speechOverlay;
	private readonly GameCameraState cameraState = new(
		new Vector3(8, 8, -14),
		new Vector3(8, 2, 7),
		Vector3.UnitY,
		70,
		CameraProjectionKind.Perspective
	);
	private bool disposed;

	internal bool IsFogVolumeReady => voxelScene.FogVolume.CurrentFrame.HasValue;

	public FishGfxGameplaySmokeState(
		IFishGfxGameWindow window,
		IFishEngineRunner engine
	) : base(window, engine)
	{
		this.window = window ?? throw new ArgumentNullException(nameof(window));
		ChunkMap generatedWorld = new();
		BuildWorld(generatedWorld);
		ChunkColumnSnapshot[] startupColumns = generatedWorld.GetColumnCoordinates()
			.Select(coordinate => generatedWorld.CaptureColumn(coordinate.X, coordinate.Z))
			.ToArray();
		dayNight.SetTime(13);
		ConfigureCamera(new Vector2(1280, 720));

		voxelScene = new FishGfxVoxelScene(
			window.RenderWindow.Graphics,
			window.Assets,
			map,
			synchronizeExisting: false
		);
		foreach (ChunkColumnSnapshot column in startupColumns)
			map.ApplyColumn(column);
		celestial = new FishGfxCelestialLayer(window);
		FishGfxGameplayParticleAssets particleAssets =
			FishGfxGameplayParticleAssets.Register(
				window.Assets,
				"acceptance.particles"
			);
		particles = new FishGfxGameplayParticles(
			window.RenderWindow.Graphics,
			particleAssets,
			voxelScene
		);
		entityAssets = new FishGfxEntityRenderAssets(window);
		npc = entityAssets.CreateNpcAdapter();
		door = entityAssets.CreateSlidingDoorAdapter();
		pickup = entityAssets.CreatePickupAdapter();
		viewmodelAssetProbe = new FishGfxViewModelRenderer(window);
		gui = new FishUIManager(window, engine.DI.GetRequiredService<IFishLogging>());
		speechOverlay = new NpcSpeechBubbleOverlay
		{
			Size = new Vector2(window.Width, window.Height),
		};
		gui.AddControl(speechOverlay);

		for (int index = 0; index < 12; index++)
		{
			Vector3 position = new(5 + index * 0.15f, 2, 4);
			particles.EnqueueSpark(
				position,
				new Vector3(-0.5f + index * 0.08f, 1, 0.25f),
				new Rgba32(255, 220, 120),
				0.7f
			);
			particles.EnqueueFire(
				new Vector3(11, 2, 7),
				Vector3.UnitY,
				Rgba32.White,
				0.7f,
				noCollision: true
			);
		}
	}

	public override void BeginFrame(in FrameTiming timing)
	{
		gui.Update(timing.DeltaTime, timing.TotalTime);
		voxelScene.SetEnvironmentLighting(
			dayNight.SkyLightMultiplier,
			dayNight.AmbientLight
		);
		Vector3 towardSun = dayNight.GetSunDirection();
		voxelScene.Renderer.SunSettings = new VoxelSunSettings(
			-Vector3.Normalize(towardSun),
			FishGfx.Color.White,
			dayNight.SkyLightMultiplier,
			0.35f
		);
		voxelScene.Update(camera);
		particles.UpdateVoxelEmitters(
			timing.DeltaTime,
			cameraState.Position,
			voxelScene.FireParticleEmitters
		);
		particles.Update(timing.DeltaTime);
		npc.Update(
			new NpcRenderState(
				new Vector3(6, 1, 8),
				new Vector3(0.8f, 1.8f, 0.8f),
				new Vector3(0, 0, -1),
				"walk",
				new Vector3(8, 0, 0),
				EntityAssetIds.HumanoidTexture,
				voxelScene.SampleEntityLight(new Vector3(6, 2, 8))
			),
			timing.DeltaTime
		);
		Vector3 projected = camera.WorldToScreen(npc.GetAnimationBounds().Max + new Vector3(-0.4f, 0.2f, -0.4f));
		Vector2 framebuffer = window.RenderWindow.FramebufferSize;
		speechOverlay.Size = new Vector2(window.Width, window.Height);
		speechOverlay.SetItems([
			new NpcSpeechBubbleItem(
				1,
				"NPC #1: Automatic speech bubble validation",
				new Vector2(projected.X * window.Width / framebuffer.X, projected.Y * window.Height / framebuffer.Y),
				Vector3.Distance(cameraState.Position, new Vector3(6, 2.8f, 8))
			),
		]);
	}

	public override void BeginInputFrame() => gui.BeginInputFrame();

	public override GameStateRenderSettings GetRenderSettings(Vector2 framebufferSize)
	{
		ConfigureCamera(framebufferSize);
		GameStateRenderSettings overlay = GameStateRenderSettings.CreateOverlay(
			new Vector2(window.Width, window.Height)
		);
		Rgba32 sky = dayNight.SkyColor;
		return new GameStateRenderSettings
		{
			WorldView = new RenderView(camera),
			ViewmodelView = new RenderView(camera),
			OverlayView = overlay.OverlayView,
			ClearColor = new FishGfx.Color(sky.R, sky.G, sky.B, sky.A),
		};
	}

	public override GameDirectionalShadowRequest? GetDirectionalShadowRequest()
	{
		return new GameDirectionalShadowRequest(
			camera,
			voxelScene.Renderer.SunSettings.Direction,
			1,
			new DirectionalShadowOptions(
				3,
				2048,
				128,
				0.65f,
				0.1f,
				DirectionalShadowFilter.Pcf3x3,
				0.75f,
				1f
			)
			{
				UpdateIntervals = new[] { 1, 2, 4 },
			},
			voxelScene.GeometryRevision,
			true
		);
	}

	public override void RenderShadowCasters(
		RenderPass pass,
		in DirectionalShadowCascade cascade,
		in FrameTiming timing)
	{
		voxelScene.Renderer.RenderShadowCasters(pass, cascade);
		npc.RenderShadow(pass);
		door.RenderShadow(pass, CreateDoorState());
		pickup.RenderShadow(pass, CreatePickupState(timing.TotalTime));
	}

	public override void RenderWorld(
		RenderPass pass,
		in FrameTiming timing,
		DirectionalShadowFrame? shadows)
	{
		renderQueue.BeginFrame();
		celestial.Render(pass, cameraState, dayNight);
		voxelScene.Enqueue(renderQueue, camera, shadows);
		pass.Execute(renderQueue, RenderQueueBucket.Opaque);
		EntityWorldLighting lighting = new(voxelScene.Renderer.SunSettings, shadows);
		npc.Render(pass, lighting);
		door.Render(
			pass,
			CreateDoorState(),
			lighting
		);
		pickup.Render(
			pass,
			CreatePickupState(timing.TotalTime),
			lighting
		);
		pass.Execute(renderQueue, RenderQueueBucket.Transparent);
		particles.Render(
			pass,
			cameraState.Position,
			cameraState.Target,
			cameraState.Up
		);
	}

	public override FishGfxFogFrame? GetLocalFogFrame()
	{
		return voxelScene.FogVolume.CurrentFrame;
	}

	public override void RenderFogDepthOccluders(
		RenderPass pass,
		in FrameTiming timing)
	{
		voxelScene.Renderer.RenderFogDepthOccluders(pass, camera, 128);
	}

	private SlidingDoorRenderState CreateDoorState()
	{
		return new SlidingDoorRenderState(
			new Vector3(10, 1, 9),
			new Vector3(1, 2, 0.25f),
			Vector3.UnitZ,
			0.45f,
			90,
			voxelScene.SampleEntityLight(new Vector3(10, 2, 9))
		);
	}

	private PickupRenderState CreatePickupState(float totalTime)
	{
		return new PickupRenderState(
			new Vector3(8, 1.25f, 6),
			new Vector3(0.5f),
			totalTime * 60,
			0.25f + MathF.Sin(totalTime * 2) * 0.1f,
			voxelScene.SampleEntityLight(new Vector3(8, 2, 6))
		);
	}

	public override void RenderOverlay(RenderPass pass, in FrameTiming timing)
	{
		gui.Render(pass, timing.DeltaTime, timing.TotalTime);
	}

	public override void Dispose()
	{
		if (disposed)
		{
			return;
		}
		viewmodelAssetProbe.Dispose();
		gui.Dispose();
		entityAssets.Dispose();
		particles.Dispose();
		celestial.Dispose();
		voxelScene.Dispose();
		disposed = true;
	}

	private void ConfigureCamera(Vector2 framebufferSize)
	{
		float width = Math.Max(1, framebufferSize.X);
		float height = Math.Max(1, framebufferSize.Y);
		camera.Position = cameraState.Position;
		camera.CameraUpNormal = cameraState.Up;
		camera.LookAt(cameraState.Target);
		float verticalFov = cameraState.FieldOfView * MathF.PI / 180;
		float horizontalFov = 2 * MathF.Atan(
			MathF.Tan(verticalFov * 0.5f) * width / height
		);
		camera.SetPerspective(width, height, horizontalFov, 0.05f, 512);
	}

	private static void BuildWorld(ChunkMap map)
	{
		for (int x = 0; x < 16; x++)
		{
			for (int z = 0; z < 16; z++)
			{
				map.SetBlock(x, 0, z, BlockType.Stone);
				map.SetBlock(x, 1, z, BlockType.Grass);
			}
		}

		for (int x = 2; x <= 4; x++)
		{
			for (int z = 7; z <= 10; z++)
			{
				map.SetBlock(x, 1, z, BlockType.Water);
			}
		}
		map.SetBlock(5, 2, 7, BlockType.Glass);
		map.SetBlock(5, 2, 8, BlockType.Ice);
		map.SetBlock(11, 2, 7, BlockType.Campfire);
		map.SetBlock(12, 2, 7, BlockType.Glowstone);
		map.SetBlock(13, 2, 7, BlockType.Torch);
		map.SetBlock(7, 2, 11, BlockType.Leaf);
		map.SetBlock(8, 2, 11, BlockType.Foliage);
		// Keep a second resident chunk with no alpha-shadow geometry. The fog-depth
		// pass must skip its absent foliage allocation instead of submitting null.
		map.SetBlock(18, 1, 8, BlockType.Stone);
		map.FillFog(
			3,
			2,
			4,
			9,
			3,
			10,
			FogVoxel.FromStraight(new Rgba32(110, 175, 255), 96)
		);
		map.FillFog(
			9,
			2,
			6,
			13,
			2,
			11,
			FogVoxel.FromStraight(new Rgba32(255, 110, 85), 144)
		);
	}
}
