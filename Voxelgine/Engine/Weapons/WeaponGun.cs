using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine;
using Voxelgine.Engine.DI;
using Voxelgine.States;

namespace Voxelgine.Engine
{
	public class WeaponGun : Weapon
	{

		/// <summary>
		/// Whether the gun is currently in aiming mode (right-click held).
		/// Firing is only allowed while aiming.
		/// </summary>
		public bool IsAiming { get; private set; }

		/// <summary>
		/// Gun supports automatic fire when aiming.
		/// </summary>
		public override bool SupportsAutoFire => true;

		/// <summary>
		/// Fire rate in shots per second.
		/// </summary>
		public override float AutoFireRate => 10f;

		public WeaponGun(IFishEngineRunner Eng, Player ParentPlayer, string Name) : base(Eng, ParentPlayer, Name, IconType.Gun)
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

			GameState GState = ((GameState)Eng.GameState);

			// Calculate muzzle position (slightly in front of camera)
			Vector3 muzzlePos = E.Start + E.Dir * 0.5f;

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
						Logging.WriteLine($"Hit NPC body part: {bodyPart} at distance {entityHit.Distance:F2}");

						// Apply twitch effect to the hit body part
						npc.TwitchBodyPart(bodyPart, E.Dir);
					}
					else
					{
						Logging.WriteLine($"Hit NPC (AABB) at distance {entityHit.Distance:F2}");
					}

					// Spawn blood particles for NPC hits
					for (int i = 0; i < 8; i++)
					{
						GState.Particle.SpawnBlood(hitPos, hitNormal * 0.5f, (0.8f + (float)Utils.Rnd.NextDouble() * 0.4f) * 0.85f);
					}
				}
				else
				{
					Logging.WriteLine($"Hit entity: {hitEntity.GetType().Name} at distance {entityHit.Distance:F2}");
				}
			}
			else if (worldHitPos != Vector3.Zero)
			{
				// World hit is closer (or no entity hit)
				hitPos = worldHitPos;
				hitNormal = worldNorm;
				Logging.WriteLine("Hit world!");
			}
			else
			{
				// No hit at all - tracer goes to max range
				hitPos = E.Start + E.Dir * E.MaxLen;
				hitNormal = -E.Dir;
			}

			// Spawn tracer line from muzzle to hit point
			GState.Particle.SpawnTracer(muzzlePos, hitPos);

			// Only spawn fire for actual world hits (not NPCs, not empty air)
			bool hitWorld = worldHitPos != Vector3.Zero && (hitEntity == null || worldDist <= entityHit.Distance);
			if (hitWorld && hitEntity is not VEntNPC)
			{
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
}
