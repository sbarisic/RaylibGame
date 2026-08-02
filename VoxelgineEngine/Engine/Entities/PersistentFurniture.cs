using System.Numerics;
using Voxelgine.Engine.World.Structures;

namespace Voxelgine.Engine;

public readonly record struct PersistentEntityId
{
	public PersistentEntityId(ulong value) { if (value == 0) throw new ArgumentOutOfRangeException(nameof(value)); Value = value; }
	public ulong Value { get; }
}

public sealed class PersistentEntityIdAllocator
{
	private ulong next = 1;
	public PersistentEntityId Allocate() => new(next++);
	public void Observe(PersistentEntityId id) => next = Math.Max(next, checked(id.Value + 1));
}

public enum PersistentFurnitureKeyKind : byte { Generated, Placed }

public readonly record struct PersistentFurnitureKey
{
	private PersistentFurnitureKey(PersistentFurnitureKeyKind kind, GeneratedMarkerId marker, PersistentEntityId entity)
	{ Kind=kind; GeneratedMarkerId=marker; PersistentEntityId=entity; }
	public PersistentFurnitureKeyKind Kind { get; }
	public GeneratedMarkerId GeneratedMarkerId { get; }
	public PersistentEntityId PersistentEntityId { get; }
	public static PersistentFurnitureKey Generated(GeneratedMarkerId marker) => new(PersistentFurnitureKeyKind.Generated, marker, default);
	public static PersistentFurnitureKey Placed(PersistentEntityId entity) => new(PersistentFurnitureKeyKind.Placed, default, entity);
	public override string ToString() => Kind == PersistentFurnitureKeyKind.Generated
		? $"g:{GeneratedMarkerId.Site.Value}:{GeneratedMarkerId.BlueprintMarkerId}"
		: $"p:{PersistentEntityId.Value}";
}

public enum FurnitureType : byte { ItemBasket = 1, Bed = 2 }

public readonly record struct PersistentFurnitureRecord(
	PersistentFurnitureKey Key,
	FurnitureType Type,
	BlockCoordinate Anchor,
	byte Facing,
	IReadOnlyList<ItemStack> Slots);

public sealed class FurnitureStore
{
	private readonly Dictionary<PersistentFurnitureKey, PersistentFurnitureRecord> records = new();
	private readonly Dictionary<BlockCoordinate, PersistentFurnitureKey> positions = new();
	private readonly PersistentEntityIdAllocator ids;
	public FurnitureStore(PersistentEntityIdAllocator ids = null) => this.ids = ids ?? new PersistentEntityIdAllocator();
	public int Count => records.Count;
	public PersistentFurnitureKey AllocatePlacedKey() => PersistentFurnitureKey.Placed(ids.Allocate());
	public IReadOnlyList<PersistentFurnitureRecord> GetAll() => records.Values.OrderBy(static record => record.Anchor.X).ThenBy(static record => record.Anchor.Z).ThenBy(static record => record.Anchor.Y).ToArray();
	public bool TryGet(PersistentFurnitureKey key, out PersistentFurnitureRecord record) => records.TryGetValue(key, out record);
	public bool IsCellOccupied(BlockCoordinate cell)=>records.Values.Any(record=>record.Anchor==cell||(record.Type==FurnitureType.Bed&&record.Anchor+VEntBed.FacingOffset(record.Facing)==cell));
	public bool TryGetAt(BlockCoordinate anchor, out PersistentFurnitureRecord record)
	{
		if (positions.TryGetValue(anchor, out PersistentFurnitureKey key)) return records.TryGetValue(key, out record);
		record = default; return false;
	}
	public void Add(PersistentFurnitureRecord record)
	{
		Validate(record);
		if (!records.TryAdd(record.Key, record) || !positions.TryAdd(record.Anchor, record.Key)) throw new InvalidOperationException("Furniture identity or anchor is occupied.");
		if (record.Key.Kind == PersistentFurnitureKeyKind.Placed) ids.Observe(record.Key.PersistentEntityId);
	}
	public bool Remove(PersistentFurnitureKey key, out PersistentFurnitureRecord record)
	{
		if (!records.Remove(key, out record)) return false; positions.Remove(record.Anchor); return true;
	}
	public void Replace(PersistentFurnitureRecord record)
	{
		Validate(record); if (!records.TryGetValue(record.Key, out PersistentFurnitureRecord previous)) throw new KeyNotFoundException();
		if (previous.Anchor != record.Anchor && positions.ContainsKey(record.Anchor)) throw new InvalidOperationException("Furniture anchor is occupied.");
		positions.Remove(previous.Anchor); positions[record.Anchor]=record.Key; records[record.Key]=record;
	}
	public void Restore(IEnumerable<PersistentFurnitureRecord> source)
	{
		records.Clear(); positions.Clear(); foreach (PersistentFurnitureRecord record in source) Add(record);
	}
	private static void Validate(PersistentFurnitureRecord record)
	{
		if (!Enum.IsDefined(record.Type) || record.Facing > 3 || record.Slots == null || record.Slots.Any(static stack=>!ItemCatalog.IsCanonical(stack))) throw new ArgumentException("Furniture record is invalid.", nameof(record));
		int expected=record.Type==FurnitureType.ItemBasket?VEntItemBasket.SlotCount:0; if(record.Slots.Count!=expected) throw new ArgumentException("Furniture slot count is invalid.", nameof(record));
	}
}

public sealed class VEntBed : VoxEntity
{
	public PersistentFurnitureKey PersistentKey { get; private set; }
	public byte Facing { get; private set; }
	public override EntityPhysicsProperties PhysicsProperties => new(false,false,false,true,true,false);
	public VEntBed(){Size=new Vector3(1f,0.55f,2f);SetModelName("furniture/bed.json");}
	public void Initialize(PersistentFurnitureKey key,BlockCoordinate anchor,byte facing)
	{
		if(facing>3)throw new ArgumentOutOfRangeException(nameof(facing));PersistentKey=key;Facing=facing;Position=new Vector3(anchor.X+0.5f,anchor.Y,anchor.Z+0.5f);
	}
	public BlockCoordinate Anchor=>new((int)MathF.Floor(Position.X),(int)MathF.Floor(Position.Y),(int)MathF.Floor(Position.Z));
	public BlockCoordinate HeadCell=>Anchor+FacingOffset(Facing);
	public PersistentFurnitureRecord CaptureRecord()=>new(PersistentKey,FurnitureType.Bed,Anchor,Facing,Array.Empty<ItemStack>());
	public static BlockCoordinate FacingOffset(byte facing)=>facing switch{0=>new BlockCoordinate(0,0,-1),1=>new BlockCoordinate(1,0,0),2=>new BlockCoordinate(0,0,1),3=>new BlockCoordinate(-1,0,0),_=>throw new ArgumentOutOfRangeException(nameof(facing))};
	protected override void WriteSpawnPropertiesExtra(BinaryWriter writer){writer.Write((byte)PersistentKey.Kind);if(PersistentKey.Kind==PersistentFurnitureKeyKind.Generated){writer.Write(PersistentKey.GeneratedMarkerId.Site.Value);writer.Write(PersistentKey.GeneratedMarkerId.BlueprintMarkerId);}else writer.Write(PersistentKey.PersistentEntityId.Value);writer.Write(Facing);}
	protected override void ReadSpawnPropertiesExtra(BinaryReader reader){PersistentFurnitureKeyKind kind=(PersistentFurnitureKeyKind)reader.ReadByte();PersistentKey=kind==PersistentFurnitureKeyKind.Generated?PersistentFurnitureKey.Generated(new GeneratedMarkerId(new GeneratedSiteId(reader.ReadString()),reader.ReadString())):PersistentFurnitureKey.Placed(new PersistentEntityId(reader.ReadUInt64()));Facing=reader.ReadByte();}
}

public sealed class VEntItemBasket : VoxEntity
{
	public const int SlotCount = 12;
	public PersistentFurnitureKey PersistentKey { get; private set; }
	public byte Facing { get; private set; }
	public ContainerInventory Inventory { get; } = new(SlotCount);
	public override EntityPhysicsProperties PhysicsProperties => new(false, false, false, true, true, false);
	public VEntItemBasket() { Size = new Vector3(0.8f, 0.6f, 0.8f); SetModelName("furniture/item_basket.json"); }
	public void Initialize(PersistentFurnitureKey key, BlockCoordinate anchor, byte facing, ReadOnlySpan<ItemStack> slots)
	{
		if(facing>3) throw new ArgumentOutOfRangeException(nameof(facing)); PersistentKey=key; Facing=facing; Inventory.Restore(slots);
		Position=new Vector3(anchor.X+0.5f, anchor.Y, anchor.Z+0.5f);
	}
	public PersistentFurnitureRecord CaptureRecord() => new(PersistentKey, FurnitureType.ItemBasket,
		new BlockCoordinate((int)MathF.Floor(Position.X),(int)MathF.Floor(Position.Y),(int)MathF.Floor(Position.Z)), Facing, Inventory.GetSlots().ToArray());
	protected override void WriteSpawnPropertiesExtra(BinaryWriter writer) { writer.Write((byte)PersistentKey.Kind); if(PersistentKey.Kind==PersistentFurnitureKeyKind.Generated){writer.Write(PersistentKey.GeneratedMarkerId.Site.Value);writer.Write(PersistentKey.GeneratedMarkerId.BlueprintMarkerId);}else writer.Write(PersistentKey.PersistentEntityId.Value); writer.Write(Facing); }
	protected override void ReadSpawnPropertiesExtra(BinaryReader reader) { PersistentFurnitureKeyKind kind=(PersistentFurnitureKeyKind)reader.ReadByte(); PersistentKey=kind==PersistentFurnitureKeyKind.Generated?PersistentFurnitureKey.Generated(new GeneratedMarkerId(new GeneratedSiteId(reader.ReadString()),reader.ReadString())):PersistentFurnitureKey.Placed(new PersistentEntityId(reader.ReadUInt64())); Facing=reader.ReadByte(); }
}
