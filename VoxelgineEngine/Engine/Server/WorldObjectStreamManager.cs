using Voxelgine.Engine.DI;
using Voxelgine.Graphics;

namespace Voxelgine.Engine.Server;

public sealed class WorldObjectStreamManager : IDisposable
{
	private readonly NetServer server;
	private readonly WorldStreamManager voxelStream;
	private readonly WorldObjectStore store;
	private readonly IFishLogging logging;
	private readonly Func<float> getCurrentTime;
	private ulong nextSnapshotId;

	public WorldObjectStreamManager(NetServer server, WorldStreamManager voxelStream, WorldObjectStore store, IFishLogging logging, Func<float> getCurrentTime)
	{
		this.server = server; this.voxelStream = voxelStream; this.store = store; this.logging = logging; this.getCurrentTime = getCurrentTime;
		store.ColumnChanged += OnColumnChanged;
	}

	public bool SendSnapshot(int playerId, int x, int z, float currentTime)
	{
		int streamId = voxelStream.GetStreamId(playerId);
		if (streamId == 0 || !voxelStream.IsApplied(playerId, x, z)) return false;
		WorldObjectColumnState column = store.GetColumn(x, z);
		WorldObjectColumnPacket[] packets = CreateSnapshotPackets(streamId, column, ++nextSnapshotId);
		foreach (WorldObjectColumnPacket packet in packets)
			if (!server.TrySendTo(playerId, packet, true, currentTime, ReliableSendClass.Bulk)) return false;
		return true;
	}

	public void HandleResync(int playerId, WorldObjectResyncRequestPacket packet, float currentTime)
	{
		if (voxelStream.GetStreamId(playerId) == packet.StreamId) SendSnapshot(playerId, packet.X, packet.Z, currentTime);
	}

	private void OnColumnChanged(WorldObjectColumnState column, WorldObjectDeltaRecord delta)
	{
		byte[] payload = WorldObjectWireCodec.EncodeOperations(delta.Operations);
		if (payload.Length > 1024 * 1024) throw new InvalidOperationException("World-object delta exceeds the wire limit.");
		foreach (NetConnection connection in server.GetConnections())
		{
			if (!connection.IsGameplayActive || !voxelStream.IsApplied(connection.PlayerId, column.X, column.Z)) continue;
			server.TrySendTo(connection.PlayerId, new WorldObjectDeltaPacket
			{
				StreamId = voxelStream.GetStreamId(connection.PlayerId), X = column.X, Z = column.Z,
				Epoch = column.Epoch, BaseRevision = delta.BaseRevision, Revision = delta.Revision,
				OperationCount = checked((ushort)delta.Operations.Count), Payload = payload,
			}, true, getCurrentTime(), ReliableSendClass.Gameplay);
		}
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

	public void Dispose() => store.ColumnChanged -= OnColumnChanged;
}
