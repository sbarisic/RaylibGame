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
			SetupModel("hammer/hammer.obj");
		}

		public override void OnLeftClick(InventoryClickEventArgs E)
		{
			// Apply swing animation to the view model
			ParentPlayer.ViewMdl.ApplySwing();
		}
	}
}
