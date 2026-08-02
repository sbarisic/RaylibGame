using Voxelgine.Engine;
using Voxelgine.Engine.World.Structures;
using Voxelgine.Graphics;

namespace VoxelgineEngine.Tests;

public sealed class PhaseOneMarkerTests
{
	[Fact]
	public void BlueprintLoaderValidatesFurniturePlantSchemasAndExactContents()
	{
		using TempBlueprintDirectory temp=new(BlueprintJson());StructureBlueprint blueprint=Assert.Single(StructureBlueprintCatalog.LoadDirectory(temp.Path).Blueprints);
		BedMarkerData bed=PhaseOneMarkerSchemas.ParseBed(blueprint.Markers.Single(marker=>marker.Kind==StructureMarkerKind.Bed).Data);BasketMarkerData basket=PhaseOneMarkerSchemas.ParseBasket(blueprint.Markers.Single(marker=>marker.Kind==StructureMarkerKind.ItemBasket).Data);PlantMarkerData plant=PhaseOneMarkerSchemas.ParsePlant(blueprint.Markers.Single(marker=>marker.Kind==StructureMarkerKind.Plant).Data);
		Assert.Equal((byte)1,bed.Facing);Assert.Equal("worker",bed.NpcAssignmentTag);Assert.Equal(5,basket.Slots[2].Count);Assert.Equal(ItemIds.Wheat,basket.Slots[2].Item);Assert.Equal(new BlockCoordinate(3,0,3),plant.Support);Assert.Equal(ushort.MaxValue,plant.GrowthStage==7?ushort.MaxValue:(ushort)(plant.GrowthStage*8192));
	}

	[Fact]
	public void RotationTransformsFurnitureFacingAndExplicitPlantSupport()
	{
		BlockCoordinate size=new(5,3,5),origin=new(10,20,30);string bed=PhaseOneMarkerSchemas.RotateData(StructureMarkerKind.Bed,"{\"facing\":\"East\"}",size,90,origin);string plant=PhaseOneMarkerSchemas.RotateData(StructureMarkerKind.Plant,"{\"plantType\":\"Wheat\",\"growthStage\":2,\"support\":[3,0,3]}",size,90,origin);
		Assert.Equal((byte)2,PhaseOneMarkerSchemas.ParseBed(bed).Facing);Assert.Equal(origin+new BlockCoordinate(1,0,3),PhaseOneMarkerSchemas.ParsePlant(plant).Support);
	}

	[Fact]
	public void OverlappingBlueprintObjectsFailDeterministically()
	{
		string invalid=BlueprintJson().Replace("\"position\":[1,1,3]","\"position\":[1,1,1]");using TempBlueprintDirectory temp=new(invalid);Assert.Throws<InvalidDataException>(()=>StructureBlueprintCatalog.LoadDirectory(temp.Path));
	}

	[Fact]
	public void TombstonesRoundTripAndKeepGeneratedPrimaryKeyAbsent()
	{
		GeneratedMarkerId marker=new(new GeneratedSiteId("site"),"bed");GeneratedTombstoneStore store=new();Assert.True(store.Add(GeneratedObjectKind.Furniture,marker));Assert.False(store.Add(GeneratedObjectKind.Furniture,marker));
		using MemoryStream archive=new();WorldArchive.Write(archive,new ChunkMap(),default,tombstones:store.GetAll());archive.Position=0;GeneratedTombstone decoded=Assert.Single(WorldArchive.Read(archive).Tombstones);Assert.Equal(marker,decoded.MarkerId);
		FurnitureStore furniture=new();Assert.False(furniture.TryGet(PersistentFurnitureKey.Generated(marker),out _));Assert.True(store.Contains(GeneratedObjectKind.Furniture,marker));
	}

	private static string BlueprintJson()=>"""
{
  "formatVersion":1,"markerDataVersion":1,"id":"phase.one","role":"Shelter","size":[5,3,5],"anchor":[2,0,2],"rotations":[0,90,180,270],
  "palette":{"d":"Dirt","f":"WetFarmland"},
  "layers":[["ddddd","ddddd","ddddd","dddfd","ddddd"],["_____","_____","_____","_____","_____"],["_____","_____","_____","_____","_____"]],
  "markers":[
    {"id":"npc","kind":"NpcSpawn","position":[4,1,4],"data":{"tag":"worker"}},
    {"id":"bed","kind":"Bed","position":[1,1,1],"data":{"facing":"East","variant":"basic","npcAssignmentTag":"worker"}},
    {"id":"basket","kind":"ItemBasket","position":[1,1,3],"data":{"facing":"North","slots":[{"slot":2,"item":1005,"count":5}]}},
    {"id":"wheat","kind":"Plant","position":[3,1,3],"data":{"plantType":"Wheat","growthStage":7,"support":[3,0,3]}}
  ],"connectors":[],"fogVolumes":[]
}
""";

	private sealed class TempBlueprintDirectory:IDisposable
	{
		public TempBlueprintDirectory(string json){Path=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"voxelgine-marker-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path);File.WriteAllText(System.IO.Path.Combine(Path,"phase.json"),json);}
		public string Path{get;}public void Dispose(){Directory.Delete(Path,true);}
	}
}
