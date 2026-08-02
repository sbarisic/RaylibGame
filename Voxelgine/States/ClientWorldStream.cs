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

/// <summary>
/// Owns bounded client column decoding, application, acknowledgement, interest,
/// bootstrap readiness, and stream cancellation. Gameplay state owns the world
/// and render facade supplied to this controller.
/// </summary>
internal sealed partial class ClientWorldStream : IDisposable
{
	private const int LoadingColumnApplyLimit = 4;
	private const double LoadingColumnApplyBudgetMilliseconds = 4;
	private const int GameplayColumnApplyLimit = 2;
	private const double GameplayColumnApplyBudgetMilliseconds = 2;
	private const float InterestRefreshSeconds = 0.5f;
	private const float IntegrityCheckSeconds = 0.25f;
	private const float IntegrityRepairGraceSeconds = 0.75f;
	private const float IntegrityRepairRetrySeconds = 2f;

	private readonly IFishLogging logging;
	private readonly Func<NetClient> getClient;
	private readonly Func<GameSimulation> getSimulation;
	private readonly Func<FishGfxVoxelScene> getScene;
	private readonly Func<bool> getInitialized;
	private readonly Func<int> getDrawDistance;
	private readonly Func<float> getTime;
	private readonly Action<Exception> fail;
	private readonly ConcurrentQueue<DecodedWorldColumn> decodedColumns = new();
	private readonly HashSet<ChunkColumnCoordinate> receivedCoreColumns = new();
	private readonly HashSet<ChunkColumnCoordinate> receivedHaloColumns = new();
	private readonly HashSet<ChunkColumnCoordinate> appliedCoreColumns = new();
	private readonly HashSet<ChunkColumnCoordinate> appliedHaloColumns = new();
	private readonly HashSet<ChunkColumnCoordinate> receivedOrdinaryColumns = new();
	private readonly HashSet<ChunkColumnCoordinate> appliedOrdinaryColumns = new();
	private readonly HashSet<ChunkColumnCoordinate> requestedResyncColumns = new();
	private readonly Dictionary<ChunkColumnCoordinate, ChunkCoordinate[]> coreColumnChunks = new();
	private readonly Dictionary<ChunkColumnCoordinate, ChunkCoordinate[]> haloColumnChunks = new();
	private readonly HashSet<ChunkCoordinate> coreChunks = new();
	private readonly DeferredColumnAcknowledgements acknowledgements = new();

	private Channel<WorldColumnPacket> columnDecodeChannel;
	private CancellationTokenSource cancellation;
	private Task decodeTask;
	private int expectedCoreColumns;
	private int expectedHaloColumns;
	private bool bootstrapComplete;
	private bool clientReadySent;
	private float nextClientReadySendTime;
	private float nextReadinessLogTime;
	private Vector3 focus;
	private long decodeTicks;
	private long applyTicks;
	private int decodeCount;
	private int applyCount;
	private float nextInterestRefreshTime;
	private int lastInterestChunkX = int.MinValue;
	private int lastInterestChunkZ = int.MinValue;
	private int lastInterestRadius;
	private float nextIntegrityCheckTime;
	private float integritySuspectSince;
	private float nextIntegrityRepairTime;
	private ClientColumnIntegrityResult integritySuspect;
	private int generation;
	private bool disposed;

	internal ClientWorldStream(
		IFishLogging logging,
		Func<NetClient> getClient,
		Func<GameSimulation> getSimulation,
		Func<FishGfxVoxelScene> getScene,
		Func<bool> getInitialized,
		Func<int> getDrawDistance,
		Func<float> getTime,
		Action<Exception> fail)
	{
		this.logging = logging ?? throw new ArgumentNullException(nameof(logging));
		this.getClient = getClient ?? throw new ArgumentNullException(nameof(getClient));
		this.getSimulation = getSimulation ?? throw new ArgumentNullException(nameof(getSimulation));
		this.getScene = getScene ?? throw new ArgumentNullException(nameof(getScene));
		this.getInitialized = getInitialized ?? throw new ArgumentNullException(nameof(getInitialized));
		this.getDrawDistance = getDrawDistance ?? throw new ArgumentNullException(nameof(getDrawDistance));
		this.getTime = getTime ?? throw new ArgumentNullException(nameof(getTime));
		this.fail = fail ?? throw new ArgumentNullException(nameof(fail));
	}

	internal int StreamId { get; private set; }
	internal Vector3 Focus => focus;

	internal void Begin(WorldStreamBeginPacket packet)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		Cancel();

		StreamId = packet.StreamId;
		focus = packet.FocusPosition;
		expectedCoreColumns = packet.BootstrapCoreColumns;
		expectedHaloColumns = packet.BootstrapHaloColumns;
		bootstrapComplete = false;
		clientReadySent = false;
		nextReadinessLogTime = getTime() + 2;

		cancellation = new CancellationTokenSource();
		columnDecodeChannel = Channel.CreateBounded<WorldColumnPacket>(new BoundedChannelOptions(16)
		{
			SingleReader = true,
			SingleWriter = true,
			FullMode = BoundedChannelFullMode.Wait,
		});
		CancellationToken token = cancellation.Token;
		int streamGeneration = Interlocked.Increment(ref generation);
		decodeTask = Task.Run(
			() => DecodeColumnsAsync(columnDecodeChannel.Reader, streamGeneration, token),
			token);

		SendInterest(force: true);
		logging.Log(
			GameLogLevel.Info,
			"WorldStream",
			$"begin streamId={packet.StreamId} focus={packet.FocusPosition} core={packet.BootstrapCoreColumns} halo={packet.BootstrapHaloColumns} total={packet.TotalColumns}");
	}

	internal void ReceiveColumn(WorldColumnPacket packet)
	{
		if (packet.StreamId != StreamId || columnDecodeChannel == null)
			return;
		if (!acknowledgements.RegisterReceived(packet))
			return;

		ChunkColumnCoordinate coordinate = new(packet.X, packet.Z);
		if (requestedResyncColumns.Contains(coordinate))
		{
			logging.Log(
				GameLogLevel.Info,
				"WorldStream",
				$"resync-received streamId={packet.StreamId} column={packet.X},{packet.Z} revision={packet.Revision} bytes={packet.Payload.Length}");
		}
		switch (packet.Kind)
		{
			case WorldColumnStreamKind.BootstrapCore:
				receivedCoreColumns.Add(coordinate);
				break;
			case WorldColumnStreamKind.BootstrapHalo:
				receivedHaloColumns.Add(coordinate);
				break;
			default:
				receivedOrdinaryColumns.Add(coordinate);
				break;
		}

		if (!columnDecodeChannel.Writer.TryWrite(packet))
			fail(new InvalidOperationException("The bounded column decode queue overflowed."));
	}

	internal void CompleteBootstrap(int streamId)
	{
		if (streamId != StreamId)
			return;
		bootstrapComplete = true;
		logging.Log(GameLogLevel.Debug, "WorldStream", $"bootstrap-complete streamId={streamId}");
	}

	internal void Update()
	{
		GameSimulation simulation = getSimulation();
		if (simulation == null || StreamId == 0)
			return;

		bool initialized = getInitialized();
		int limit = initialized ? GameplayColumnApplyLimit : LoadingColumnApplyLimit;
		double budget = initialized ? GameplayColumnApplyBudgetMilliseconds : LoadingColumnApplyBudgetMilliseconds;
		long started = Stopwatch.GetTimestamp();
		int applied = 0;
		int processed = 0;
		while (processed < limit && decodedColumns.TryDequeue(out DecodedWorldColumn decoded))
		{
			processed++;
			if (decoded.Error != null)
			{
				acknowledgements.Forget(decoded.Packet.StreamId, decoded.Packet.X, decoded.Packet.Z, decoded.Packet.Revision);
				RequestResync(decoded.Packet, decoded.Error);
				if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= budget)
					break;
				continue;
			}

			PreparedClientColumn prepared = decoded.Prepared;
			IReadOnlyList<PreparedRenderChunk> renderChunks = prepared.RenderChunks;
			simulation.Map.CommitPreparedColumn(prepared.DomainColumn);
			TrackAppliedColumn(decoded.Packet, renderChunks);
			getScene().EnqueuePreparedColumn(prepared);
			applied++;
			Interlocked.Increment(ref applyCount);

			if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= budget)
				break;
		}
		if (applied != 0)
			Interlocked.Add(ref applyTicks, Stopwatch.GetTimestamp() - started);

		FlushAcknowledgements();
		if (!initialized)
			TrySendReady();
		else
		{
			SendInterest(force: false);
			RepairFocusedColumnIfNeeded(simulation);
		}
	}

	internal void MarkRenderColumnApplied(int x, int z, long revision)
	{
		acknowledgements.MarkReady(StreamId, x, z, revision);
		ChunkColumnCoordinate coordinate = new(x, z);
		if (requestedResyncColumns.Remove(coordinate))
		{
			logging.Log(
				GameLogLevel.Info,
				"WorldStream",
				$"resync-applied streamId={StreamId} column={x},{z} revision={revision}");
		}
	}

	internal void RequestFreshColumn(
		int columnX,
		int columnZ,
		long clientRevision,
		string trigger)
	{
		if (StreamId == 0 || getClient() == null)
			return;
		ChunkColumnCoordinate coordinate = new(columnX, columnZ);
		if (!requestedResyncColumns.Add(coordinate))
			return;

		int forgotten = acknowledgements.ForgetColumn(StreamId, columnX, columnZ);
		float now = getTime();
		logging.Log(
			GameLogLevel.Warning,
			"WorldStream",
			$"resync-request trigger={trigger} streamId={StreamId} column={columnX},{columnZ} clientRevision={clientRevision} forgottenAcks={forgotten}");
		getClient().Send(new WorldColumnResyncRequestPacket
		{
			StreamId = StreamId,
			X = columnX,
			Z = columnZ,
			Revision = clientRevision,
		}, true, now);
	}

	internal void SendInterest(bool force)
	{
		NetClient client = getClient();
		if (client == null || StreamId == 0)
			return;

		Vector3 interestFocus = getSimulation()?.LocalPlayer?.Position ?? focus;
		int chunkX = (int)Math.Floor((double)interestFocus.X / Chunk.ChunkSize);
		int chunkZ = (int)Math.Floor((double)interestFocus.Z / Chunk.ChunkSize);
		int radius = getDrawDistance() + 32;
		float now = getTime();
		if (!force && chunkX == lastInterestChunkX && chunkZ == lastInterestChunkZ
			&& radius == lastInterestRadius && now < nextInterestRefreshTime)
		{
			return;
		}

		lastInterestChunkX = chunkX;
		lastInterestChunkZ = chunkZ;
		lastInterestRadius = radius;
		nextInterestRefreshTime = now + InterestRefreshSeconds;
		client.Send(new ChunkInterestPacket
		{
			StreamId = StreamId,
			CenterX = (int)MathF.Floor(interestFocus.X),
			CenterZ = (int)MathF.Floor(interestFocus.Z),
			RadiusBlocks = radius,
		}, true, now);
	}

	internal void Cancel()
	{
		Interlocked.Increment(ref generation);
		Channel<WorldColumnPacket> channel = columnDecodeChannel;
		columnDecodeChannel = null;
		channel?.Writer.TryComplete();

		CancellationTokenSource currentCancellation = cancellation;
		cancellation = null;
		currentCancellation?.Cancel();
		Task currentTask = decodeTask;
		decodeTask = null;
		if (currentTask != null)
		{
			_ = currentTask.ContinueWith(
				static (completed, state) =>
				{
					_ = completed.Exception;
					((CancellationTokenSource)state)?.Dispose();
				},
				currentCancellation,
				CancellationToken.None,
				TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);
		}
		else
		{
			currentCancellation?.Dispose();
		}

		while (decodedColumns.TryDequeue(out DecodedWorldColumn decoded))
			decoded.Prepared?.Dispose();
		receivedCoreColumns.Clear();
		receivedHaloColumns.Clear();
		appliedCoreColumns.Clear();
		appliedHaloColumns.Clear();
		receivedOrdinaryColumns.Clear();
		appliedOrdinaryColumns.Clear();
		requestedResyncColumns.Clear();
		coreColumnChunks.Clear();
		haloColumnChunks.Clear();
		coreChunks.Clear();
		acknowledgements.Clear();
		StreamId = 0;
		expectedCoreColumns = 0;
		expectedHaloColumns = 0;
		bootstrapComplete = false;
		clientReadySent = false;
		nextClientReadySendTime = 0;
		nextReadinessLogTime = 0;
		lastInterestChunkX = int.MinValue;
		lastInterestChunkZ = int.MinValue;
		lastInterestRadius = 0;
		nextIntegrityCheckTime = 0;
		integritySuspectSince = 0;
		nextIntegrityRepairTime = 0;
		integritySuspect = default;
	}

	public void Dispose()
	{
		if (disposed)
			return;
		disposed = true;
		Cancel();
	}

	private async Task DecodeColumnsAsync(
		ChannelReader<WorldColumnPacket> reader,
		int streamGeneration,
		CancellationToken token)
	{
		await foreach (WorldColumnPacket packet in reader.ReadAllAsync(token))
		{
			if (streamGeneration != Volatile.Read(ref generation))
				return;
			long started = Stopwatch.GetTimestamp();
			try
			{
				uint checksum = WorldColumnCodec.ComputeChecksum(packet.Payload);
				if (checksum != packet.Checksum)
					throw new InvalidDataException($"Checksum mismatch for column ({packet.X}, {packet.Z}).");
				ChunkColumnSnapshot column = WorldColumnCodec.Decode(packet.X, packet.Z, packet.Revision, packet.Payload);
				if (streamGeneration != Volatile.Read(ref generation))
					return;
				FishGfxVoxelScene scene = getScene()
					?? throw new InvalidOperationException("The voxel scene is unavailable while preparing a streamed column.");
				PreparedClientColumn prepared = scene.PrepareStreamedColumn(column);
				if (streamGeneration != Volatile.Read(ref generation))
				{
					prepared.Dispose();
					return;
				}
				Interlocked.Add(ref decodeTicks, Stopwatch.GetTimestamp() - started);
				Interlocked.Increment(ref decodeCount);
				decodedColumns.Enqueue(new DecodedWorldColumn(packet, prepared, null));
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				decodedColumns.Enqueue(new DecodedWorldColumn(packet, null, exception));
			}
		}
	}

	private void TrackAppliedColumn(WorldColumnPacket packet, IReadOnlyList<PreparedRenderChunk> chunks)
	{
		ChunkColumnCoordinate coordinate = new(packet.X, packet.Z);
		switch (packet.Kind)
		{
			case WorldColumnStreamKind.BootstrapCore:
				appliedCoreColumns.Add(coordinate);
				ChunkCoordinate[] core = chunks.Select(static chunk => chunk.Coordinate).ToArray();
				coreColumnChunks[coordinate] = core;
				foreach (ChunkCoordinate chunk in core)
					coreChunks.Add(chunk);
				break;
			case WorldColumnStreamKind.BootstrapHalo:
				appliedHaloColumns.Add(coordinate);
				haloColumnChunks[coordinate] = chunks.Select(static chunk => chunk.Coordinate).ToArray();
				break;
			default:
				appliedOrdinaryColumns.Add(coordinate);
				break;
		}
	}

	private void TrySendReady()
	{
		FishGfxVoxelScene scene = getScene();
		if (!bootstrapComplete || CoreApplied < expectedCoreColumns || HaloApplied < expectedHaloColumns
			|| scene == null || !scene.IsLightingIdle || !scene.HasValidTransparentOrdering)
		{
			LogReadinessBlockers();
			return;
		}

		foreach (ChunkCoordinate coordinate in coreChunks)
		{
			VoxelPresentationState state = scene.GetPresentationState(coordinate);
			if (state is not (VoxelPresentationState.Resident or VoxelPresentationState.EmptyComplete))
			{
				LogReadinessBlockers();
				return;
			}
		}

		float now = getTime();
		if (clientReadySent && now < nextClientReadySendTime)
			return;
		bool firstSend = !clientReadySent;
		clientReadySent = true;
		nextClientReadySendTime = now + InterestRefreshSeconds;
		getClient().Send(new ClientWorldReadyPacket { StreamId = StreamId }, true, now);
		logging.Log(
			firstSend ? GameLogLevel.Info : GameLogLevel.Trace,
			"WorldStream",
			$"ready-{(firstSend ? "sent" : "retry")} streamId={StreamId} core={CoreApplied} halo={HaloApplied} chunks={coreChunks.Count}");
	}

	private void RequestResync(WorldColumnPacket packet, Exception exception)
	{
		ChunkColumnCoordinate coordinate = new(packet.X, packet.Z);
		receivedCoreColumns.Remove(coordinate);
		receivedHaloColumns.Remove(coordinate);
		requestedResyncColumns.Remove(coordinate);
		logging.Log(
			GameLogLevel.Warning,
			"WorldStream",
			$"column-decode-failed streamId={packet.StreamId} column={packet.X},{packet.Z} revision={packet.Revision}",
			exception);
		RequestFreshColumn(
			packet.X,
			packet.Z,
			packet.Revision,
			"decode-failed");
	}

	private void FlushAcknowledgements()
	{
		if (getScene()?.IsStreamingBackpressured == true)
			return;
		float now = getTime();
		for (int sent = 0; sent < 2 && acknowledgements.TryDequeueReady(out WorldColumnPacket packet); sent++)
		{
			if (packet.StreamId != StreamId)
				continue;
			getClient().Send(new WorldColumnAppliedPacket
			{
				StreamId = StreamId,
				X = packet.X,
				Z = packet.Z,
				Revision = packet.Revision,
			}, true, now);
		}
	}

	private void RepairFocusedColumnIfNeeded(GameSimulation simulation)
	{
		float now = getTime();
		if (now < nextIntegrityCheckTime)
			return;
		nextIntegrityCheckTime = now + IntegrityCheckSeconds;

		FishGfxVoxelScene scene = getScene();
		if (scene == null)
			return;

		Vector3 position = simulation.LocalPlayer.Position;
		ClientColumnIntegrityResult inspected = ClientColumnIntegrity.Inspect(
			simulation.Map,
			position,
			coordinate => scene.World.TryGetChunk(coordinate, out _));
		if (inspected.Problem == ClientColumnIntegrityProblem.MissingRenderChunk &&
			scene.PendingPreparedColumnCount != 0)
		{
			integritySuspect = default;
			integritySuspectSince = 0;
			return;
		}
		if (inspected.IsHealthy)
		{
			integritySuspect = default;
			integritySuspectSince = 0;
			return;
		}

		if (inspected != integritySuspect)
		{
			integritySuspect = inspected;
			integritySuspectSince = now;
			return;
		}

		if (now - integritySuspectSince < IntegrityRepairGraceSeconds ||
			now < nextIntegrityRepairTime)
		{
			return;
		}

		nextIntegrityRepairTime = now + IntegrityRepairRetrySeconds;
		long revision = simulation.Map.GetColumnRevision(
			inspected.Column.X,
			inspected.Column.Z);
		RequestFreshColumn(
			inspected.Column.X,
			inspected.Column.Z,
			revision,
			$"focus-{inspected.Problem}-chunkY-{inspected.ChunkY}-position-{position}");
	}

	private bool CalculateBackpressure()
	{
		FishGfxVoxelScene scene = getScene();
		return scene?.IsStreamingBackpressured == true
			|| (scene?.PendingPreparedColumnCount ?? 0) > 32
			|| (columnDecodeChannel?.Reader.Count ?? 0) > 12
			|| decodedColumns.Count > 32;
	}

	private sealed record DecodedWorldColumn(
		WorldColumnPacket Packet,
		PreparedClientColumn Prepared,
		Exception Error);
}
