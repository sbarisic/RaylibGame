using Voxelgine.WorldGeneration;

namespace Voxelgine.Engine.World.Structures;

/// <summary>Mutable, undoable editing model used by the CeramicFish Village Lab.</summary>
public sealed class CeramicVillageEditingSession
{
	private readonly Stack<CeramicFishDefinition> undo = [];
	private readonly Stack<CeramicFishDefinition> redo = [];
	private CeramicFishDefinition definition;
	private long savedRevision;

	public CeramicVillageEditingSession(CeramicVillageCatalog catalog)
	{
		ArgumentNullException.ThrowIfNull(catalog);
		definition = Clone(catalog.Definition);
	}

	public CeramicFishDefinition Definition => definition;
	public IReadOnlyList<CeramicPrefabDefinition> Prefabs => definition.Prefabs;
	public long Revision { get; private set; }
	public bool IsDirty => Revision != savedRevision;
	public bool CanUndo => undo.Count != 0;
	public bool CanRedo => redo.Count != 0;

	public CeramicPrefabDefinition Get(string id) => Prefabs.First(prefab => prefab.Id == id);

	public void ReplacePrefab(CeramicPrefabDefinition prefab)
	{
		ArgumentNullException.ThrowIfNull(prefab);
		int index = Prefabs.ToList().FindIndex(value => value.Id == prefab.Id);
		if (index < 0) throw new KeyNotFoundException($"Unknown CeramicFish prefab '{prefab.Id}'.");
		CeramicPrefabDefinition[] values = Prefabs.ToArray();
		values[index] = Clone(prefab);
		ReplaceDefinition(definition with { Prefabs = values });
	}

	public void AddPrefab(CeramicPrefabDefinition prefab)
	{
		ArgumentNullException.ThrowIfNull(prefab);
		if (Prefabs.Any(value => value.Id == prefab.Id)) throw new InvalidDataException($"Prefab ID '{prefab.Id}' already exists.");
		ReplaceDefinition(definition with { Prefabs = [.. Prefabs, Clone(prefab)] });
	}

	public void RemovePrefab(string id)
	{
		if (Prefabs.Count <= 1) throw new InvalidOperationException("A CeramicFish definition must retain at least one prefab.");
		CeramicPrefabDefinition[] values = Prefabs.Where(value => value.Id != id).ToArray();
		if (values.Length == Prefabs.Count) throw new KeyNotFoundException($"Unknown CeramicFish prefab '{id}'.");
		ReplaceDefinition(definition with { Prefabs = values });
	}

	public void ReplaceDefinition(CeramicFishDefinition value)
	{
		ArgumentNullException.ThrowIfNull(value);
		undo.Push(Clone(definition));
		while (undo.Count > 100)
		{
			CeramicFishDefinition[] keep = undo.Reverse().TakeLast(100).ToArray();
			undo.Clear();
			foreach (CeramicFishDefinition item in keep) undo.Push(item);
		}
		redo.Clear();
		definition = Clone(value);
		Revision++;
	}

	public bool Undo()
	{
		if (!undo.TryPop(out CeramicFishDefinition value)) return false;
		redo.Push(Clone(definition));
		definition = value;
		Revision++;
		return true;
	}

	public bool Redo()
	{
		if (!redo.TryPop(out CeramicFishDefinition value)) return false;
		undo.Push(Clone(definition));
		definition = value;
		Revision++;
		return true;
	}

	public void MarkSaved(long revision)
	{
		if (revision > Revision) throw new ArgumentOutOfRangeException(nameof(revision));
		savedRevision = revision;
	}

	public static CeramicPrefabDefinition EmptyPrefab(string id) => new(
		id, [], CeramicVillageCatalog.PrefabWidth, CeramicVillageCatalog.PrefabHeight,
		CeramicVillageCatalog.PrefabLength, [],
		Enum.GetValues<CeramicDirection>().Select(direction => new CeramicSocket(direction, CeramicSocket.NoConnection)).ToArray(),
		CeramicRotationOptions.All, 1);

	private static CeramicFishDefinition Clone(CeramicFishDefinition value) => value with
	{
		Prefabs = value.Prefabs.Select(Clone).ToArray(),
		ConnectionPolicies = value.ConnectionPolicies.ToArray(),
		ComponentAdjacencyPolicies = value.ComponentAdjacencyPolicies.ToArray(),
		ComponentTagPolicies = value.ComponentTagPolicies.ToArray(),
		ComponentEntryPolicies = value.ComponentEntryPolicies.ToArray(),
		WallFeaturePolicies = value.WallFeaturePolicies.ToArray(),
	};

	private static CeramicPrefabDefinition Clone(CeramicPrefabDefinition value) => value with
	{
		Tags = value.Tags.ToArray(),
		Entities = value.Entities.ToArray(),
		Sockets = value.Sockets.ToArray(),
	};
}
