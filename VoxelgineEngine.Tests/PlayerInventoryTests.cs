using System;
using System.Linq;
using Voxelgine.Engine;

public sealed class PlayerInventoryTests
{
	[Fact]
	public void ItemIdsAreDeterministicAndBlockPoliciesAreExhaustive()
	{
		Assert.Equal((ushort)0, ItemId.Empty.Value);
		Assert.Equal((ushort)BlockType.Stone, ItemIds.FromBlock(BlockType.Stone).Value);
		Assert.Equal((ushort)1000, ItemIds.Gun.Value);
		Assert.Equal((ushort)1001, ItemIds.Hammer.Value);

		BlockType[] blocks = Enum.GetValues<BlockType>().Where(static block => block != BlockType.None).ToArray();
		Assert.Equal(blocks.Length, ItemCatalog.AllBlocks.Count());
		foreach (BlockType block in blocks)
			Assert.Equal(block, ItemCatalog.GetBlock(block).Block);

		Assert.False(ItemCatalog.GetBlock(BlockType.Water).DropsItem);
		Assert.False(ItemCatalog.GetBlock(BlockType.Leaf).DropsItem);
		Assert.True(ItemCatalog.GetBlock(BlockType.Torch).DropsItem);
		Assert.Equal(ItemIds.FromBlock(BlockType.Torch), ItemCatalog.GetBlock(BlockType.Torch).Drop.Item);
	}

	[Fact]
	public void LeftClickPicksPlacesMergesAndSwapsWithOneRevisionEach()
	{
		var inventory = new PlayerInventory();
		inventory.Grant(new ItemStack(ItemIds.FromBlock(BlockType.Dirt), 32));
		long revision = inventory.Revision;

		InventoryMutationResult pick = inventory.ApplyClick(InventoryActionKind.LeftClickSlot, 0);
		Assert.True(pick.Changed);
		Assert.Equal(revision + 1, inventory.Revision);
		Assert.Equal(0, inventory.CursorOriginSlot);
		Assert.True(inventory.GetSlot(0).IsEmpty);

		InventoryMutationResult place = inventory.ApplyClick(InventoryActionKind.LeftClickSlot, 1);
		Assert.True(place.Changed);
		Assert.True(inventory.Cursor.IsEmpty);
		Assert.Equal(PlayerInventory.NoCursorOrigin, inventory.CursorOriginSlot);

		inventory.Grant(new ItemStack(ItemIds.FromBlock(BlockType.Dirt), 40));
		inventory.ApplyClick(InventoryActionKind.LeftClickSlot, 0);
		inventory.ApplyClick(InventoryActionKind.LeftClickSlot, 1);
		Assert.Equal((ushort)64, inventory.GetSlot(1).Count);
		Assert.Equal((ushort)8, inventory.Cursor.Count);

		var swapInventory = new PlayerInventory();
		swapInventory.Grant(new ItemStack(ItemIds.FromBlock(BlockType.Dirt), 1));
		swapInventory.Grant(new ItemStack(ItemIds.FromBlock(BlockType.Stone), 1));
		swapInventory.ApplyClick(InventoryActionKind.LeftClickSlot, 0);
		swapInventory.ApplyClick(InventoryActionKind.LeftClickSlot, 1);
		Assert.Equal(ItemIds.FromBlock(BlockType.Dirt), swapInventory.GetSlot(1).Item);
		Assert.Equal(ItemIds.FromBlock(BlockType.Stone), swapInventory.Cursor.Item);
		Assert.Equal(1, swapInventory.CursorOriginSlot);
	}

	[Theory]
	[InlineData(5, 3, 2)]
	[InlineData(6, 3, 3)]
	public void RightClickTakesLargerHalfAndPlacesOne(int initial, int cursor, int remaining)
	{
		var inventory = new PlayerInventory();
		inventory.Grant(ItemStack.Create(ItemIds.FromBlock(BlockType.Stone), initial));
		inventory.ApplyClick(InventoryActionKind.RightClickSlot, 0);
		Assert.Equal((ushort)cursor, inventory.Cursor.Count);
		Assert.Equal((ushort)remaining, inventory.GetSlot(0).Count);

		long revision = inventory.Revision;
		inventory.ApplyClick(InventoryActionKind.RightClickSlot, 1);
		Assert.Equal(revision + 1, inventory.Revision);
		Assert.Equal((ushort)1, inventory.GetSlot(1).Count);
	}

	[Fact]
	public void CancellationRetainsOriginWhenRemainderCannotFit()
	{
		var inventory = new PlayerInventory();
		inventory.Grant(new ItemStack(ItemIds.Gun, 1));
		inventory.ApplyClick(InventoryActionKind.LeftClickSlot, 0);

		for (int i = 0; i < PlayerInventory.SlotCount; i++)
		{
			inventory.Grant(new ItemStack(ItemIds.Hammer, 1));
		}

		inventory.ApplyClick(InventoryActionKind.LeftClickSlot, 0);
		Assert.Equal(ItemIds.Hammer, inventory.Cursor.Item);
		Assert.Equal(0, inventory.CursorOriginSlot);
		inventory.ApplyClick(InventoryActionKind.CancelCursor, PlayerInventory.NoCursorOrigin);
		Assert.Equal(ItemIds.Hammer, inventory.Cursor.Item);
		Assert.Equal(0, inventory.CursorOriginSlot);
	}

	[Fact]
	public void MultiSlotInsertionAdvancesRevisionOnce()
	{
		var inventory = new PlayerInventory();
		inventory.Grant(new ItemStack(ItemIds.FromBlock(BlockType.Dirt), 60));
		long revision = inventory.Revision;

		InventoryInsertionResult result = inventory.TryInsert(new ItemStack(ItemIds.FromBlock(BlockType.Dirt), 64));

		Assert.True(result.Changed);
		Assert.Equal(revision + 1, inventory.Revision);
		Assert.Equal((ushort)64, inventory.GetSlot(0).Count);
		Assert.Equal((ushort)60, inventory.GetSlot(1).Count);
	}

	[Fact]
	public void AdministrativeGrantAcrossManySlotsIsOneTransaction()
	{
		var inventory = new PlayerInventory();
		long revision = inventory.Revision;

		int granted = inventory.Grant(ItemIds.FromBlock(BlockType.Dirt), 130);

		Assert.Equal(130, granted);
		Assert.Equal(revision + 1, inventory.Revision);
		Assert.Equal((ushort)64, inventory.GetSlot(0).Count);
		Assert.Equal((ushort)64, inventory.GetSlot(1).Count);
		Assert.Equal((ushort)2, inventory.GetSlot(2).Count);
	}

	[Fact]
	public void PreparedConsumptionRejectsInterveningMutation()
	{
		var inventory = new PlayerInventory();
		ItemId dirt = ItemIds.FromBlock(BlockType.Dirt);
		inventory.Grant(new ItemStack(dirt, 2));
		PreparedConsumption prepared = inventory.TryPrepareConsumption(0, dirt, 1);
		Assert.True(prepared.IsValid);
		Assert.True(inventory.ApplyPreparedConsumption(prepared));
		Assert.Equal((ushort)1, inventory.GetSlot(0).Count);

		PreparedConsumption stale = inventory.TryPrepareConsumption(0, dirt, 1);
		inventory.Grant(new ItemStack(ItemIds.FromBlock(BlockType.Stone), 1));
		Assert.False(inventory.ApplyPreparedConsumption(stale));
	}

	[Fact]
	public void FullCancellationIsANoOpAndKeepsItsOrigin()
	{
		var inventory = new PlayerInventory();
		inventory.Restore(
			Enumerable.Repeat(new ItemStack(ItemIds.Hammer, 1), PlayerInventory.SlotCount).ToArray(),
			new ItemStack(ItemIds.Gun, 1),
			0);
		long revision = inventory.Revision;

		InventoryMutationResult result = inventory.ApplyClick(
			InventoryActionKind.CancelCursor,
			PlayerInventory.NoCursorOrigin);

		Assert.False(result.Changed);
		Assert.Equal(revision, inventory.Revision);
		Assert.Equal(ItemIds.Gun, inventory.Cursor.Item);
		Assert.Equal(0, inventory.CursorOriginSlot);
	}

	[Fact]
	public void InventoryPacketsRoundTripAllSlotsCursorAndSelection()
	{
		ItemStack[] slots = new ItemStack[PlayerInventory.SlotCount];
		slots[0] = ItemStack.Create(ItemIds.FromBlock(BlockType.Stone), 64);
		slots[59] = new ItemStack(ItemIds.Hammer, 1);
		InventoryStatePacket source = new()
		{
			AcknowledgedActionId = 7,
			ActionAccepted = true,
			Revision = 19,
			SelectedHotbarSlot = 9,
			SelectionCommandTick = 42,
			Cursor = ItemStack.Create(ItemIds.FromBlock(BlockType.Dirt), 3),
			CursorOriginSlot = 11,
			Slots = slots,
		};

		InventoryStatePacket decoded = Assert.IsType<InventoryStatePacket>(Packet.Deserialize(source.Serialize()));

		Assert.Equal(source.Revision, decoded.Revision);
		Assert.Equal(source.Cursor, decoded.Cursor);
		Assert.Equal(source.CursorOriginSlot, decoded.CursorOriginSlot);
		Assert.Equal(source.SelectedHotbarSlot, decoded.SelectedHotbarSlot);
		Assert.Equal(source.SelectionCommandTick, decoded.SelectionCommandTick);
		Assert.Equal(source.Slots, decoded.Slots);
	}

	[Fact]
	public void DeathResolutionIsOneTransactionAndReturnsOnlyTheRemainder()
	{
		var slots = Enumerable.Repeat(new ItemStack(ItemIds.Hammer, 1), PlayerInventory.SlotCount).ToArray();
		slots[0] = ItemStack.Empty;
		var inventory = new PlayerInventory();
		inventory.Restore(slots, ItemStack.Create(ItemIds.FromBlock(BlockType.Dirt), 3), 0);
		long revision = inventory.Revision;

		ItemStack remainder = inventory.ResolveCursorForDeath();

		Assert.True(remainder.IsEmpty);
		Assert.Equal((ushort)3, inventory.GetSlot(0).Count);
		Assert.True(inventory.Cursor.IsEmpty);
		Assert.Equal(revision + 1, inventory.Revision);
	}

	[Fact]
	public void SimulatedCommandHistoryExpiresOldRecordsAndPreservesInteractionRay()
	{
		var history = new SimulatedCommandHistory();
		for (int tick = 1; tick <= SimulatedCommandHistory.Capacity + 1; tick++)
		{
			history.Record(new SimulatedCommandRecord(
				tick,
				(byte)(tick % PlayerInventory.HotbarSlotCount),
				new System.Numerics.Vector3(tick, 2, 3),
				System.Numerics.Vector3.UnitZ,
				PrimaryUse: true,
				SecondaryUse: false));
		}

		Assert.False(history.TryGet(1, out _));
		Assert.True(history.TryGet(SimulatedCommandHistory.Capacity + 1, out SimulatedCommandRecord record));
		Assert.Equal(new System.Numerics.Vector3(SimulatedCommandHistory.Capacity + 1, 2, 3), record.InteractionOrigin);
		Assert.Equal(System.Numerics.Vector3.UnitZ, record.InteractionDirection);
	}

	[Fact]
	public void ItemUsePacketsRoundTripActionCommandAndChannel()
	{
		BlockPlaceRequestPacket source = new()
		{
			ItemUseActionId = 42,
			CommandTick = 91,
			Channel = ItemUseChannel.Secondary,
			X = -5,
			Y = 12,
			Z = 8,
			BlockType = (ushort)BlockType.Stone,
		};

		BlockPlaceRequestPacket decoded = Assert.IsType<BlockPlaceRequestPacket>(
			Packet.Deserialize(source.Serialize()));

		Assert.Equal(source.ItemUseActionId, decoded.ItemUseActionId);
		Assert.Equal(source.CommandTick, decoded.CommandTick);
		Assert.Equal(source.Channel, decoded.Channel);
		Assert.Equal(source.X, decoded.X);
		Assert.Equal(source.BlockType, decoded.BlockType);
	}

	[Fact]
	public void ItemDropSpawnAndSnapshotStateRoundTripsCountAndProtection()
	{
		var source = new VEntItemDrop();
		source.SetStack(ItemStack.Create(ItemIds.FromBlock(BlockType.Dirt), 17));
		source.PickupDelayTicks = 4;
		source.IsProtected = true;
		source.ProtectionUntilServerTick = 500;
		source.ExpiryServerTick = 900;

		byte[] spawn;
		using (var stream = new MemoryStream())
		{
			using var writer = new BinaryWriter(stream);
			source.WriteSpawnProperties(writer);
			spawn = stream.ToArray();
		}
		var decoded = new VEntItemDrop();
		using (var stream = new MemoryStream(spawn))
		using (var reader = new BinaryReader(stream))
			decoded.ReadSpawnProperties(reader);

		Assert.Equal(source.Stack, decoded.Stack);
		Assert.Equal(source.PickupDelayTicks, decoded.PickupDelayTicks);
		Assert.True(decoded.IsProtected);
		Assert.Equal(source.ProtectionUntilServerTick, decoded.ProtectionUntilServerTick);
		Assert.Equal(source.ExpiryServerTick, decoded.ExpiryServerTick);
	}
}
