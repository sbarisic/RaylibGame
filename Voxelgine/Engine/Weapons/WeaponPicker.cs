using Voxelgine.Engine.DI;

namespace Voxelgine.Engine;

public sealed class WeaponPicker : Weapon
{
	public WeaponPicker(
		IFishEngineRunner engine,
		ClientPlayer parentPlayer,
		string name
	) : base(engine, parentPlayer, name, IconType.Hammer)
	{
		SetViewModelInfo(ViewModelRotationMode.Tool);
	}

	public override void OnLeftClick(InventoryClickEventArgs args)
	{
		ParentPlayer.ViewMdl.ApplySwing();
	}
}
