using System;
using System.Linq;
using System.Reflection;
using BenchmarkDotNet.Attributes;
using Voxelgine.Engine;
using Voxelgine.Engine.Server;
using Voxelgine.Engine.World.Structures;
using Voxelgine.Graphics;

namespace BenchmarkSuite1;

[MemoryDiagnoser]
public class FarmingBenchmarks
{
	[Params(1024, 1025, 10000)] public int Plants;
	private ChunkMap map;
	private WorldObjectStore store;
	private FarmingService farming;
	private WorldPlantRecord[] initial;
	[GlobalSetup]
	public void Setup()
	{
		map = new(); store = new();
		initial = Enumerable.Range(0,Plants).Select(i => new WorldPlantRecord(PersistentWorldObjectKey.Placed(new PersistentWorldObjectId((ulong)i+1)),WorldPlantType.Wheat,0,255,ItemIds.Wheat,new(i%100,0,i/100))).ToArray();
		foreach (var plant in initial) map.SetBlock(plant.Support.X,0,plant.Support.Z,BlockType.WetFarmland);
		store.Restore(initial); farming = new(map,store);
	}
	[IterationSetup] public void Reset() => store.Restore(initial);
	[Benchmark] public void Grow() => farming.Update(1);
	[GlobalCleanup] public void Cleanup() => farming.Dispose();
}

[MemoryDiagnoser]
public class ObjectOverlayBenchmarks
{
	private readonly WorldPlantOverlay overlay = new();
	private WorldObjectColumnState column;
	[GlobalSetup] public void Setup()
	{
		column = new(0,0,1,1,Enumerable.Range(0,1024).Select(i => new WorldPlantRecord(PersistentWorldObjectKey.Placed(new PersistentWorldObjectId((ulong)i+1)),WorldPlantType.Wheat,547,255,ItemIds.Wheat,new(i%16,i/256,(i/16)%16))).ToArray());
		UnchangedStage();
	}
	[Benchmark] public int UnchangedStage() => overlay.Apply(column,static stage => (ushort)(100+stage),static (_,_) => { });
}

[MemoryDiagnoser]
public class ColumnRevisionBenchmarks
{
	private readonly ChunkMap map = new();
	private NetServer server;
	private WorldStreamManager stream;
	private readonly MethodInfo encode = typeof(WorldStreamManager).GetMethod("GetEncoded",BindingFlags.Instance|BindingFlags.NonPublic);
	private int revision;
	[GlobalSetup] public void Setup() { server = new(); stream = new(server,map,null); }
	[Benchmark] public int Revise()
	{
		map.SetBlock(0,0,0,++revision%2==0 ? BlockType.Stone : BlockType.Dirt);
		encode.Invoke(stream,new object[] { new ChunkColumnCoordinate(0,0) });
		return stream.CachedColumnCount;
	}
	[GlobalCleanup] public void Cleanup() => server.Dispose();
}
