using System.Net;
using System.Numerics;
using System.Reflection;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.Engine.Server;
using Voxelgine.Engine.World.Structures;
using Voxelgine.Graphics;

namespace VoxelgineEngine.Tests;

public sealed class ServerTransactionRegressionTests
{
	[Fact]
	public void LoopbackPlayerRequiresAdministratorForGlobalCommandsAndDebugMutation()
	{
		using Harness harness = new(); ServerClientSession player = harness.AddPlayer(1);
		var night = new ConsoleCommandRequestPacket {RequestId=1,CommandName="night"};
		var place = new DebugPlaceBlockRequestPacket {X=10,Y=10,Z=10,BlockType=(ushort)BlockType.Stone};
		harness.Call("OnPacketReceived",player.Connection,night); harness.Call("OnPacketReceived",player.Connection,place);
		Assert.Equal(8,harness.Loop.Simulation.DayNight.TimeOfDay); Assert.Equal(BlockType.None,harness.Loop.Simulation.Map.GetBlock(10,10,10));
		harness.Loop.ExecuteCommand("op "+player.Connection.Identity.Fingerprint);harness.Call("ProcessCommands");
		harness.Call("OnPacketReceived",player.Connection,night);harness.Call("OnPacketReceived",player.Connection,place);
		Assert.Equal(0,harness.Loop.Simulation.DayNight.TimeOfDay);Assert.Equal(BlockType.Stone,harness.Loop.Simulation.Map.GetBlock(10,10,10));
		harness.Loop.ExecuteCommand("deop "+player.Connection.Identity.Fingerprint);harness.Call("ProcessCommands");
		harness.Call("OnPacketReceived",player.Connection,new ConsoleCommandRequestPacket {RequestId=2,CommandName="day"});Assert.Equal(0,harness.Loop.Simulation.DayNight.TimeOfDay);
	}

	[Theory]
	[InlineData("kind")]
	[InlineData("channel")]
	[InlineData("input")]
	[InlineData("ahead")]
	[InlineData("expired")]
	[InlineData("dead")]
	[InlineData("item")]
	public void RejectedPlacementAlwaysCompletesItsAction(string cause)
	{
		using Harness harness=new();ServerClientSession player=harness.AddPlayer(1);harness.Loop.Simulation.Map.SetBlock(0,0,0,BlockType.WetFarmland);
		if(cause!="item")player.Inventory.Grant(ItemIds.WheatSeeds,1);
		player.CommandHistory.Record(new(1,0,new(.5f,3,.5f),-Vector3.UnitY,false,cause!="input"));
		if(cause=="expired")player.CommandHistory.Record(new(130,0,Vector3.Zero,Vector3.UnitY,false,false));
		if(cause=="dead")player.Player.Health=0;
		harness.Call("EnqueueItemUse",player.Connection,new WorldObjectPlaceRequestPacket{ActionId=1,CommandTick=cause=="ahead"?129:1,Channel=cause=="channel"?ItemUseChannel.Primary:ItemUseChannel.Secondary,X=0,Y=0,Z=0,PlantType=cause=="kind"?(byte)255:(byte)1});
		harness.Call("ProcessPendingItemUses");Assert.True(player.ItemUseActionHistory.TryGet(1,out var result));Assert.False(result.Accepted);Assert.Equal(2u,player.NextExpectedItemUseActionId);Assert.Empty(player.PendingItemUses);
	}

	[Fact]
	public void FortyPlacementsThenMineBuildAndFireShareAnOrderedQueue()
	{
		using Harness harness = new(); ServerLoop loop = harness.Loop; ServerClientSession session = harness.AddPlayer(1);
		session.Inventory.Grant(ItemIds.WheatSeeds, 64); session.Inventory.Grant(ItemIds.FromBlock(BlockType.Dirt), 5); session.Inventory.Grant(ItemIds.Gun, 1);
		for (int index = 1; index <= 40; index++)
		{
			loop.Simulation.Map.SetBlock(index, 0, 0, BlockType.WetFarmland);
			session.CommandHistory.Record(new(index, 0, new(index + .5f, 3, .5f), -Vector3.UnitY, false, true));
			Packet request = new WorldObjectPlaceRequestPacket { ActionId=(uint)index, CommandTick=index, Channel=ItemUseChannel.Secondary, X=index, Y=0, Z=0, PlantType=1 };
			harness.Call("EnqueueItemUse", session.Connection, request); harness.Call("ProcessPendingItemUses");
			Assert.True(session.ItemUseActionHistory.TryGet((uint)index, out var outcome)); Assert.True(outcome.Accepted);
			harness.Call("EnqueueItemUse", session.Connection, request); // Duplicates never consume twice.
		}
		Assert.Equal(40, loop.Simulation.WorldObjects.Count); Assert.Equal(24, session.Inventory.GetSlot(0).Count);
		session.CommandHistory.Record(new(41, 1, new(1.5f, 3, .5f), -Vector3.UnitY, true, false));
		harness.Call("EnqueueItemUse", session.Connection, new BlockRemoveRequestPacket { ItemUseActionId=41, CommandTick=41, Channel=ItemUseChannel.Primary, X=1, Y=0, Z=0 }); harness.Call("ProcessPendingItemUses");
		Assert.Equal(BlockType.None, loop.Simulation.Map.GetBlock(1,0,0));
		session.CommandHistory.Record(new(42, 1, new(2.5f, 3, .5f), -Vector3.UnitY, false, true));
		harness.Call("EnqueueItemUse", session.Connection, new BlockPlaceRequestPacket { ItemUseActionId=42, CommandTick=42, Channel=ItemUseChannel.Secondary, X=2, Y=1, Z=0, BlockType=(ushort)BlockType.Dirt }); harness.Call("ProcessPendingItemUses");
		Assert.Equal(BlockType.Dirt, loop.Simulation.Map.GetBlock(2,1,0));
		session.CommandHistory.Record(new(43, 2, new(2.5f, 3, .5f), Vector3.UnitY, true, false));
		harness.Call("EnqueueItemUse", session.Connection, new WeaponFirePacket { ItemUseActionId=43, CommandTick=43, Channel=ItemUseChannel.Primary }); harness.Call("ProcessPendingItemUses");
		Assert.Equal(44u, session.NextExpectedItemUseActionId); Assert.Empty(session.PendingItemUses);
		for (uint action = 41; action <= 43; action++) { Assert.True(session.ItemUseActionHistory.TryGet(action, out var result)); Assert.True(result.Accepted); }
	}

	[Theory]
	[InlineData(2)]
	[InlineData(3)]
	public void FurniturePlacementCompletesAndRejectionsDoNotBlockLaterActions(byte kind)
	{
		using Harness harness = new(); ServerClientSession session = harness.AddPlayer(1);
		session.Inventory.Grant(kind == 2 ? ItemIds.ItemBasket : ItemIds.Bed, 1);
		for (int x = -2; x <= 2; x++) for (int z = -2; z <= 2; z++) harness.Loop.Simulation.Map.SetBlock(x,0,z,BlockType.Stone);
		for (int tick = 1; tick <= 3; tick++)
		{
			session.CommandHistory.Record(new(tick,0,new(.5f,3,.5f),-Vector3.UnitY,false,true));
			harness.Call("EnqueueItemUse",session.Connection,new WorldObjectPlaceRequestPacket {ActionId=(uint)tick,CommandTick=tick,Channel=ItemUseChannel.Secondary,X=0,Y=1,Z=0,PlantType=kind});
			harness.Call("ProcessPendingItemUses"); Assert.True(session.ItemUseActionHistory.TryGet((uint)tick,out var result)); Assert.Equal(tick==1,result.Accepted);
		}
		Assert.Equal(4u,session.NextExpectedItemUseActionId); Assert.Empty(session.PendingItemUses);
	}

	[Fact]
	public void ContainerCursorReturnUpdatesFurnitureAndRemainingViewerAndClosesOnLostAccess()
	{
		using Harness harness = new(); ServerClientSession first = harness.AddPlayer(1), second = harness.AddPlayer(2);
		VEntItemBasket basket = harness.AddBasket(new(0,1,0)); first.Player.SetPosition(new(.5f,2,2)); second.Player.SetPosition(new(.5f,2,2));
		harness.Call("OpenBasket",first,basket); harness.Call("OpenBasket",second,basket);
		Assert.NotNull(first.ContainerSession); Assert.NotNull(second.ContainerSession);
		harness.Call("HandleContainerAction",first.Connection,new ContainerActionRequestPacket { SessionId=first.ContainerSession.SessionId,ActionId=1,ExpectedPlayerRevision=first.Inventory.Revision,ExpectedContainerRevision=basket.Inventory.Revision,Kind=InventoryActionKind.LeftClickSlot,Slot=0 });
		Assert.Equal(10,first.Inventory.Cursor.Count); Assert.True(basket.Inventory.GetSlot(0).IsEmpty);
		int before = second.Connection.ReliableQueueCount;
		harness.Call("CloseContainer",first,true); Assert.True(first.Inventory.Cursor.IsEmpty); Assert.Equal(10,basket.Inventory.GetSlot(0).Count);
		Assert.True(second.Connection.ReliableQueueCount > before);
		Assert.True(harness.Loop.Simulation.Furniture.TryGet(basket.PersistentKey,out var saved)); Assert.Equal(10,saved.Slots[0].Count);
		second.Player.SetPosition(new(100,2,100)); harness.Call("UpdateContainerSessions"); Assert.Null(second.ContainerSession);
		first.Player.SetPosition(new(.5f,2,2)); harness.Call("OpenBasket",first,basket); first.Player.Health=0; harness.Call("UpdateContainerSessions"); Assert.Null(first.ContainerSession);
	}

	[Fact]
	public void SwitchingContainersResolvesThePreviousCursorBeforeOpeningAnother()
	{
		using Harness harness = new(); ServerClientSession player = harness.AddPlayer(1); player.Player.SetPosition(new(.5f,2,2));
		VEntItemBasket first = harness.AddBasket(new(0,1,0)), second = harness.AddBasket(new(2,1,0));
		harness.Call("OpenBasket",player,first);
		harness.Call("HandleContainerAction",player.Connection,new ContainerActionRequestPacket { SessionId=player.ContainerSession.SessionId,ActionId=1,ExpectedPlayerRevision=player.Inventory.Revision,ExpectedContainerRevision=first.Inventory.Revision,Kind=InventoryActionKind.LeftClickSlot,Slot=0 });
		harness.Call("OpenBasket",player,second); Assert.True(player.Inventory.Cursor.IsEmpty); Assert.Equal(10,first.Inventory.GetSlot(0).Count); Assert.Equal(second.PersistentKey,player.ContainerSession.ContainerKey);
	}

	[Theory]
	[InlineData("disconnect")]
	[InlineData("removal")]
	[InlineData("blocked")]
	public void ContainerLifecycleResolvesCursorAndPublishesToOtherViewers(string cause)
	{
		using Harness harness = new(); ServerClientSession first = harness.AddPlayer(1), second = harness.AddPlayer(2);
		first.Player.SetPosition(new(.5f, 2, 3)); second.Player.SetPosition(new(.5f, 2, 3));
		VEntItemBasket basket = harness.AddBasket(new(0, 1, 0));
		harness.Call("OpenBasket", first, basket); harness.Call("OpenBasket", second, basket);
		harness.Call("HandleContainerAction", first.Connection, new ContainerActionRequestPacket { SessionId=first.ContainerSession.SessionId, ActionId=1, ExpectedPlayerRevision=first.Inventory.Revision, ExpectedContainerRevision=basket.Inventory.Revision, Kind=InventoryActionKind.LeftClickSlot, Slot=0 });
		Assert.Equal(10, first.Inventory.Cursor.Count);
		int before = second.Connection.ReliableQueueCount;
		if (cause == "disconnect") harness.Call("OnClientDisconnected", first.Connection, "Regression test");
		else if (cause == "removal") harness.Call("RemoveBasket", basket);
		else
		{
			for (int y = 1; y <= 3; y++) harness.Loop.Simulation.Map.SetBlock(0, y, 1, BlockType.Stone);
			harness.Call("UpdateContainerSessions");
		}
		Assert.Null(first.ContainerSession); Assert.True(first.Inventory.Cursor.IsEmpty);
		Assert.True(second.Connection.ReliableQueueCount > before);
		if (cause == "removal") Assert.Null(second.ContainerSession);
		else Assert.Equal(10, basket.Inventory.GetSlot(0).Count);
		if (cause == "disconnect")
		{
			Assert.Equal(0u, first.LastContainerActionId); Assert.Empty(first.PendingItemUses);
			Assert.Equal(1u, first.NextExpectedItemUseActionId);
		}
	}

	[Fact]
	public void ChangedDuplicateCannotRetireAQueuedActionBeforeItExecutes()
	{
		using Harness harness = new(); ServerClientSession player = harness.AddPlayer(1);
		player.Inventory.Grant(ItemIds.WheatSeeds, 2);
		harness.Loop.Simulation.Map.SetBlock(0, 0, 0, BlockType.WetFarmland);
		WorldObjectPlaceRequestPacket request = new() { ActionId=1, CommandTick=1, Channel=ItemUseChannel.Secondary, X=0, Y=0, Z=0, PlantType=1 };
		harness.Call("EnqueueItemUse", player.Connection, request);
		harness.Call("EnqueueItemUse", player.Connection, new WorldObjectPlaceRequestPacket { ActionId=1, CommandTick=0 });
		Assert.Single(player.PendingItemUses); Assert.Equal(1u, player.NextExpectedItemUseActionId);
		player.CommandHistory.Record(new(1, 0, new(.5f, 3, .5f), -Vector3.UnitY, false, true));
		harness.Call("ProcessPendingItemUses");
		Assert.Empty(player.PendingItemUses); Assert.Equal(2u, player.NextExpectedItemUseActionId);
		Assert.True(player.ItemUseActionHistory.TryGet(1, out var result)); Assert.True(result.Accepted);
		Assert.Equal(1, player.Inventory.GetSlot(0).Count);
	}

	private sealed class Harness : IDisposable
	{
		private readonly string root = Path.Combine(Path.GetTempPath(),"voxel-transactions-"+Guid.NewGuid());
		public ServerLoop Loop { get; }
		public Harness()
		{
			Loop = new(RuntimePathResolver.ResolveRuntimePaths(ApplicationKind.Test,root,Environment.CurrentDirectory),GameLogLevel.Error);
			typeof(ServerLoop).GetField("_farming",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(Loop,new FarmingService(Loop.Simulation.Map,Loop.Simulation.WorldObjects));
		}
		public object Call(string method,params object[] args) => typeof(ServerLoop).GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(Loop,args);
		public ServerClientSession AddPlayer(int id)
		{
			using NetworkIdentity identity = new();
			NetConnection connection = new(new(IPAddress.Loopback,10000+id),0) { PlayerId=id,PlayerName="Player"+id,Identity=identity.Identity,State=ConnectionState.Connected,IsGameplayActive=true };
			IFishEngineRunner engine = (IFishEngineRunner)typeof(ServerLoop).GetField("_eng",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Loop)!;
			Player player = new(engine,id); player.SetPosition(new(10,10,10));
			ServerClientSession session = new(new((ulong)id),connection,player,new()) { IsGameplayActive=true };
			((Dictionary<int,ServerClientSession>)typeof(ServerLoop).GetField("_sessions",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Loop)!).Add(id,session);
			((Dictionary<int,NetConnection>)typeof(NetServer).GetField("_playerConnections",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Loop.Server)!).Add(id,connection);
			return session;
		}
		public VEntItemBasket AddBasket(BlockCoordinate anchor)
		{
			Loop.Simulation.Map.SetBlock(anchor.X,anchor.Y-1,anchor.Z,BlockType.Stone);
			ItemStack[] slots = new ItemStack[VEntItemBasket.SlotCount]; slots[0]=new(ItemIds.Wheat,10);
			PersistentFurnitureRecord record = new(Loop.Simulation.Furniture.AllocatePlacedKey(),FurnitureType.ItemBasket,anchor,0,slots); Loop.Simulation.Furniture.Add(record);
			return (VEntItemBasket)Call("SpawnBasket",record,false);
		}
		public void Dispose() { Loop.Dispose(); Directory.Delete(root,true); }
	}
}
