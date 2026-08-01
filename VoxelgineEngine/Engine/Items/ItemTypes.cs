using System;

namespace Voxelgine.Engine;

public readonly record struct ItemId(ushort Value)
{
	public static readonly ItemId Empty = new(0);

	public bool IsEmpty => Value == 0;

	public override string ToString() => Value.ToString();
}

public readonly record struct ItemStack(ItemId Item, ushort Count)
{
	public static readonly ItemStack Empty = new(ItemId.Empty, 0);

	public bool IsEmpty => Item.IsEmpty;

	public static ItemStack Create(ItemId item, int count)
	{
		if (item.IsEmpty)
		{
			if (count != 0)
				throw new ArgumentOutOfRangeException(nameof(count), "An empty item must have a zero count.");
			return Empty;
		}

		if (count is <= 0 or > ushort.MaxValue)
			throw new ArgumentOutOfRangeException(nameof(count));

		return new ItemStack(item, checked((ushort)count));
	}
}

[Flags]
public enum ToolCapabilities : ushort
{
	None = 0,
	FireWeapon = 1 << 0,
	BreakBlocks = 1 << 1,
}

public sealed record ItemDefinition(
	ItemId Id,
	string DisplayName,
	ushort MaximumStack,
	BlockType? PlacesBlock,
	ToolCapabilities Capabilities);

public sealed record BlockGameplayDefinition(
	BlockType Block,
	bool BreakableByHand,
	ItemStack Drop,
	bool DropsItem);

