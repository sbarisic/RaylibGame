using System.Text.Json;
using System.Text.Json.Nodes;
using Voxelgine.Graphics;

namespace Voxelgine.Engine.World.Structures;

public readonly record struct BedMarkerData(byte Facing,string Variant,string NpcAssignmentTag);
public readonly record struct BasketMarkerData(byte Facing,string Variant,ItemStack[] Slots);
public readonly record struct PlantMarkerData(WorldPlantType PlantType,byte GrowthStage,BlockCoordinate Support);

public static class PhaseOneMarkerSchemas
{
	public static BedMarkerData ParseBed(string data)
	{
		JsonElement root=ParseObject(data,"bed");byte facing=ReadFacing(root);string variant=ReadOptionalString(root,"variant");string tag=ReadOptionalString(root,"npcAssignmentTag");return new(facing,variant,tag);
	}
	public static BasketMarkerData ParseBasket(string data)
	{
		JsonElement root=ParseObject(data,"basket");byte facing=ReadFacing(root);string variant=ReadOptionalString(root,"variant");ItemStack[] slots=new ItemStack[VEntItemBasket.SlotCount];
		if(root.TryGetProperty("slots",out JsonElement entries)){if(entries.ValueKind!=JsonValueKind.Array||entries.GetArrayLength()>VEntItemBasket.SlotCount)throw new InvalidDataException("Basket marker slots are invalid.");HashSet<int> used=new();foreach(JsonElement entry in entries.EnumerateArray()){int slot=entry.GetProperty("slot").GetInt32();if((uint)slot>=slots.Length||!used.Add(slot))throw new InvalidDataException("Basket marker contains an invalid or duplicate slot.");ItemStack stack=new(new ItemId(entry.GetProperty("item").GetUInt16()),entry.GetProperty("count").GetUInt16());if(!ItemCatalog.IsCanonical(stack)||stack.IsEmpty)throw new InvalidDataException("Basket marker contains a non-canonical stack.");slots[slot]=stack;}}
		return new(facing,variant,slots);
	}
	public static PlantMarkerData ParsePlant(string data)
	{
		JsonElement root=ParseObject(data,"plant");if(!root.TryGetProperty("plantType",out JsonElement typeElement)||!Enum.TryParse(typeElement.GetString(),false,out WorldPlantType type)||!Enum.IsDefined(type))throw new InvalidDataException("Plant marker type is invalid.");int stage=root.GetProperty("growthStage").GetInt32();if(stage is <0 or >7)throw new InvalidDataException("Plant marker growth stage is invalid.");JsonElement support=root.GetProperty("support");if(support.ValueKind!=JsonValueKind.Array||support.GetArrayLength()!=3)throw new InvalidDataException("Plant marker support is invalid.");return new(type,(byte)stage,new BlockCoordinate(support[0].GetInt32(),support[1].GetInt32(),support[2].GetInt32()));
	}
	public static string RotateData(StructureMarkerKind kind,string data,BlockCoordinate size,int rotation,BlockCoordinate origin)
	{
		if(kind is not (StructureMarkerKind.Bed or StructureMarkerKind.ItemBasket or StructureMarkerKind.Plant))return data;
		JsonObject root=JsonNode.Parse(data)?.AsObject()??throw new InvalidDataException($"{kind} marker data must be an object.");
		if(kind is StructureMarkerKind.Bed or StructureMarkerKind.ItemBasket){byte facing=kind==StructureMarkerKind.Bed?ParseBed(data).Facing:ParseBasket(data).Facing;root["facing"]=FacingName(RotateFacing(facing,rotation));}
		else{PlantMarkerData plant=ParsePlant(data);BlockCoordinate support=origin+WorldStructurePlanner.RotateLocalPosition(plant.Support,size,rotation);root["support"]=new JsonArray(support.X,support.Y,support.Z);}
		return root.ToJsonString();
	}
	public static byte RotateFacing(byte facing,int rotation)=>checked((byte)((facing+rotation/90)&3));
	private static JsonElement ParseObject(string data,string name){if(string.IsNullOrWhiteSpace(data))throw new InvalidDataException($"{name} marker data is required.");try{JsonDocument document=JsonDocument.Parse(data);JsonElement clone=document.RootElement.Clone();if(clone.ValueKind!=JsonValueKind.Object)throw new InvalidDataException($"{name} marker data must be an object.");return clone;}catch(JsonException exception){throw new InvalidDataException($"{name} marker data is invalid.",exception);}}
	private static byte ReadFacing(JsonElement root){if(!root.TryGetProperty("facing",out JsonElement value))throw new InvalidDataException("Furniture marker facing is required.");if(value.ValueKind==JsonValueKind.Number){int number=value.GetInt32();if(number is>=0 and<=3)return(byte)number;}if(value.ValueKind==JsonValueKind.String)return value.GetString() switch{"North"=>0,"East"=>1,"South"=>2,"West"=>3,_=>throw new InvalidDataException("Furniture marker facing is invalid.")};throw new InvalidDataException("Furniture marker facing is invalid.");}
	private static string ReadOptionalString(JsonElement root,string property){if(!root.TryGetProperty(property,out JsonElement value))return null;if(value.ValueKind!=JsonValueKind.String||string.IsNullOrWhiteSpace(value.GetString())||value.GetString().Length>64)throw new InvalidDataException($"Marker property {property} is invalid.");return value.GetString();}
	private static string FacingName(byte facing)=>facing switch{0=>"North",1=>"East",2=>"South",3=>"West",_=>throw new ArgumentOutOfRangeException(nameof(facing))};
}
