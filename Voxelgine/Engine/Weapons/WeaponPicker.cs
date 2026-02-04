using Raylib_cs;

using RaylibGame.States;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine {
	public class WeaponPicker : Weapon {

		public WeaponPicker(Player ParentPlayer, string Name) : base(ParentPlayer, Name, IconType.Hammer) {
			SetViewModelInfo(ViewModelRotationMode.Tool);
			SetupModel("hammer/hammer.obj");
		}

		public override void OnLeftClick(InventoryClickEventArgs E) {
			return;

			/*
			Vector3 Pos = Raycast(E.Map, E.Start, E.Dir, E.MaxLen, out Vector3 Norm);

			if (Pos != Vector3.Zero) {
				Console.WriteLine("Hit!");

				GameState GState = ((GameState)Program.GameState);
				GState.Particle.SpawnSmoke(Pos, Norm * 1.6f, Color.White);
			}
			//*/
		}
	}
}
