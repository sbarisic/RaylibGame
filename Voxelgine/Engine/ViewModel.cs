using RaylibGame.Engine;

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


namespace Voxelgine.Engine {
	public enum ViewModelRotationMode {
		Block,
		Tool,
		Gun,
		GunIronsight,
	}

	public class ViewModel {
		// Viewmodel fields
		Model VModel; // Non-animated viewmodel
		const string DefaultViewModelName = "gun/gun.obj";
		ViewModelRotationMode ViewMdlRotMode = ViewModelRotationMode.GunIronsight;

		Vector3 DesiredViewModelPos = Vector3.Zero;
		public Vector3 ViewModelPos = Vector3.Zero;

		Quaternion DesiredVMRot = Quaternion.Identity;
		public Quaternion VMRot = Quaternion.Identity;

		public bool IsActive;

		LerpVec3 LrpPos;
		LerpQuat LrpRot;

		public ViewModel() {
			SetModel(DefaultViewModelName);
			IsActive = true;

			if (VModel.MeshCount == 0) {
				IsActive = false;
				Console.WriteLine("======================== Warning! Zero meshes in model {0}", DefaultViewModelName);
			}

			LrpPos = new LerpVec3();
			LrpPos.Easing = Easing.Linear;
			LrpPos.Loop = false;
			LrpPos.StartLerp(1, Vector3.Zero, Vector3.Zero);

			LrpRot = new LerpQuat();
			LrpRot.Easing = Easing.Linear;
			LrpRot.Loop = false;
			LrpRot.StartLerp(1, Quaternion.CreateFromYawPitchRoll(0, 0, 0), Quaternion.CreateFromYawPitchRoll(Utils.ToRad(0), 0, 0));
		}

		public void SetModel(string ModelName) {
			VModel = ResMgr.GetModel(ModelName);
		}

		public void SetModel(Model Mdl) {
			VModel = Mdl;
		}

		public void SetRotationMode(ViewModelRotationMode Mode) {
			if (ViewMdlRotMode != Mode) {
				ViewMdlRotMode = Mode;
				// TODO: Toggle animation

				Console.WriteLine("Toggle anim!");
			}
		}


		// TODO: Make animation system for viewmodels better, lerp between rotations and positions instead of using a switch statement?
		public void DrawViewModel(Player Ply, float TimeAlpha, ref GameFrameInfo LastFrame, ref GameFrameInfo CurFame) {
			if (!IsActive)
				return;

			// Camera basis
			var cam = Ply.Cam;
			Vector3 worldUp = Vector3.UnitY;
			Vector3 camForward = Ply.GetForward();
			Vector3 camRight = -Ply.GetLeft();
			Vector3 camUp = Ply.GetUp();

			// Set desired position and rotation based on mode
			Vector3 newDesiredPos;
			Quaternion newDesiredRotOffset;
			switch (ViewMdlRotMode) {
				case ViewModelRotationMode.Block:
				case ViewModelRotationMode.Tool:
					newDesiredPos = cam.Position + camForward * 0.5f + camRight * 0.5f + camUp * -0.3f;
					newDesiredRotOffset = Quaternion.CreateFromYawPitchRoll(Utils.ToRad(0), 0, 0);
					break;
				case ViewModelRotationMode.Gun:
					newDesiredPos = cam.Position + camForward * 0.7f + camRight * 0.4f + camUp * -0.3f;
					newDesiredRotOffset = Quaternion.CreateFromYawPitchRoll(Utils.ToRad(45), 0, 0);
					break;
				case ViewModelRotationMode.GunIronsight:
					newDesiredPos = cam.Position + camForward * 0.72f + camRight * 0.125f + camUp * -0.19f;
					newDesiredRotOffset = Quaternion.CreateFromYawPitchRoll(Utils.ToRad(0), 0, 0);
					break;
				default:
					throw new NotImplementedException();
			}

			// Start lerps if desired position/rotation changed
			if (DesiredViewModelPos != newDesiredPos) {
				LrpPos.StartLerp(0.2f, ViewModelPos, newDesiredPos);
				DesiredViewModelPos = newDesiredPos;
			}
			if (LrpRot.GetQuat() != newDesiredRotOffset) {
				LrpRot.StartLerp(0.2f, LrpRot.GetQuat(), newDesiredRotOffset);
			}

			// Lerp position and rotation
			ViewModelPos = LrpPos.GetVec3();

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

			DesiredVMRot = qPitch * qYaw;
			switch (ViewMdlRotMode) {
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
			DesiredVMRot = System.Numerics.Quaternion.Normalize(DesiredVMRot);

			// Lerp rotation
			VMRot = Quaternion.Slerp(VMRot, DesiredVMRot * LrpRot.GetQuat(), 0.2f);

			float angle = 2.0f * MathF.Acos(VMRot.W) * 180f / MathF.PI;
			float s = MathF.Sqrt(1 - VMRot.W * VMRot.W);
			Vector3 axis = s < 0.001f ? new Vector3(1, 0, 0) : new Vector3(VMRot.X / s, VMRot.Y / s, VMRot.Z / s);

			Raylib.DrawModelEx(VModel, ViewModelPos, axis, angle, new Vector3(1, 1, 1), Color.White);
		}
	}
}
