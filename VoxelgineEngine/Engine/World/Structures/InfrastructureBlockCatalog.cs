namespace Voxelgine.Engine.World.Structures;

public enum InfrastructureComponentKind : byte
{
	Structure,
	PowerSupply,
	Conduit,
	Terminal,
	Logic,
	Function,
}

public enum InfrastructureFunctionKind : byte
{
	Relay,
	GravityAnchor,
	Transit,
	Fabricator,
}

public sealed record InfrastructureBlockDefinition(
	BlockType Block,
	InfrastructureComponentKind Component,
	InfrastructureFunctionKind? Function,
	int PowerSupply,
	int PowerDemand,
	int StructuralPoints);

public static class InfrastructureBlockCatalog
{
	private static readonly IReadOnlyDictionary<BlockType, InfrastructureBlockDefinition> Definitions =
		new Dictionary<BlockType, InfrastructureBlockDefinition>
		{
			[BlockType.SteelFrame] = Define(BlockType.SteelFrame, InfrastructureComponentKind.Structure, structure: 2),
			[BlockType.MachineCasing] = Define(BlockType.MachineCasing, InfrastructureComponentKind.Structure, structure: 1),
			[BlockType.PowerCell] = Define(BlockType.PowerCell, InfrastructureComponentKind.PowerSupply, supply: 4),
			[BlockType.PowerConduit] = Define(BlockType.PowerConduit, InfrastructureComponentKind.Conduit),
			[BlockType.ControlTerminal] = Define(BlockType.ControlTerminal, InfrastructureComponentKind.Terminal, demand: 1),
			[BlockType.LogicCore] = Define(BlockType.LogicCore, InfrastructureComponentKind.Logic, demand: 1),
			[BlockType.RelayEmitter] = Define(BlockType.RelayEmitter, InfrastructureComponentKind.Function, InfrastructureFunctionKind.Relay, demand: 4),
			[BlockType.GravityCoil] = Define(BlockType.GravityCoil, InfrastructureComponentKind.Function, InfrastructureFunctionKind.GravityAnchor, demand: 3),
			[BlockType.LinearActuator] = Define(BlockType.LinearActuator, InfrastructureComponentKind.Function, InfrastructureFunctionKind.Transit, demand: 2),
			[BlockType.FabricatorCore] = Define(BlockType.FabricatorCore, InfrastructureComponentKind.Function, InfrastructureFunctionKind.Fabricator, demand: 6),
		};

	public static IReadOnlyCollection<InfrastructureBlockDefinition> All { get; } =
		Definitions.Values.OrderBy(static definition => definition.Block).ToArray();

	public static InfrastructureBlockDefinition Get(BlockType block) =>
		Definitions.TryGetValue(block, out InfrastructureBlockDefinition definition)
			? definition
			: throw new KeyNotFoundException($"Block {block} is not an infrastructure block.");

	public static bool TryGet(BlockType block, out InfrastructureBlockDefinition definition) =>
		Definitions.TryGetValue(block, out definition);

	private static InfrastructureBlockDefinition Define(
		BlockType block,
		InfrastructureComponentKind component,
		InfrastructureFunctionKind? function = null,
		int supply = 0,
		int demand = 0,
		int structure = 0) =>
		new(block, component, function, supply, demand, structure);
}
