using Voxelgine.WorldGeneration;

namespace Voxelgine.Engine.World.Structures;

public sealed class VillagePrefabEditingSession
{
	private readonly List<VillagePrefab> prefabs;
	private readonly List<string> socketSemantics;
	private readonly List<VillageAdjacencyRuleDescriptor> adjacencyRules;

	public VillagePrefabEditingSession(VillagePrefabCatalog catalog)
	{
		ArgumentNullException.ThrowIfNull(catalog);
		prefabs = catalog.Prefabs.ToList();
		socketSemantics = catalog.SocketSemantics.ToList();
		adjacencyRules = catalog.AdjacencyRules.ToList();
		ExternalEntrySemantic = catalog.ExternalEntrySemantic;
	}

	public IReadOnlyList<VillagePrefab> Prefabs => prefabs;
	public IReadOnlyList<string> SocketSemantics => socketSemantics;
	public string ExternalEntrySemantic { get; private set; }
	public IReadOnlyList<VillageAdjacencyRuleDescriptor> AdjacencyRules => adjacencyRules;
	public long Revision { get; private set; }
	public long SavedRevision { get; private set; }
	public bool IsDirty => Revision != SavedRevision;

	public void Replace(VillagePrefab prefab)
	{
		ArgumentNullException.ThrowIfNull(prefab);
		int index = prefabs.FindIndex(value => value.Descriptor.Id == prefab.Descriptor.Id);
		if (index < 0) throw new KeyNotFoundException($"Unknown village prefab '{prefab.Descriptor.Id}'.");
		prefabs[index] = prefab;
		Revision++;
	}

	public void Add(VillagePrefab prefab)
	{
		ArgumentNullException.ThrowIfNull(prefab);
		if (prefabs.Any(value => value.Descriptor.Id == prefab.Descriptor.Id))
			throw new InvalidDataException($"A prefab named '{prefab.Descriptor.Id}' already exists.");
		prefabs.Add(prefab);
		Revision++;
	}

	public void Remove(string id)
	{
		if (prefabs.Count <= 1) throw new InvalidOperationException("A catalog must contain at least one prefab.");
		if (prefabs.RemoveAll(value => value.Descriptor.Id == id) == 0)
			throw new KeyNotFoundException($"Unknown village prefab '{id}'.");
		Revision++;
	}

	public string AddSemantic(string value)
	{
		value = VillagePrefabCatalog.ValidateSocketSemantic(value);
		if (socketSemantics.Contains(value, StringComparer.Ordinal))
			throw new InvalidDataException($"Socket semantic '{value}' already exists.");
		socketSemantics.Add(value);
		Revision++;
		return value;
	}

	public void RemoveSemantic(string value)
	{
		if (value == ExternalEntrySemantic) throw new InvalidDataException($"'{value}' is the external-entry semantic and cannot be removed.");
		int users = prefabs.Count(prefab => prefab.Descriptor.Sockets.Any(socket => socket.Types.Contains(value, StringComparer.Ordinal)));
		if (users != 0) throw new InvalidDataException($"'{value}' is used by {users} prefab(s). Change those sockets first.");
		if (!socketSemantics.Remove(value)) throw new KeyNotFoundException($"Unknown socket semantic '{value}'.");
		Revision++;
	}

	public void SetExternalEntrySemantic(string value)
	{
		value = VillagePrefabCatalog.ValidateSocketSemantic(value);
		if (!socketSemantics.Contains(value, StringComparer.Ordinal))
			throw new InvalidDataException($"Socket semantic '{value}' is not defined.");
		if (ExternalEntrySemantic == value) return;
		ExternalEntrySemantic = value;
		Revision++;
	}

	public void AddAdjacencyRule(VillageAdjacencyRuleDescriptor rule)
	{
		rule.Validate();
		if (adjacencyRules.Any(value => value.Id == rule.Id)) throw new InvalidDataException($"An adjacency rule named '{rule.Id}' already exists.");
		adjacencyRules.Add(rule); Revision++;
	}

	public void RemoveAdjacencyRule(string id)
	{
		if (adjacencyRules.RemoveAll(value => value.Id == id) == 0) throw new KeyNotFoundException($"Unknown adjacency rule '{id}'.");
		Revision++;
	}

	public (VillagePrefab[] Prefabs, string[] Semantics, string ExternalEntrySemantic,
		VillageAdjacencyRuleDescriptor[] AdjacencyRules, long Revision) Snapshot() =>
		(prefabs.ToArray(), socketSemantics.ToArray(), ExternalEntrySemantic, adjacencyRules.ToArray(), Revision);

	public void MarkSaved(long revision)
	{
		if (revision != Revision) throw new InvalidOperationException("The editing session changed while it was being saved.");
		SavedRevision = revision;
	}
}
