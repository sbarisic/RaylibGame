using Raylib_cs;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;
using Voxelgine.Engine.DI;
using Voxelgine.Graphics;
using Voxelgine.GUI;


namespace Voxelgine.Engine
{
	public class Weapon : InventoryItem
	{
		public Weapon(IFishEngineRunner Eng, Player ParentPlayer, string Name, BlockType BlockIcon) : base(Eng, ParentPlayer, Name, BlockIcon)
		{
		}

		public Weapon(IFishEngineRunner Eng, Player ParentPlayer, BlockType BlockIcon) : this(Eng, ParentPlayer, BlockIcon.ToString(), BlockIcon)
		{
		}

		public Weapon(IFishEngineRunner Eng, Player ParentPlayer, string Name, IconType Icon) : base(Eng, ParentPlayer, Name, Icon)
		{
		}
	}
}
