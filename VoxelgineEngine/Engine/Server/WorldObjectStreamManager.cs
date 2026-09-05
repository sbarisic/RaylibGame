using Voxelgine.Engine.DI;
using Voxelgine.Graphics;

namespace Voxelgine.Engine.Server;

public sealed class WorldObjectStreamManager : IDisposable
{
	private readonly NetServer server;
	private readonly WorldStreamManager voxelStream;
	private readonly WorldObjectStore store;
	private readonly IFishLogging logging;
	private readonly Func<double> getCurrentTime;
	private ulong nextSnapshotId;

	public WorldObjectStreamManager(NetServer server, WorldStreamManager voxelStream, WorldObjectStore store, IFishLogging logging, Func<double> getCurrentTime)
	{
		this.server = server; this.voxelStream = voxelStream; this.store = store; this.logging = logging; this.getCurrentTime = getCurrentTime;
		store.ColumnDelta += OnColumnChanged;
	}

	public const int MaximumOutstandingSnapshots = 16;
	private readonly Dictionary<int, ClientWork> clients = new();
	public int GetOutstandingCount(int playerId) => clients.TryGetValue(playerId, out ClientWork work) ? work.Outstanding.Count : 0;

	public bool SendSnapshot(int playerId, int x, int z, double currentTime)
	{
		int streamId = voxelStream.GetStreamId(playerId);
		if (streamId == 0 || !voxelStream.IsApplied(playerId, x, z)) return false;
		ClientWork work = GetWork(playerId, streamId);
		var coordinate = (x, z);
		if (!work.Outstanding.ContainsKey(coordinate)) work.Pending.Add(coordinate);
		Pump(playerId, work, currentTime);
		return true;
	}

	public void HandleResync(int playerId, WorldObjectResyncRequestPacket packet, double currentTime)
	{
		if (voxelStream.GetStreamId(playerId) == packet.StreamId) SendSnapshot(playerId, packet.X, packet.Z, currentTime);
	}

	public void HandleApplied(int playerId, WorldObjectColumnAppliedPacket packet)
	{
		if (!clients.TryGetValue(playerId, out ClientWork work) || work.StreamId != packet.StreamId ||
			!work.Outstanding.TryGetValue((packet.X, packet.Z), out SnapshotWork snapshot)) return;
		WorldObjectColumnPacket basis = snapshot.Packets[0];
		if (basis.Epoch == packet.Epoch && basis.Revision == packet.Revision)
			work.Outstanding.Remove((packet.X, packet.Z));
	}

	public void Cancel(int playerId) => clients.Remove(playerId);
	public void Tick(double currentTime)
	{
		foreach ((int playerId, ClientWork work) in clients.ToArray())
		{
			if (voxelStream.GetStreamId(playerId) != work.StreamId) { Cancel(playerId); continue; }
			Pump(playerId, work, currentTime);
		}
	}

	private ClientWork GetWork(int playerId, int streamId)
	{
		if (!clients.TryGetValue(playerId, out ClientWork work) || work.StreamId != streamId)
			clients[playerId] = work = new ClientWork(streamId);
		return work;
	}

	private void Pump(int playerId, ClientWork work, double currentTime)
	{
		foreach (var coordinate in work.Pending.ToArray())
		{
			if (work.Outstanding.Count >= MaximumOutstandingSnapshots) break;
			if (work.Outstanding.ContainsKey(coordinate)) continue;
			work.Pending.Remove(coordinate);
			if (!voxelStream.IsApplied(playerId, coordinate.X, coordinate.Z)) continue;
			WorldObjectColumnState column = store.GetColumn(coordinate.X, coordinate.Z);
			work.Outstanding.Add(coordinate, new SnapshotWork(CreateSnapshotPackets(work.StreamId, column, ++nextSnapshotId), currentTime));
		}
		foreach (SnapshotWork snapshot in work.Outstanding.Values)
		{
			if (currentTime - snapshot.LastAttempt >= 2d && snapshot.NextPart == snapshot.Packets.Length)
			{
				snapshot.NextPart = 0; snapshot.LastAttempt = currentTime;
			}
			while (snapshot.NextPart < snapshot.Packets.Length)
			{
				if (!server.TrySendTo(playerId, snapshot.Packets[snapshot.NextPart], true, currentTime, ReliableSendClass.Bulk)) break;
				snapshot.NextPart++; snapshot.LastAttempt = currentTime;
			}
		}
	}

	private void OnColumnChanged(WorldObjectColumnState column, WorldObjectDeltaRecord delta)
	{
		byte[] payload = WorldObjectWireCodec.EncodeOperations(delta.Operations);
		foreach (NetConnection connection in server.GetConnections())
		{
			int playerId = connection.PlayerId;
			if (!connection.IsGameplayActive || !voxelStream.IsApplied(playerId, column.X, column.Z)) continue;
			ClientWork work = GetWork(playerId, voxelStream.GetStreamId(playerId));
			var coordinate = (column.X, column.Z);
			if (work.Outstanding.ContainsKey(coordinate) || work.Pending.Contains(coordinate))
			{
				work.Pending.Add(coordinate); continue;
			}
			if (!server.TrySendTo(playerId, new WorldObjectDeltaPacket
			{
				StreamId = work.StreamId, X = column.X, Z = column.Z,
				Epoch = column.Epoch, BaseRevision = delta.BaseRevision, Revision = delta.Revision,
				OperationCount = checked((ushort)delta.Operations.Count), Payload = payload,
			}, true, getCurrentTime(), ReliableSendClass.Gameplay)) work.Pending.Add(coordinate);
		}
	}

	private sealed class ClientWork(int streamId)
	{
		public int StreamId { get; } = streamId;
		public HashSet<(int X, int Z)> Pending { get; } = new();
		public Dictionary<(int X, int Z), SnapshotWork> Outstanding { get; } = new();
	}
	private sealed class SnapshotWork(WorldObjectColumnPacket[] packets, double currentTime)
	{
		public WorldObjectColumnPacket[] Packets { get; } = packets;
		public int NextPart;
		public double LastAttempt = currentTime;
	}

	internal static WorldObjectColumnPacket[] CreateSnapshotPackets(int streamId, WorldObjectColumnState column, ulong snapshotId)
	{
		if (column.Records.Count > WorldObjectStore.MaximumColumnRecords) throw new InvalidOperationException("World-object column exceeds the record limit.");
		List<(byte[] Payload, ushort Count)> parts = new();
		for (int index = 0; index < column.Records.Count;)
		{
			int count = Math.Min(WorldObjectColumnPacket.MaximumRecordsPerPart, column.Records.Count - index);
			byte[] payload;
			while (true)
			{
				payload = WorldObjectWireCodec.EncodeRecords(column.Records.Skip(index).Take(count).ToArray());
				if (payload.Length <= WorldObjectColumnPacket.MaximumPartBytes) break;
				if (--count == 0) throw new InvalidOperationException("A world-object record exceeds the part byte limit.");
			}
			parts.Add((payload, checked((ushort)count))); index += count;
		}
		if (parts.Count == 0) parts.Add((Array.Empty<byte>(), 0));
		if (parts.Count > WorldObjectColumnPacket.MaximumParts) throw new InvalidOperationException("World-object snapshot exceeds the part count limit.");
		byte[] full = parts.SelectMany(static part => part.Payload).ToArray();
		if (full.Length > 4 * 1024 * 1024) throw new InvalidOperationException("World-object snapshot exceeds the decoded byte limit.");
		uint checksum = WorldColumnCodec.ComputeChecksum(full);
		return parts.Select((part, index) => new WorldObjectColumnPacket
		{
			StreamId=streamId, X=column.X, Z=column.Z, Epoch=column.Epoch, Revision=column.Revision, SnapshotId=snapshotId,
			PartIndex=checked((ushort)index), PartCount=checked((ushort)parts.Count), TotalRecordCount=column.Records.Count,
			TotalDecodedBytes=full.Length, FullPayloadChecksum=checksum, PartRecordCount=part.Count, Payload=part.Payload,
		}).ToArray();
	}

	public void Dispose() { store.ColumnDelta -= OnColumnChanged; clients.Clear(); }
}
