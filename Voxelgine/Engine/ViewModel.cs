using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Voxelgine.Graphics;
using Voxelgine.GUI;
using Voxelgine.Engine.DI;


namespace Voxelgine.Engine
{
	public enum ViewModelRotationMode
	{
		Block,
		Tool,
		Gun,
		GunIronsight,
	}

	public class ViewModel
	{
		// Arm model (always loaded)
		CustomModel ArmModel;
		bool ArmModelLoaded;

		// Current weapon model (null when nothing equipped)
		CustomModel WeaponModel;
		bool WeaponModelLoaded;

		// Offset to align weapon grip with arm hand (computed when weapon is set)
		Vector3 WeaponGripOffset = Vector3.Zero;

		/// <summary>
		/// World-space muzzle point extracted from the weapon's "projectile" mesh.
		/// Used for fire effects (WeaponFireEffectPacket origin).
		/// </summary>
		public Vector3 MuzzlePoint;

		ViewModelRotationMode ViewMdlRotMode = ViewModelRotationMode.GunIronsight;

		// Store offset from camera instead of absolute position for proper interpolation
		Vector3 DesiredViewModelOffset = Vector3.Zero;
		public Vector3 ViewModelOffset = Vector3.Zero;
		public Vector3 ViewModelPos = Vector3.Zero; // Final position (for capture in GameFrameInfo)

		Quaternion DesiredVMRot = Quaternion.Identity;
		public Quaternion VMRot = Quaternion.Identity;

		public bool IsActive;

		LerpVec3 LrpOffset;
		LerpQuat LrpRot;

		// Kickback animation for weapon firing
		LerpVec3 LrpKickback;
		Vector3 KickbackOffset = Vector3.Zero;

		// Jiggle animation for empty hand clicks
		LerpVec3 LrpJiggle;
		Vector3 JiggleOffset = Vector3.Zero;

		// Swing animation for melee weapons
		LerpFloat LrpSwing;
		float SwingAngle = 0f;
		bool IsSwinging = false;

		IFishEngineRunner Eng;
		IFishLogging Logging;

		public ViewModel(IFishEngineRunner Eng)
		{
			this.Eng = Eng;
			this.Logging = Eng.DI.GetRequiredService<IFishLogging>();

			LoadArmModel();
			IsActive = true;

			var lerpMgr = Eng.DI.GetRequiredService<ILerpManager>();

			LrpOffset = new LerpVec3(lerpMgr);
			LrpOffset.Easing = Easing.Linear;
			LrpOffset.Loop = false;
			LrpOffset.StartLerp(1, Vector3.Zero, Vector3.Zero);

			LrpRot = new LerpQuat(lerpMgr);
			LrpRot.Easing = Easing.Linear;
			LrpRot.Loop = false;
			LrpRot.StartLerp(1, Quaternion.CreateFromYawPitchRoll(0, 0, 0), Quaternion.CreateFromYawPitchRoll(Utils.ToRad(0), 0, 0));

			LrpKickback = new LerpVec3(lerpMgr);
			LrpKickback.Easing = Easing.EaseOutQuad;
			LrpKickback.Loop = false;
			LrpKickback.StartLerp(0.01f, Vector3.Zero, Vector3.Zero);

			LrpJiggle = new LerpVec3(lerpMgr);
			LrpJiggle.Easing = Easing.EaseOutQuad;
			LrpJiggle.Loop = false;
			LrpJiggle.StartLerp(0.01f, Vector3.Zero, Vector3.Zero);

			LrpSwing = new LerpFloat(lerpMgr);
			LrpSwing.Easing = Easing.EaseOutQuad;
			LrpSwing.Loop = false;
			LrpSwing.StartLerp(0.01f, 0f, 0f);
		}

		void LoadArmModel()
		{
			try
			{
				MinecraftModel jsonModel = ResMgr.GetJsonModel("viewmodel_arm/viewmodel_arm.json");
				ArmModel = MeshGenerator.Generate(jsonModel);
				ArmModel.SetTexture(ResMgr.GetModelTexture("viewmodel_arm/viewmodel_arm_tex.png"));
				ArmModel.SetupHierarchy("arm", "hand");
				ArmModelLoaded = true;
			}
			catch (Exception ex)
			{
				Logging.WriteLine($"ViewModel: Failed to load arm model: {ex.Message}");
				ArmModelLoaded = false;
			}
		}

		/// <summary>
		/// Sets a JSON-based weapon model to be drawn attached to the arm's hand.
		/// The weapon's "grip" mesh is aligned to the arm's "hand" mesh.
		/// </summary>
		public void SetWeaponModel(CustomModel weapon)
		{
			WeaponModel = weapon;
			WeaponModelLoaded = weapon != null;

			if (WeaponModelLoaded && ArmModelLoaded)
			{
				CustomMesh hand = ArmModel.GetMeshByName("hand");
				CustomMesh grip = weapon.GetMeshByName("grip");
				if (hand != null && grip != null)
				{
					Vector3 handCenter = (hand.BBox.Min + hand.BBox.Max) * 0.5f;
					Vector3 gripCenter = (grip.BBox.Min + grip.BBox.Max) * 0.5f;
					WeaponGripOffset = handCenter - gripCenter;
				}
				else
				{
					WeaponGripOffset = Vector3.Zero;
				}
			}
		}

		/// <summary>
		/// Clears the current weapon model. The arm will be drawn in a lowered position.
		/// </summary>
		public void ClearWeaponModel()
		{
			WeaponModel = null;
			WeaponModelLoaded = false;
			WeaponGripOffset = Vector3.Zero;
		}

		public void SetRotationMode(ViewModelRotationMode Mode)
		{
			if (ViewMdlRotMode != Mode)
			{
				ViewMdlRotMode = Mode;
			}
		}

		/// <summary>
		/// Applies a kickback animation to the view model (e.g., when firing a weapon).
		/// The weapon moves backward briefly then returns to its original position.
		/// </summary>
		public void ApplyKickback()
		{
			// Kickback moves the weapon backward (negative forward direction)
			// The kickback amount is applied in local camera space
			const float KickbackAmount = 0.08f;
			const float KickbackDuration = 0.12f;

			// Start kickback animation: move back then return to zero
			LrpKickback.Easing = Easing.EaseOutQuad;
			LrpKickback.StartLerp(KickbackDuration * 0.3f, Vector3.Zero, new Vector3(0, 0, -KickbackAmount));
			LrpKickback.OnComplete = (lerp) =>
			{
				// Return to original position
				LrpKickback.Easing = Easing.EaseOutQuad;
				LrpKickback.StartLerp(KickbackDuration * 0.7f, new Vector3(0, 0, -KickbackAmount), Vector3.Zero);
				LrpKickback.OnComplete = null;
			};
		}

		/// <summary>
		/// Applies a swing animation to the view model (e.g., for melee weapons like hammer).
		/// The weapon swings forward and down, then returns to idle position.
		/// </summary>
		public void ApplySwing()
		{
			if (IsSwinging)
				return;

			IsSwinging = true;
			const float SwingAmount = 60f; // Degrees to swing
			const float SwingDuration = 0.35f;

			// Swing forward (wind up slightly, then swing down)
			LrpSwing.Easing = Easing.EaseOutCubic;
			LrpSwing.StartLerp(SwingDuration * 0.4f, 0f, SwingAmount);
			LrpSwing.OnComplete = (lerp) =>
			{
				// Return to original position
				LrpSwing.Easing = Easing.EaseOutQuad;
				LrpSwing.StartLerp(SwingDuration * 0.6f, SwingAmount, 0f);
				LrpSwing.OnComplete = (lerp2) =>
				{
					IsSwinging = false;
					LrpSwing.OnComplete = null;
				};
			};
		}

		/// <summary>
		/// Applies a subtle jiggle animation to the arm when clicking with an empty hand.
		/// A quick forward-down reach then return, like a tap or punch gesture.
		/// </summary>
		public void ApplyJiggle()
		{
			const float JiggleForward = 0.04f;
			const float JiggleDown = -0.03f;
			const float JiggleDuration = 0.18f;

			LrpJiggle.Easing = Easing.EaseOutQuad;
			LrpJiggle.StartLerp(JiggleDuration * 0.35f, Vector3.Zero, new Vector3(0, JiggleDown, JiggleForward));
			LrpJiggle.OnComplete = (lerp) =>
			{
				LrpJiggle.Easing = Easing.EaseOutQuad;
				LrpJiggle.StartLerp(JiggleDuration * 0.65f, new Vector3(0, JiggleDown, JiggleForward), Vector3.Zero);
				LrpJiggle.OnComplete = null;
			};
		}

		public void Update(Player Ply)
		{
			// Camera basis
			var cam = Ply.Cam;
			Vector3 worldUp = Vector3.UnitY;
			Vector3 camForward = Ply.GetForward();
			Vector3 camRight = -Ply.GetLeft();
			Vector3 camUp = Ply.GetUp();

			// Calculate offset from camera based on mode (not absolute position)
			Vector3 newDesiredOffset;
			Quaternion newDesiredRotOffset;

			if (!WeaponModelLoaded)
			{
				// No weapon equipped: lower the arm so it doesn't obstruct the view
				newDesiredOffset = camForward * 0.3f + camRight * 0.5f + camUp * -0.7f;
				newDesiredRotOffset = Quaternion.CreateFromYawPitchRoll(0, 0, 0);
			}
			else
			{
				switch (ViewMdlRotMode)
				{
					case ViewModelRotationMode.Block:
					case ViewModelRotationMode.Tool:
						newDesiredOffset = camForward * 0.5f + camRight * 0.5f + camUp * -0.3f;
						newDesiredRotOffset = Quaternion.CreateFromYawPitchRoll(Utils.ToRad(0), 0, 0);
						break;
					case ViewModelRotationMode.Gun:
						newDesiredOffset = camForward * 0.7f + camRight * 0.4f + camUp * -0.6f;
						newDesiredRotOffset = Quaternion.CreateFromYawPitchRoll(Utils.ToRad(45), 0, 0);
						break;
					case ViewModelRotationMode.GunIronsight:
						newDesiredOffset = camForward * 0.72f + camRight * 0.125f + camUp * -0.19f;
						newDesiredRotOffset = Quaternion.CreateFromYawPitchRoll(Utils.ToRad(0), 0, 0);
						break;
					default:
						throw new NotImplementedException();
				}
			}

			// Start lerps if desired offset/rotation changed
			if (DesiredViewModelOffset != newDesiredOffset)
			{
				LrpOffset.StartLerp(0.2f, ViewModelOffset, newDesiredOffset);
				DesiredViewModelOffset = newDesiredOffset;
			}
			if (LrpRot.GetQuat() != newDesiredRotOffset)
			{
				LrpRot.StartLerp(0.2f, LrpRot.GetQuat(), newDesiredRotOffset);
			}

			// Lerp offset
			ViewModelOffset = LrpOffset.GetVec3();

			// Apply kickback offset in camera space
			KickbackOffset = LrpKickback.GetVec3();

			// Apply jiggle offset for empty hand clicks
			JiggleOffset = LrpJiggle.GetVec3();

			// Update swing angle
			SwingAngle = LrpSwing.GetFloat();

			// Calculate absolute position for GameFrameInfo capture
			ViewModelPos = cam.Position + ViewModelOffset;

			Vector3 CamAngle = Ply.GetCamAngle();
			float yaw = Utils.ToRad(0) - CamAngle.X * MathF.PI / 180f;
			float pitch = Utils.ToRad(90) - CamAngle.Y * MathF.PI / 180f;
			var yawRot = Matrix4x4.CreateFromAxisAngle(worldUp, -yaw);
			camRight = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, yawRot));
			var pitchRot = Matrix4x4.CreateFromAxisAngle(camRight, -pitch);
			var modelMat = pitchRot * yawRot * Matrix4x4.CreateTranslation(ViewModelPos);
			var qYaw = Quaternion.CreateFromAxisAngle(worldUp, -yaw);
			var qPitch = Quaternion.CreateFromAxisAngle(camRight, -pitch);
			var qInitial = Quaternion.CreateFromAxisAngle(camUp, Utils.ToRad(90));
			var qWeaponAngle = Quaternion.CreateFromAxisAngle(camRight, Utils.ToRad(180 + 35));
			var qAwayFromCam = Quaternion.CreateFromAxisAngle(camUp, Utils.ToRad(-22));

			if (!WeaponModelLoaded)
			{
				// Simple camera-following rotation for lowered arm
				DesiredVMRot = qPitch * qYaw;
			}
			else
			{
				switch (ViewMdlRotMode)
				{
					case ViewModelRotationMode.Block:
					case ViewModelRotationMode.Tool:
						DesiredVMRot = qAwayFromCam * qWeaponAngle * qInitial * qPitch * qYaw;
						break;
					case ViewModelRotationMode.Gun:
						DesiredVMRot = Quaternion.CreateFromAxisAngle(camForward, Utils.ToRad(180)) * qInitial * qPitch * qYaw;
						break;
					case ViewModelRotationMode.GunIronsight:
						DesiredVMRot = Quaternion.CreateFromAxisAngle(camRight, Utils.ToRad(2)) * Quaternion.CreateFromAxisAngle(camForward, Utils.ToRad(180)) * qInitial * qPitch * qYaw;
						break;
					default:
						throw new NotImplementedException();
				}
			}
			DesiredVMRot = System.Numerics.Quaternion.Normalize(DesiredVMRot);

			// Lerp rotation
			VMRot = Quaternion.Slerp(VMRot, DesiredVMRot * LrpRot.GetQuat(), 0.2f);
		}


		public void DrawViewModel(Player Ply, float TimeAlpha, ref GameFrameInfo LastFrame, ref GameFrameInfo CurFame)
		{
			if (!IsActive || !ArmModelLoaded)
				return;

			// Calculate kickback and jiggle in world space using camera basis
			Vector3 camForward = Ply.GetForward();
			Vector3 camRight = -Ply.GetLeft();
			Vector3 camUp = Ply.GetUp();
			Vector3 KickbackWorld = camForward * KickbackOffset.Z;
			Vector3 JiggleWorld = camForward * JiggleOffset.Z + camUp * JiggleOffset.Y;

			// Calculate final position using interpolated render camera position + offset + kickback + jiggle
			Vector3 P = Ply.RenderCam.Position + ViewModelOffset + KickbackWorld + JiggleWorld;
			Quaternion R = VMRot;

			// Apply swing rotation around the right axis (pitches the weapon forward)
			if (SwingAngle != 0f)
			{
				Quaternion swingRot = Quaternion.CreateFromAxisAngle(camRight, Utils.ToRad(SwingAngle));
				R = swingRot * R;
			}

			// Sample light level at player position and apply to view model
			Color lightColor = Color.White;
			if (Eng.MultiplayerGameState?.Map != null)
			{
				lightColor = Eng.MultiplayerGameState.Map.GetLightColor(Ply.Position);
			}

			// Build model matrix from quaternion rotation + position
			Matrix4x4 rotMatrix = Matrix4x4.CreateFromQuaternion(R);
			Matrix4x4 armMat = rotMatrix * Matrix4x4.CreateTranslation(P);

			// Draw arm
			ArmModel.DrawWithMatrix(armMat, lightColor);

			// Draw weapon attached to hand
			if (WeaponModelLoaded)
			{
				Matrix4x4 weaponMat = Matrix4x4.CreateTranslation(WeaponGripOffset) * armMat;
				WeaponModel.DrawWithMatrix(weaponMat, lightColor);

				// Extract muzzle point from projectile mesh
				CustomMesh projectile = WeaponModel.GetMeshByName("projectile");
				if (projectile != null)
				{
					Matrix4x4 muzzleWorld = projectile.GetWorldMatrix(weaponMat);
					MuzzlePoint = new Vector3(muzzleWorld.M41, muzzleWorld.M42, muzzleWorld.M43);
				}
			}
		}
	}
}
