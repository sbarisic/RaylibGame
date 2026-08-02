using System.Numerics;

namespace Voxelgine.Engine;

internal readonly record struct BlockFaceTextureTiles(
	int PositiveX,
	int NegativeX,
	int PositiveY,
	int NegativeY,
	int PositiveZ,
	int NegativeZ)
{
	internal BlockFaceTextureTiles(int uniformTile)
		: this(uniformTile, uniformTile, uniformTile, uniformTile, uniformTile, uniformTile)
	{
	}

	internal BlockFaceTextureTiles(int sideTile, int capTile)
		: this(sideTile, sideTile, capTile, capTile, sideTile, sideTile)
	{
	}

	internal int GetTile(Vector3 face) => face switch
	{
		{ X: 1, Y: 0, Z: 0 } => PositiveX,
		{ X: -1, Y: 0, Z: 0 } => NegativeX,
		{ X: 0, Y: 1, Z: 0 } => PositiveY,
		{ X: 0, Y: -1, Z: 0 } => NegativeY,
		{ X: 0, Y: 0, Z: 1 } => PositiveZ,
		{ X: 0, Y: 0, Z: -1 } => NegativeZ,
		_ => throw new ArgumentOutOfRangeException(nameof(face), face, "Face must be an axis-aligned unit vector."),
	};
}

internal readonly record struct MachineBlockTextureDefinition(
	BlockType Block,
	string DisplayName,
	BlockFaceTextureTiles Faces);

internal static class MachineBlockTextureCatalog
{
	internal static IReadOnlyList<MachineBlockTextureDefinition> All { get; } =
	[
		new(BlockType.SteelFrame, "Steel Frame", new BlockFaceTextureTiles(22)),
		new(BlockType.MachineCasing, "Machine Casing", new BlockFaceTextureTiles(23, 24)),
		new(BlockType.PowerCell, "Power Cell", new BlockFaceTextureTiles(25, 26)),
		new(BlockType.PowerConduit, "Power Conduit", new BlockFaceTextureTiles(40, 41)),
		new(
			BlockType.ControlTerminal,
			"Control Terminal",
			new BlockFaceTextureTiles(31, 31, 31, 31, 31, 39)),
		new(BlockType.LogicCore, "Logic Core", new BlockFaceTextureTiles(27, 28)),
		new(BlockType.RelayEmitter, "Relay Emitter", new BlockFaceTextureTiles(42, 43)),
		new(BlockType.GravityCoil, "Gravity Coil", new BlockFaceTextureTiles(44, 45)),
		new(BlockType.LinearActuator, "Linear Actuator", new BlockFaceTextureTiles(46, 47)),
		new(BlockType.FabricatorCore, "Fabricator Core", new BlockFaceTextureTiles(29, 30)),
	];

	internal static bool TryGet(BlockType block, out MachineBlockTextureDefinition definition)
	{
		foreach (MachineBlockTextureDefinition candidate in All)
		{
			if (candidate.Block != block)
				continue;

			definition = candidate;
			return true;
		}

		definition = default;
		return false;
	}
}
