using Voxelgine.Engine;
using Voxelgine.Graphics;
using Voxelgine.Engine.World.Structures;

namespace Voxelgine.States;

public unsafe partial class MPClientGameState
{
	private readonly Dictionary<ObjectAssemblyKey, ObjectAssembly> _objectAssemblies = new();

	private void HandleWorldObjectColumn(WorldObjectColumnPacket packet, float currentTime)
	{
		if (_simulation == null || packet.StreamId != WorldStreamId) return;
		ObjectAssemblyKey key = new(packet.StreamId, packet.X, packet.Z, packet.Epoch, packet.Revision, packet.SnapshotId);
		if (!_objectAssemblies.TryGetValue(key, out ObjectAssembly assembly))
		{
			if (_objectAssemblies.Count >= 64)
			{
				ObjectAssemblyKey oldest = _objectAssemblies.MinBy(static pair => pair.Value.CreatedAt).Key;
				_objectAssemblies.Remove(oldest);
			}
			_objectAssemblies[key] = assembly = new ObjectAssembly(packet, currentTime);
		}
		if (!assembly.TryAdd(packet)) { RequestWorldObjectResync(packet.X, packet.Z, packet.Epoch, packet.Revision); _objectAssemblies.Remove(key); return; }
		if (!assembly.IsComplete) return;
		_objectAssemblies.Remove(key);
		try
		{
			byte[] payload = assembly.Combine();
			if (payload.Length != packet.TotalDecodedBytes || WorldColumnCodec.ComputeChecksum(payload) != packet.FullPayloadChecksum)
				throw new InvalidDataException("World-object snapshot checksum mismatch.");
			WorldPlantRecord[] records = WorldObjectWireCodec.DecodeRecords(payload, packet.TotalRecordCount);
			_simulation.WorldObjects.InstallColumnSnapshot(packet.X, packet.Z, packet.Epoch, packet.Revision, records);
			_client.Send(new WorldObjectColumnAppliedPacket { StreamId=packet.StreamId, X=packet.X, Z=packet.Z, Epoch=packet.Epoch, Revision=packet.Revision }, true, GetClientTime());
		}
		catch (Exception exception) when (exception is InvalidDataException or ArgumentException)
		{
			_logging.Log(Voxelgine.Engine.DI.GameLogLevel.Warning, "WorldObjects", $"snapshot-rejected column={packet.X},{packet.Z}", exception);
			RequestWorldObjectResync(packet.X, packet.Z, packet.Epoch, packet.Revision);
		}
	}

	private void HandleWorldObjectDelta(WorldObjectDeltaPacket packet)
	{
		if (_simulation == null || packet.StreamId != WorldStreamId) return;
		try
		{
			WorldObjectOperation[] operations = WorldObjectWireCodec.DecodeOperations(packet.Payload, packet.OperationCount);
			if (_simulation.WorldObjects.TryApplyReplicatedDelta(packet.X, packet.Z, packet.Epoch, packet.BaseRevision, packet.Revision, operations)) return;
		}
		catch (InvalidDataException) { }
		RequestWorldObjectResync(packet.X, packet.Z, packet.Epoch, packet.Revision);
	}

	private void RequestWorldObjectResync(int x, int z, ulong epoch, long revision)
	{
		if (_client == null || WorldStreamId == 0) return;
		_client.Send(new WorldObjectResyncRequestPacket { StreamId=WorldStreamId, X=x, Z=z, Epoch=epoch, Revision=revision }, true, GetClientTime());
	}

	private void ExpireWorldObjectAssemblies(float currentTime)
	{
		foreach ((ObjectAssemblyKey key, ObjectAssembly assembly) in _objectAssemblies.ToArray())
			if (currentTime - assembly.CreatedAt >= 10f) { _objectAssemblies.Remove(key); RequestWorldObjectResync(key.X, key.Z, key.Epoch, key.Revision); }
	}

	private void TryRequestWorldObjectInteraction()
	{
		if (_simulation?.LocalPlayer == null || _client == null) return;
		System.Numerics.Vector3 origin = _simulation.LocalPlayer.Position;
		System.Numerics.Vector3 direction = _simulation.LocalPlayer.GetForward();
		RaycastHit entityHit = _simulation.Entities.Raycast(origin, direction, 20);
		if (entityHit.Hit && entityHit.Entity is VEntItemBasket basket)
		{
			BlockCoordinate anchor = basket.CaptureRecord().Anchor;
			_client.Send(new WorldInteractRequestPacket { X=anchor.X, Y=anchor.Y, Z=anchor.Z }, true, GetClientTime());
			return;
		}
		if(entityHit.Hit&&entityHit.Entity is VEntBed bed)
		{
			BlockCoordinate anchor=bed.Anchor;_client.Send(new WorldInteractRequestPacket{X=anchor.X,Y=anchor.Y,Z=anchor.Z},true,GetClientTime());return;
		}
		WorldPlantRecord? best = null; float bestDistance = 20f;
		foreach (WorldPlantRecord plant in _simulation.WorldObjects.GetAll())
		{
			System.Numerics.Vector3 center = new(plant.Position.X + 0.5f, plant.Position.Y + 0.5f, plant.Position.Z + 0.5f);
			float along = System.Numerics.Vector3.Dot(center - origin, direction);
			if (along < 0 || along >= bestDistance) continue;
			System.Numerics.Vector3 closest = origin + direction * along;
			if (System.Numerics.Vector3.DistanceSquared(closest, center) > 0.5f) continue;
			best=plant; bestDistance=along;
		}
		if (best is WorldPlantRecord selected)
			_client.Send(new WorldInteractRequestPacket { X=selected.Position.X, Y=selected.Position.Y, Z=selected.Position.Z }, true, GetClientTime());
	}

	private readonly record struct ObjectAssemblyKey(int StreamId, int X, int Z, ulong Epoch, long Revision, ulong SnapshotId);
	private sealed class ObjectAssembly
	{
		private readonly byte[][] parts;
		private readonly ushort[] recordCounts;
		private readonly WorldObjectColumnPacket basis;
		public ObjectAssembly(WorldObjectColumnPacket packet, float createdAt) { basis=packet; CreatedAt=createdAt; parts=new byte[packet.PartCount][]; recordCounts=new ushort[packet.PartCount]; }
		public float CreatedAt { get; }
		public bool IsComplete => parts.All(static part => part != null);
		public bool TryAdd(WorldObjectColumnPacket packet)
		{
			if (packet.PartCount!=basis.PartCount || packet.TotalRecordCount!=basis.TotalRecordCount || packet.TotalDecodedBytes!=basis.TotalDecodedBytes || packet.FullPayloadChecksum!=basis.FullPayloadChecksum) return false;
			byte[] existing=parts[packet.PartIndex];
			if(existing!=null) return existing.SequenceEqual(packet.Payload) && recordCounts[packet.PartIndex]==packet.PartRecordCount;
			if(parts.Sum(static part=>part?.Length??0)+packet.Payload.Length>4*1024*1024) return false;
			parts[packet.PartIndex]=packet.Payload; recordCounts[packet.PartIndex]=packet.PartRecordCount; return true;
		}
		public byte[] Combine()
		{
			if(recordCounts.Sum(static count=>(int)count)!=basis.TotalRecordCount) throw new InvalidDataException("World-object part record count mismatch.");
			return parts.SelectMany(static part=>part).ToArray();
		}
	}
}
