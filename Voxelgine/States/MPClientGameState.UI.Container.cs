using System.Numerics;
using FishUI;
using FishUI.Controls;
using Voxelgine.Engine;
using Voxelgine.GUI;

namespace Voxelgine.States;

public unsafe partial class MPClientGameState
{
	private void CreateContainerUI(int screenWidth, int screenHeight)
	{
		_containerWindow=new Window{Title="Item Basket",Size=new Vector2(300,190),Position=new Vector2(screenWidth/2f-150,screenHeight/2f-95),IsResizable=false,ShowCloseButton=true,Visible=false};
		_containerWindow.OnClosed+=_=>CloseContainerUI(send:true);
		_containerSlotBoxes=new FishUIItemBox[VEntItemBasket.SlotCount];
		for(int slot=0;slot<_containerSlotBoxes.Length;slot++)
		{
			int local=slot;FishUIItemBox box=new(){SlotIndex=slot,Position=new Vector2(18+(slot%6)*(InventorySlotSize+InventorySlotSpacing),42+(slot/6)*(InventorySlotSize+InventorySlotSpacing)),Size=new Vector2(InventorySlotSize,InventorySlotSize)};
			box.LoadTextures(_gui.UI);box.OnItemMouseClicked+=(clicked,button)=>HandleContainerSlotClick(local,button);_containerSlotBoxes[slot]=box;_containerWindow.AddChild(box);
		}
		_gui.AddControl(_containerWindow);
	}

	private void HandleContainerState(ContainerStatePacket packet)
	{
		if(!packet.IsOpen){if(_containerState?.SessionId==packet.SessionId)CloseContainerUI(send:false);return;}
		_containerState=packet;if(_containerWindow==null)return;
		if(!_inputOwnership.OpenInventory())return;
		if(_inventoryWindow!=null)_inventoryWindow.Visible=false;_containerWindow.Visible=true;_containerWindow.BringToFront();RefreshContainerUI();RefreshInventoryUI();ApplyInputOwnership();
	}

	private void HandleContainerSlotClick(int slot,FishMouseButton button)
	{
		if(_containerState==null||_client==null||button is not (FishMouseButton.Left or FishMouseButton.Right))return;
		_client.Send(new ContainerActionRequestPacket{SessionId=_containerState.SessionId,ActionId=_nextContainerActionId++,ExpectedPlayerRevision=_containerState.PlayerRevision,ExpectedContainerRevision=_containerState.ContainerRevision,Kind=button==FishMouseButton.Left?InventoryActionKind.LeftClickSlot:InventoryActionKind.RightClickSlot,Slot=checked((byte)slot)},true,GetClientTime());
	}

	private void RefreshContainerUI()
	{
		if(_containerState==null||_containerSlotBoxes==null||_gui==null)return;
		for(int slot=0;slot<_containerSlotBoxes.Length;slot++)
		{
			ItemStack stack=slot<_containerState.Slots.Length?_containerState.Slots[slot]:ItemStack.Empty;FishUIItemBox box=_containerSlotBoxes[slot];
			if(box.Stack.Item!=stack.Item)ClientItemPresentationCatalog.ApplyIcon(_gui.UI,box,stack);box.SetStack(stack);box.TooltipText=stack.IsEmpty?null:$"{ItemCatalog.Get(stack.Item).DisplayName} x{stack.Count}";
		}
	}

	private void CloseContainerUI(bool send)
	{
		ContainerStatePacket state=_containerState;_containerState=null;if(_containerWindow!=null)_containerWindow.Visible=false;
		if(send&&state!=null&&_client?.IsConnected==true)_client.Send(new ContainerClosePacket{SessionId=state.SessionId},true,GetClientTime());
		if(_inventoryCursorGhost!=null)_inventoryCursorGhost.Visible=false;if(_inputOwnership.Mode==GameplayInputMode.Inventory){_inputOwnership.CloseOverlay();ApplyInputOwnership();}
	}
}
