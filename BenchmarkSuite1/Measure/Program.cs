using System.Diagnostics;
using System.Net;
using System.Reflection;
using System.Text.Json;
using Voxelgine.Engine;
using Voxelgine.Engine.Server;
using Voxelgine.Engine.World.Structures;
using Voxelgine.Graphics;

#if HARDENED
if (args.Length == 3 && args[0] == "--multiplayer-smoke")
{
	MultiplayerSmoke.Run(args[1], args[2]);
	return;
}
#endif

// Identical source is compiled against the baseline and current engine. Setup is outside samples.
List<object> results = new();
foreach ((int total, int active) in new[] { (1024,1024), (10000,1024), (1025,1025), (10000,10000) })
{
	ChunkMap map = new(); WorldObjectStore store = new();
	WorldPlantRecord[] plants = Enumerable.Range(0,total).Select(i => new WorldPlantRecord(PersistentWorldObjectKey.Placed(new PersistentWorldObjectId((ulong)i+1)),WorldPlantType.Wheat,i<active?(ushort)0:ushort.MaxValue,255,ItemIds.Wheat,new(i%100,0,i/100))).ToArray();
	foreach (var plant in plants) map.SetBlock(plant.Support.X,0,plant.Support.Z,BlockType.WetFarmland);
	store.Restore(plants); using FarmingService farming = new(map,store);
	using NetServer server = new(); WorldStreamManager voxels = new(server,map,null);
#if HARDENED
	using WorldObjectStreamManager objects = new(server,voxels,store,null,()=>0d);
#else
	using WorldObjectStreamManager objects = new(server,voxels,store,null,()=>0f);
#endif
	Measure($"farm-{total}-active-{active}",()=>store.Restore(plants),()=>farming.Update(1),()=>new { plants=store.Count });
}
{
	WorldObjectColumnState column = new(0,0,1,1,Enumerable.Range(0,1024).Select(i=>new WorldPlantRecord(PersistentWorldObjectKey.Placed(new PersistentWorldObjectId((ulong)i+1)),WorldPlantType.Wheat,547,255,ItemIds.Wheat,new(i%16,i/256,(i/16)%16))).ToArray());
	int writes=0, invalidations=0;
#if HARDENED
	WorldPlantOverlay overlay = new(); overlay.Apply(column,s=>(ushort)(100+s),(_,_)=>{});
	Action apply = () => { HashSet<(int,int,int)> chunks=new(); writes=overlay.Apply(column,s=>(ushort)(100+s),(p,_)=>chunks.Add((p.X/16,p.Y/16,p.Z/16))); invalidations=chunks.Count; };
#else
	HashSet<BlockCoordinate> overlay = column.Records.Select(p=>p.Position).ToHashSet();
	Action apply = () => { writes=invalidations=0; foreach(var position in overlay) {writes++;invalidations++;} HashSet<BlockCoordinate> next=new(); foreach(var plant in column.Records){next.Add(plant.Position);writes++;invalidations++;} overlay=next; };
#endif
	Measure("overlay-unchanged-stage",()=>{},apply,()=>new { writes, invalidations });
}
{
	ChunkMap map=new(); using NetServer server=new(); WorldStreamManager stream=new(server,map,null);
	MethodInfo encode=typeof(WorldStreamManager).GetMethod("GetEncoded",BindingFlags.NonPublic|BindingFlags.Instance)!; int revision=0;
	Measure("column-revisions",()=>map.SetBlock(0,0,0,++revision%2==0?BlockType.Stone:BlockType.Dirt),()=>encode.Invoke(stream,new object[]{new ChunkColumnCoordinate(0,0)}),()=>new { cacheEntries=stream.CachedColumnCount });
}
{
	using NetServer server=new(); server.Start(0);
	MethodInfo receive=typeof(NetServer).GetMethod("QueueReceivedData",BindingFlags.NonPublic|BindingFlags.Instance)!;
	IPEndPoint[] peers=Enumerable.Range(1,8).Select(i=>new IPEndPoint(IPAddress.Loopback,20000+i)).ToArray();
	Measure("network-server-tick-burst",()=> { for(int i=0;i<4096;i++)receive.Invoke(server,new object[]{new byte[]{0},peers[i%8]}); },()=>server.Tick(0),()=>new {
#if HARDENED
		queueDatagrams=server.IncomingDatagramCount, queueBytes=server.IncomingDatagramBytes
#else
		queueDatagrams=0,queueBytes=0
#endif
	});
}
{
	string root=Path.Combine(Path.GetTempPath(),"voxel-tick-measure-"+Guid.NewGuid());
	try
	{
		using ServerLoop loop=new(RuntimePathResolver.ResolveRuntimePaths(ApplicationKind.Test,root,Environment.CurrentDirectory),Voxelgine.Engine.DI.GameLogLevel.Error);
		MethodInfo tick=typeof(ServerLoop).GetMethod("Tick",BindingFlags.NonPublic|BindingFlags.Instance)!;
#if HARDENED
		object[] arguments={0d,.015f};
#else
		object[] arguments={0f,.015f};
#endif
		Measure("simulation-tick-empty-world",()=>{},()=>tick.Invoke(loop,arguments),()=>new { players=0 });
		loop.Simulation.Map.SetBlock(0,0,0,BlockType.Stone);
		MethodInfo save=typeof(ServerLoop).GetMethod("SaveWorld",BindingFlags.NonPublic|BindingFlags.Instance)!;
		Measure("synchronous-save-one-column",()=>{},()=>save.Invoke(loop,null),()=>new { columns=1 });
	}
	finally { Directory.Delete(root,true); }
}
Console.WriteLine(JsonSerializer.Serialize(results,new JsonSerializerOptions{WriteIndented=true}));

void Measure(string name,Action setup,Action action,Func<object> counters)
{
	try
	{
		for(int i=0;i<5;i++){setup();action();}
		List<double> times=new();long allocations=0;
		for(int i=0;i<100;i++) { setup();long bytes=GC.GetAllocatedBytesForCurrentThread();long start=Stopwatch.GetTimestamp();action();times.Add(Stopwatch.GetElapsedTime(start).TotalMilliseconds);allocations+=GC.GetAllocatedBytesForCurrentThread()-bytes; }
		times.Sort();results.Add(new{name,medianMs=times[50],p95Ms=times[95],p99Ms=times[99],allocatedBytes=allocations/100,counters=counters()});
	}
	catch(Exception error){results.Add(new{name,error=error.GetBaseException().GetType().Name+": "+error.GetBaseException().Message});}
}
