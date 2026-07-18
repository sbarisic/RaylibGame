using Voxelgine.Engine.DI;

namespace Voxelgine.Engine;

public class Weapon : InventoryItem
{
	public Weapon(
		IFishEngineRunner engine,
		ClientPlayer parentPlayer,
		string name,
		BlockType blockIcon
	) : base(engine, parentPlayer, name, blockIcon)
	{
	}

	public Weapon(
		IFishEngineRunner engine,
		ClientPlayer parentPlayer,
		BlockType blockIcon
	) : this(engine, parentPlayer, blockIcon.ToString(), blockIcon)
	{
	}

	public Weapon(
		IFishEngineRunner engine,
		ClientPlayer parentPlayer,
		string name,
		IconType icon
	) : base(engine, parentPlayer, name, icon)
	{
	}
}
