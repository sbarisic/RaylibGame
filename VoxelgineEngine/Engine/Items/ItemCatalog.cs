using System;
using System.Collections.Generic;
using System.Linq;

namespace Voxelgine.Engine;

public static class ItemIds
{
	public static readonly ItemId Gun = new(1000);
	public static readonly ItemId Hammer = new(1001);
	public static readonly ItemId Bed = new(1002);
	public static readonly ItemId ItemBasket = new(1003);
	public static readonly ItemId WheatSeeds = new(1004);
	public static readonly ItemId Wheat = new(1005);

	public static ItemId FromBlock(BlockType block)
	{
		if (block == BlockType.None)
			return ItemId.Empty;

		ushort value = (ushort)block;
		if (value >= 1000)
			throw new ArgumentOutOfRangeException(nameof(block), block, "Block item IDs must be below 1000.");

		return new ItemId(value);
	}
}

public static class ItemCatalog
{
	private static readonly IReadOnlyDictionary<ItemId, ItemDefinition> Items;
	private static readonly IReadOnlyDictionary<BlockType, BlockGameplayDefinition> Blocks;

	static ItemCatalog()
	{
		var items = new Dictionary<ItemId, ItemDefinition>();
		var blocks = new Dictionary<BlockType, BlockGameplayDefinition>();

		foreach (BlockType block in Enum.GetValues<BlockType>())
		{
			if (block == BlockType.None)
				continue;

			ItemId id = ItemIds.FromBlock(block);
			AddItem(items, new ItemDefinition(id, GetDisplayName(block), 64, block, ToolCapabilities.None));

			bool dropsItem = block is not (
				BlockType.Water or
				BlockType.Foliage or
				BlockType.Leaf or
				BlockType.Test or
				BlockType.Test2);
			ItemStack drop = dropsItem ? new ItemStack(id, 1) : ItemStack.Empty;
			blocks.Add(block, new BlockGameplayDefinition(block, true, drop, dropsItem));
		}

		AddItem(items, new ItemDefinition(ItemIds.Gun, "Gun", 1, null, ToolCapabilities.FireWeapon));
		AddItem(items, new ItemDefinition(ItemIds.Hammer, "Hammer", 1, null, ToolCapabilities.BreakBlocks));
		AddItem(items, new ItemDefinition(ItemIds.Bed, "Bed", 1, null, ToolCapabilities.None));
		AddItem(items, new ItemDefinition(ItemIds.ItemBasket, "Item Basket", 1, null, ToolCapabilities.None));
		AddItem(items, new ItemDefinition(ItemIds.WheatSeeds, "Wheat Seeds", 64, null, ToolCapabilities.None));
		AddItem(items, new ItemDefinition(ItemIds.Wheat, "Wheat", 64, null, ToolCapabilities.None));

		Validate(items, blocks);
		Items = items;
		Blocks = blocks;
	}

	public static IEnumerable<ItemDefinition> AllItems => Items.Values.OrderBy(static item => item.Id.Value);

	public static IEnumerable<BlockGameplayDefinition> AllBlocks => Blocks.Values.OrderBy(static block => (ushort)block.Block);

	public static bool TryGet(ItemId id, out ItemDefinition definition) => Items.TryGetValue(id, out definition);

	public static ItemDefinition Get(ItemId id) =>
		Items.TryGetValue(id, out ItemDefinition definition)
			? definition
			: throw new KeyNotFoundException($"Unknown item ID {id.Value}.");

	public static BlockGameplayDefinition GetBlock(BlockType block) =>
		Blocks.TryGetValue(block, out BlockGameplayDefinition definition)
			? definition
			: throw new KeyNotFoundException($"Missing gameplay definition for block {block}.");

	public static bool IsCanonical(ItemStack stack)
	{
		if (stack.IsEmpty)
			return stack.Item == ItemId.Empty && stack.Count == 0;

		return stack.Count > 0 &&
			Items.TryGetValue(stack.Item, out ItemDefinition definition) &&
			stack.Count <= definition.MaximumStack;
	}

	private static void AddItem(Dictionary<ItemId, ItemDefinition> items, ItemDefinition definition)
	{
		if (definition.Id.IsEmpty)
			throw new InvalidOperationException("Item ID zero is reserved for empty stacks.");
		if (string.IsNullOrWhiteSpace(definition.DisplayName))
			throw new InvalidOperationException($"Item {definition.Id.Value} has no display name.");
		if (definition.MaximumStack == 0)
			throw new InvalidOperationException($"Item {definition.Id.Value} has a zero stack limit.");
		if (definition.Capabilities != ToolCapabilities.None && definition.MaximumStack != 1)
			throw new InvalidOperationException($"Tool item {definition.Id.Value} must have a stack limit of one.");
		if (!items.TryAdd(definition.Id, definition))
			throw new InvalidOperationException($"Duplicate item ID {definition.Id.Value}.");
	}

	private static void Validate(
		IReadOnlyDictionary<ItemId, ItemDefinition> items,
		IReadOnlyDictionary<BlockType, BlockGameplayDefinition> blocks)
	{
		foreach (BlockType block in Enum.GetValues<BlockType>())
		{
			if (block == BlockType.None)
				continue;
			if (!blocks.ContainsKey(block))
				throw new InvalidOperationException($"Missing block gameplay definition for {block}.");
			if (!items.ContainsKey(ItemIds.FromBlock(block)))
				throw new InvalidOperationException($"Missing block item definition for {block}.");
		}

		foreach (BlockGameplayDefinition block in blocks.Values)
		{
			if (block.DropsItem && !IsCanonical(block.Drop, items))
				throw new InvalidOperationException($"Block {block.Block} has an invalid drop.");
			if (!block.DropsItem && !block.Drop.IsEmpty)
				throw new InvalidOperationException($"Non-dropping block {block.Block} has a drop stack.");
		}
	}

	private static bool IsCanonical(
		ItemStack stack,
		IReadOnlyDictionary<ItemId, ItemDefinition> items)
	{
		if (stack.IsEmpty)
			return stack.Item == ItemId.Empty && stack.Count == 0;

		return stack.Count > 0 &&
			items.TryGetValue(stack.Item, out ItemDefinition definition) &&
			stack.Count <= definition.MaximumStack;
	}

	private static string GetDisplayName(BlockType block)
	{
		string name = block.ToString();
		var chars = new List<char>(name.Length + 4);
		for (int i = 0; i < name.Length; i++)
		{
			if (i > 0 && char.IsUpper(name[i]))
				chars.Add(' ');
			chars.Add(name[i]);
		}
		return new string(chars.ToArray());
	}
}
