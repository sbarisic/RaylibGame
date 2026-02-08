using Raylib_cs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Voxelgine.Engine.DI;

namespace Voxelgine.Engine
{
	public class WeaponPicker : Weapon
	{

		public WeaponPicker(IFishEngineRunner Eng, Player ParentPlayer, string Name) : base(Eng, ParentPlayer, Name, IconType.Hammer)
		{
			SetViewModelInfo(ViewModelRotationMode.Tool);
			SetupJsonModel("hammer/hammer.json", "hammer/hammer_tex.png");
		}

		public override void OnLeftClick(InventoryClickEventArgs E)
		{
			// Apply swing animation to the view model
			ParentPlayer.ViewMdl.ApplySwing();

			// Destroy the block (base handles the raycast and Map.SetBlock call) - don't destroy, do nothing for now
			// base.OnLeftClick(E);
		}
	}
}
