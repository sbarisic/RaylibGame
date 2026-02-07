using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine;
using Voxelgine.Engine.DI;
using Voxelgine.Graphics;
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

			// Create fire intent
			FireIntent intent = new FireIntent(E.Start, E.Dir, E.MaxLen, Name, ParentPlayer);

			// Apply immediate fire effects (kickback, sound) — these play regardless of hit result
			ApplyFireEffects(intent);

			// In multiplayer, send fire packet to server for authoritative hit resolution
			var mpState = Eng.MultiplayerGameState;
			if (mpState != null)
			{
				mpState.SendWeaponFire(E.Start, E.Dir);
				return;
			}

			// Single-player: resolve hit locally
			FireResult result = ResolveFireIntent(intent, E.Map);
			ApplyHitEffects(intent, result);
		}

		/// <summary>
		/// Applies immediate fire effects that play when the trigger is pulled,
		/// before hit resolution. Provides responsive feedback.
		/// </summary>
		void ApplyFireEffects(FireIntent intent)
		{
			ParentPlayer.ViewMdl.ApplyKickback();
			ParentPlayer.PlaySound("shoot1", ParentPlayer.Position);
		}

		/// <summary>
		/// Resolves a fire intent by raycasting against the world and entities.
		/// In single-player, called locally. In multiplayer, the server calls this authoritatively.
		/// </summary>
		public FireResult ResolveFireIntent(FireIntent intent, ChunkMap map)
		{
			GameState GState = ((GameState)Eng.GameState);

			// Raycast against world (blocks)
			Vector3 worldHitPos = Raycast(map, intent.Origin, intent.Direction, intent.MaxRange, out Vector3 worldNorm);
			float worldDist = worldHitPos != Vector3.Zero ? Vector3.Distance(intent.Origin, worldHitPos) : float.MaxValue;

			// Raycast against entities
			RaycastHit entityHit = GState.Entities.Raycast(intent.Origin, intent.Direction, intent.MaxRange);

			if (entityHit.Hit && entityHit.Distance < worldDist)
			{
				// Entity hit is closer
				Vector3 hitPos = entityHit.HitPosition;
				Vector3 hitNormal = entityHit.HitNormal;
				string bodyPart = null;

				// If it's an NPC, perform detailed body part raycast
				if (entityHit.Entity is VEntNPC npc)
				{
					bodyPart = npc.RaycastBodyPart(intent.Origin, intent.Direction, out Vector3 partHitPos, out Vector3 partHitNormal);
					if (bodyPart != null)
					{
						hitPos = partHitPos;
						hitNormal = partHitNormal;
					}
				}

				return new FireResult(FireHitType.Entity, hitPos, hitNormal, entityHit.Distance, entityHit.Entity, bodyPart);
			}
			else if (worldHitPos != Vector3.Zero)
			{
				return new FireResult(FireHitType.World, worldHitPos, worldNorm, worldDist);
			}

			return FireResult.Miss(intent.Origin, intent.Direction, intent.MaxRange);
		}

		/// <summary>
		/// Applies visual/audio effects based on the hit result (tracer, blood, sparks, twitch).
		/// In multiplayer, the client calls this when it receives the server's fire result.
		/// </summary>
		void ApplyHitEffects(FireIntent intent, FireResult result)
		{
			GameState GState = ((GameState)Eng.GameState);

			// Muzzle position (slightly in front of camera)
			Vector3 muzzlePos = intent.Origin + intent.Direction * 0.5f;

			// Tracer line from muzzle to hit point
			GState.Particle.SpawnTracer(muzzlePos, result.HitPosition);

			switch (result.HitType)
			{
				case FireHitType.Entity:
					if (result.HitEntity is VEntNPC npc)
					{
						Logging.WriteLine(result.BodyPartName != null
							? $"Hit NPC body part: {result.BodyPartName} at distance {result.HitDistance:F2}"
							: $"Hit NPC (AABB) at distance {result.HitDistance:F2}");

						// Apply twitch effect
						if (result.BodyPartName != null)
							npc.TwitchBodyPart(result.BodyPartName, intent.Direction);

						// Blood particles
						for (int i = 0; i < 8; i++)
						{
							GState.Particle.SpawnBlood(result.HitPosition, result.HitNormal * 0.5f, (0.8f + (float)Utils.Rnd.NextDouble() * 0.4f) * 0.85f);
						}
					}
					else
					{
						Logging.WriteLine($"Hit entity: {result.HitEntity.GetType().Name} at distance {result.HitDistance:F2}");
					}
					break;

				case FireHitType.World:
					Logging.WriteLine("Hit world!");

					for (int i = 0; i < 6; i++)
					{
						float ForceFactor = 10.6f;
						float RandomUnitFactor = 0.6f;

						if (result.HitNormal.Y == 0)
						{
							ForceFactor *= 2;
							RandomUnitFactor = 0.4f;
						}

						Vector3 RndDir = Vector3.Normalize(result.HitNormal + Utils.GetRandomUnitVector() * RandomUnitFactor);
						GState.Particle.SpawnFire(result.HitPosition, RndDir * ForceFactor, Color.White, (float)(Utils.Rnd.NextDouble() + 0.5));
					}
					break;
			}
		}
	}
}
