using Raylib_cs;

using RaylibGame.States;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Graphics;
using Voxelgine.GUI;


namespace Voxelgine.Engine {
	public class Weapon : InventoryItem {
		public Weapon(Player ParentPlayer, string Name, BlockType BlockIcon) : base(ParentPlayer, Name, BlockIcon) {
		}

		public Weapon(Player ParentPlayer, BlockType BlockIcon) : this(ParentPlayer, BlockIcon.ToString(), BlockIcon) {
		}

		public Weapon(Player ParentPlayer, string Name, IconType Icon) : base(ParentPlayer, Name, Icon) {
		}
	}
}
