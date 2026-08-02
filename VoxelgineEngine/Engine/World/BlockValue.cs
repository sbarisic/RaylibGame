using Voxelgine.Engine;

namespace Voxelgine.Graphics;

/// <summary>
/// Canonical persisted and networked voxel value. Lighting remains transient
/// chunk data and is intentionally not part of this value.
/// </summary>
public readonly record struct BlockValue
{
	public BlockValue(BlockType type, byte state = 0)
	{
		BlockStateCatalog.Validate(type, state);
		Type = type;
		State = state;
	}

	public BlockType Type { get; }

	public byte State { get; }

	public static BlockValue Empty => new(BlockType.None);
}

/// <summary>Validates block-state bytes before they enter authoritative storage.</summary>
public static class BlockStateCatalog
{
	public const byte ReservedHighBitsMask = 0b1111_1000;

	public static void Validate(BlockType type, byte state)
	{
		if (!Enum.IsDefined(type))
			throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown block type.");
		if ((state & ReservedHighBitsMask) != 0)
			throw new ArgumentOutOfRangeException(nameof(state), state, "Block-state bits 3-7 are reserved.");
		bool stateful = type is BlockType.StoneStairs or BlockType.WoodStairs or BlockType.ConcreteStairs;
		if (state != 0 && !stateful)
			throw new ArgumentOutOfRangeException(nameof(state), state, $"Block type '{type}' does not define state semantics.");
	}

	public static bool IsValid(BlockType type, byte state)
	{
		try
		{
			Validate(type, state);
			return true;
		}
		catch (ArgumentOutOfRangeException)
		{
			return false;
		}
	}
}
