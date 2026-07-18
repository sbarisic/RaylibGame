using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.GUI;

#if WINDOWS
using FishGfx.Graphics;
using FishGfx.Voxels;
using Voxelgine.Audio;
using Voxelgine.FishGfxClient;
using Voxelgine.FishGfxClient.Audio;
using Voxelgine.FishGfxClient.Effects;
using Voxelgine.FishGfxClient.Entities;
using Voxelgine.FishGfxClient.Gameplay;
using Voxelgine.FishGfxClient.Rendering;
using Voxelgine.FishGfxClient.Voxels;
using FishColor = FishGfx.Color;
#endif

namespace Voxelgine.States;

public unsafe partial class MPClientGameState
{
	private float _clientFrameTime;
	private float _clientDeltaTime;
	private bool _hasClientFrameTime;
	private readonly RollingFrameRateCounter _frameRateCounter = new();

#if WINDOWS
	private readonly RenderQueue _fishRenderQueue = new();
	private readonly FishGfx.Graphics.Camera _fishWorldCamera = new();
	private FishGfxVoxelScene _fishVoxelScene;
	private FishGfxCelestialLayer _fishCelestial;
	private FishGfxGameplayParticleAssets? _fishParticleAssets;
	private FishGfxGameplayParticles _fishParticles;
	private FishGfxAmbienceSession _fishAmbience;
	private FishGfxEntityRenderAssets _fishEntityAssets;
	private readonly Dictionary<int, FishGfxRemotePlayerRenderAdapter> _fishRemotePlayers = new();
	private readonly Dictionary<int, FishGfxNpcRenderAdapter> _fishNpcs = new();
	private readonly Dictionary<int, SpeechOcclusionCache> _speechOcclusion = new();
	private FishGfxSlidingDoorRenderAdapter _fishDoorRenderer;
	private FishGfxPickupRenderAdapter _fishPickupRenderer;
	private GameCameraState _fishCameraState;
	private GameCameraState _previousFishCameraState;
	private bool _hasPreviousFishCameraState;
	private bool _rendererProfilingEnabled;
	private float _nextRendererProfileLogTime;

	private sealed class SpeechOcclusionCache
	{
		public float NextCheckTime;
		public bool Occluded;
	}
#endif

	private bool IsUsingFishGfx => _gameWindow is IFishGfxGameWindow;

	private void RecordClientFrameTime(float gameTime)
	{
		if (_hasClientFrameTime)
		{
			_clientDeltaTime = Math.Clamp(gameTime - _clientFrameTime, 0, 0.25f);
		}
		else
		{
			_clientDeltaTime = 0;
			_hasClientFrameTime = true;
		}

		_clientFrameTime = gameTime;
	}

	internal float GetClientTime()
	{
		return Eng.TotalTime;
	}

	private float GetClientDeltaTime()
	{
		return _clientDeltaTime;
	}

	private void SetCursorCaptured(bool captured)
	{
		if (_gameWindow is IFishGfxGameWindow fishWindow)
		{
			fishWindow.RenderWindow.CaptureCursor = captured;
			fishWindow.RenderWindow.ShowCursor = !captured;
		}
	}

	private static void ReadFishGfxSpawnProperties(VoxEntity entity, BinaryReader reader)
	{
		Vector3 size = new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
		string modelName = reader.ReadString();
		entity.SetSize(size);
		entity.SetModelName(modelName);

		switch (entity)
		{
			case VEntNPC npc:
				string textureName = reader.ReadString();
				if (!string.IsNullOrWhiteSpace(textureName))
				{
					npc.SetTextureName(textureName);
				}
				break;

			case VEntSlidingDoor door:
				float openAngle = reader.ReadSingle();
				float triggerRadius = reader.ReadSingle();
				Vector3 closedPosition = new(
					reader.ReadSingle(),
					reader.ReadSingle(),
					reader.ReadSingle()
				);
				Vector3 facing = new(
					reader.ReadSingle(),
					reader.ReadSingle(),
					reader.ReadSingle()
				);
				door.Initialize(closedPosition, size, openAngle);
				door.TriggerRadius = triggerRadius;
				door.FacingDirection = facing;
				break;
		}
	}

	private void CreateFishGfxVoxelScene()
	{
#if WINDOWS
		DisposeFishGfxVoxelScene();
		if (_gameWindow is not IFishGfxGameWindow fishWindow || _simulation?.Map is null)
		{
			return;
		}

		GameConfig config = Eng.DI.GetRequiredService<GameConfig>();
		_fishVoxelScene = new FishGfxVoxelScene(
			fishWindow.RenderWindow.Graphics,
			fishWindow.Assets,
			_simulation.Map,
			config.MaxChunkDrawDistance,
			config.ChunkMeshUploadBudget
		);
		_fishVoxelScene.GpuProfilingEnabled = _rendererProfilingEnabled;
		_fishCelestial = new FishGfxCelestialLayer(fishWindow);
		_fishParticleAssets ??= FishGfxGameplayParticleAssets.Register(fishWindow.Assets);
		_fishParticles = new FishGfxGameplayParticles(
			fishWindow.RenderWindow.Graphics,
			_fishParticleAssets.Value,
			_fishVoxelScene
		);
		_logging?.Log(
			GameLogLevel.Debug,
			"VoxelRenderer",
			$"Configured drawDistance={_fishVoxelScene.MaxChunkDrawDistance} meshUploadBudget={_fishVoxelScene.ChunkMeshUploadBudget}; indexed fire emitters campfires={_fishVoxelScene.CampfirePositions.Count} torches={_fishVoxelScene.TorchCount}"
		);
		_fishAmbience = new FishGfxAmbienceSession(
			Eng.DI.GetRequiredService<IAudioSystem>(),
			_fishVoxelScene
		);
		try
		{
			_fishEntityAssets = new FishGfxEntityRenderAssets(fishWindow);
			_fishDoorRenderer = _fishEntityAssets.CreateSlidingDoorAdapter();
			_fishPickupRenderer = _fishEntityAssets.CreatePickupAdapter();
		}
		catch (Exception exception)
		{
			_logging?.Log(GameLogLevel.Error, "Assets", "FishGfx entity assets are unavailable.", exception);
			_fishEntityAssets?.Dispose();
			_fishEntityAssets = null;
		}
		_hasPreviousFishCameraState = false;
#endif
	}

	private void DisposeFishGfxVoxelScene()
	{
#if WINDOWS
		_fishRenderQueue.Clear();
		_fishAmbience?.Dispose();
		_fishAmbience = null;
		_fishParticles?.Dispose();
		_fishParticles = null;
		_fishCelestial?.Dispose();
		_fishCelestial = null;
		_fishRemotePlayers.Clear();
		_fishNpcs.Clear();
		_speechOcclusion.Clear();
		_fishDoorRenderer = null;
		_fishPickupRenderer = null;
		_fishEntityAssets?.Dispose();
		_fishEntityAssets = null;
		_fishVoxelScene?.Dispose();
		_fishVoxelScene = null;
		_hasPreviousFishCameraState = false;
#endif
	}

#if WINDOWS
	private void PrepareFishGfxFrame(in FrameTiming timing)
	{
		if (!IsUsingFishGfx)
		{
			return;
		}

		UpdateFishGfxUi(timing);
		if (!_initialized || _simulation?.LocalPlayer is null || _fishVoxelScene is null)
		{
			return;
		}

		Player player = _simulation.LocalPlayer;
		player.UpdateDirectionVectors();
		GameCameraState current = CreateCameraState(player);
		float decay = MathF.Max(0, 1 - CorrectionSmoothRate * timing.DeltaTime);
		if (_correctionSmoothOffset.LengthSquared() > 0.0001f)
		{
			_correctionSmoothOffset *= decay;
		}
		else
		{
			_correctionSmoothOffset = Vector3.Zero;
		}

		current = current with
		{
			Position = current.Position + _correctionSmoothOffset,
			Target = current.Target + _correctionSmoothOffset,
		};

		_fishCameraState = _hasPreviousFishCameraState
			? InterpolateCamera(_previousFishCameraState, current, timing.InterpolationAlpha)
			: current;
		_previousFishCameraState = current;
		_hasPreviousFishCameraState = true;

		player.RenderCam = _fishCameraState;

		Vector2 framebufferSize = ((IFishGfxGameWindow)_gameWindow).RenderWindow.FramebufferSize;
		ConfigureFishCamera(_fishWorldCamera, _fishCameraState, framebufferSize);
		ConfigureVoxelEnvironment(_fishVoxelScene, _simulation.DayNight, player.Position);
		_fishVoxelScene.Update(_fishWorldCamera);
		VoxelRendererFrameDiagnostics rendererDiagnostics = _fishVoxelScene.FrameDiagnostics;
		Eng.ChunkDrawCalls = rendererDiagnostics.DriverDrawCalls;

		if (_rendererProfilingEnabled && timing.TotalTime >= _nextRendererProfileLogTime)
		{
			_nextRendererProfileLogTime = timing.TotalTime + 1;
			VoxelRendererStatistics rendererStatistics = _fishVoxelScene.Statistics;
			_logging?.Log(
				GameLogLevel.Debug,
				"Performance",
				$"VoxelRenderer active={rendererDiagnostics.ActiveChunks} visible={rendererDiagnostics.VisibleChunks} cachedInactive={rendererDiagnostics.InactiveCachedChunks} "
					+ $"logicalDraws={rendererDiagnostics.LogicalDraws} driverDraws={rendererDiagnostics.DriverDrawCalls} indirectCommands={rendererDiagnostics.IndirectCommandCount} "
					+ $"pagesTouched={rendererDiagnostics.GeometryPagesTouched} pagesResident={rendererStatistics.GeometryPages} "
					+ $"cullMs={rendererDiagnostics.CullingMilliseconds:F3} commandMs={rendererDiagnostics.CommandBuildMilliseconds:F3} "
					+ $"submitMs={rendererDiagnostics.SubmissionMilliseconds:F3} gpuMs={rendererDiagnostics.GpuMilliseconds:F3} allocations={rendererDiagnostics.ManagedAllocatedBytes}"
			);
		}
		_fishParticles?.UpdateVoxelEmitters(
			timing.DeltaTime,
			_fishCameraState.Position,
			_fishVoxelScene.FireParticleEmitters
		);
		_fishParticles?.Update(timing.DeltaTime);
		_fishAmbience?.Update(
			timing.DeltaTime,
			_fishCameraState.Position,
			_simulation.DayNight.SkyLightMultiplier
		);

		UpdateRemoteInterpolation(timing.TotalTime, timing.DeltaTime);
		UpdateEntityInterpolation(timing.TotalTime);
		UpdateFishGfxEntityAdapters(timing.DeltaTime);
	}

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

		if (_initialized && _simulation?.LocalPlayer is not null)
		{
			ConfigureFishCamera(_fishWorldCamera, _fishCameraState, framebufferSize);
			var sky = _simulation.DayNight.SkyColor;
			clearColor = new FishColor(sky.R, sky.G, sky.B, sky.A);
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

	public override void RenderWorld(RenderPass pass, in FrameTiming timing)
	{
		if (!_initialized || _simulation?.LocalPlayer is null || _fishVoxelScene is null)
		{
			return;
		}

		_fishRenderQueue.BeginFrame();
		_fishCelestial?.Render(pass, _fishCameraState, _simulation.DayNight);
		_fishVoxelScene.Enqueue(_fishRenderQueue, _fishWorldCamera);
		pass.Execute(_fishRenderQueue, RenderQueueBucket.Opaque);
		DrawFishGfxGameplayGeometry(pass);
		pass.Execute(_fishRenderQueue, RenderQueueBucket.Transparent);
		_fishParticles?.Render(
			pass,
			_fishCameraState.Position,
			_fishCameraState.Target,
			_fishCameraState.Up
		);
	}

	public override void RenderViewmodel(RenderPass pass, in FrameTiming timing)
	{
		(_simulation?.LocalPlayer as ClientPlayer)?.RenderFishGfxViewModel(pass);
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

	private void UpdateNpcSpeechOverlay(float totalTime)
	{
		if (_npcSpeechOverlay is null || !_initialized || _simulation is null)
		{
			return;
		}

		List<NpcSpeechBubbleItem> visible = new();
		Vector3 cameraPosition = _fishCameraState.Position;
		Vector3 cameraForward = Vector3.Normalize(_fishCameraState.Target - cameraPosition);
		IFishGfxGameWindow fishWindow = (IFishGfxGameWindow)_gameWindow;
		Vector2 framebuffer = fishWindow.RenderWindow.FramebufferSize;
		Vector2 logicalScale = new(
			Window.Width / MathF.Max(1, framebuffer.X),
			Window.Height / MathF.Max(1, framebuffer.Y)
		);

		foreach (VoxEntity entity in _simulation.Entities.GetAllEntities())
		{
			if (entity is not VEntNPC npc
				|| string.IsNullOrWhiteSpace(npc.SpeechText)
				|| npc.SpeechDuration <= 0
				|| !_fishNpcs.TryGetValue(npc.NetworkId, out FishGfxNpcRenderAdapter adapter))
			{
				continue;
			}

			EntityRenderBounds bounds = adapter.GetAnimationBounds();
			Vector3 anchor = new(bounds.Center.X, bounds.Max.Y + 0.2f, bounds.Center.Z);
			Vector3 offset = anchor - cameraPosition;
			float distance = offset.Length();
			if (distance > 48 || distance <= 0.001f || Vector3.Dot(offset, cameraForward) <= 0)
				continue;

			if (!_speechOcclusion.TryGetValue(npc.NetworkId, out SpeechOcclusionCache cache))
			{
				cache = new SpeechOcclusionCache();
				_speechOcclusion[npc.NetworkId] = cache;
			}
			if (totalTime >= cache.NextCheckTime)
			{
				cache.NextCheckTime = totalTime + 0.1f;
				Vector3 direction = offset / distance;
				cache.Occluded = _simulation.Map.TryRaycast(
					cameraPosition,
					direction,
					MathF.Max(0, distance - 0.2f),
					out _
				);
			}
			if (cache.Occluded)
				continue;

			Vector3 framebufferPoint = _fishWorldCamera.WorldToScreen(anchor);
			Vector2 logicalPoint = new(framebufferPoint.X * logicalScale.X, framebufferPoint.Y * logicalScale.Y);
			if (logicalPoint.X < 0 || logicalPoint.X > Window.Width || logicalPoint.Y < 0 || logicalPoint.Y > Window.Height)
				continue;

			visible.Add(new NpcSpeechBubbleItem(npc.NetworkId, npc.SpeechText, logicalPoint, distance));
		}
		foreach (int staleId in _speechOcclusion.Keys.Where(id => !_fishNpcs.ContainsKey(id)).ToArray())
		{
			_speechOcclusion.Remove(staleId);
		}

		_npcSpeechOverlay.SetItems(visible);
	}

	private void DrawFishGfxGameplayGeometry(RenderPass pass)
	{
		foreach (RemotePlayer remotePlayer in _simulation.Players.GetAllRemotePlayers())
		{
			if (_fishRemotePlayers.TryGetValue(remotePlayer.PlayerId, out FishGfxRemotePlayerRenderAdapter adapter))
				adapter.Render(pass);
		}

		foreach (VoxEntity entity in _simulation.Entities.GetAllEntities())
		{
			Rgba32 light = _fishVoxelScene.SampleLight(entity.Position + Vector3.UnitY * 0.5f);
			switch (entity)
			{
				case VEntNPC npc when _fishNpcs.TryGetValue(npc.NetworkId, out FishGfxNpcRenderAdapter npcAdapter):
					npcAdapter.Render(pass);
					break;
				case VEntSlidingDoor door when _fishDoorRenderer is not null:
					_fishDoorRenderer.Render(pass, new SlidingDoorRenderState(
						door.Position,
						door.Size,
						door.FacingDirection,
						door.OpenAmount,
						door.OpenAngleDeg,
						light
					));
					break;
				case VEntPickup pickup when _fishPickupRenderer is not null:
					_fishPickupRenderer.Render(pass, new PickupRenderState(
						pickup.Position,
						pickup.Size,
						pickup.RotationDegrees,
						pickup.VerticalModelOffset,
						light
					));
					break;
				default:
					FishGfxGameplayPrimitives.DrawWireBox(
						pass,
						entity.WorldBounds.Min,
						entity.WorldBounds.Max,
						FishColor.Amber
					);
					break;
			}
		}

		InventoryItem activeItem = (_simulation.LocalPlayer as ClientPlayer)?.GetActiveItem();
		if (activeItem is null || !activeItem.IsPlaceableBlock())
		{
			return;
		}

		Vector3? placement = activeItem.GetBlockPlacementPosition(
			_simulation.Map,
			_simulation.LocalPlayer.Position,
			_simulation.LocalPlayer.GetForward(),
			20
		);
		if (placement.HasValue)
		{
			FishGfxGameplayPrimitives.DrawWireBox(
				pass,
				placement.Value,
				placement.Value + Vector3.One,
				FishColor.White,
				2
			);
		}
	}

	private BlockType GetBlockAtImpact(Vector3 hitPosition, Vector3 hitNormal)
	{
		if (_simulation?.Map is null)
		{
			return BlockType.None;
		}

		Vector3 sample = hitPosition - hitNormal * 0.5f;
		return _simulation.Map.GetBlock(
			(int)MathF.Floor(sample.X),
			(int)MathF.Floor(sample.Y),
			(int)MathF.Floor(sample.Z)
		);
	}

	private void SpawnFishGfxEntityHit(
		bool isFlesh,
		Vector3 hitPosition,
		Vector3 hitNormal
	)
	{
		if (_fishParticles is null)
		{
			return;
		}

		Vector3 normal = NormalizeHitNormal(hitNormal);
		if (isFlesh)
		{
			for (int index = 0; index < 8; index++)
			{
				float scale = 0.68f + Random.Shared.NextSingle() * 0.34f;
				_fishParticles.EnqueueBlood(hitPosition, normal * 0.5f, scale);
			}
			return;
		}

		SpawnDirectionalFishParticles(
			FishHitParticleKind.Spark,
			6,
			hitPosition,
			normal,
			Rgba32.White,
			0.5f,
			0.5f,
			10.6f
		);
	}

	private void SpawnFishGfxBlockHit(
		BlockType blockType,
		Vector3 hitPosition,
		Vector3 hitNormal
	)
	{
		if (_fishParticles is null)
		{
			return;
		}

		FishHitMaterial material = blockType switch
		{
			BlockType.None or BlockType.Water or BlockType.Leaf or BlockType.Foliage =>
				FishHitMaterial.None,
			BlockType.Plank or BlockType.Wood or BlockType.CraftingTable or
				BlockType.Barrel or BlockType.Campfire or BlockType.Torch =>
				FishHitMaterial.Wood,
			BlockType.Dirt or BlockType.Grass or BlockType.Sand => FishHitMaterial.Earth,
			BlockType.Glass or BlockType.Ice => FishHitMaterial.Glass,
			_ => FishHitMaterial.Stone,
		};

		Vector3 normal = NormalizeHitNormal(hitNormal);
		switch (material)
		{
			case FishHitMaterial.Stone:
				SpawnDirectionalFishParticles(
					FishHitParticleKind.Spark,
					6,
					hitPosition,
					normal,
					Rgba32.White,
					0.5f,
					0.5f,
					10.6f
				);
				break;
			case FishHitMaterial.Wood:
				SpawnDirectionalFishParticles(
					FishHitParticleKind.Fire,
					4,
					hitPosition,
					normal,
					Rgba32.White,
					0.5f,
					0.5f,
					8
				);
				SpawnDirectionalFishParticles(
					FishHitParticleKind.Smoke,
					2,
					hitPosition,
					normal,
					new Rgba32(180, 160, 130),
					0.4f,
					0.3f,
					4
				);
				break;
			case FishHitMaterial.Earth:
				SpawnDirectionalFishParticles(
					FishHitParticleKind.Smoke,
					5,
					hitPosition,
					normal,
					new Rgba32(160, 140, 100),
					0.5f,
					0.3f,
					5
				);
				break;
			case FishHitMaterial.Glass:
				SpawnDirectionalFishParticles(
					FishHitParticleKind.Spark,
					8,
					hitPosition,
					normal,
					new Rgba32(200, 220, 255),
					0.3f,
					0.3f,
					12
				);
				break;
		}
	}

	private void SpawnDirectionalFishParticles(
		FishHitParticleKind kind,
		int count,
		Vector3 position,
		Vector3 normal,
		Rgba32 color,
		float minimumScale,
		float scaleRange,
		float force
	)
	{
		float spread = 0.6f;
		if (MathF.Abs(normal.Y) < 0.001f)
		{
			force *= 2;
			spread = 0.4f;
		}

		for (int index = 0; index < count; index++)
		{
			Vector3 direction = normal + Utils.GetRandomUnitVector() * spread;
			if (direction.LengthSquared() < 0.000001f)
			{
				direction = normal;
			}
			direction = Vector3.Normalize(direction);
			float scale = minimumScale + Random.Shared.NextSingle() * scaleRange;

			switch (kind)
			{
				case FishHitParticleKind.Fire:
					_fishParticles.EnqueueFire(
						position,
						direction * force,
						color,
						scale,
						noCollision: true,
						lifetime: 1
					);
					break;
				case FishHitParticleKind.Spark:
					_fishParticles.EnqueueSpark(position, direction * force, color, scale);
					break;
				case FishHitParticleKind.Smoke:
					_fishParticles.EnqueueShortSmoke(position, direction * force, color);
					break;
			}
		}
	}

	private static Vector3 NormalizeHitNormal(Vector3 hitNormal)
	{
		return hitNormal.LengthSquared() >= 0.000001f
			? Vector3.Normalize(hitNormal)
			: Vector3.UnitY;
	}

	private enum FishHitMaterial
	{
		None,
		Stone,
		Wood,
		Earth,
		Glass,
	}

	private enum FishHitParticleKind
	{
		Fire,
		Spark,
		Smoke,
	}

	private void UpdateRemoteInterpolation(float totalTime, float deltaTime)
	{
		foreach (RemotePlayer remotePlayer in _simulation.Players.GetAllRemotePlayers())
		{
			remotePlayer.Update(totalTime, deltaTime);
			if (_snd is not null && remotePlayer.TryPlayFootstep())
			{
				_snd.Emit(new Voxelgine.Engine.Audio.GameAudioEvent(
					"walk",
					remotePlayer.Position,
					remotePlayer.Velocity,
					remotePlayer.PlayerId
				));
			}
		}
	}

	private void UpdateFishGfxEntityAdapters(float deltaTime)
	{
		if (_fishEntityAssets is null || _fishVoxelScene is null || _simulation is null)
			return;

		HashSet<int> activeRemotePlayers = new();
		foreach (RemotePlayer remotePlayer in _simulation.Players.GetAllRemotePlayers())
		{
			activeRemotePlayers.Add(remotePlayer.PlayerId);
			if (!_fishRemotePlayers.TryGetValue(
				remotePlayer.PlayerId,
				out FishGfxRemotePlayerRenderAdapter adapter
			))
			{
				adapter = _fishEntityAssets.CreateRemotePlayerAdapter();
				_fishRemotePlayers.Add(remotePlayer.PlayerId, adapter);
			}

			Rgba32 light = _fishVoxelScene.SampleLight(remotePlayer.Position);
			adapter.Update(new RemotePlayerRenderState(
				remotePlayer.Position,
				remotePlayer.CameraAngle,
				remotePlayer.CurrentAnimationState,
				light
			), deltaTime);
		}
		foreach (int id in _fishRemotePlayers.Keys.Where(id => !activeRemotePlayers.Contains(id)).ToArray())
			_fishRemotePlayers.Remove(id);

		HashSet<int> activeNpcs = new();
		foreach (VEntNPC npc in _simulation.Entities.GetAllEntities().OfType<VEntNPC>())
		{
			activeNpcs.Add(npc.NetworkId);
			if (!_fishNpcs.TryGetValue(npc.NetworkId, out FishGfxNpcRenderAdapter adapter))
			{
				adapter = _fishEntityAssets.CreateNpcAdapter();
				_fishNpcs.Add(npc.NetworkId, adapter);
			}

			Rgba32 light = _fishVoxelScene.SampleLight(npc.Position + Vector3.UnitY);
			adapter.Update(new NpcRenderState(
				npc.Position,
				npc.Size,
				npc.LookDirection,
				npc.CurrentAnimationName,
				npc.HeadTrackRotation,
				npc.TextureAssetId,
				light
			), deltaTime);
		}
		foreach (int id in _fishNpcs.Keys.Where(id => !activeNpcs.Contains(id)).ToArray())
			_fishNpcs.Remove(id);
	}

	private void UpdateFishGfxUi(in FrameTiming timing)
	{
		if (timing.DeltaTime > 0)
		{
			_frameRateCounter.Update(timing.TotalTime, timing.DeltaTime);
		}

		_totalTime = timing.TotalTime;
		if (!_initialized)
		{
			if (_loadingStatusLabel is not null)
			{
				_loadingStatusLabel.Text = _statusText ?? string.Empty;
			}

			bool hasError = !string.IsNullOrEmpty(_errorText);
			if (_loadingErrorLabel is not null)
			{
				_loadingErrorLabel.Text = _errorText ?? string.Empty;
				_loadingErrorLabel.Visible = hasError;
			}
			if (_loadingHintLabel is not null)
			{
				_loadingHintLabel.Visible = hasError;
			}

			if (_loadingProgressBar is not null)
			{
				bool loading = _client?.State == ClientState.Loading;
				_loadingProgressBar.Visible = loading;
				if (loading)
				{
					_loadingProgressBar.Value = _client.WorldReceiver.Progress;
				}
			}
		}
		else
		{
			UpdateHUDInfo();
			UpdateHealthBar();
			UpdateConnectionStatus();

			if (_netStatsPanel is not null)
			{
				_netStatsPanel.Visible = _showNetStats;
			}
			if (_showNetStats)
			{
				UpdateNetStats();
			}

			if (_playerListPanel is not null)
			{
				_playerListPanel.Visible = _showPlayerList;
			}
			if (_showPlayerList)
			{
				UpdatePlayerList();
			}

			if (_deathOverlayPanel is not null)
			{
				_deathOverlayPanel.Visible = _simulation?.LocalPlayer?.IsDead == true;
			}
			if (_connectionLostPanel is not null)
			{
				_connectionLostPanel.Visible = _connectionLost;
				if (_connectionLost && _connectionLostReasonLabel is not null)
				{
					_connectionLostReasonLabel.Text = _disconnectReason ?? string.Empty;
				}
			}
		}

		_gui?.Update(timing.DeltaTime, timing.TotalTime);
	}

	private static GameCameraState CreateCameraState(Player player)
	{
		Vector3 up = player.Cam.Up.LengthSquared() > 0.000001f
			? Vector3.Normalize(player.Cam.Up)
			: Vector3.UnitY;
		Vector3 target = player.Cam.Target;
		if (Vector3.DistanceSquared(player.Cam.Position, target) < 0.000001f)
		{
			target = player.Cam.Position + player.GetForward();
		}

		return new GameCameraState(
			player.Cam.Position,
			target,
			up,
			player.Cam.FieldOfView,
			CameraProjectionKind.Perspective
		);
	}

	private static GameCameraState InterpolateCamera(
		in GameCameraState previous,
		in GameCameraState current,
		float alpha
	)
	{
		float amount = Math.Clamp(alpha, 0, 1);
		return current with
		{
			Position = Vector3.Lerp(previous.Position, current.Position, amount),
			Target = Vector3.Lerp(previous.Target, current.Target, amount),
			Up = Vector3.Normalize(Vector3.Lerp(previous.Up, current.Up, amount)),
			FieldOfView = float.Lerp(previous.FieldOfView, current.FieldOfView, amount),
		};
	}

	private static void ConfigureFishCamera(
		FishGfx.Graphics.Camera camera,
		in GameCameraState state,
		Vector2 framebufferSize
	)
	{
		float width = Math.Max(1, framebufferSize.X);
		float height = Math.Max(1, framebufferSize.Y);
		camera.Position = state.Position;
		camera.CameraUpNormal = state.Up;
		camera.LookAt(state.Target);
		float verticalFov = Math.Clamp(state.FieldOfView, 1, 179) * MathF.PI / 180;
		float horizontalFov = 2 * MathF.Atan(MathF.Tan(verticalFov * 0.5f) * width / height);
		camera.SetPerspective(width, height, horizontalFov, 0.05f, 512);
	}

	private static void ConfigureVoxelEnvironment(
		FishGfxVoxelScene scene,
		DayNightCycle dayNight,
		Vector3 listenerPosition
	)
	{
		Vector3 towardSun = dayNight.GetSunDirection();
		Vector3 lightDirection = towardSun.LengthSquared() > 0.000001f
			? -Vector3.Normalize(towardSun)
			: new Vector3(-0.45f, -1, -0.3f);
		var sun = dayNight.SunColor;
		var sky = dayNight.SkyColor;
		float daylight = Math.Clamp(dayNight.SkyLightMultiplier, 0, 1);
		scene.SetEnvironmentLighting(daylight, dayNight.AmbientLight);
		FishColor directionalColor = sun.A == 0
			? new FishColor(145, 165, 215)
			: new FishColor(sun.R, sun.G, sun.B);
		scene.Renderer.SunSettings = new VoxelSunSettings(
			lightDirection,
			directionalColor,
			Math.Max(0.15f, daylight),
			0.18f + daylight * 0.22f
		);

		bool underwater = scene.IsInsideMaterial(listenerPosition, BlockType.Water);
		scene.Renderer.FogSettings = underwater
			? new VoxelFogSettings(new FishColor(24, 72, 125), 0.035f, 0.55f)
			: new VoxelFogSettings(new FishColor(sky.R, sky.G, sky.B), 0.0035f);
	}
#endif
}
