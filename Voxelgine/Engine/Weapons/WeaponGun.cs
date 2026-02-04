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

		/// <summary>
		/// Whether the gun is currently in aiming mode (right-click held).
		/// Firing is only allowed while aiming.
		/// </summary>
		public bool IsAiming { get; private set; }

		public WeaponGun(Player ParentPlayer, string Name) : base(ParentPlayer, Name, IconType.Gun) {
			SetViewModelInfo(ViewModelRotationMode.Gun);
			SetupModel("gun/gun.obj");
		}

		public override void Tick(ViewModel ViewMdl, InputMgr InMgr)
		{
			// Track aiming state
			IsAiming = InMgr.IsInputDown(InputKey.Click_Right);

			// Update view model rotation mode based on aim state
			if (IsAiming)
				ViewModelRotationMode = ViewModelRotationMode.GunIronsight;
			else
				ViewModelRotationMode = ViewModelRotationMode.Gun;

			ViewMdl.SetRotationMode(ViewModelRotationMode);
		}

		public override void OnLeftClick(InventoryClickEventArgs E) {
			// Only allow firing when aiming (right-click held)
			if (!IsAiming)
				return;

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
