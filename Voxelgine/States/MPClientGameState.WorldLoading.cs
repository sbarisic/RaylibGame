using System.Collections.Concurrent;
using System.Diagnostics;
using System.Numerics;
using System.Threading.Channels;
using FishGfx.Voxels;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.FishGfxClient.Voxels;
using Voxelgine.Graphics;

namespace Voxelgine.States;

public partial class MPClientGameState
{
	private const int LoadingColumnApplyLimit = 4;
	private const double LoadingColumnApplyBudgetMilliseconds = 4;
	private const int GameplayColumnApplyLimit = 2;
	private const double GameplayColumnApplyBudgetMilliseconds = 2;
	private const float InterestRefreshSeconds = 0.5f;

	private readonly ConcurrentQueue<DecodedWorldColumn> _decodedColumns = new();
	private readonly HashSet<ChunkColumnCoordinate> _receivedCoreColumns = new();
	private readonly HashSet<ChunkColumnCoordinate> _receivedHaloColumns = new();
	private readonly HashSet<ChunkColumnCoordinate> _appliedCoreColumns = new();
	private readonly HashSet<ChunkColumnCoordinate> _appliedHaloColumns = new();
	private readonly HashSet<ChunkColumnCoordinate> _receivedOrdinaryColumns = new();
	private readonly HashSet<ChunkColumnCoordinate> _appliedOrdinaryColumns = new();
	private readonly Dictionary<ChunkColumnCoordinate, ChunkCoordinate[]> _coreColumnChunks = new();
	private readonly Dictionary<ChunkColumnCoordinate, ChunkCoordinate[]> _haloColumnChunks = new();
	private readonly HashSet<ChunkCoordinate> _coreChunks = new();
	private readonly DeferredColumnAcknowledgements _columnAcknowledgements = new();
	private Channel<WorldColumnPacket> _columnDecodeChannel;
	private CancellationTokenSource _worldLoadCancellation;
	private Task _worldLoadTask;
	private int _worldStreamId;
	private int _expectedCoreColumns;
	private int _expectedHaloColumns;
	private bool _bootstrapComplete;
	private bool _clientReadySent;
	private float _nextClientReadySendTime;
	private float _nextWorldReadinessLogTime;
	private Vector3 _worldStreamFocus;
	private long _columnDecodeTicks;
	private long _columnApplyTicks;
	private int _columnDecodeCount;
	private int _columnApplyCount;
	private int _maximumDecodeQueueDepth;
	private float _nextInterestRefreshTime;
	private int _lastInterestChunkX = int.MinValue;
	private int _lastInterestChunkZ = int.MinValue;
	private int _lastInterestRadius;

	internal int WorldStreamId => _worldStreamId;
	internal int WorldDecodeQueueDepth => _columnDecodeChannel?.Reader.Count ?? 0;
	internal int WorldApplyQueueDepth => _decodedColumns.Count;
	internal int WorldDeferredAcknowledgements => _columnAcknowledgements.Count;
	internal bool WorldStreamingBackpressured => IsWorldStreamingBackpressured();
	internal float WorldLoadingProgress => CalculateWorldLoadingProgress();
	internal int WorldCoreReceived => _receivedCoreColumns.Count;
	internal int WorldCoreApplied => _appliedCoreColumns.Count;
	internal int WorldHaloReceived => _receivedHaloColumns.Count;
	internal int WorldHaloApplied => _appliedHaloColumns.Count;
	internal int WorldOrdinaryReceived => _receivedOrdinaryColumns.Count;
	internal int WorldOrdinaryApplied => _appliedOrdinaryColumns.Count;
	internal int WorldCachedColumns => _simulation?.Map.ColumnCount ?? 0;
	internal int WorldCoreLit => CountColumnsInState(_coreColumnChunks, requireMesh: false);
	internal int WorldCoreMeshed => CountColumnsInState(_coreColumnChunks, requireMesh: true);
	internal int WorldHaloLit => CountColumnsInState(_haloColumnChunks, requireMesh: false);
	internal int WorldHaloMeshed => CountColumnsInState(_haloColumnChunks, requireMesh: true);
	internal double AverageColumnDecodeMilliseconds => _columnDecodeCount == 0
		? 0
		: _columnDecodeTicks * 1000.0 / Stopwatch.Frequency / _columnDecodeCount;
	internal double AverageColumnApplyMilliseconds => _columnApplyCount == 0
		? 0
		: _columnApplyTicks * 1000.0 / Stopwatch.Frequency / _columnApplyCount;

	private void BeginWorldStream(WorldStreamBeginPacket packet)
	{
		CancelWorldLoad();
		DisposeFishGfxVoxelScene();
		_simulation?.LocalPlayer?.Dispose();

		_worldStreamId = packet.StreamId;
		_worldStreamFocus = packet.FocusPosition;
		_expectedCoreColumns = packet.BootstrapCoreColumns;
		_expectedHaloColumns = packet.BootstrapHaloColumns;
		_bootstrapComplete = false;
		_clientReadySent = false;
		_nextWorldReadinessLogTime = GetClientTime() + 2;
		_errorText = string.Empty;
		_statusText = "Receiving bootstrap columns";

		_simulation = new GameSimulation(Eng);
		_simulation.DayNight.IsAuthority = false;
		_simulation.Entities.IsAuthority = false;
		_simulation.Map.UnknownColumnsAreBoundaries = true;
		CreateFishGfxVoxelScene(synchronizeExisting: false);

		_worldLoadCancellation = new CancellationTokenSource();
		_columnDecodeChannel = Channel.CreateBounded<WorldColumnPacket>(new BoundedChannelOptions(16)
		{
			SingleReader = true,
			SingleWriter = true,
			FullMode = BoundedChannelFullMode.Wait,
		});
		CancellationToken cancellationToken = _worldLoadCancellation.Token;
		_worldLoadTask = Task.Run(() => DecodeColumnsAsync(_columnDecodeChannel.Reader, cancellationToken), cancellationToken);

		SendChunkInterest(force: true);
		_logging.Log(
			GameLogLevel.Info,
			"WorldStream",
			$"begin streamId={packet.StreamId} focus={packet.FocusPosition} core={packet.BootstrapCoreColumns} halo={packet.BootstrapHaloColumns} total={packet.TotalColumns}");
	}

	private void ReceiveWorldColumn(WorldColumnPacket packet)
	{
		if (packet.StreamId != _worldStreamId || _columnDecodeChannel == null)
			return;
		if (!_columnAcknowledgements.RegisterReceived(packet))
			return;

		ChunkColumnCoordinate coordinate = new(packet.X, packet.Z);
		switch (packet.Kind)
		{
			case WorldColumnStreamKind.BootstrapCore:
				_receivedCoreColumns.Add(coordinate);
				break;
			case WorldColumnStreamKind.BootstrapHalo:
				_receivedHaloColumns.Add(coordinate);
				break;
			default:
				_receivedOrdinaryColumns.Add(coordinate);
				break;
		}

		if (!_columnDecodeChannel.Writer.TryWrite(packet))
		{
			FailWorldLoad(new InvalidOperationException("The bounded column decode queue overflowed."));
			return;
		}
		_maximumDecodeQueueDepth = Math.Max(_maximumDecodeQueueDepth, _columnDecodeChannel.Reader.Count);
	}

	private async Task DecodeColumnsAsync(
		ChannelReader<WorldColumnPacket> reader,
		CancellationToken cancellationToken)
	{
		await foreach (WorldColumnPacket packet in reader.ReadAllAsync(cancellationToken))
		{
			long started = Stopwatch.GetTimestamp();
			try
			{
				uint checksum = WorldColumnCodec.ComputeChecksum(packet.Payload);
				if (checksum != packet.Checksum)
					throw new InvalidDataException($"Checksum mismatch for column ({packet.X}, {packet.Z}).");
				ChunkColumnSnapshot column = WorldColumnCodec.Decode(
					packet.X,
					packet.Z,
					packet.Revision,
					packet.Payload);
				FishGfxVoxelScene scene = _fishVoxelScene
					?? throw new InvalidOperationException("The voxel scene is unavailable while preparing a streamed column.");
				PreparedClientColumn prepared = scene.PrepareStreamedColumn(column);
				Interlocked.Add(ref _columnDecodeTicks, Stopwatch.GetTimestamp() - started);
				Interlocked.Increment(ref _columnDecodeCount);
				_decodedColumns.Enqueue(new DecodedWorldColumn(packet, prepared, null));
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				_decodedColumns.Enqueue(new DecodedWorldColumn(packet, null, exception));
			}
		}
	}

	private void UpdateWorldStream()
	{
		if (_simulation == null || _worldStreamId == 0)
			return;

		int limit = _initialized ? GameplayColumnApplyLimit : LoadingColumnApplyLimit;
		double budget = _initialized
			? GameplayColumnApplyBudgetMilliseconds
			: LoadingColumnApplyBudgetMilliseconds;
		long started = Stopwatch.GetTimestamp();
		int applied = 0;
		int processed = 0;
		while (processed < limit && _decodedColumns.TryDequeue(out DecodedWorldColumn decoded))
		{
			processed++;
			if (decoded.Error != null)
			{
				_columnAcknowledgements.Forget(
					decoded.Packet.StreamId,
					decoded.Packet.X,
					decoded.Packet.Z,
					decoded.Packet.Revision
				);
				RequestColumnResync(decoded.Packet, decoded.Error);
				if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= budget)
					break;
				continue;
			}

			PreparedClientColumn prepared = decoded.Prepared;
			IReadOnlyList<PreparedRenderChunk> renderChunks = prepared.RenderChunks;
			_simulation.Map.CommitPreparedColumn(prepared.DomainColumn);
			TrackAppliedColumn(decoded.Packet, renderChunks);
			_fishVoxelScene.EnqueuePreparedColumn(prepared);
			applied++;
			Interlocked.Increment(ref _columnApplyCount);

			if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= budget)
				break;
		}
		if (applied != 0)
			Interlocked.Add(ref _columnApplyTicks, Stopwatch.GetTimestamp() - started);

		FlushColumnAcknowledgements();

		if (!_initialized)
			TrySendWorldReady();
		else
			SendChunkInterest(force: false);

		_statusText = GetWorldLoadStatus();
	}

	private void TrackAppliedColumn(
		WorldColumnPacket packet,
		IReadOnlyList<PreparedRenderChunk> chunks)
	{
		ChunkColumnCoordinate coordinate = new(packet.X, packet.Z);
		switch (packet.Kind)
		{
			case WorldColumnStreamKind.BootstrapCore:
				_appliedCoreColumns.Add(coordinate);
				ChunkCoordinate[] coreChunks = chunks
					.Select(static chunk => chunk.Coordinate)
					.ToArray();
				_coreColumnChunks[coordinate] = coreChunks;
				foreach (ChunkCoordinate chunk in coreChunks)
					_coreChunks.Add(chunk);
				break;
			case WorldColumnStreamKind.BootstrapHalo:
				_appliedHaloColumns.Add(coordinate);
				_haloColumnChunks[coordinate] = chunks
					.Select(static chunk => chunk.Coordinate)
					.ToArray();
				break;
			default:
				_appliedOrdinaryColumns.Add(coordinate);
				break;
		}
	}

	private int CountColumnsInState(
		IReadOnlyDictionary<ChunkColumnCoordinate, ChunkCoordinate[]> columns,
		bool requireMesh)
	{
		if (_fishVoxelScene == null)
			return 0;

		int complete = 0;
		foreach (ChunkCoordinate[] chunks in columns.Values)
		{
			bool columnComplete = true;
			foreach (ChunkCoordinate chunk in chunks)
			{
				VoxelPresentationState state = _fishVoxelScene.GetPresentationState(chunk);
				columnComplete &= requireMesh
					? state is VoxelPresentationState.Resident or VoxelPresentationState.EmptyComplete
					: state is VoxelPresentationState.Meshing or VoxelPresentationState.Resident or VoxelPresentationState.EmptyComplete;
				if (!columnComplete)
					break;
			}
			if (columnComplete)
				complete++;
		}
		return complete;
	}

	private void TrySendWorldReady()
	{
		if (!_bootstrapComplete ||
			_appliedCoreColumns.Count < _expectedCoreColumns ||
			_appliedHaloColumns.Count < _expectedHaloColumns ||
			_fishVoxelScene == null ||
			!_fishVoxelScene.IsLightingIdle ||
			!_fishVoxelScene.HasValidTransparentOrdering)
		{
			LogWorldReadinessBlockers();
			return;
		}

		foreach (ChunkCoordinate coordinate in _coreChunks)
		{
			VoxelPresentationState state = _fishVoxelScene.GetPresentationState(coordinate);
			if (state is not (VoxelPresentationState.Resident or VoxelPresentationState.EmptyComplete))
			{
				LogWorldReadinessBlockers();
				return;
			}
		}

		float now = GetClientTime();
		if (_clientReadySent && now < _nextClientReadySendTime)
			return;

		bool firstSend = !_clientReadySent;
		_clientReadySent = true;
		_nextClientReadySendTime = now + InterestRefreshSeconds;
		_client.Send(new ClientWorldReadyPacket { StreamId = _worldStreamId }, true, now);
		_statusText = "Waiting for server start";
		_logging.Log(
			firstSend ? GameLogLevel.Info : GameLogLevel.Trace,
			"WorldStream",
			$"ready-{(firstSend ? "sent" : "retry")} streamId={_worldStreamId} core={_appliedCoreColumns.Count} halo={_appliedHaloColumns.Count} chunks={_coreChunks.Count}");
	}

	private void LogWorldReadinessBlockers()
	{
		float now = GetClientTime();
		if (now < _nextWorldReadinessLogTime)
			return;
		_nextWorldReadinessLogTime = now + 2;

		int missing = 0;
		int waitingForLighting = 0;
		int meshing = 0;
		int resident = 0;
		int emptyComplete = 0;
		if (_fishVoxelScene != null)
		{
			foreach (ChunkCoordinate coordinate in _coreChunks)
			{
				switch (_fishVoxelScene.GetPresentationState(coordinate))
				{
					case VoxelPresentationState.Missing:
						missing++;
						break;
					case VoxelPresentationState.WaitingForLighting:
						waitingForLighting++;
						break;
					case VoxelPresentationState.Meshing:
						meshing++;
						break;
					case VoxelPresentationState.Resident:
						resident++;
						break;
					case VoxelPresentationState.EmptyComplete:
						emptyComplete++;
						break;
				}
			}
		}

		VoxelRendererWorkload workload = _fishVoxelScene?.Workload ?? default;
		VoxelRendererFrameDiagnostics renderer = _fishVoxelScene?.FrameDiagnostics ?? default;
		_logging.Log(
			GameLogLevel.Debug,
			"WorldStream",
			$"bootstrap-wait streamId={_worldStreamId} complete={_bootstrapComplete} "
			+ $"core={_appliedCoreColumns.Count}/{_expectedCoreColumns} "
			+ $"halo={_appliedHaloColumns.Count}/{_expectedHaloColumns} "
			+ $"lightingIdle={_fishVoxelScene?.IsLightingIdle == true} "
			+ $"transparentReady={_fishVoxelScene?.HasValidTransparentOrdering == true} "
			+ $"states=missing:{missing},lighting:{waitingForLighting},meshing:{meshing},resident:{resident},empty:{emptyComplete} "
			+ $"work=dirty:{workload.DirtyMeshes},running:{workload.InFlightMeshes},completed:{workload.CompletedMeshes},uploads:{workload.PendingUploadJobs},bytes:{workload.PendingUploadBytes},backpressured:{workload.IsBackpressured} "
			+ $"transparent=pending:{renderer.TransparentOrderingPending},running:{renderer.TransparentOrderingRunning},faces:{renderer.TransparentFaceCount},indices:{renderer.TransparentIndexCount},uploadBytes:{renderer.TransparentUploadBytes},stale:{renderer.TransparentStaleResults},reason:{renderer.TransparentInvalidationReason}");
	}

	private void FinishWorldStart(ClientWorldStartPacket packet)
	{
		if (packet.StreamId != _worldStreamId || _simulation == null || _initialized)
			return;

		CreateGameplayUI();
		_snd = CreateAudioSink();
		ClientPlayer player = new(Eng, _gui, _playerName, true, _snd, _client.PlayerId);
		_simulation.Players.AddLocalPlayer(_client.PlayerId, player);
		player.InitGUI(_gameWindow, _gui);
		player.Init(_simulation.Map);
		player.Health = packet.Health;
		player.ApplyPhysicsState(packet.PhysicsState);

		ReplayPendingWorldPackets();
		_client.FinishLoading();
		_initialized = true;
		_statusText = string.Empty;
		_errorText = string.Empty;
		ApplyInputOwnership();
		SendChunkInterest(force: true);
		_logging.Log(GameLogLevel.Info, "WorldStream", $"started streamId={packet.StreamId} serverTick={packet.ServerTick} position={player.Position}");
	}

	private void ReplayPendingWorldPackets()
	{
		if (_pendingWorldPackets.Count == 0)
			return;
		Packet[] packets = _pendingWorldPackets.ToArray();
		_pendingWorldPackets.Clear();
		_replayingPendingWorldPackets = true;
		try
		{
			foreach (Packet packet in packets)
				OnPacketReceived(packet);
		}
		finally
		{
			_replayingPendingWorldPackets = false;
		}
	}

	private void RequestColumnResync(WorldColumnPacket packet, Exception exception)
	{
		ChunkColumnCoordinate coordinate = new(packet.X, packet.Z);
		_receivedCoreColumns.Remove(coordinate);
		_receivedHaloColumns.Remove(coordinate);
		_logging.Log(
			GameLogLevel.Warning,
			"WorldStream",
			$"column-decode-failed streamId={packet.StreamId} column={packet.X},{packet.Z} revision={packet.Revision}",
			exception);
		_client.Send(new WorldColumnResyncRequestPacket
		{
			StreamId = packet.StreamId,
			X = packet.X,
			Z = packet.Z,
			Revision = packet.Revision,
		}, true, GetClientTime());
	}

	private void SendChunkInterest(bool force)
	{
		if (_client == null || _worldStreamId == 0)
			return;
		if (!force && IsWorldStreamingBackpressured())
			return;
		Vector3 focus = _simulation?.LocalPlayer?.Position ?? _worldStreamFocus;
		int chunkX = (int)Math.Floor((double)focus.X / Chunk.ChunkSize);
		int chunkZ = (int)Math.Floor((double)focus.Z / Chunk.ChunkSize);
		int radius = Eng.DI.GetRequiredService<GameConfig>().MaxChunkDrawDistance + 32;
		float now = GetClientTime();
		if (!force && chunkX == _lastInterestChunkX && chunkZ == _lastInterestChunkZ &&
			radius == _lastInterestRadius && now < _nextInterestRefreshTime)
		{
			return;
		}

		_lastInterestChunkX = chunkX;
		_lastInterestChunkZ = chunkZ;
		_lastInterestRadius = radius;
		_nextInterestRefreshTime = now + InterestRefreshSeconds;
		_client.Send(new ChunkInterestPacket
		{
			StreamId = _worldStreamId,
			CenterX = (int)MathF.Floor(focus.X),
			CenterZ = (int)MathF.Floor(focus.Z),
			RadiusBlocks = radius,
		}, true, now);
	}

	private string GetWorldLoadStatus()
	{
		if (!string.IsNullOrEmpty(_errorText))
			return string.Empty;
		if (_worldStreamId == 0)
			return _client?.State == ClientState.Connecting ? "Connecting" : "Starting hosted server";
		if (_receivedCoreColumns.Count < _expectedCoreColumns || _receivedHaloColumns.Count < _expectedHaloColumns)
			return $"Receiving bootstrap columns ({_receivedCoreColumns.Count + _receivedHaloColumns.Count}/{_expectedCoreColumns + _expectedHaloColumns})";
		if (_appliedCoreColumns.Count < _expectedCoreColumns || _appliedHaloColumns.Count < _expectedHaloColumns)
			return $"Applying terrain ({_appliedCoreColumns.Count + _appliedHaloColumns.Count}/{_expectedCoreColumns + _expectedHaloColumns})";
		if (_fishVoxelScene?.IsLightingIdle != true)
			return "Computing nearby lighting";
		if (!_clientReadySent)
			return "Uploading nearby meshes";
		return "Waiting for server start";
	}

	private float CalculateWorldLoadingProgress()
	{
		int expected = _expectedCoreColumns + _expectedHaloColumns;
		if (expected <= 0)
			return 0;
		float received = Math.Clamp(
			(float)(_receivedCoreColumns.Count + _receivedHaloColumns.Count) / expected,
			0,
			1);
		float applied = Math.Clamp(
			(float)(_appliedCoreColumns.Count + _appliedHaloColumns.Count) / expected,
			0,
			1);
		return _clientReadySent ? 0.95f : received * 0.4f + applied * 0.5f;
	}

	private void FailWorldLoad(Exception exception)
	{
		_logging.Log(GameLogLevel.Error, "WorldStream", "Client world streaming failed.", exception);
		_statusText = string.Empty;
		_errorText = $"Failed to load world: {exception.Message}";
		if (_client?.IsConnected == true)
			_client.Disconnect("Failed to stream world", GetClientTime());
	}

	private void CancelWorldLoad()
	{
		Channel<WorldColumnPacket> channel = _columnDecodeChannel;
		_columnDecodeChannel = null;
		channel?.Writer.TryComplete();
		CancellationTokenSource cancellation = _worldLoadCancellation;
		_worldLoadCancellation = null;
		cancellation?.Cancel();
		Task task = _worldLoadTask;
		_worldLoadTask = null;
		if (task != null)
		{
			_ = task.ContinueWith(
				static (completed, state) =>
				{
					_ = completed.Exception;
					((CancellationTokenSource)state)?.Dispose();
				},
				cancellation,
				CancellationToken.None,
				TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);
		}
		else
		{
			cancellation?.Dispose();
		}

		while (_decodedColumns.TryDequeue(out DecodedWorldColumn decoded))
			decoded.Prepared?.Dispose();
		_receivedCoreColumns.Clear();
		_receivedHaloColumns.Clear();
		_appliedCoreColumns.Clear();
		_appliedHaloColumns.Clear();
		_receivedOrdinaryColumns.Clear();
		_appliedOrdinaryColumns.Clear();
		_coreColumnChunks.Clear();
		_haloColumnChunks.Clear();
		_coreChunks.Clear();
		_columnAcknowledgements.Clear();
		_worldStreamId = 0;
		_expectedCoreColumns = 0;
		_expectedHaloColumns = 0;
		_bootstrapComplete = false;
		_clientReadySent = false;
		_nextClientReadySendTime = 0;
		_nextWorldReadinessLogTime = 0;
		_lastInterestChunkX = int.MinValue;
		_lastInterestChunkZ = int.MinValue;
		_lastInterestRadius = 0;
	}

	private void OnPreparedRenderColumnApplied(int x, int z, long revision)
	{
		_columnAcknowledgements.MarkReady(_worldStreamId, x, z, revision);
	}

	private void FlushColumnAcknowledgements()
	{
		if (_fishVoxelScene?.IsStreamingBackpressured == true)
			return;
		float now = GetClientTime();
		for (int sent = 0; sent < 2
			&& _columnAcknowledgements.TryDequeueReady(out WorldColumnPacket packet);
			sent++)
		{
			if (packet.StreamId != _worldStreamId)
				continue;
			_client.Send(new WorldColumnAppliedPacket
			{
				StreamId = _worldStreamId,
				X = packet.X,
				Z = packet.Z,
				Revision = packet.Revision,
			}, true, now);
		}
	}

	private bool IsWorldStreamingBackpressured()
	{
		return _fishVoxelScene?.IsStreamingBackpressured == true
			|| (_fishVoxelScene?.PendingPreparedColumnCount ?? 0) > 32
			|| (_columnDecodeChannel?.Reader.Count ?? 0) > 12
			|| _decodedColumns.Count > 32;
	}

	private sealed record DecodedWorldColumn(
		WorldColumnPacket Packet,
		PreparedClientColumn Prepared,
		Exception Error);
}
