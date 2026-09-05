using System.Net;
using System.Numerics;
using System.Reflection;
using Voxelgine.Engine;
using Voxelgine.Engine.Server;
using Voxelgine.Engine.World.Structures;
using Voxelgine.Graphics;

namespace VoxelgineEngine.Tests;

public sealed class ObjectStreamRecoveryTests
{
	[Fact]
	public void FailedFirstPartRetainsWorkAndLostApplicationAckRetries()
	{
		using Fixture fixture = new(600);
		fixture.FillBulk(128);
		Assert.True(fixture.Objects.SendSnapshot(1,0,0,0)); Assert.Equal(1,fixture.Objects.GetOutstandingCount(1));
		Assert.Equal(0,fixture.NextPart());
		fixture.ClearQueues(); fixture.Objects.Tick(.1); Assert.Equal(3,fixture.NextPart());
		fixture.ClearQueues(); fixture.Objects.Tick(2.2); Assert.Equal(3,fixture.NextPart()); Assert.True(fixture.Connection.ReliableQueueCount>0);
		fixture.Acknowledge(); Assert.Equal(0,fixture.Objects.GetOutstandingCount(1));
	}

	[Fact]
	public void PartialEnqueueResumesAndChangesWaitBehindSnapshot()
	{
		using Fixture fixture = new(600); fixture.FillBulk(116);
		fixture.Objects.SendSnapshot(1,0,0,0); Assert.InRange(fixture.NextPart(),1,2);
		fixture.ClearQueues(); fixture.Objects.Tick(.1); Assert.Equal(3,fixture.NextPart());
		fixture.Store.TryGet(PersistentWorldObjectKey.Placed(new PersistentWorldObjectId(1)),out var plant);
		fixture.Store.ApplyTransaction(new[] { new WorldObjectOperation(WorldObjectOperationKind.Upsert,plant with {GrowthProgress=547},default) });
		fixture.Acknowledge(); Assert.Equal(0,fixture.Objects.GetOutstandingCount(1));
		fixture.Objects.Tick(.2); Assert.Equal(1,fixture.Objects.GetOutstandingCount(1));
		Assert.Equal(2,fixture.Snapshot()[0].Revision);
	}

	[Fact]
	public void ResyncCoalescesAndOutstandingWorkIsBoundedAndCancelledOnNewStream()
	{
		using Fixture fixture = new(0);
		for (int x=0;x<40;x++) { fixture.Voxels.RecordBlockChange(1,x,0,1); fixture.Objects.SendSnapshot(1,x,0,0); }
		Assert.Equal(16,fixture.Objects.GetOutstandingCount(1));
		var original=fixture.Snapshot()[0];
		for(int i=0;i<50;i++) fixture.Objects.HandleResync(1,new WorldObjectResyncRequestPacket { StreamId=original.StreamId,X=0,Z=0 },.1);
		Assert.Equal(original.SnapshotId,fixture.Snapshot()[0].SnapshotId);
		fixture.Voxels.Begin(1,Vector3.Zero,1,1); fixture.Objects.Tick(1); Assert.Equal(0,fixture.Objects.GetOutstandingCount(1));
		fixture.Objects.Cancel(1); Assert.Equal(0,fixture.Objects.GetOutstandingCount(1));
	}

	[Fact]
	public void FailedDeltaEnqueueSchedulesSnapshot()
	{
		using Fixture fixture = new(1);
		for(int i=0;i<256;i++) fixture.Connection.QueuePacket(new ChatMessagePacket {Message="fill"},true,0);
		fixture.Store.TryGet(PersistentWorldObjectKey.Placed(new PersistentWorldObjectId(1)),out var plant);
		fixture.Store.ApplyTransaction(new[] { new WorldObjectOperation(WorldObjectOperationKind.Upsert,plant with {GrowthProgress=547},default) });
		fixture.Objects.Tick(0); Assert.Equal(1,fixture.Objects.GetOutstandingCount(1)); Assert.Equal(2,fixture.Snapshot()[0].Revision);
	}

	private sealed class Fixture : IDisposable
	{
		public NetServer Server {get;}=new(); public NetConnection Connection {get;}=new(new(IPAddress.Loopback,9000),0){PlayerId=1,State=ConnectionState.Connected,IsGameplayActive=true};
		public WorldObjectStore Store {get;}=new(); public WorldStreamManager Voxels {get;} public WorldObjectStreamManager Objects {get;}
		public Fixture(int count)
		{
			((Dictionary<int,NetConnection>)Field(Server,"_playerConnections")).Add(1,Connection);
			((Dictionary<IPEndPoint,NetConnection>)Field(Server,"_connections")).Add(Connection.RemoteEndPoint,Connection);
			Voxels=new(Server,new ChunkMap(),null); Voxels.Begin(1,Vector3.Zero,1,0); Voxels.RecordBlockChange(1,0,0,1); ClearQueues();
			Store.Restore(Enumerable.Range(0,count).Select(i=>new WorldPlantRecord(PersistentWorldObjectKey.Placed(new PersistentWorldObjectId((ulong)i+1)),WorldPlantType.Wheat,0,255,ItemIds.Wheat,new(i%16,i/256,(i/16)%16))));
			Objects=new(Server,Voxels,Store,null,()=>0);
		}
		private object Work()
		{
			var clients=(System.Collections.IDictionary)Field(Objects,"clients"); var client=clients[1]!;
			var outstanding=(System.Collections.IDictionary)client.GetType().GetProperty("Outstanding")!.GetValue(client)!; return outstanding[(0,0)]!;
		}
		public int NextPart() => (int)Field(Work(),"NextPart");
		public WorldObjectColumnPacket[] Snapshot() => (WorldObjectColumnPacket[])Work().GetType().GetProperty("Packets")!.GetValue(Work())!;
		public void Acknowledge() { var basis=Snapshot()[0]; Objects.HandleApplied(1,new WorldObjectColumnAppliedPacket {StreamId=basis.StreamId,X=0,Z=0,Epoch=basis.Epoch,Revision=basis.Revision}); }
		public void ClearQueues(){Connection.Reset();Connection.IsGameplayActive=true;}
		public void FillBulk(int count){for(int i=0;i<count;i++)Assert.True(Connection.QueuePacket(new ChatMessagePacket{Message="fill"},true,0,ReliableSendClass.Bulk));}
		public void Dispose(){Objects.Dispose();Server.Dispose();}
		private static object Field(object target,string name) => target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Public|BindingFlags.Instance)!.GetValue(target)!;
	}
}
