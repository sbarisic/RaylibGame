using Raylib_cs;

using RaylibGame.States;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine {
	public class WeaponGun : Weapon {

		public WeaponGun(Player ParentPlayer, string Name) : base(ParentPlayer, Name, IconType.Gun) {
			SetViewModelInfo(ViewModelRotationMode.Gun);
			SetupModel("gun/gun.obj");
		}

		public override void OnLeftClick(InventoryClickEventArgs E) {

			Vector3 Pos = Raycast(E.Map, E.Start, E.Dir, E.MaxLen, out Vector3 Norm);

			if (Pos != Vector3.Zero) {
				Console.WriteLine("Hit!");

				GameState GState = ((GameState)Program.GameState);
				GState.Particle.SpawnSmoke(Pos, Norm * 1.6f, Color.White);
				/*float ScaleFact = 1.0f / 5;



				Vector3[] RawPoints = Utils.GenerateVoxelSphere(10, false);
				Vector3[] Points = new Vector3[0];

				while (Points.Length < 30)
					Points = RawPoints.TakeWhile(V => Random.Shared.NextSingle() < 0.99f).Take(30).ToArray();


				for (int i = 0; i < Points.Length; i++) {
					float X = Random.Shared.NextSingle() - 0.5f;
					float Y = Random.Shared.NextSingle() - 0.5f;
					float Z = Random.Shared.NextSingle() - 0.5f;
					Points[i] = (Points[i] + new Vector3(X, Y, Z)) * ScaleFact;

					GState.Particle.SpawnSmoke(Pos + Points[i] + new Vector3(0, 2, 0), Norm * 1.6f, Color.White);
				}*/
			}

		}
	}
}
