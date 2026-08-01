using Voxelgine.Engine;

namespace Voxelgine.States;

public unsafe partial class MPClientGameState
{
	private void HandleLocalItemUseRequested(ItemUseChannel channel)
	{
		_itemUseController?.RequestUse(channel);
	}

	private void HandleItemUseResult(ItemUseResultPacket packet)
	{
		_itemUseController?.HandleResult(packet);
	}
}
