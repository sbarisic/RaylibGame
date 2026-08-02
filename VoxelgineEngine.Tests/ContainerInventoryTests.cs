using Voxelgine.Engine;
using Voxelgine.Engine.World.Structures;
using Voxelgine.Graphics;

namespace VoxelgineEngine.Tests;

public sealed class ContainerInventoryTests
{
	[Fact]
	public void SharedTransactionMovesStacksAndRejectsStaleRevisionsAtomically()
	{
		InventoryTransactionService service=new();PlayerInventory player=new();ContainerInventory container=new(VEntItemBasket.SlotCount);
		player.Grant(ItemIds.Wheat,10);long playerRevision=player.Revision;
		InventoryTransactionResult picked=service.ApplyPlayerClick("player",player,InventoryActionKind.LeftClickSlot,0,playerRevision);
		Assert.True(picked.Changed);Assert.Equal(10,player.Cursor.Count);
		InventoryTransactionResult placed=service.ApplyContainerClick("player",player,container,"p:1",InventoryActionKind.LeftClickSlot,0,player.Revision,container.Revision);
		Assert.True(placed.Changed);Assert.True(player.Cursor.IsEmpty);Assert.Equal(10,container.GetSlot(0).Count);
		long stablePlayer=player.Revision,stableContainer=container.Revision;
		InventoryTransactionResult stale=service.ApplyContainerClick("player",player,container,"p:1",InventoryActionKind.LeftClickSlot,0,stablePlayer-1,stableContainer);
		Assert.False(stale.Accepted);Assert.Equal(stablePlayer,player.Revision);Assert.Equal(stableContainer,container.Revision);Assert.Equal(10,container.GetSlot(0).Count);
	}

	[Fact]
	public void CursorReturnsToContainerOriginBeforePlayerOverflow()
	{
		InventoryTransactionService service=new();PlayerInventory player=new();ContainerInventory container=new(12);
		container.Restore(new[]{new ItemStack(ItemIds.Wheat,7)}.Concat(Enumerable.Repeat(ItemStack.Empty,11)).ToArray());
		InventoryTransactionResult picked=service.ApplyContainerClick("player",player,container,"p:9",InventoryActionKind.LeftClickSlot,0,player.Revision,container.Revision);
		Assert.Equal(new SlotAddress(InventoryStoreKind.Container,"p:9",0),picked.CursorOrigin);
		ItemStack overflow=service.ResolveCursor("player",player,key=>key=="p:9"?container:null);
		Assert.True(overflow.IsEmpty);Assert.True(player.Cursor.IsEmpty);Assert.Equal(7,container.GetSlot(0).Count);Assert.All(player.GetSlots().ToArray(),static stack=>Assert.True(stack.IsEmpty));
	}

	[Fact]
	public void FurnitureArchivePreservesDiscriminatedIdentityAndContents()
	{
		ChunkMap map=new();PersistentFurnitureKey key=PersistentFurnitureKey.Generated(new GeneratedMarkerId(new GeneratedSiteId("site"),"basket"));
		ItemStack[] slots=new ItemStack[VEntItemBasket.SlotCount];slots[3]=new ItemStack(ItemIds.Wheat,12);
		PersistentFurnitureRecord record=new(key,FurnitureType.ItemBasket,new BlockCoordinate(1,2,3),2,slots);
		using MemoryStream stream=new();WorldArchive.Write(stream,map,default,furniture:new[]{record});stream.Position=0;
		PersistentFurnitureRecord decoded=Assert.Single(WorldArchive.Read(stream).Furniture);
		Assert.Equal(key,decoded.Key);Assert.Equal(record.Anchor,decoded.Anchor);Assert.Equal(12,decoded.Slots[3].Count);
	}

	[Fact]
	public void ContainerPacketsUseReservedIdsAndRoundTripState()
	{
		Assert.Equal(0x96,(byte)PacketType.ContainerState);Assert.Equal(0x97,(byte)PacketType.ContainerActionRequest);Assert.Equal(0x98,(byte)PacketType.ContainerClose);
		ContainerStatePacket source=new(){SessionId=5,ContainerKey="p:2",ContainerRevision=7,PlayerRevision=8,IsOpen=true,Slots=new[]{new ItemStack(ItemIds.Wheat,2)}};
		ContainerStatePacket decoded=Assert.IsType<ContainerStatePacket>(Packet.Deserialize(source.Serialize()));
		Assert.Equal(source.SessionId,decoded.SessionId);Assert.Equal(source.ContainerRevision,decoded.ContainerRevision);Assert.Equal(source.Slots,decoded.Slots);
	}

	[Fact]
	public void PlacedFurnitureIdsAreMonotonicAfterRestore()
	{
		FurnitureStore store=new();PersistentFurnitureKey restored=PersistentFurnitureKey.Placed(new PersistentEntityId(41));
		store.Restore(new[]{new PersistentFurnitureRecord(restored,FurnitureType.ItemBasket,new BlockCoordinate(0,1,0),0,new ItemStack[12])});
		Assert.Equal((ulong)42,store.AllocatePlacedKey().PersistentEntityId.Value);
		Assert.Equal((ushort)1003,ItemIds.ItemBasket.Value);
	}
}
