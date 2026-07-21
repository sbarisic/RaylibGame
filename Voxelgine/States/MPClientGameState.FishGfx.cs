using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.GUI;

#if WINDOWS
using FishGfx.Graphics;
using FishGfx.Graphics.Shadows;
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
	private readonly RollingFrameTimeline _frameTimeline = new();
	private FramePercentiles _framePercentiles;

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
	private readonly Dictionary<int, ActorLightCache> _remotePlayerLightCache = new();
	private readonly Dictionary<int, ActorLightCache> _npcLightCache = new();
	private readonly HashSet<int> _activeRemotePlayerIds = new();
	private readonly HashSet<int> _activeNpcIds = new();
	private readonly List<int> _staleActorIds = new();
	private readonly Dictionary<int, SpeechOcclusionCache> _speechOcclusion = new();
	private FishGfxSlidingDoorRenderAdapter _fishDoorRenderer;
	private FishGfxPickupRenderAdapter _fishPickupRenderer;
	private GameCameraState _fishCameraState;
	private GameCameraState _previousFishCameraState;
	private bool _hasPreviousFishCameraState;
	private bool _rendererProfilingEnabled;
	private float _nextRendererProfileLogTime;
	private float _nextHitchLogTime;
	private string _pendingHitchLog;
	private GameCameraState _profilingPreviousCamera;
	private bool _hasProfilingPreviousCamera;
	private int _profilingGen0Collections;
	private int _profilingGen1Collections;
	private int _profilingGen2Collections;
	private long _lastShadowActorRevision = long.MinValue;
	private readonly List<FishGfx.AxisAlignedBoundingBox> _shadowInvalidations = new();

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

	private void CreateFishGfxVoxelScene(bool synchronizeExisting = true)
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
			config.ChunkMeshUploadBudget,
			config.VolumetricFogQuality,
			synchronizeExisting
		);
		_fishVoxelScene.GpuProfilingEnabled = _rendererProfilingEnabled;
		_fishVoxelScene.PreparedColumnApplied += OnPreparedRenderColumnApplied;
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
		_remotePlayerLightCache.Clear();
		_npcLightCache.Clear();
		_activeRemotePlayerIds.Clear();
		_activeNpcIds.Clear();
		_staleActorIds.Clear();
		_speechOcclusion.Clear();
		_fishDoorRenderer = null;
		_fishPickupRenderer = null;
		_fishEntityAssets?.Dispose();
		_fishEntityAssets = null;
		if (_fishVoxelScene != null)
		{
			_fishVoxelScene.PreparedColumnApplied -= OnPreparedRenderColumnApplied;
			_fishVoxelScene.Dispose();
		}
		_fishVoxelScene = null;
		_hasPreviousFishCameraState = false;
		_lastShadowActorRevision = long.MinValue;
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
		if (_fishVoxelScene is null)
		{
			return;
		}
		_fishVoxelScene.FogVolume.Quality = Eng.DI
			.GetRequiredService<GameConfig>()
			.VolumetricFogQuality;
		if (!_initialized || _simulation?.LocalPlayer is null)
		{
			Vector2 loadingFramebuffer = ((IFishGfxGameWindow)_gameWindow).RenderWindow.FramebufferSize;
			_fishCameraState = new GameCameraState(
				_worldStreamFocus + new Vector3(0, 12, -20),
				_worldStreamFocus,
				Vector3.UnitY,
				60,
				CameraProjectionKind.Perspective);
			ConfigureFishCamera(_fishWorldCamera, _fishCameraState, loadingFramebuffer);
			if (_simulation != null)
				ConfigureVoxelEnvironment(_fishVoxelScene, _simulation.DayNight, _worldStreamFocus);
			_fishVoxelScene.Update(_fishWorldCamera);
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
		UpdateFrameTimeline(timing, rendererDiagnostics);

		if (_rendererProfilingEnabled && timing.TotalTime >= _nextRendererProfileLogTime)
		{
			_nextRendererProfileLogTime = timing.TotalTime + 1;
			VoxelRendererStatistics rendererStatistics = _fishVoxelScene.Statistics;
			VoxelRendererWorkload rendererWorkload = rendererDiagnostics.Workload;
			NetConnectionDiagnostics networkDiagnostics = _client?.Diagnostics ?? default;
			DirectionalShadowDiagnostics shadowDiagnostics =
				((IFishGfxGameWindow)_gameWindow).ShadowDiagnostics;
			VoxelShadowSubmissionDiagnostics shadowCasterDiagnostics =
				_fishVoxelScene.Renderer.LastShadowSubmission;
			FishGfxFogDiagnostics fogDiagnostics = _fishVoxelScene.FogVolume.Diagnostics;
			FishGfxFogFrame? fogFrame = _fishVoxelScene.FogVolume.CurrentFrame;
			_logging?.Log(
				GameLogLevel.Debug,
				"Performance",
				$"VoxelRenderer active={rendererDiagnostics.ActiveChunks} visible={rendererDiagnostics.VisibleChunks} cachedInactive={rendererDiagnostics.InactiveCachedChunks} "
					+ $"logicalDraws={rendererDiagnostics.LogicalDraws} driverDraws={rendererDiagnostics.DriverDrawCalls} indirectCommands={rendererDiagnostics.IndirectCommandCount} "
					+ $"pagesTouched={rendererDiagnostics.GeometryPagesTouched} pagesResident={rendererStatistics.GeometryPages} "
					+ $"cullMs={rendererDiagnostics.CullingMilliseconds:F3} commandMs={rendererDiagnostics.CommandBuildMilliseconds:F3} "
					+ $"activeSetMs={rendererDiagnostics.ActiveSetRefreshMilliseconds:F3} activeSetAlloc={rendererDiagnostics.ActiveSetAllocatedBytes} "
					+ $"activeSetColumns={rendererDiagnostics.ActiveSetVisitedColumns} activeSetTested={rendererDiagnostics.ActiveSetTestedChunks} "
					+ $"activeSetDelta=+{rendererDiagnostics.ActiveSetAdditions}/-{rendererDiagnostics.ActiveSetRemovals} activeSetReason={rendererDiagnostics.ActiveSetRefreshReason} "
					+ $"submitMs={rendererDiagnostics.SubmissionMilliseconds:F3} gpuMs={rendererDiagnostics.GpuMilliseconds:F3} allocations={rendererDiagnostics.ManagedAllocatedBytes} "
					+ $"meshUploadBytes={rendererDiagnostics.MeshUploadBytes} meshUploadSlices={rendererDiagnostics.MeshUploadSlices} meshUploadQueued={rendererDiagnostics.QueuedMeshUploadBytes} meshPrepareMs={rendererDiagnostics.MeshUploadPreparationMilliseconds:F3} meshGrowths={rendererDiagnostics.MeshUploadStorageGrowths} "
					+ $"meshUploadOldest={rendererDiagnostics.OldestMeshUploadJobAgeSeconds:F2}s meshUploadJobs={rendererDiagnostics.CompletedUploadJobs}/{rendererDiagnostics.DiscardedUploadJobs} "
					+ $"meshWork={rendererWorkload.DirtyMeshes}/{rendererWorkload.InFlightMeshes}/{rendererWorkload.CompletedMeshes}/{rendererWorkload.PendingUploadJobs} meshReadyBytes={rendererWorkload.PendingUploadBytes} meshBackpressure={rendererWorkload.IsBackpressured} lightingPending={_fishVoxelScene.LightingPendingCount} "
					+ $"frameMs={timing.DeltaTime * 1000:F3} frameMedian={_framePercentiles.MedianMilliseconds:F3} frameP95={_framePercentiles.P95Milliseconds:F3} frameP99={_framePercentiles.P99Milliseconds:F3} frameMax={_framePercentiles.MaximumMilliseconds:F3} "
					+ $"transparentFaces={rendererDiagnostics.TransparentFaceCount} transparentIndices={rendererDiagnostics.TransparentIndexCount} "
					+ $"transparentSourceMs={rendererDiagnostics.TransparentSourceBuildMilliseconds:F3} transparentSortMs={rendererDiagnostics.TransparentWorkerSortMilliseconds:F3} "
					+ $"transparentApplyMs={rendererDiagnostics.TransparentResultApplyMilliseconds:F3} transparentUploadMs={rendererDiagnostics.TransparentIndexUploadMilliseconds:F3} "
					+ $"transparentGpuMs={rendererDiagnostics.TransparentGpuMilliseconds:F3} transparentMainAlloc={rendererDiagnostics.TransparentMainThreadAllocatedBytes} "
					+ $"transparentWorkerAlloc={rendererDiagnostics.TransparentWorkerAllocatedBytes} transparentPending={rendererDiagnostics.TransparentOrderingPending} "
					+ $"transparentRunning={rendererDiagnostics.TransparentOrderingRunning} transparentCoalesced={rendererDiagnostics.TransparentCoalescedRequests} "
					+ $"transparentStale={rendererDiagnostics.TransparentStaleResults} transparentAge={rendererDiagnostics.TransparentOrderingAgeSeconds:F2}s "
					+ $"transparentRequestReason={rendererDiagnostics.TransparentInvalidationReason} transparentOrderingReason={rendererDiagnostics.TransparentOrderingReason} "
					+ $"transparentGeometryRevision={rendererDiagnostics.TransparentOrderingGeometryRevision} "
					+ $"transparentCameraDelta={rendererDiagnostics.TransparentOrderingCameraDistanceDelta:F2}/{rendererDiagnostics.TransparentOrderingCameraAngleDeltaDegrees:F2}deg"
					+ $" streamCore={WorldCoreReceived}/{WorldCoreApplied}/{WorldCoreLit}/{WorldCoreMeshed} streamHalo={WorldHaloReceived}/{WorldHaloApplied}/{WorldHaloLit}/{WorldHaloMeshed} "
					+ $"streamOrdinary={WorldOrdinaryReceived}/{WorldOrdinaryApplied} decodeQueue={WorldDecodeQueueDepth} applyQueue={WorldApplyQueueDepth} deferredAcks={WorldDeferredAcknowledgements} streamPaused={WorldStreamingBackpressured} "
					+ $"streamedColumns={WorldCoreReceived + WorldHaloReceived + WorldOrdinaryReceived} cachedColumns={WorldCachedColumns} "
					+ $"columnDecodeMs={AverageColumnDecodeMilliseconds:F3} columnApplyMs={AverageColumnApplyMilliseconds:F3} "
					+ $"reliableFlight={networkDiagnostics.ReliableInFlight} queues={networkDiagnostics.ControlQueued}/{networkDiagnostics.GameplayQueued}/{networkDiagnostics.BulkQueued} "
					+ $"ackOnly={networkDiagnostics.AcknowledgementsSent} ackOnlyPerSecond={networkDiagnostics.AcknowledgementsPerSecond:F1} "
					+ $"retransmissions={networkDiagnostics.RetransmissionsSent} retransmissionsPerSecond={networkDiagnostics.RetransmissionsPerSecond:F1} retryFailures={networkDiagnostics.RetryFailures} "
					+ $"shadowsEnabled={shadowDiagnostics.Enabled} shadowCascades={shadowDiagnostics.CascadeCount} shadowRefreshed={shadowDiagnostics.RefreshedCascadeCount} "
					+ $"shadowDistance={shadowDiagnostics.EffectiveDistance:F0} shadowReasons={shadowDiagnostics.DirtyReasons} "
					+ $"shadowCasterChunks={shadowCasterDiagnostics.CasterChunkCount} shadowDraws={shadowCasterDiagnostics.DriverDrawCount} "
					+ $"shadowCommands={shadowCasterDiagnostics.OpaqueCommandCount + shadowCasterDiagnostics.CutoutCommandCount + shadowCasterDiagnostics.AlphaShadowCommandCount} "
					+ $"shadowLeafVertices={shadowCasterDiagnostics.AlphaShadowVertexCount} "
					+ $"shadowCullMs={shadowCasterDiagnostics.CullingMilliseconds:F3} shadowBuildMs={shadowCasterDiagnostics.CommandBuildMilliseconds:F3} "
					+ $"shadowSubmitMs={shadowCasterDiagnostics.SubmissionMilliseconds:F3} shadowAlloc={shadowCasterDiagnostics.ManagedAllocationBytes} "
					+ $"shadowGpuMs={SumShadowGpuMilliseconds(shadowDiagnostics):F3} "
					+ $"fogCells={fogDiagnostics.ActiveCells} fogOrigin={fogDiagnostics.Origin.X:F0}/{fogDiagnostics.Origin.Y:F0}/{fogDiagnostics.Origin.Z:F0} "
					+ $"fogQueues={fogDiagnostics.PendingBuilds}/{fogDiagnostics.PendingUploads} fogUploadedBytes={fogDiagnostics.UploadedBytes} "
					+ $"fogRebuildMs={fogDiagnostics.RebuildMilliseconds:F3} fogUploadMs={fogDiagnostics.UploadMilliseconds:F3} "
					+ $"fogMainAlloc={fogDiagnostics.MainThreadAllocatedBytes} fogWorkerAlloc={fogDiagnostics.WorkerAllocatedBytes} "
					+ $"fogStep={fogFrame?.StepLength ?? 0:F2} fogMaxSteps={fogFrame?.MaximumSteps ?? 0} "
					+ $"fogGpuMs={((IFishGfxGameWindow)_gameWindow).FogGpuMilliseconds:F3}"
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

	private readonly record struct ActorLightCache(
		int X,
		int Y,
		int Z,
		long LightingRevision,
		EntityLightSample Sample);

	private void UpdateFrameTimeline(
		in FrameTiming timing,
		in VoxelRendererFrameDiagnostics rendererDiagnostics)
	{
		if (!_rendererProfilingEnabled)
		{
			_frameTimeline.Reset();
			_framePercentiles = default;
			_pendingHitchLog = null;
			_hasProfilingPreviousCamera = false;
			_profilingGen0Collections = GC.CollectionCount(0);
			_profilingGen1Collections = GC.CollectionCount(1);
			_profilingGen2Collections = GC.CollectionCount(2);
			return;
		}

		_frameTimeline.Update(timing.TotalTime, timing.DeltaTime);
		_framePercentiles = _frameTimeline.Capture();
		if (_pendingHitchLog is not null && timing.TotalTime >= _nextHitchLogTime)
		{
			_logging?.Log(GameLogLevel.Debug, "PerformanceHitch", _pendingHitchLog);
			_pendingHitchLog = null;
			_nextHitchLogTime = timing.TotalTime + 0.5f;
		}

		float translation = _hasProfilingPreviousCamera
			? Vector3.Distance(_profilingPreviousCamera.Position, _fishCameraState.Position)
			: 0;
		float rotation = _hasProfilingPreviousCamera
			? CameraAngleDelta(_profilingPreviousCamera, _fishCameraState)
			: 0;
		_profilingPreviousCamera = _fishCameraState;
		_hasProfilingPreviousCamera = true;
		int gen0 = GC.CollectionCount(0);
		int gen1 = GC.CollectionCount(1);
		int gen2 = GC.CollectionCount(2);
		int gen0Delta = gen0 - _profilingGen0Collections;
		int gen1Delta = gen1 - _profilingGen1Collections;
		int gen2Delta = gen2 - _profilingGen2Collections;
		_profilingGen0Collections = gen0;
		_profilingGen1Collections = gen1;
		_profilingGen2Collections = gen2;

		if (timing.DeltaTime * 1000 <= 12 || _pendingHitchLog is not null)
		{
			return;
		}

		DirectionalShadowDiagnostics shadows =
			((IFishGfxGameWindow)_gameWindow).ShadowDiagnostics;
		FishGfxFogDiagnostics fog = _fishVoxelScene.FogVolume.Diagnostics;
		VoxelRendererWorkload workload = rendererDiagnostics.Workload;
		double presentMilliseconds = ((IFishGfxGameWindow)_gameWindow).LastPresentMilliseconds;
		_pendingHitchLog =
			$"frameMs={timing.DeltaTime * 1000:F3} cameraDelta={translation:F3}/{rotation:F2}deg "
			+ $"activeSet={rendererDiagnostics.ActiveSetRefreshReason}:{rendererDiagnostics.ActiveSetRefreshMilliseconds:F3}ms "
			+ $"transparent={rendererDiagnostics.TransparentInvalidationReason}:{rendererDiagnostics.TransparentWorkerSortMilliseconds:F3}ms "
			+ $"meshUpload={rendererDiagnostics.UploadedMeshes}:{rendererDiagnostics.MeshUploadMilliseconds:F3}ms/{rendererDiagnostics.MeshUploadBytes}B prepare={rendererDiagnostics.MeshUploadPreparationMilliseconds:F3}ms/{rendererDiagnostics.MeshUploadStorageGrowths}growth "
			+ $"meshWork={workload.DirtyMeshes}/{workload.InFlightMeshes}/{workload.CompletedMeshes}/{workload.PendingUploadJobs}:{workload.PendingUploadBytes}B paused={workload.IsBackpressured} "
			+ $"shadows={shadows.RefreshedCascadeCount}:{shadows.DirtyReasons} "
			+ $"fogBuild={fog.PendingBuilds}:{fog.RebuildMilliseconds:F3}ms fogUpload={fog.PendingUploads}:{fog.UploadMilliseconds:F3}ms "
			+ $"columns={WorldDecodeQueueDepth}/{WorldApplyQueueDepth}/{_fishVoxelScene.PendingPreparedColumnCount}queued deferredAcks={WorldDeferredAcknowledgements} streamPaused={WorldStreamingBackpressured} "
			+ $"present={presentMilliseconds:F3}ms gc={gen0Delta}/{gen1Delta}/{gen2Delta} "
			+ $"alloc={rendererDiagnostics.ManagedAllocatedBytes + fog.MainThreadAllocatedBytes}B";
	}

	private static float CameraAngleDelta(
		in GameCameraState previous,
		in GameCameraState current)
	{
		Vector3 previousForward = NormalizeOr(previous.Target - previous.Position, Vector3.UnitZ);
		Vector3 currentForward = NormalizeOr(current.Target - current.Position, Vector3.UnitZ);
		return MathF.Acos(Math.Clamp(
			Vector3.Dot(previousForward, currentForward),
			-1,
			1
		)) * (180f / MathF.PI);
	}

	private void UpdateNpcSpeechOverlay(float totalTime)
	{
		if (_npcSpeechOverlay is null || !_initialized || _simulation is null)
		{
			return;
		}

		List<NpcSpeechBubbleItem> visible = new();
		Vector3 cameraPosition = _fishCameraState.Position;
		Vector3 cameraForward = NormalizeOr(_fishCameraState.Target - cameraPosition, Vector3.UnitZ);
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
					_loadingProgressBar.Value = WorldLoadingProgress;
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

	internal static DirectionalShadowOptions CreateShadowOptions(
		SunShadowQuality quality,
		int maxChunkDrawDistance)
	{
		int drawDistance = Math.Clamp(
			maxChunkDrawDistance,
			GameConfig.MinimumMaxChunkDrawDistance,
			GameConfig.MaximumMaxChunkDrawDistance
		);
		return quality switch
		{
			SunShadowQuality.Off => new DirectionalShadowOptions(
				0, 1, 0, 0.65f, 0.1f, DirectionalShadowFilter.Pcf3x3, 0.75f, 1f),
			SunShadowQuality.Low => new DirectionalShadowOptions(
				2, 1024, Math.Min(64, drawDistance), 0.65f, 0.1f,
				DirectionalShadowFilter.Pcf3x3, 0.75f, 1f)
			{
				UpdateIntervals = new[] { 1, 4 },
			},
			SunShadowQuality.High => new DirectionalShadowOptions(
				4, 2048, Math.Min(256, drawDistance), 0.65f, 0.1f,
				DirectionalShadowFilter.Pcf5x5, 0.75f, 1f)
			{
				UpdateIntervals = new[] { 1, 1, 2, 4 },
			},
			_ => new DirectionalShadowOptions(
				3, 2048, Math.Min(128, drawDistance), 0.65f, 0.1f,
				DirectionalShadowFilter.Pcf3x3, 0.75f, 1f)
			{
				UpdateIntervals = new[] { 1, 2, 4 },
			},
		};
	}

	private static float SmoothStep(float edge0, float edge1, float value)
	{
		float amount = Math.Clamp((value - edge0) / (edge1 - edge0), 0, 1);
		return amount * amount * (3 - 2 * amount);
	}

	private static double SumShadowGpuMilliseconds(
		in DirectionalShadowDiagnostics diagnostics)
	{
		double total = 0;
		int count = diagnostics.Cascades?.Count ?? 0;

		for (int index = 0; index < count; index++)
		{
			total += diagnostics.Cascades[index].GpuMilliseconds;
		}

		return total;
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
