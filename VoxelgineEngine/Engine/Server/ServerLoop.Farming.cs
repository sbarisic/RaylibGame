using System.Numerics;
using Voxelgine.Engine.World.Structures;
using Voxelgine.Graphics;

namespace Voxelgine.Engine.Server;

public partial class ServerLoop
{
	private void HandleWorldObjectPlace(NetConnection connection, WorldObjectPlaceRequestPacket packet)
	{
		if (!_sessions.TryGetValue(connection.PlayerId, out ServerClientSession session) || session.SelectedHotbarSlot >= PlayerInventory.HotbarSlotCount)
			return;
		if (packet.PlantType == (byte)WorldObjectPlacementKind.ItemBasket) { HandleBasketPlacement(session, packet); return; }
		if (packet.PlantType == (byte)WorldObjectPlacementKind.Bed) { HandleBedPlacement(session,packet); return; }
		if (packet.PlantType != (byte)WorldObjectPlacementKind.Wheat) return;
		BlockCoordinate support = new(packet.X, packet.Y, packet.Z);
		Vector3 center = new(packet.X + 0.5f, packet.Y + 0.5f, packet.Z + 0.5f);
		if (Vector3.DistanceSquared(session.Player.Position, center) > MaxBlockReach * MaxBlockReach) return;
		ItemStack selected = session.Inventory.GetSlot(session.SelectedHotbarSlot);
		if (selected.Item != ItemIds.WheatSeeds) return;
		PreparedConsumption consumption = session.Inventory.TryPrepareConsumption(session.SelectedHotbarSlot, ItemIds.WheatSeeds, 1);
		if (!consumption.IsValid) return;
		PersistentWorldObjectKey key = _simulation.WorldObjects.AllocatePlacedKey();
		if (!_farming.TryPlantWheat(support, key)) return;
		if (!session.Inventory.ApplyPreparedConsumption(consumption)) throw new InvalidOperationException("Serialized seed consumption changed.");
		SendInventoryState(session, packet.ActionId, true);
	}

	private void HandleWorldInteract(NetConnection connection, WorldInteractRequestPacket packet)
	{
		if (!_sessions.TryGetValue(connection.PlayerId, out ServerClientSession session)) return;
		BlockCoordinate position = new(packet.X, packet.Y, packet.Z);
		Vector3 center = new(packet.X + 0.5f, packet.Y + 0.5f, packet.Z + 0.5f);
		if (Vector3.DistanceSquared(session.Player.Position, center) > MaxBlockReach * MaxBlockReach) return;
		if(packet.Interaction==WorldInteractionKind.RemoveFurniture)
		{
			if(session.SelectedHotbarSlot>=PlayerInventory.HotbarSlotCount||session.Inventory.GetSlot(session.SelectedHotbarSlot).Item!=ItemIds.Hammer)return;
			if(_simulation.Furniture.TryGetAt(position,out PersistentFurnitureRecord removed)){if(removed.Type==FurnitureType.ItemBasket){VEntItemBasket basket=FindBasket(removed.Key);if(basket!=null)RemoveBasket(basket);}else{VEntBed bed=_simulation.Entities.GetAllEntities().OfType<VEntBed>().FirstOrDefault(candidate=>candidate.PersistentKey==removed.Key);if(bed!=null)RemoveBed(bed);}}return;
		}
		if (_simulation.Furniture.TryGetAt(position, out PersistentFurnitureRecord furniture) && furniture.Type == FurnitureType.ItemBasket)
		{
			VEntItemBasket basket=FindBasket(furniture.Key);if(basket!=null)OpenBasket(session,basket);return;
		}
		if(_simulation.Furniture.TryGetAt(position,out furniture)&&furniture.Type==FurnitureType.Bed)
		{
			VEntBed bed=_simulation.Entities.GetAllEntities().OfType<VEntBed>().FirstOrDefault(candidate=>candidate.PersistentKey==furniture.Key);string occupant=_npcLife?.Capture().FirstOrDefault(record=>record.AssignedBed==furniture.Key).NpcId.ToString();if(string.IsNullOrWhiteSpace(occupant)||occupant=="p:0")occupant="unassigned";_server.TrySendTo(session.Player.PlayerId,new ChatMessagePacket{PlayerId=0,Message=$"Bed {furniture.Key}: {occupant}"},true,CurrentTime,ReliableSendClass.Gameplay);return;
		}
		if (!_farming.TryHarvest(position, out WorldPlantRecord plant)) return;
		if(plant.Key.Kind==PersistentWorldObjectKeyKind.Generated)_simulation.Tombstones.Add(GeneratedObjectKind.WorldObject,plant.Key.GeneratedMarkerId);
		GrantOrDrop(session, new ItemStack(plant.HarvestItem, 1), center);
		GrantOrDrop(session, new ItemStack(ItemIds.WheatSeeds, checked((ushort)Random.Shared.Next(1, 3))), center);
		SendInventoryState(session, 0, true);
	}

	private void HandleBasketPlacement(ServerClientSession session, WorldObjectPlaceRequestPacket packet)
	{
		BlockCoordinate anchor=new(packet.X,packet.Y,packet.Z);Vector3 center=new(packet.X+0.5f,packet.Y+0.5f,packet.Z+0.5f);
		if(Vector3.DistanceSquared(session.Player.Position,center)>MaxBlockReach*MaxBlockReach||_simulation.Map.GetBlock(anchor.X,anchor.Y,anchor.Z)!=BlockType.None||!BlockInfo.IsSolid(_simulation.Map.GetBlock(anchor.X,anchor.Y-1,anchor.Z))||_simulation.Furniture.TryGetAt(anchor,out _))return;
		ItemStack selected=session.Inventory.GetSlot(session.SelectedHotbarSlot);if(selected.Item!=ItemIds.ItemBasket)return;
		PreparedConsumption consumption=session.Inventory.TryPrepareConsumption(session.SelectedHotbarSlot,ItemIds.ItemBasket,1);if(!consumption.IsValid)return;
		PersistentFurnitureKey key=_simulation.Furniture.AllocatePlacedKey();byte facing=(byte)(BlockShapeCatalog.GetNormalStairState(session.Player.GetForward())&3);
		PersistentFurnitureRecord record=new(key,FurnitureType.ItemBasket,anchor,facing,new ItemStack[VEntItemBasket.SlotCount]);
		_simulation.Furniture.Add(record);SpawnBasket(record,broadcast:true);
		if(!session.Inventory.ApplyPreparedConsumption(consumption))throw new InvalidOperationException("Serialized basket consumption changed.");
		SendInventoryState(session,packet.ActionId,true);
	}

	private void HandleBedPlacement(ServerClientSession session,WorldObjectPlaceRequestPacket packet)
	{
		BlockCoordinate anchor=new(packet.X,packet.Y,packet.Z);Vector3 center=new(packet.X+0.5f,packet.Y+0.5f,packet.Z+0.5f);byte facing=(byte)(BlockShapeCatalog.GetNormalStairState(session.Player.GetForward())&3);BlockCoordinate head=anchor+VEntBed.FacingOffset(facing);
		if(Vector3.DistanceSquared(session.Player.Position,center)>MaxBlockReach*MaxBlockReach||_simulation.Map.GetBlock(anchor.X,anchor.Y,anchor.Z)!=BlockType.None||_simulation.Map.GetBlock(head.X,head.Y,head.Z)!=BlockType.None||!BlockInfo.IsSolid(_simulation.Map.GetBlock(anchor.X,anchor.Y-1,anchor.Z))||!BlockInfo.IsSolid(_simulation.Map.GetBlock(head.X,head.Y-1,head.Z))||_simulation.Furniture.IsCellOccupied(anchor)||_simulation.Furniture.IsCellOccupied(head))return;
		if(session.Inventory.GetSlot(session.SelectedHotbarSlot).Item!=ItemIds.Bed)return;PreparedConsumption consumption=session.Inventory.TryPrepareConsumption(session.SelectedHotbarSlot,ItemIds.Bed,1);if(!consumption.IsValid)return;
		PersistentFurnitureKey key=_simulation.Furniture.AllocatePlacedKey();PersistentFurnitureRecord record=new(key,FurnitureType.Bed,anchor,facing,Array.Empty<ItemStack>());_simulation.Furniture.Add(record);SpawnBed(record,broadcast:true);if(!session.Inventory.ApplyPreparedConsumption(consumption))throw new InvalidOperationException("Serialized bed consumption changed.");SendInventoryState(session,packet.ActionId,true);
	}

	private void HandleCraftRequest(NetConnection connection, CraftRequestPacket packet)
	{
		if (!_sessions.TryGetValue(connection.PlayerId, out ServerClientSession session)) return;
		bool accepted = packet.RecipeId == 1 && HasNearbyWater(session.Player.Position) &&
			session.Inventory.TryApplyRecipe(
				new[] { new ItemStack(ItemIds.FromBlock(BlockType.Sand), 1), new ItemStack(ItemIds.FromBlock(BlockType.Gravel), 1) },
				new ItemStack(ItemIds.FromBlock(BlockType.Concrete), 1));
		if (accepted) SendInventoryState(session, packet.ActionId, true);
		_server.TrySendTo(connection.PlayerId, new CraftResultPacket { ActionId=packet.ActionId, Accepted=accepted, Reason=(byte)(accepted?0:1) }, true, CurrentTime, ReliableSendClass.Gameplay);
	}

	private bool HasNearbyWater(Vector3 position)
	{
		int centerX=(int)MathF.Floor(position.X), centerY=(int)MathF.Floor(position.Y), centerZ=(int)MathF.Floor(position.Z);
		for(int y=centerY-2;y<=centerY+2;y++) for(int x=centerX-4;x<=centerX+4;x++) for(int z=centerZ-4;z<=centerZ+4;z++)
			if(_simulation.Map.GetBlock(x,y,z)==BlockType.Water) return true;
		return false;
	}

	private void GrantOrDrop(ServerClientSession session, ItemStack stack, Vector3 position)
	{
		InventoryInsertionResult result = session.Inventory.TryInsert(stack);
		if (!result.Remainder.IsEmpty) SpawnItemDrop(result.Remainder, position);
	}
}
