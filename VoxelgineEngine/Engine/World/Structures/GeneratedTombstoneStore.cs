namespace Voxelgine.Engine.World.Structures;

public enum GeneratedObjectKind : byte { Furniture, WorldObject }
public readonly record struct GeneratedTombstone(GeneratedObjectKind Kind,GeneratedMarkerId MarkerId);

public sealed class GeneratedTombstoneStore
{
	private readonly HashSet<GeneratedTombstone> items=new();
	public int Count=>items.Count;
	public bool Contains(GeneratedObjectKind kind,GeneratedMarkerId marker)=>items.Contains(new GeneratedTombstone(kind,marker));
	public bool Add(GeneratedObjectKind kind,GeneratedMarkerId marker)
	{
		if(string.IsNullOrWhiteSpace(marker.Site.Value)||string.IsNullOrWhiteSpace(marker.BlueprintMarkerId))throw new ArgumentException("Generated marker identity is invalid.",nameof(marker));return items.Add(new GeneratedTombstone(kind,marker));
	}
	public IReadOnlyList<GeneratedTombstone> GetAll()=>items.OrderBy(static item=>item.Kind).ThenBy(static item=>item.MarkerId.Site).ThenBy(static item=>item.MarkerId.BlueprintMarkerId,StringComparer.Ordinal).ToArray();
	public void Restore(IEnumerable<GeneratedTombstone> tombstones){items.Clear();foreach(GeneratedTombstone tombstone in tombstones)if(!Add(tombstone.Kind,tombstone.MarkerId))throw new InvalidDataException("Generated tombstone archive contains a duplicate.");}
}
