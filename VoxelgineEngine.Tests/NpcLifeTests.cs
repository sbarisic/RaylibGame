using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.Engine.World.Structures;
using Voxelgine.Graphics;

namespace VoxelgineEngine.Tests;

public sealed class NpcLifeTests
{
	[Theory]
	[InlineData("dawn")]
	[InlineData("death")]
	[InlineData("detach")]
	[InlineData("navigation")]
	[InlineData("bed")]
	public void SeekingSleepReleasesNavigationAndReservation(string cause)
	{
		ChunkMap map=new(); for(int x=-12;x<=12;x++)for(int z=-12;z<=12;z++)map.SetBlock(x,0,z,BlockType.Stone);
		FurnitureStore furniture=new(); var key=furniture.AllocatePlacedKey(); var anchor=new BlockCoordinate(0,1,0);
		furniture.Add(new(key,FurnitureType.Bed,anchor,2,Array.Empty<ItemStack>())); VEntBed bed=new();bed.Initialize(key,anchor,2);
		var id=StableNpcId.Persistent(new PersistentEntityId(90)); NpcLifeService life=new(furniture,()=>new[]{bed},20);
		life.Restore(new[]{new NpcLifeRecord(id,7000,null)},20); VEntNPC npc=CreateNpc(id);npc.SetPosition(new(8,1,8));npc.InitPathfinding(map);life.Attach(npc);
		life.Update(20,20,0);Assert.True(npc.IsLifeControlled);Assert.False(npc.IsSleeping);
		switch(cause)
		{
			case "death":npc.Health=0;break;
			case "detach":life.Detach(npc);break;
			case "navigation":npc.StopNavigation();break;
			case "bed":life.OnBedRemoved(key);break;
		}
		if(cause is not ("detach" or "bed"))life.Update(20,cause=="dawn"?6:20,1);
		Assert.False(npc.IsLifeControlled);Assert.False(npc.IsSleeping);Assert.False(npc.IsNavigating);
		var reservations=(System.Collections.IDictionary)typeof(NpcLifeService).GetField("reservations",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance)!.GetValue(life)!;
		Assert.Empty(reservations);
	}

	[Fact]
	public void ExplicitClockChangesDoNotManufactureFatigue()
	{
		var id=StableNpcId.Persistent(new PersistentEntityId(91));DayNightCycle clock=new();
		NpcLifeService life=new(new(),()=>Array.Empty<VEntBed>(),clock.AbsoluteGameHours);life.Attach(CreateNpc(id));clock.TimeChanged+=life.RebaseTime;
		clock.SetTime(23);life.Update(clock.AbsoluteGameHours,clock.TimeOfDay,100_000_000d);
		Assert.True(life.TryGet(id,out var unchanged));Assert.Equal(0,unchanged.Fatigue);
		clock.SetTime(6);clock.Update(25);life.Update(clock.AbsoluteGameHours,clock.TimeOfDay,100_000_000.015);
		Assert.True(life.TryGet(id,out var elapsed));Assert.Equal(625,elapsed.Fatigue);
	}

	[Fact]
	public void FatigueUsesGameHoursAndStableIdentity()
	{
		FurnitureStore furniture=new();List<VEntBed> beds=new();StableNpcId id=StableNpcId.Persistent(new PersistentEntityId(8));
		NpcLifeService life=new(furniture,()=>beds,10);life.Restore(new[]{new NpcLifeRecord(id,0,null)},10);VEntNPC npc=CreateNpc(id);life.Attach(npc);
		life.Update(11,11,1);
		Assert.True(life.TryGet(id,out NpcLifeRecord record));Assert.Equal(625,record.Fatigue);Assert.NotEqual(npc.NetworkId.ToString(),id.ToString());
	}

	[Fact]
	public void MissingBedAssignmentIsClearedAfterRestoration()
	{
		FurnitureStore furniture=new();PersistentFurnitureKey missing=PersistentFurnitureKey.Placed(new PersistentEntityId(4));StableNpcId npc=StableNpcId.Persistent(new PersistentEntityId(5));
		NpcLifeService life=new(furniture,()=>Array.Empty<VEntBed>(),8);life.Restore(new[]{new NpcLifeRecord(npc,7000,missing)},8);
		var repair=Assert.Single(life.RepairAssignments());Assert.Equal(npc,repair.NpcId);Assert.True(life.TryGet(npc,out NpcLifeRecord record));Assert.Null(record.AssignedBed);
	}

	[Fact]
	public void BedAssignmentAndLifeStateRoundTripWithAbsoluteGameTime()
	{
		PersistentFurnitureKey bed=PersistentFurnitureKey.Generated(new GeneratedMarkerId(new GeneratedSiteId("site"),"bed"));StableNpcId npc=StableNpcId.Generated(new GeneratedMarkerId(new GeneratedSiteId("site"),"npc"));
		WorldArchiveMetadata metadata=new(1,Vector3.Zero,Vector3.Zero,Vector3.Zero,AbsoluteGameHours:123.5);using MemoryStream stream=new();WorldArchive.Write(stream,new ChunkMap(),metadata,furniture:new[]{new PersistentFurnitureRecord(bed,FurnitureType.Bed,new BlockCoordinate(1,2,3),1,Array.Empty<ItemStack>())},npcLife:new[]{new NpcLifeRecord(npc,6123,bed)});stream.Position=0;
		WorldArchiveReadResult read=WorldArchive.Read(stream);Assert.Equal(123.5,read.Metadata.AbsoluteGameHours);NpcLifeRecord life=Assert.Single(read.NpcLife);Assert.Equal(npc,life.NpcId);Assert.Equal(bed,life.AssignedBed);Assert.Equal(6123,life.Fatigue);
	}

	[Fact]
	public void BedOccupiesFootAndFacingHeadCells()
	{
		FurnitureStore furniture=new();PersistentFurnitureKey key=furniture.AllocatePlacedKey();PersistentFurnitureRecord record=new(key,FurnitureType.Bed,new BlockCoordinate(3,4,5),1,Array.Empty<ItemStack>());furniture.Add(record);
		Assert.True(furniture.IsCellOccupied(new BlockCoordinate(3,4,5)));Assert.True(furniture.IsCellOccupied(new BlockCoordinate(4,4,5)));Assert.False(furniture.IsCellOccupied(new BlockCoordinate(3,4,6)));
	}

	[Fact]
	public void NpcSpawnPropertiesPreserveStableIdentity()
	{
		StableNpcId id=StableNpcId.Generated(new GeneratedMarkerId(new GeneratedSiteId("habitat"),"npc-a"));VEntNPC source=CreateNpc(id);using MemoryStream stream=new();using(BinaryWriter writer=new(stream,System.Text.Encoding.UTF8,true))source.WriteSpawnProperties(writer);stream.Position=0;VEntNPC decoded=new();using(BinaryReader reader=new(stream,System.Text.Encoding.UTF8,true))decoded.ReadSpawnProperties(reader);Assert.Equal(id,decoded.StableId);
	}

	[Fact]
	public void AbsoluteGameTimeAdvancesOnlyWithAuthoritativeSimulation()
	{
		DayNightCycle cycle=new(){DayLengthSeconds=600};cycle.RestoreAbsoluteGameTime(50);cycle.Update(25);Assert.Equal(51,cycle.AbsoluteGameHours,5);DayNightCycle client=new(){IsAuthority=false};client.RestoreAbsoluteGameTime(50);client.Update(25);Assert.Equal(50,client.AbsoluteGameHours);
	}

	[Fact]
	public void TiredNpcSleepsAtAvailableBedAndWakesInMorning()
	{
		ChunkMap map=new();for(int x=-3;x<=3;x++)for(int z=-3;z<=3;z++)map.SetBlock(x,0,z,BlockType.Stone);
		FurnitureStore furniture=new();PersistentFurnitureKey bedKey=furniture.AllocatePlacedKey();PersistentFurnitureRecord bedRecord=new(bedKey,FurnitureType.Bed,new BlockCoordinate(0,1,0),2,Array.Empty<ItemStack>());furniture.Add(bedRecord);VEntBed bed=new();bed.Initialize(bedKey,bedRecord.Anchor,bedRecord.Facing);
		StableNpcId id=StableNpcId.Persistent(new PersistentEntityId(20));NpcLifeService life=new(furniture,()=>new[]{bed},20);life.Restore(new[]{new NpcLifeRecord(id,6000,null)},20);VEntNPC npc=CreateNpc(id);npc.SetPosition(new Vector3(.5f,1,-.5f));npc.InitPathfinding(map);life.Attach(npc);
		life.Update(20,20,0);Assert.True(npc.IsSleeping);
		life.Update(21,6,1);Assert.False(npc.IsSleeping);Assert.True(life.TryGet(id,out NpcLifeRecord record));Assert.Equal(1000,record.Fatigue);
	}

	private static VEntNPC CreateNpc(StableNpcId id){VEntNPC npc=new(){Eng=new TestEngineRunner()};npc.SetStableId(id);npc.SetSize(new Vector3(.9f,1.8f,.9f));return npc;}
	private sealed class TestEngineRunner:IFishEngineRunner{public IFishLogging Logging{get;}=new NullLogging();public ILerpManager LerpManager{get;}=new LerpManager();public int ChunkDrawCalls{get;set;}public bool DebugMode{get;set;}public float TotalTime{get;set;}}
	private sealed class NullLogging:IFishLogging{public void Init(bool IsServer=false){}public void WriteLine(string message){}public void ServerWriteLine(string message){}public void ClientWriteLine(string message){}public void ServerNetworkWriteLine(string message){}public void ClientNetworkWriteLine(string message){}}
}
