using System.Numerics;

using Voxelgine.Engine.DI;

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

			// Send fire packet to server for authoritative hit resolution
			var mpState = Eng.MultiplayerGameState;
			if (mpState != null && mpState.IsActive)
			{
				mpState.SendWeaponFire(E.Start, E.Dir);
			}
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
	}
}
