using System.Numerics;
using Voxelgine.Engine.DI;
using Voxelgine.Graphics;

namespace Voxelgine.Engine.Server;

/// <summary>
/// Server-side spatial world streamer. It owns per-client bootstrap/interest state,
/// caches archive-compatible column payloads, and treats application acknowledgements
/// as the flow-control boundary.
/// </summary>
public sealed class WorldStreamManager
{
	public const int BootstrapRadiusBlocks = 32;
	public const int BootstrapHaloBlocks = Chunk.ChunkSize;
	public const int MaximumUnappliedColumns = 16;

	private readonly NetServer server;
	private readonly ChunkMap map;
	private readonly IFishLogging logging;
	private readonly Dictionary<int, ClientStream> streams = new();
	private readonly Dictionary<EncodedColumnKey, EncodedColumn> encodedCache = new();
	private WorldArchivePayloadCache archivePayloads;
	private int nextStreamId;

	public WorldStreamManager(NetServer server, ChunkMap map, IFishLogging logging)
	{
		this.server = server ?? throw new ArgumentNullException(nameof(server));
		this.map = map ?? throw new ArgumentNullException(nameof(map));
		this.logging = logging;
	}

	public event Action<int> ClientReady;

	public int StreamedColumnCount { get; private set; }
	public int CachedColumnCount => encodedCache.Count;
	public int StreamStallCount { get; private set; }
	public int GetOutstandingColumnCount(int playerId) =>
		streams.TryGetValue(playerId, out ClientStream stream) ? stream.Outstanding.Count : 0;

	public void SetArchivePayloadCache(WorldArchivePayloadCache payloadCache)
	{
		archivePayloads = payloadCache;
		encodedCache.Clear();
	}

	public void Begin(int playerId, Vector3 focusPosition, int worldSeed, float currentTime)
	{
		Cancel(playerId);
		int streamId = unchecked(++nextStreamId);
		if (streamId == 0)
			streamId = unchecked(++nextStreamId);

		List<ColumnWork> core = SelectColumns(focusPosition, BootstrapRadiusBlocks, WorldColumnStreamKind.BootstrapCore);
		List<ColumnWork> halo = SelectColumns(
			focusPosition,
			BootstrapRadiusBlocks + BootstrapHaloBlocks,
			WorldColumnStreamKind.BootstrapHalo,
			core.Select(static item => item.Coordinate).ToHashSet());

		ClientStream stream = new(streamId, focusPosition, core.Count, halo.Count);
		foreach (ColumnWork item in core)
		{
			stream.Bootstrap.Enqueue(item);
			stream.BootstrapKinds[item.Coordinate] = item.Kind;
		}
		foreach (ColumnWork item in halo)
		{
			stream.Bootstrap.Enqueue(item);
			stream.BootstrapKinds[item.Coordinate] = item.Kind;
		}
		streams.Add(playerId, stream);

		server.TrySendTo(
			playerId,
			new WorldStreamBeginPacket
			{
				StreamId = streamId,
				FocusPosition = focusPosition,
				WorldSeed = worldSeed,
				TotalColumns = map.ColumnCount,
				BootstrapCoreColumns = core.Count,
				BootstrapHaloColumns = halo.Count,
			},
			true,
			currentTime,
			ReliableSendClass.Control);

		logging?.Log(
			GameLogLevel.Info,
			"WorldStream",
			$"begin playerId={playerId} streamId={streamId} focus={focusPosition} core={core.Count} halo={halo.Count} total={map.ColumnCount}");
	}

	public void Tick(float currentTime)
	{
		foreach ((int playerId, ClientStream stream) in streams)
		{
			while (stream.Outstanding.Count < MaximumUnappliedColumns)
			{
				if (!TryGetNext(stream, out ColumnWork work))
					break;

				EncodedColumn encoded = GetEncoded(work.Coordinate);
				WorldColumnPacket packet = new()
				{
					StreamId = stream.StreamId,
					X = work.Coordinate.X,
					Z = work.Coordinate.Z,
					Revision = encoded.Revision,
					Kind = work.Kind,
					Checksum = encoded.Checksum,
					Payload = encoded.Payload,
				};

				if (!server.TrySendTo(playerId, packet, true, currentTime, ReliableSendClass.Bulk))
				{
					ReturnToFront(stream, work);
					StreamStallCount++;
					if (StreamStallCount == 1 || StreamStallCount % 64 == 0)
					{
						logging?.Log(
							GameLogLevel.Debug,
							"WorldStream",
							$"bulk-queue-saturated playerId={playerId} streamId={stream.StreamId} stalls={StreamStallCount}");
					}
					break;
				}

				stream.Outstanding[work.Coordinate] = new OutstandingColumn(encoded.Revision, work.Kind);
				StreamedColumnCount++;
				logging?.Log(
					GameLogLevel.Trace,
					"WorldStream",
					$"queued playerId={playerId} streamId={stream.StreamId} column={work.Coordinate.X},{work.Coordinate.Z} revision={encoded.Revision} kind={work.Kind} bytes={encoded.Payload.Length} outstanding={stream.Outstanding.Count}");
			}

			if (!stream.BootstrapCompleteSent &&
				stream.Bootstrap.Count == 0 &&
				stream.Outstanding.Values.All(static item => item.Kind == WorldColumnStreamKind.Ordinary))
			{
				stream.BootstrapCompleteSent = server.TrySendTo(
					playerId,
					new WorldBootstrapCompletePacket { StreamId = stream.StreamId },
					true,
					currentTime,
					ReliableSendClass.Control);
				if (stream.BootstrapCompleteSent)
					logging?.Log(GameLogLevel.Debug, "WorldStream", $"bootstrap-sent playerId={playerId} streamId={stream.StreamId}");
			}
		}
	}

	public bool HandleApplied(int playerId, WorldColumnAppliedPacket packet)
	{
		if (!TryGetMatchingStream(playerId, packet.StreamId, out ClientStream stream))
			return false;

		ChunkColumnCoordinate coordinate = new(packet.X, packet.Z);
		if (!stream.Outstanding.Remove(coordinate, out OutstandingColumn outstanding))
		{
			logging?.Log(GameLogLevel.Warning, "WorldStream", $"unexpected-apply playerId={playerId} streamId={packet.StreamId} column={packet.X},{packet.Z} revision={packet.Revision}");
			QueueResync(stream, coordinate, GetKnownKind(stream, coordinate));
			return false;
		}

		if (outstanding.Revision != packet.Revision)
		{
			logging?.Log(GameLogLevel.Warning, "WorldStream", $"revision-race playerId={playerId} column={packet.X},{packet.Z} expected={outstanding.Revision} applied={packet.Revision}");
			QueueResync(stream, coordinate, outstanding.Kind);
			return false;
		}

		long currentRevision = map.GetColumnRevision(packet.X, packet.Z);
		if (currentRevision != packet.Revision)
		{
			logging?.Log(
				GameLogLevel.Warning,
				"WorldStream",
				$"acknowledgement-race playerId={playerId} column={packet.X},{packet.Z} applied={packet.Revision} current={currentRevision}");
			QueueResync(stream, coordinate, outstanding.Kind);
			return false;
		}

		stream.Applied[coordinate] = packet.Revision;
		return true;
	}

	public void HandleResyncRequest(int playerId, WorldColumnResyncRequestPacket packet)
	{
		if (!TryGetMatchingStream(playerId, packet.StreamId, out ClientStream stream))
			return;

		ChunkColumnCoordinate coordinate = new(packet.X, packet.Z);
		WorldColumnStreamKind kind = stream.Outstanding.Remove(coordinate, out OutstandingColumn outstanding)
			? outstanding.Kind
			: GetKnownKind(stream, coordinate);
		QueueResync(stream, coordinate, kind);
		logging?.Log(GameLogLevel.Warning, "WorldStream", $"resync playerId={playerId} streamId={packet.StreamId} column={packet.X},{packet.Z} clientRevision={packet.Revision}");
	}

	public void RequestFreshSnapshot(int playerId, int worldX, int worldZ)
	{
		if (!streams.TryGetValue(playerId, out ClientStream stream))
			return;
		ChunkColumnCoordinate coordinate = new(
			(int)Math.Floor((double)worldX / Chunk.ChunkSize),
			(int)Math.Floor((double)worldZ / Chunk.ChunkSize));
		stream.Outstanding.Remove(coordinate);
		QueueResync(stream, coordinate, WorldColumnStreamKind.Ordinary);
	}

	public void HandleInterest(int playerId, ChunkInterestPacket packet)
	{
		if (!TryGetMatchingStream(playerId, packet.StreamId, out ClientStream stream))
			return;

		int radius = Math.Clamp(packet.RadiusBlocks, BootstrapRadiusBlocks, 4096);
		Vector3 focus = new(packet.CenterX, stream.FocusPosition.Y, packet.CenterZ);
		stream.FocusPosition = focus;
		HashSet<ChunkColumnCoordinate> alreadyQueued = stream.Applied.Keys
			.Concat(stream.Outstanding.Keys)
			.Concat(stream.Bootstrap.Select(static item => item.Coordinate))
			.Concat(stream.Ordinary.Select(static item => item.Coordinate))
			.ToHashSet();

		foreach (ColumnWork work in SelectColumns(focus, radius, WorldColumnStreamKind.Ordinary, alreadyQueued))
			stream.Ordinary.Enqueue(work);
	}

	public bool HandleReady(int playerId, ClientWorldReadyPacket packet)
	{
		if (!TryGetMatchingStream(playerId, packet.StreamId, out ClientStream stream) ||
			stream.ReadyAccepted ||
			!stream.BootstrapCompleteSent ||
			stream.BootstrapKinds.Keys.Any(coordinate => !stream.Applied.ContainsKey(coordinate)) ||
			stream.Outstanding.Values.Any(static item => item.Kind != WorldColumnStreamKind.Ordinary))
		{
			return false;
		}

		stream.ReadyAccepted = true;
		logging?.Log(GameLogLevel.Info, "WorldStream", $"ready playerId={playerId} streamId={packet.StreamId} applied={stream.Applied.Count}");
		ClientReady?.Invoke(playerId);
		return true;
	}

	public bool IsApplied(int playerId, int columnX, int columnZ)
	{
		return streams.TryGetValue(playerId, out ClientStream stream) &&
			stream.Applied.ContainsKey(new ChunkColumnCoordinate(columnX, columnZ));
	}

	public int GetStreamId(int playerId) => streams.TryGetValue(playerId, out ClientStream stream) ? stream.StreamId : 0;

	public void RecordBlockChange(int playerId, int columnX, int columnZ, long revision)
	{
		if (streams.TryGetValue(playerId, out ClientStream stream))
			stream.Applied[new ChunkColumnCoordinate(columnX, columnZ)] = revision;
	}

	public void Cancel(int playerId) => streams.Remove(playerId);

	private EncodedColumn GetEncoded(ChunkColumnCoordinate coordinate)
	{
		long revision = map.GetColumnRevision(coordinate.X, coordinate.Z);
		EncodedColumnKey key = new(coordinate, revision);
		if (encodedCache.TryGetValue(key, out EncodedColumn encoded))
			return encoded;
		if (archivePayloads?.TryGet(
			coordinate.X,
			coordinate.Z,
			revision,
			out byte[] archivedPayload,
			out uint archivedChecksum) == true)
		{
			encoded = new EncodedColumn(revision, archivedPayload, archivedChecksum);
			encodedCache[key] = encoded;
			return encoded;
		}

		ChunkColumnSnapshot snapshot = map.CaptureColumn(coordinate.X, coordinate.Z);
		byte[] payload = WorldColumnCodec.Encode(snapshot);
		encoded = new EncodedColumn(revision, payload, WorldColumnCodec.ComputeChecksum(payload));
		encodedCache[key] = encoded;
		return encoded;
	}

	private List<ColumnWork> SelectColumns(
		Vector3 focus,
		int radiusBlocks,
		WorldColumnStreamKind kind,
		HashSet<ChunkColumnCoordinate> excluded = null)
	{
		float radiusSquared = radiusBlocks * radiusBlocks;
		List<ColumnWork> selected = new();
		foreach (ChunkColumnCoordinate coordinate in map.GetColumnCoordinates())
		{
			if (excluded?.Contains(coordinate) == true)
				continue;
			float minX = coordinate.X * Chunk.ChunkSize;
			float minZ = coordinate.Z * Chunk.ChunkSize;
			float nearestX = Math.Clamp(focus.X, minX, minX + Chunk.ChunkSize);
			float nearestZ = Math.Clamp(focus.Z, minZ, minZ + Chunk.ChunkSize);
			float distanceSquared = Vector2.DistanceSquared(
				new Vector2(focus.X, focus.Z),
				new Vector2(nearestX, nearestZ));
			if (distanceSquared <= radiusSquared)
				selected.Add(new ColumnWork(coordinate, kind, distanceSquared));
		}
		selected.Sort(static (left, right) => left.DistanceSquared.CompareTo(right.DistanceSquared));
		return selected;
	}

	private static bool TryGetNext(ClientStream stream, out ColumnWork work)
	{
		if (stream.Bootstrap.TryDequeue(out work))
			return true;
		if (stream.ReadyAccepted && stream.Ordinary.TryDequeue(out work))
			return true;
		work = default;
		return false;
	}

	private static void ReturnToFront(ClientStream stream, ColumnWork work)
	{
		Queue<ColumnWork> source = work.Kind == WorldColumnStreamKind.Ordinary ? stream.Ordinary : stream.Bootstrap;
		Queue<ColumnWork> replacement = new();
		replacement.Enqueue(work);
		while (source.TryDequeue(out ColumnWork queued))
			replacement.Enqueue(queued);
		if (work.Kind == WorldColumnStreamKind.Ordinary)
			stream.Ordinary = replacement;
		else
			stream.Bootstrap = replacement;
	}

	private static void QueueResync(ClientStream stream, ChunkColumnCoordinate coordinate, WorldColumnStreamKind kind)
	{
		if (stream.Outstanding.ContainsKey(coordinate))
			return;
		stream.Applied.Remove(coordinate);
		stream.Bootstrap.Enqueue(new ColumnWork(coordinate, kind, 0));
	}

	private static WorldColumnStreamKind GetKnownKind(
		ClientStream stream,
		ChunkColumnCoordinate coordinate) =>
		stream.BootstrapKinds.TryGetValue(coordinate, out WorldColumnStreamKind kind)
			? kind
			: WorldColumnStreamKind.Ordinary;

	private bool TryGetMatchingStream(int playerId, int streamId, out ClientStream stream)
	{
		return streams.TryGetValue(playerId, out stream) && stream.StreamId == streamId;
	}

	private sealed class ClientStream
	{
		public ClientStream(int streamId, Vector3 focusPosition, int coreCount, int haloCount)
		{
			StreamId = streamId;
			FocusPosition = focusPosition;
			CoreCount = coreCount;
			HaloCount = haloCount;
		}

		public int StreamId { get; }
		public Vector3 FocusPosition { get; set; }
		public int CoreCount { get; }
		public int HaloCount { get; }
		public Queue<ColumnWork> Bootstrap { get; set; } = new();
		public Queue<ColumnWork> Ordinary { get; set; } = new();
		public Dictionary<ChunkColumnCoordinate, OutstandingColumn> Outstanding { get; } = new();
		public Dictionary<ChunkColumnCoordinate, long> Applied { get; } = new();
		public Dictionary<ChunkColumnCoordinate, WorldColumnStreamKind> BootstrapKinds { get; } = new();
		public bool BootstrapCompleteSent { get; set; }
		public bool ReadyAccepted { get; set; }
	}

	private readonly record struct ColumnWork(
		ChunkColumnCoordinate Coordinate,
		WorldColumnStreamKind Kind,
		float DistanceSquared);
	private readonly record struct OutstandingColumn(long Revision, WorldColumnStreamKind Kind);
	private readonly record struct EncodedColumn(long Revision, byte[] Payload, uint Checksum);
	private readonly record struct EncodedColumnKey(ChunkColumnCoordinate Coordinate, long Revision);
}
