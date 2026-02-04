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

			// Play shooting sound at player position
			ParentPlayer.PlaySound("shoot1", ParentPlayer.Position);

			GameState GState = ((GameState)Program.GameState);

			// Raycast against world (blocks)
			Vector3 worldHitPos = Raycast(E.Map, E.Start, E.Dir, E.MaxLen, out Vector3 worldNorm);
			float worldDist = worldHitPos != Vector3.Zero ? Vector3.Distance(E.Start, worldHitPos) : float.MaxValue;

			// Raycast against entities
			RaycastHit entityHit = GState.Entities.Raycast(E.Start, E.Dir, E.MaxLen);

			// Determine which hit is closer
			Vector3 hitPos;
			Vector3 hitNormal;
			VoxEntity hitEntity = null;

			if (entityHit.Hit && entityHit.Distance < worldDist)
			{
				// Entity hit is closer
				hitPos = entityHit.HitPosition;
				hitNormal = entityHit.HitNormal;
				hitEntity = entityHit.Entity;

				// If it's an NPC, perform detailed body part raycast
				if (hitEntity is VEntNPC npc)
				{
					string bodyPart = npc.RaycastBodyPart(E.Start, E.Dir, out Vector3 partHitPos, out Vector3 partHitNormal);
					if (bodyPart != null)
					{
						// Use the more precise mesh hit position/normal
						hitPos = partHitPos;
						hitNormal = partHitNormal;
						Console.WriteLine($"Hit NPC body part: {bodyPart} at distance {entityHit.Distance:F2}");
					}
					else
					{
						Console.WriteLine($"Hit NPC (AABB) at distance {entityHit.Distance:F2}");
					}
				}
				else
				{
					Console.WriteLine($"Hit entity: {hitEntity.GetType().Name} at distance {entityHit.Distance:F2}");
				}
			}
			else if (worldHitPos != Vector3.Zero)
			{
				// World hit is closer (or no entity hit)
				hitPos = worldHitPos;
				hitNormal = worldNorm;
				Console.WriteLine("Hit world!");
			}
			else
			{
				// No hit at all
				return;
			}

			// Spawn fire effect at hit position with surface normal as initial force
			for (int i = 0; i < 6; i++)
			{
				float ForceFactor = 10.6f;
				float RandomUnitFactor = 0.6f;

				if (hitNormal.Y == 0)
				{
					ForceFactor *= 2;
					RandomUnitFactor = 0.4f;
				}

				Vector3 RndDir = Vector3.Normalize(hitNormal + Utils.GetRandomUnitVector() * RandomUnitFactor);
				GState.Particle.SpawnFire(hitPos, RndDir * ForceFactor, Color.White, (float)(Utils.Rnd.NextDouble() + 0.5));
			}
		}
	}
}
