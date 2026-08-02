using Voxelgine.Engine.DI;
using Voxelgine.Graphics;

namespace Voxelgine.Engine.World.Structures;

public readonly record struct MachineKey(
	BlockCoordinate FunctionCoordinate,
	InfrastructureFunctionKind Function) : IComparable<MachineKey>
{
	public int CompareTo(MachineKey other)
	{
		int comparison = FunctionCoordinate.CompareTo(other.FunctionCoordinate);
		return comparison != 0 ? comparison : Function.CompareTo(other.Function);
	}
}

public enum InfrastructureMachineState : byte
{
	Disabled,
	UnpoweredDirty,
	MissingComponents,
	InsufficientPower,
	Active,
	Removed,
}

public readonly record struct InfrastructureMachineSnapshot(
	MachineKey Key,
	bool RequestedEnabled,
	InfrastructureMachineState State,
	int PowerSupply,
	int PowerDemand,
	int StructuralPoints,
	string MissingRequirements,
	GeneratedMarkerId? GeneratedMarker);

public readonly record struct DirtyNetworkWork(long WorkId, BlockCoordinate SortCoordinate);

public sealed class InfrastructureMachineService : IDisposable
{
	private static readonly BlockCoordinate[] Neighbors =
	[
		new(1, 0, 0), new(-1, 0, 0), new(0, 1, 0),
		new(0, -1, 0), new(0, 0, 1), new(0, 0, -1),
	];

	private readonly ChunkMap world;
	private readonly IFishLogging logging;
	private readonly Dictionary<BlockCoordinate, InfrastructureBlockDefinition> components = new();
	private readonly Dictionary<MachineKey, MachineRecord> machines = new();
	private readonly SortedSet<QueuedDirtyWork> dirty = new();
	private readonly Dictionary<BlockCoordinate, QueuedDirtyWork> dirtyBySeed = new();
	private readonly Dictionary<ChunkColumnCoordinate, HashSet<BlockCoordinate>> columnEntries = new();
	private long mutationSequence;
	private long dirtyOrdinal;
	private bool disposed;

	public InfrastructureMachineService(ChunkMap world, WorldFeaturePlan features, IFishLogging logging)
	{
		this.world = world ?? throw new ArgumentNullException(nameof(world));
		this.logging = logging ?? throw new ArgumentNullException(nameof(logging));
		IndexExistingWorld(features ?? WorldFeaturePlan.Empty);
		world.BlockChanged += OnBlockChanged;
		world.ColumnReplacing += OnColumnReplacing;
		world.ColumnCommitted += OnColumnCommitted;
	}

	public event Action<InfrastructureMachineSnapshot> StateChanged;

	public IReadOnlyList<InfrastructureMachineSnapshot> Machines => machines.Values
		.Select(static record => record.Snapshot)
		.OrderBy(static snapshot => snapshot.Key)
		.ToArray();

	public bool TryGet(MachineKey key, out InfrastructureMachineSnapshot snapshot)
	{
		if (machines.TryGetValue(key, out MachineRecord record))
		{
			snapshot = record.Snapshot;
			return true;
		}
		snapshot = default;
		return false;
	}

	public bool SetRequestedEnabled(MachineKey key, bool enabled)
	{
		if (!machines.TryGetValue(key, out MachineRecord record) || record.RequestedEnabled == enabled)
			return false;
		record.RequestedEnabled = enabled;
		long sequence = NextMutationSequence();
		QueueNetworksFromSeeds([key.FunctionCoordinate], sequence, key.FunctionCoordinate, "requested state changed");
		return true;
	}

	public void RestoreRequestedStates(IEnumerable<(MachineKey Key, bool RequestedEnabled)> states)
	{
		foreach ((MachineKey key, bool requested) in states.OrderBy(static state => state.Key))
		{
			if (!machines.TryGetValue(key, out MachineRecord record))
			{
				logging.Log(GameLogLevel.Warning, "Infrastructure", $"discarded stale machine intent key={key}");
				continue;
			}
			record.RequestedEnabled = requested;
			long sequence = NextMutationSequence();
			QueueNetworksFromSeeds([key.FunctionCoordinate], sequence, key.FunctionCoordinate, "restored requested state");
		}
	}

	public IReadOnlyList<(MachineKey Key, bool RequestedEnabled)> CaptureRequestedStates() => machines.Values
		.Where(static record => record.RequestedEnabled)
		.Select(static record => (record.Key, record.RequestedEnabled))
		.OrderBy(static value => value.Key)
		.ToArray();

	public void Update(int maximumNetworks)
	{
		if (maximumNetworks < 0)
			throw new ArgumentOutOfRangeException(nameof(maximumNetworks));
		for (int count = 0; count < maximumNetworks && dirty.Count > 0; count++)
		{
			QueuedDirtyWork work = dirty.Min;
			dirty.Remove(work);
			dirtyBySeed.Remove(work.Seed);
			HashSet<BlockCoordinate> network = CaptureNetwork(work.Seed);
			if (network.Count == 0)
				continue;
			CoalesceQueuedNetwork(network);
			RebuildNetwork(network);
		}
	}

	private void IndexExistingWorld(WorldFeaturePlan features)
	{
		foreach (KeyValuePair<BlockCoordinate, BlockType> entry in world.GetInfrastructureBlocks())
			AddComponent(entry.Key, InfrastructureBlockCatalog.Get(entry.Value));

		foreach (PlannedMarker marker in features.Markers.Where(static marker => marker.Kind == StructureMarkerKind.MachineFunction))
		{
			if (marker.ExpectedBlock == null || !InfrastructureBlockCatalog.TryGet(marker.ExpectedBlock.Value, out InfrastructureBlockDefinition definition) || definition.Function == null)
				continue;
			MachineKey key = new(marker.Position, definition.Function.Value);
			if (machines.TryGetValue(key, out MachineRecord record))
				record.GeneratedMarker = marker.Id;
		}

		MachineRecord[] orderedMachines = machines.Values.OrderBy(static record => record.Key).ToArray();
		if (orderedMachines.Length != 0)
		{
			long sequence = NextMutationSequence();
			QueueNetworksFromSeeds(
				orderedMachines.Select(static record => record.Key.FunctionCoordinate),
				sequence,
				orderedMachines[0].Key.FunctionCoordinate,
				"initializing");
		}
	}

	private void AddComponent(BlockCoordinate coordinate, InfrastructureBlockDefinition definition)
	{
		components[coordinate] = definition;
		ChunkColumnCoordinate column = new(FloorDiv(coordinate.X, Chunk.ChunkSize), FloorDiv(coordinate.Z, Chunk.ChunkSize));
		if (!columnEntries.TryGetValue(column, out HashSet<BlockCoordinate> entries))
		{
			entries = new HashSet<BlockCoordinate>();
			columnEntries.Add(column, entries);
		}
		entries.Add(coordinate);
		if (definition.Function != null)
		{
			MachineKey key = new(coordinate, definition.Function.Value);
			machines.TryAdd(key, new MachineRecord(key));
		}
	}

	private void OnBlockChanged(BlockChange change)
	{
		BlockCoordinate coordinate = new(change.X, change.Y, change.Z);
		bool hadInfrastructure = InfrastructureBlockCatalog.TryGet(change.OldType, out InfrastructureBlockDefinition oldDefinition);
		bool hasInfrastructure = InfrastructureBlockCatalog.TryGet(change.NewType, out InfrastructureBlockDefinition newDefinition);
		if (!hadInfrastructure && !hasInfrastructure)
			return;
		HashSet<BlockCoordinate> oldNetwork = hadInfrastructure
			? CaptureNetwork(coordinate)
			: new HashSet<BlockCoordinate>();
		PublishDirty(oldNetwork, "network mutation pending");
		if (hadInfrastructure)
			RemoveComponent(coordinate, oldDefinition);
		if (hasInfrastructure)
			AddComponent(coordinate, newDefinition);

		long sequence = NextMutationSequence();
		IEnumerable<BlockCoordinate> seeds = components.ContainsKey(coordinate)
			? [coordinate]
			: Neighbors.Select(direction => coordinate + direction).Where(components.ContainsKey);
		QueueNetworksFromSeeds(seeds, sequence, coordinate, "network dirty");
	}

	private void OnColumnReplacing(ChunkColumnCoordinate column)
	{
		if (!columnEntries.TryGetValue(column, out HashSet<BlockCoordinate> entries))
			return;
		BlockCoordinate[] removedCoordinates = entries.OrderBy(static value => value).ToArray();
		HashSet<BlockCoordinate> affected = new();
		HashSet<BlockCoordinate> inspected = new();
		HashSet<BlockCoordinate> survivingSeeds = new();
		foreach (BlockCoordinate coordinate in removedCoordinates)
		{
			if (!inspected.Contains(coordinate))
			{
				HashSet<BlockCoordinate> network = CaptureNetwork(coordinate);
				inspected.UnionWith(network);
				affected.UnionWith(network);
			}
			foreach (BlockCoordinate direction in Neighbors)
			{
				BlockCoordinate neighbor = coordinate + direction;
				if (!entries.Contains(neighbor) && components.ContainsKey(neighbor))
					survivingSeeds.Add(neighbor);
			}
		}
		PublishDirty(affected, "network replacement pending");
		foreach (BlockCoordinate coordinate in removedCoordinates)
		{
			if (components.TryGetValue(coordinate, out InfrastructureBlockDefinition definition))
				RemoveComponent(coordinate, definition);
		}
		columnEntries.Remove(column);
		if (removedCoordinates.Length != 0)
		{
			long sequence = NextMutationSequence();
			QueueNetworksFromSeeds(survivingSeeds, sequence, removedCoordinates[0], "column replacement split network");
		}
	}

	private void OnColumnCommitted(ChunkColumnCoordinate column)
	{
		ChunkColumnSnapshot snapshot = world.CaptureColumn(column.X, column.Z);
		List<BlockCoordinate> added = new();
		foreach (ChunkSnapshot chunk in snapshot.Chunks.OrderBy(static value => value.ChunkY))
		{
			ReadOnlySpan<BlockType> blocks = chunk.BlockMemory.Span;
			for (int index = 0; index < blocks.Length; index++)
			{
				if (!InfrastructureBlockCatalog.TryGet(blocks[index], out InfrastructureBlockDefinition definition))
					continue;
				int x = index % Chunk.ChunkSize;
				int yz = index / Chunk.ChunkSize;
				int y = yz % Chunk.ChunkSize;
				int z = yz / Chunk.ChunkSize;
				BlockCoordinate coordinate = new(column.X * Chunk.ChunkSize + x, chunk.ChunkY * Chunk.ChunkSize + y, column.Z * Chunk.ChunkSize + z);
				AddComponent(coordinate, definition);
				added.Add(coordinate);
			}
		}
		if (added.Count != 0)
		{
			added.Sort();
			long sequence = NextMutationSequence();
			QueueNetworksFromSeeds(added, sequence, added[0], "column committed");
		}
	}

	private void RemoveComponent(BlockCoordinate coordinate, InfrastructureBlockDefinition definition)
	{
		if (dirtyBySeed.Remove(coordinate, out QueuedDirtyWork queued))
			dirty.Remove(queued);
		components.Remove(coordinate);
		ChunkColumnCoordinate column = new(FloorDiv(coordinate.X, Chunk.ChunkSize), FloorDiv(coordinate.Z, Chunk.ChunkSize));
		if (columnEntries.TryGetValue(column, out HashSet<BlockCoordinate> entries))
		{
			entries.Remove(coordinate);
			if (entries.Count == 0)
				columnEntries.Remove(column);
		}
		if (definition.Function != null)
		{
			MachineKey key = new(coordinate, definition.Function.Value);
			if (machines.Remove(key, out MachineRecord record))
			{
				record.RequestedEnabled = false;
				Publish(record, InfrastructureMachineState.Removed, 0, 0, 0, "function block removed");
			}
		}
	}

	private long NextMutationSequence() => checked(++mutationSequence);

	private void QueueNetworksFromSeeds(
		IEnumerable<BlockCoordinate> seeds,
		long workId,
		BlockCoordinate sortCoordinate,
		string reason)
	{
		HashSet<BlockCoordinate> handled = new();
		foreach (BlockCoordinate seed in seeds.Distinct().OrderBy(static value => value))
		{
			if (handled.Contains(seed) || !components.ContainsKey(seed))
				continue;
			HashSet<BlockCoordinate> network = CaptureNetwork(seed);
			handled.UnionWith(network);
			PublishDirty(network, reason);
			BlockCoordinate canonicalSeed = network.Min();
			QueueNetwork(canonicalSeed, workId, sortCoordinate);
		}
	}

	private void QueueNetwork(BlockCoordinate seed, long workId, BlockCoordinate sortCoordinate)
	{
		DirtyNetworkWork candidateWork = new(workId, sortCoordinate);
		if (dirtyBySeed.TryGetValue(seed, out QueuedDirtyWork existing))
		{
			BlockCoordinate combinedCoordinate = existing.Work.SortCoordinate.CompareTo(sortCoordinate) <= 0
				? existing.Work.SortCoordinate
				: sortCoordinate;
			long combinedWorkId = Math.Min(existing.Work.WorkId, workId);
			if (combinedCoordinate == existing.Work.SortCoordinate && combinedWorkId == existing.Work.WorkId)
				return;
			dirty.Remove(existing);
			candidateWork = new DirtyNetworkWork(combinedWorkId, combinedCoordinate);
		}
		QueuedDirtyWork queued = new(candidateWork, seed, ++dirtyOrdinal);
		dirty.Add(queued);
		dirtyBySeed[seed] = queued;
	}

	private HashSet<BlockCoordinate> CaptureNetwork(BlockCoordinate seed)
	{
		HashSet<BlockCoordinate> visited = new();
		if (!components.ContainsKey(seed))
			return visited;
		Queue<BlockCoordinate> queue = new();
		queue.Enqueue(seed);
		while (queue.TryDequeue(out BlockCoordinate coordinate))
		{
			if (!visited.Add(coordinate) || !components.ContainsKey(coordinate))
				continue;
			foreach (BlockCoordinate direction in Neighbors)
			{
				BlockCoordinate neighbor = coordinate + direction;
				if (!visited.Contains(neighbor) && components.ContainsKey(neighbor))
					queue.Enqueue(neighbor);
			}
		}
		return visited;
	}

	private void CoalesceQueuedNetwork(HashSet<BlockCoordinate> network)
	{
		foreach (BlockCoordinate seed in network)
		{
			if (!dirtyBySeed.Remove(seed, out QueuedDirtyWork queued))
				continue;
			dirty.Remove(queued);
		}
	}

	private void PublishDirty(HashSet<BlockCoordinate> network, string reason)
	{
		foreach (MachineRecord machine in machines.Values
			.Where(machine => network.Contains(machine.Key.FunctionCoordinate))
			.OrderBy(static machine => machine.Key))
		{
			Publish(machine, InfrastructureMachineState.UnpoweredDirty, 0, 0, 0, reason);
		}
	}

	private void RebuildNetwork(HashSet<BlockCoordinate> visited)
	{
		int supply = visited.Sum(coordinate => components[coordinate].PowerSupply);
		MachineRecord[] networkMachines = machines.Values
			.Where(machine => visited.Contains(machine.Key.FunctionCoordinate))
			.OrderBy(static machine => machine.Key)
			.ToArray();
		Dictionary<MachineKey, MachineEvaluation> evaluations = new();
		HashSet<BlockCoordinate> demandedComponents = new();
		foreach (MachineRecord machine in networkMachines)
		{
			HashSet<BlockCoordinate> assembly = CaptureAssembly(machine.Key.FunctionCoordinate, visited);
			InfrastructureBlockDefinition[] assemblyDefinitions = assembly
				.Select(coordinate => components[coordinate])
				.ToArray();
			(string missing, int requiredStructure) = ValidateRecipe(machine.Key.Function, assemblyDefinitions);
			int structure = assemblyDefinitions.Sum(static definition => definition.StructuralPoints);
			MachineEvaluation evaluation = new(missing, requiredStructure, structure);
			evaluations.Add(machine.Key, evaluation);
			if (!machine.RequestedEnabled || missing.Length != 0 || structure < requiredStructure)
				continue;
			foreach (BlockCoordinate coordinate in assembly)
			{
				if (components[coordinate].PowerDemand > 0)
					demandedComponents.Add(coordinate);
			}
		}
		int demand = demandedComponents.Sum(coordinate => components[coordinate].PowerDemand);

		foreach (MachineRecord machine in networkMachines)
		{
			MachineEvaluation evaluation = evaluations[machine.Key];
			InfrastructureMachineState state = !machine.RequestedEnabled
				? InfrastructureMachineState.Disabled
				: evaluation.Missing.Length != 0 || evaluation.StructuralPoints < evaluation.RequiredStructuralPoints
					? InfrastructureMachineState.MissingComponents
					: supply < demand
						? InfrastructureMachineState.InsufficientPower
						: InfrastructureMachineState.Active;
			Publish(machine, state, supply, demand, evaluation.StructuralPoints,
				evaluation.StructuralPoints < evaluation.RequiredStructuralPoints
					? Append(evaluation.Missing, $"structure {evaluation.StructuralPoints}/{evaluation.RequiredStructuralPoints}")
					: evaluation.Missing);
		}
	}

	private HashSet<BlockCoordinate> CaptureAssembly(
		BlockCoordinate functionCoordinate,
		HashSet<BlockCoordinate> network)
	{
		HashSet<BlockCoordinate> visited = new();
		Queue<BlockCoordinate> queue = new();
		queue.Enqueue(functionCoordinate);
		while (queue.TryDequeue(out BlockCoordinate coordinate))
		{
			if (!network.Contains(coordinate) || !components.TryGetValue(coordinate, out InfrastructureBlockDefinition definition) ||
				definition.Component == InfrastructureComponentKind.Conduit || !visited.Add(coordinate))
			{
				continue;
			}
			foreach (BlockCoordinate direction in Neighbors)
			{
				BlockCoordinate neighbor = coordinate + direction;
				if (!visited.Contains(neighbor))
					queue.Enqueue(neighbor);
			}
		}
		return visited;
	}

	private static (string Missing, int RequiredStructure) ValidateRecipe(InfrastructureFunctionKind function, InfrastructureBlockDefinition[] definitions)
	{
		int terminals = Count(definitions, BlockType.ControlTerminal);
		int logic = Count(definitions, BlockType.LogicCore);
		int cells = Count(definitions, BlockType.PowerCell);
		List<string> missing = new();
		Require(missing, "terminal", terminals, 1);
		switch (function)
		{
			case InfrastructureFunctionKind.Relay:
				Require(missing, "emitter", Count(definitions, BlockType.RelayEmitter), 1);
				Require(missing, "logic core", logic, 1);
				Require(missing, "power cell", cells, 2);
				return (string.Join(", ", missing), 8);
			case InfrastructureFunctionKind.GravityAnchor:
				Require(missing, "gravity coil", Count(definitions, BlockType.GravityCoil), 4);
				Require(missing, "logic core", logic, 2);
				Require(missing, "power cell", cells, 4);
				return (string.Join(", ", missing), 24);
			case InfrastructureFunctionKind.Transit:
				Require(missing, "actuator", Count(definitions, BlockType.LinearActuator), 2);
				Require(missing, "logic core", logic, 1);
				Require(missing, "power cell", cells, 2);
				return (string.Join(", ", missing), 12);
			case InfrastructureFunctionKind.Fabricator:
				Require(missing, "fabricator core", Count(definitions, BlockType.FabricatorCore), 1);
				Require(missing, "logic core", logic, 2);
				Require(missing, "power cell", cells, 3);
				return (string.Join(", ", missing), 16);
			default:
				throw new ArgumentOutOfRangeException(nameof(function));
		}
	}

	private void Publish(MachineRecord record, InfrastructureMachineState state, int supply, int demand, int structure, string missing)
	{
		InfrastructureMachineSnapshot next = new(record.Key, record.RequestedEnabled, state, supply, demand, structure, missing, record.GeneratedMarker);
		if (record.Snapshot == next)
			return;
		record.Snapshot = next;
		StateChanged?.Invoke(next);
		logging.Log(GameLogLevel.Debug, "Infrastructure", $"machine key={record.Key} requested={record.RequestedEnabled} state={state} supply={supply} demand={demand} structure={structure} missing={missing}");
	}

	private static int Count(InfrastructureBlockDefinition[] definitions, BlockType block) => definitions.Count(definition => definition.Block == block);
	private static void Require(List<string> missing, string name, int actual, int required) { if (actual < required) missing.Add($"{name} {actual}/{required}"); }
	private static string Append(string value, string addition) => value.Length == 0 ? addition : $"{value}, {addition}";
	private static int FloorDiv(int value, int divisor) => value >= 0 ? value / divisor : (value - divisor + 1) / divisor;

	public void Dispose()
	{
		if (disposed)
			return;
		disposed = true;
		world.BlockChanged -= OnBlockChanged;
		world.ColumnReplacing -= OnColumnReplacing;
		world.ColumnCommitted -= OnColumnCommitted;
	}

	private sealed class MachineRecord
	{
		public MachineRecord(MachineKey key)
		{
			Key = key;
			Snapshot = new InfrastructureMachineSnapshot(key, false, InfrastructureMachineState.UnpoweredDirty, 0, 0, 0, "initializing", null);
		}

		public MachineKey Key { get; }
		public bool RequestedEnabled { get; set; }
		public GeneratedMarkerId? GeneratedMarker { get; set; }
		public InfrastructureMachineSnapshot Snapshot { get; set; }
	}

	private readonly record struct MachineEvaluation(
		string Missing,
		int RequiredStructuralPoints,
		int StructuralPoints);

	private readonly record struct QueuedDirtyWork(DirtyNetworkWork Work, BlockCoordinate Seed, long Ordinal) : IComparable<QueuedDirtyWork>
	{
		public int CompareTo(QueuedDirtyWork other)
		{
			int comparison = Work.SortCoordinate.CompareTo(other.Work.SortCoordinate);
			if (comparison != 0) return comparison;
			comparison = Work.WorkId.CompareTo(other.Work.WorkId);
			return comparison != 0 ? comparison : Ordinal.CompareTo(other.Ordinal);
		}
	}
}
