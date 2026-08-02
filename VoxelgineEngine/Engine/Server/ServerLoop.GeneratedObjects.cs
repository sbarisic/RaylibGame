using System.Text.Json;
using Voxelgine.Engine.DI;
using Voxelgine.Engine.World.Structures;
using Voxelgine.Graphics;

namespace Voxelgine.Engine.Server;

public partial class ServerLoop
{
	private readonly Dictionary<StableNpcId,PersistentFurnitureKey> _pendingGeneratedBedAssignments=new();

	private void RestoreGeneratedPhaseOneMarkers()
	{
		_pendingGeneratedBedAssignments.Clear();
		foreach(PlannedMarker marker in _simulation.Map.GeneratedFeatures.Markers.Where(static marker=>marker.Kind is StructureMarkerKind.Bed or StructureMarkerKind.ItemBasket or StructureMarkerKind.Plant).OrderBy(static marker=>marker.Id.Site).ThenBy(static marker=>marker.Id.BlueprintMarkerId,StringComparer.Ordinal))
		{
			try
			{
				if(marker.Kind==StructureMarkerKind.Plant)RestoreGeneratedPlant(marker);else RestoreGeneratedFurniture(marker);
			}
			catch(Exception exception)when(exception is InvalidDataException or InvalidOperationException or ArgumentException)
			{
				_logging.Log(GameLogLevel.Warning,"GeneratedObjects",$"marker restoration skipped marker={marker.Id} kind={marker.Kind}",exception);
			}
		}
	}

	private void RestoreGeneratedFurniture(PlannedMarker marker)
	{
		PersistentFurnitureKey key=PersistentFurnitureKey.Generated(marker.Id);if(_simulation.Tombstones.Contains(GeneratedObjectKind.Furniture,marker.Id))return;if(_simulation.Furniture.TryGet(key,out _)){if(marker.Kind==StructureMarkerKind.Bed){BedMarkerData existing=PhaseOneMarkerSchemas.ParseBed(marker.Data);if(existing.NpcAssignmentTag!=null)_pendingGeneratedBedAssignments.TryAdd(FindGeneratedNpcByTag(existing.NpcAssignmentTag),key);}return;}
		if(marker.Kind==StructureMarkerKind.ItemBasket)
		{
			BasketMarkerData data=PhaseOneMarkerSchemas.ParseBasket(marker.Data);if(_simulation.Map.GetBlock(marker.Position.X,marker.Position.Y,marker.Position.Z)!=BlockType.None||!BlockInfo.IsSolid(_simulation.Map.GetBlock(marker.Position.X,marker.Position.Y-1,marker.Position.Z))||_simulation.Furniture.IsCellOccupied(marker.Position))throw new InvalidOperationException("Generated basket footprint is blocked or unsupported.");
			_simulation.Furniture.Add(new PersistentFurnitureRecord(key,FurnitureType.ItemBasket,marker.Position,data.Facing,data.Slots));return;
		}
		BedMarkerData bed=PhaseOneMarkerSchemas.ParseBed(marker.Data);BlockCoordinate head=marker.Position+VEntBed.FacingOffset(bed.Facing);
		if(_simulation.Map.GetBlock(marker.Position.X,marker.Position.Y,marker.Position.Z)!=BlockType.None||_simulation.Map.GetBlock(head.X,head.Y,head.Z)!=BlockType.None||!BlockInfo.IsSolid(_simulation.Map.GetBlock(marker.Position.X,marker.Position.Y-1,marker.Position.Z))||!BlockInfo.IsSolid(_simulation.Map.GetBlock(head.X,head.Y-1,head.Z))||_simulation.Furniture.IsCellOccupied(marker.Position)||_simulation.Furniture.IsCellOccupied(head))throw new InvalidOperationException("Generated bed footprint is blocked or unsupported.");
		_simulation.Furniture.Add(new PersistentFurnitureRecord(key,FurnitureType.Bed,marker.Position,bed.Facing,Array.Empty<ItemStack>()));
		if(bed.NpcAssignmentTag!=null)_pendingGeneratedBedAssignments.Add(FindGeneratedNpcByTag(bed.NpcAssignmentTag),key);
	}

	private void RestoreGeneratedPlant(PlannedMarker marker)
	{
		PersistentWorldObjectKey key=PersistentWorldObjectKey.Generated(marker.Id);if(_simulation.Tombstones.Contains(GeneratedObjectKind.WorldObject,marker.Id)||_simulation.WorldObjects.TryGet(key,out _))return;PlantMarkerData data=PhaseOneMarkerSchemas.ParsePlant(marker.Data);
		if(marker.Position!=data.Support+new BlockCoordinate(0,1,0)||_simulation.WorldObjects.TryGetAt(marker.Position,out _)||_simulation.Map.GetBlock(marker.Position.X,marker.Position.Y,marker.Position.Z)!=BlockType.None)throw new InvalidOperationException("Generated plant footprint is occupied.");
		if(!_farming.TryPlantWheat(data.Support,key))throw new InvalidOperationException("Generated plant support is invalid.");ushort progress=data.GrowthStage==7?ushort.MaxValue:checked((ushort)(data.GrowthStage*8192));if(progress>0&&_simulation.WorldObjects.TryGet(key,out WorldPlantRecord record))_simulation.WorldObjects.ApplyTransaction(new[]{new WorldObjectOperation(WorldObjectOperationKind.Upsert,record with{GrowthProgress=progress},default)});
	}

	private StableNpcId FindGeneratedNpcByTag(string tag)
	{
		PlannedMarker[] matches=_simulation.Map.GeneratedFeatures.Markers.Where(marker=>marker.Kind==StructureMarkerKind.NpcSpawn&&ReadNpcTag(marker.Data)==tag).ToArray();if(matches.Length!=1)throw new InvalidDataException($"NPC assignment tag '{tag}' does not resolve uniquely.");return StableNpcId.Generated(matches[0].Id);
	}
	private static string ReadNpcTag(string data){if(string.IsNullOrWhiteSpace(data))return null;using JsonDocument document=JsonDocument.Parse(data);return document.RootElement.TryGetProperty("tag",out JsonElement tag)?tag.GetString():null;}
	private void ApplyGeneratedBedAssignments(){foreach((StableNpcId npc,PersistentFurnitureKey bed)in _pendingGeneratedBedAssignments)try{_npcLife.AssignBed(npc,bed);}catch(KeyNotFoundException){_logging.Log(GameLogLevel.Warning,"NpcLife",$"generated bed assignment NPC is absent npc={npc} bed={bed}");}}
}
