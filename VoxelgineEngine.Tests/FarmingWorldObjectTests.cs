using Voxelgine.Engine;
using Voxelgine.Engine.Server;
using Voxelgine.Engine.World.Structures;
using Voxelgine.Graphics;

namespace VoxelgineEngine.Tests;

public sealed class FarmingWorldObjectTests
{
	[Fact]
	public void HydrationUsesIndexedQueueAndOneColumnRevision()
	{
		ChunkMap map = new(); WorldObjectStore objects = new();
		map.SetBlock(0, 0, 0, BlockType.DryFarmland);
		map.SetBlock(1, 0, 0, BlockType.DryFarmland);
		using FarmingService farming = new(map, objects);
		farming.RebuildIndex();
		map.SetBlock(4, 0, 0, BlockType.Water);
		long before = map.GetColumnRevision(0, 0);
		farming.Update(1f);

		Assert.Equal(BlockType.WetFarmland, map.GetBlock(0, 0, 0));
		Assert.Equal(BlockType.WetFarmland, map.GetBlock(1, 0, 0));
		Assert.Equal(before + 1, map.GetColumnRevision(0, 0));
		Assert.Equal(2, farming.IndexedFarmlandCount);
	}

	[Fact]
	public void WheatMaturesDuringUpdate120WithClampedFixedPointProgress()
	{
		ChunkMap map = new(); WorldObjectStore objects = new();
		map.SetBlock(0, 0, 0, BlockType.WetFarmland); map.SetBlock(4, 0, 0, BlockType.Water);
		using FarmingService farming = new(map, objects);
		farming.RebuildIndex();
		PersistentWorldObjectKey key = objects.AllocatePlacedKey();
		Assert.True(farming.TryPlantWheat(new BlockCoordinate(0, 0, 0), key));
		for (int update = 0; update < 119; update++) farming.Update(1f);
		Assert.True(objects.TryGet(key, out WorldPlantRecord before));
		Assert.False(before.IsMature);
		farming.Update(1f);
		Assert.True(objects.TryGet(key, out WorldPlantRecord mature));
		Assert.Equal(ushort.MaxValue, mature.GrowthProgress);
		Assert.Equal(7, mature.GrowthStage);
	}

	[Fact]
	public void SupportLossRemovesPlantAndReportsIt()
	{
		ChunkMap map = new(); WorldObjectStore objects = new();
		map.SetBlock(0, 0, 0, BlockType.DryFarmland);
		using FarmingService farming = new(map, objects); farming.RebuildIndex();
		Assert.True(farming.TryPlantWheat(new BlockCoordinate(0, 0, 0), objects.AllocatePlacedKey()));
		WorldPlantRecord lost = default; farming.PlantLostSupport += plant => lost = plant;
		map.SetBlock(0, 0, 0, BlockType.Dirt);
		Assert.Equal(0, objects.Count);
		Assert.Equal(WorldPlantType.Wheat, lost.PlantType);
	}

	[Fact]
	public void ColumnTransactionsShareRevisionAndHistoryFallsBackAfter256Deltas()
	{
		WorldObjectStore store = new();
		PersistentWorldObjectKey first = store.AllocatePlacedKey(); PersistentWorldObjectKey second = store.AllocatePlacedKey();
		store.ApplyTransaction(new[]
		{
			Upsert(first, 0, 0, 0), Upsert(second, 1, 0, 0),
		});
		WorldObjectColumnState column = store.GetColumn(0, 0);
		Assert.Equal(2, column.Revision);
		for (int index = 0; index < 257; index++)
		{
			store.TryGet(first, out WorldPlantRecord record);
			store.ApplyTransaction(new[] { new WorldObjectOperation(WorldObjectOperationKind.Upsert, record with { GrowthProgress = (ushort)index }, default) });
		}
		Assert.False(store.TryGetDeltas(0, 0, 1, out _));
	}

	[Fact]
	public void MultipartSnapshotObeysPartAndFullLimits()
	{
		WorldObjectStore store = new(); List<WorldObjectOperation> operations = new();
		for (int index = 0; index < 600; index++) operations.Add(Upsert(store.AllocatePlacedKey(), index % 16, index / 256, (index / 16) % 16));
		store.ApplyTransaction(operations);
		WorldObjectColumnPacket[] packets = WorldObjectStreamManager.CreateSnapshotPackets(9, store.GetColumn(0, 0), 3);
		Assert.True(packets.Length >= 3);
		Assert.All(packets, packet => { Assert.InRange(packet.PartRecordCount, (ushort)0, (ushort)256); Assert.InRange(packet.Payload.Length, 0, 64 * 1024); });
		Assert.Equal(600, packets.Sum(static packet => packet.PartRecordCount));
		byte[] full = packets.OrderBy(static packet => packet.PartIndex).SelectMany(static packet => packet.Payload).ToArray();
		Assert.Equal(packets[0].FullPayloadChecksum, WorldColumnCodec.ComputeChecksum(full));
	}

	[Fact]
	public void ArchiveRoundTripsWorldObjectsWithoutCreatingEntities()
	{
		ChunkMap map = new(); WorldObjectStore store = new(); PersistentWorldObjectKey key = store.AllocatePlacedKey();
		store.ApplyTransaction(new[] { Upsert(key, 1, 2, 3) });
		using MemoryStream archive = new();
		WorldArchive.Write(archive, map, default, worldObjects: store.GetAll()); archive.Position=0;
		WorldArchiveReadResult read = WorldArchive.Read(archive);
		WorldPlantRecord plant = Assert.Single(read.WorldObjects);
		Assert.Equal(key, plant.Key); Assert.Equal(new BlockCoordinate(1,2,3), plant.Support);
	}

	[Fact]
	public void InventoryRecipeIsAtomicWhenInputsOrOutputSpaceAreMissing()
	{
		PlayerInventory inventory = new(); inventory.Grant(ItemIds.FromBlock(BlockType.Sand), 1);
		long revision = inventory.Revision;
		Assert.False(inventory.TryApplyRecipe(new[] { new ItemStack(ItemIds.FromBlock(BlockType.Sand),1), new ItemStack(ItemIds.FromBlock(BlockType.Gravel),1) }, new ItemStack(ItemIds.FromBlock(BlockType.Concrete),1)));
		Assert.Equal(revision, inventory.Revision);
		Assert.Equal(ItemIds.FromBlock(BlockType.Sand), inventory.GetSlot(0).Item);
	}

	[Fact]
	public void ReplicatedDeltaRequiresExactEpochAndBaseRevision()
	{
		WorldObjectStore store = new();
		PersistentWorldObjectKey key = store.AllocatePlacedKey();
		store.InstallColumnSnapshot(0, 0, 17, 9, new[] { Upsert(key, 0, 0, 0).Record });
		WorldPlantRecord changed = Upsert(key, 0, 0, 0).Record with { GrowthProgress = 547 };
		WorldObjectOperation[] delta = { new(WorldObjectOperationKind.Upsert, changed, default) };

		Assert.False(store.TryApplyReplicatedDelta(0, 0, 16, 9, 10, delta));
		Assert.False(store.TryApplyReplicatedDelta(0, 0, 17, 8, 9, delta));
		Assert.True(store.TryGet(key, out WorldPlantRecord unchanged));
		Assert.Equal(0, unchanged.GrowthProgress);
		Assert.True(store.TryApplyReplicatedDelta(0, 0, 17, 9, 10, delta));
		Assert.True(store.TryGet(key, out WorldPlantRecord applied));
		Assert.Equal(547, applied.GrowthProgress);
	}

	[Fact]
	public void FailedReplicatedDeltaDoesNotPartiallyMutateColumn()
	{
		WorldObjectStore store = new();
		PersistentWorldObjectKey first = store.AllocatePlacedKey();
		PersistentWorldObjectKey missing = store.AllocatePlacedKey();
		store.InstallColumnSnapshot(0, 0, 3, 4, new[] { Upsert(first, 0, 0, 0).Record });
		WorldPlantRecord changed = Upsert(first, 0, 0, 0).Record with { GrowthProgress = 1000 };
		WorldObjectOperation[] delta =
		{
			new(WorldObjectOperationKind.Upsert, changed, default),
			new(WorldObjectOperationKind.Remove, default, missing),
		};

		Assert.False(store.TryApplyReplicatedDelta(0, 0, 3, 4, 5, delta));
		Assert.True(store.TryGet(first, out WorldPlantRecord retained));
		Assert.Equal(0, retained.GrowthProgress);
		Assert.Equal(4, store.GetColumn(0, 0).Revision);
	}

	[Fact]
	public void ExistingObjectCannotMoveAcrossColumns()
	{
		WorldObjectStore store = new();
		PersistentWorldObjectKey key = store.AllocatePlacedKey();
		store.ApplyTransaction(new[] { Upsert(key, 0, 0, 0) });

		Assert.Throws<InvalidOperationException>(() =>
			store.ApplyTransaction(new[] { Upsert(key, Chunk.ChunkSize, 0, 0) }));
		Assert.True(store.TryGetAt(new BlockCoordinate(0, 1, 0), out _));
	}

	private static WorldObjectOperation Upsert(PersistentWorldObjectKey key, int x, int y, int z) => new(
		WorldObjectOperationKind.Upsert,
		new WorldPlantRecord(key, WorldPlantType.Wheat, 0, byte.MaxValue, ItemIds.Wheat, new BlockCoordinate(x,y,z)), default);
}
