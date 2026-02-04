using Raylib_cs;

using RaylibGame.States;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine;

namespace Voxelgine.Engine
{
	public class WeaponGun : Weapon
	{

		/// <summary>
		/// Whether the gun is currently in aiming mode (right-click held).
		/// Firing is only allowed while aiming.
		/// </summary>
		public bool IsAiming { get; private set; }

		public WeaponGun(Player ParentPlayer, string Name) : base(ParentPlayer, Name, IconType.Gun)
		{
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

		public override void OnLeftClick(InventoryClickEventArgs E)
		{
			// Only allow firing when aiming (right-click held)
			if (!IsAiming)
				return;

			// Apply kickback animation to the view model
			ParentPlayer.ViewMdl.ApplyKickback();

			Vector3 Pos = Raycast(E.Map, E.Start, E.Dir, E.MaxLen, out Vector3 Norm);

			if (Pos != Vector3.Zero)
			{
				Console.WriteLine("Hit!");

				GameState GState = ((GameState)Program.GameState);
				// Spawn fire effect at hit position with wall normal as initial force

				for (int i = 0; i < 6; i++)
				{
					float ForceFactor = 10.6f;
					float RandomUnitFactor = 0.6f;

					if (Norm.Y == 0)
					{
						ForceFactor *= 2;
						RandomUnitFactor = 0.4f;
					}

					Vector3 RndDir = Vector3.Normalize(Norm + Utils.GetRandomUnitVector() * RandomUnitFactor);
					GState.Particle.SpawnFire(Pos, RndDir * ForceFactor, Color.White, (float)(Utils.Rnd.NextDouble() + 0.5));
				}
			}

		}
	}
}
