using System.Numerics;
using Voxelgine.Graphics;
using Voxelgine.Engine.World.Structures;

namespace Voxelgine.Engine.Server;

public partial class ServerLoop
{
	private ulong _nextContainerSessionId = 1;
	private readonly Dictionary<PersistentFurnitureKey, long> _containerGenerations = new();

	private void OpenBasket(ServerClientSession viewer, VEntItemBasket basket)
	{
		long generation = _containerGenerations.GetValueOrDefault(basket.PersistentKey, 1);
		viewer.ContainerSession = new ContainerViewerSession(_nextContainerSessionId++, basket.PersistentKey, generation);
		SendContainerState(viewer, basket, true);
	}

	private void HandleContainerAction(NetConnection connection, ContainerActionRequestPacket packet)
	{
		if (!_sessions.TryGetValue(connection.PlayerId, out ServerClientSession viewer) || viewer.ContainerSession is not ContainerViewerSession session || session.SessionId != packet.SessionId)
			return;
		VEntItemBasket basket = FindBasket(session.ContainerKey);
		long generation = _containerGenerations.GetValueOrDefault(session.ContainerKey, 1);
		if (basket == null || generation != session.Generation)
		{
			CloseContainer(viewer, resolveCursor:true); return;
		}
		InventoryTransactionResult result = _inventoryTransactions.ApplyContainerClick(
			viewer.PlayerName, viewer.Inventory, basket.Inventory, basket.PersistentKey.ToString(), packet.Kind, packet.Slot,
			packet.ExpectedPlayerRevision, packet.ExpectedContainerRevision);
		if (result.Accepted && result.Changed)
		{
			_simulation.Furniture.Replace(basket.CaptureRecord());
			SendInventoryState(viewer, 0, true);
			BroadcastContainerState(basket);
		}
		else SendContainerState(viewer, basket, true);
	}

	private void HandleContainerClose(NetConnection connection, ContainerClosePacket packet)
	{
		if (_sessions.TryGetValue(connection.PlayerId, out ServerClientSession viewer) && viewer.ContainerSession?.SessionId == packet.SessionId)
			CloseContainer(viewer, resolveCursor:true);
	}

	private void CloseContainer(ServerClientSession viewer, bool resolveCursor)
	{
		ContainerViewerSession session = viewer.ContainerSession; if(session==null)return;
		viewer.ContainerSession=null;
		_server.TrySendTo(viewer.Player.PlayerId,new ContainerStatePacket{SessionId=session.SessionId,ContainerKey=session.ContainerKey.ToString(),ContainerRevision=1,PlayerRevision=viewer.Inventory.Revision,IsOpen=false,Slots=Array.Empty<ItemStack>()},true,CurrentTime,ReliableSendClass.Gameplay);
		if(resolveCursor)
		{
			ItemStack overflow=_inventoryTransactions.ResolveCursor(viewer.PlayerName,viewer.Inventory,FindContainerByString);
			if(!overflow.IsEmpty)SpawnItemDrop(overflow,viewer.Player.Position);
			SendInventoryState(viewer,0,true);
		}
	}

	private void RemoveBasket(VEntItemBasket basket)
	{
		long generation=_containerGenerations.GetValueOrDefault(basket.PersistentKey,1)+1;_containerGenerations[basket.PersistentKey]=generation;
		ServerClientSession[] viewers=_sessions.Values.Where(session=>session.ContainerSession?.ContainerKey==basket.PersistentKey).ToArray();
		foreach(ServerClientSession viewer in viewers){ContainerViewerSession session=viewer.ContainerSession;viewer.ContainerSession=null;_server.TrySendTo(viewer.Player.PlayerId,new ContainerStatePacket{SessionId=session.SessionId,ContainerKey=basket.PersistentKey.ToString(),ContainerRevision=basket.Inventory.Revision,PlayerRevision=viewer.Inventory.Revision,IsOpen=false,Slots=Array.Empty<ItemStack>()},true,CurrentTime,ReliableSendClass.Gameplay);}
		foreach(ServerClientSession viewer in viewers){ItemStack overflow=_inventoryTransactions.ResolveCursor(viewer.PlayerName,viewer.Inventory,_=>null);if(!overflow.IsEmpty)SpawnItemDrop(overflow,viewer.Player.Position);SendInventoryState(viewer,0,true);}
		foreach(ItemStack stack in basket.Inventory.GetSlots())if(!stack.IsEmpty)SpawnItemDrop(stack,basket.Position);SpawnItemDrop(new ItemStack(ItemIds.ItemBasket,1),basket.Position);
		if(basket.PersistentKey.Kind==PersistentFurnitureKeyKind.Generated)_simulation.Tombstones.Add(GeneratedObjectKind.Furniture,basket.PersistentKey.GeneratedMarkerId);
		_simulation.Furniture.Remove(basket.PersistentKey,out _);_simulation.Entities.Remove(basket);_server.Broadcast(new EntityRemovePacket{NetworkId=basket.NetworkId},true,CurrentTime);
	}

	private void OnFurnitureSupportChanged(BlockChange change)
	{
		if(BlockInfo.IsSolid(change.NewValue.Type))return;BlockCoordinate anchor=new(change.X,change.Y+1,change.Z);
		if(_simulation.Furniture.TryGetAt(anchor,out PersistentFurnitureRecord record)&&record.Type==FurnitureType.ItemBasket){VEntItemBasket basket=FindBasket(record.Key);if(basket!=null)RemoveBasket(basket);}
		foreach(VEntBed bed in _simulation.Entities.GetAllEntities().OfType<VEntBed>().ToArray())if(bed.Anchor==anchor||bed.HeadCell==anchor)RemoveBed(bed);
	}
	private void RemoveBed(VEntBed bed){_npcLife?.OnBedRemoved(bed.PersistentKey);if(bed.PersistentKey.Kind==PersistentFurnitureKeyKind.Generated)_simulation.Tombstones.Add(GeneratedObjectKind.Furniture,bed.PersistentKey.GeneratedMarkerId);_simulation.Furniture.Remove(bed.PersistentKey,out _);_simulation.Entities.Remove(bed);SpawnItemDrop(new ItemStack(ItemIds.Bed,1),bed.Position);_server.Broadcast(new EntityRemovePacket{NetworkId=bed.NetworkId},true,CurrentTime);}

	private void BroadcastContainerState(VEntItemBasket basket)
	{
		foreach(ServerClientSession viewer in _sessions.Values)
			if(viewer.ContainerSession?.ContainerKey==basket.PersistentKey)SendContainerState(viewer,basket,true);
	}
	private void SendContainerState(ServerClientSession viewer,VEntItemBasket basket,bool open)
	{
		ContainerViewerSession session=viewer.ContainerSession;if(session==null)return;ItemStack[] slots=basket.Inventory.GetSlots().ToArray();
		_server.TrySendTo(viewer.Player.PlayerId,new ContainerStatePacket{SessionId=session.SessionId,ContainerKey=basket.PersistentKey.ToString(),ContainerRevision=basket.Inventory.Revision,PlayerRevision=viewer.Inventory.Revision,IsOpen=open,Slots=slots},true,CurrentTime,ReliableSendClass.Gameplay);
	}
	private VEntItemBasket FindBasket(PersistentFurnitureKey key)=>_simulation.Entities.GetAllEntities().OfType<VEntItemBasket>().FirstOrDefault(basket=>basket.PersistentKey==key);
	private ContainerInventory FindContainerByString(string key)=>_simulation.Entities.GetAllEntities().OfType<VEntItemBasket>().FirstOrDefault(basket=>basket.PersistentKey.ToString()==key)?.Inventory;
}
