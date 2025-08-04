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

		public ViewModel() {
			SetModel(DefaultViewModelName);
			IsActive = true;

			if (VModel.MeshCount == 0) {
				IsActive = false;
				Console.WriteLine("======================== Warning! Zero meshes in model {0}", DefaultViewModelName);
			}
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

		public void DrawViewModel(Player Ply, float TimeAlpha, ref GameFrameInfo LastFrame, ref GameFrameInfo CurFame) {
			if (!IsActive)
				return;

			// Camera basis
			var cam = Ply.Cam;
			Vector3 worldUp = Vector3.UnitY;
			Vector3 camForward = Ply.GetForward();
			Vector3 camRight = -Ply.GetLeft();
			Vector3 camUp = Ply.GetUp();

			// Viewmodel position offset from camera
			//Vector3 vmPos = cam.Position + camForward * 0.5f + camRight * 0.5f + camUp * -0.3f;

			switch (ViewMdlRotMode) {
				case ViewModelRotationMode.Block:
				case ViewModelRotationMode.Tool:
					DesiredViewModelPos = cam.Position + camForward * 0.5f + camRight * 0.5f + camUp * -0.3f;
					break;

				case ViewModelRotationMode.Gun:
					DesiredViewModelPos = cam.Position + camForward * 0.7f + camRight * 0.4f + camUp * -0.3f;
					break;

				case ViewModelRotationMode.GunIronsight: {
					DesiredViewModelPos = cam.Position + camForward * 0.72f + camRight * 0.125f + camUp * -0.19f;
					break;
				}

				default:
					throw new NotImplementedException();
			}


			Vector3 CamAngle = Ply.GetCamAngle();

			// Get yaw and pitch from camera angles (in radians)
			float yaw = Utils.ToRad(0) - CamAngle.X * MathF.PI / 180f;   // Yaw: horizontal, around world Y
			float pitch = Utils.ToRad(90) - CamAngle.Y * MathF.PI / 180f; // Pitch: vertical, around local right

			// Yaw rotation (around world up)
			var yawRot = Matrix4x4.CreateFromAxisAngle(worldUp, -yaw);
			// Right vector after yaw
			camRight = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, yawRot));
			// Pitch rotation (around right after yaw)
			var pitchRot = Matrix4x4.CreateFromAxisAngle(camRight, -pitch);
			// Compose final rotation
			var modelMat = pitchRot * yawRot * Matrix4x4.CreateTranslation(ViewModelPos);

			// Quaternion for Raylib.DrawModelEx
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

			//ViewModelPos = Vector3.Lerp(ViewModelPos, DesiredViewModelPos, 0.1f);
			//VMRot = Quaternion.Slerp(VMRot, DesiredVMRot, 0.1f);

			ViewModelPos = DesiredViewModelPos;
			VMRot = DesiredVMRot;


			//CurFame.ViewModelPos = ViewModelPos = Vector3.Lerp(LastFrame.ViewModelPos, ViewModelPos, TimeAlpha);
			//CurFame.ViewModelRot = VMRot = Quaternion.Slerp(LastFrame.ViewModelRot, VMRot, TimeAlpha);

			float angle = 2.0f * MathF.Acos(VMRot.W) * 180f / MathF.PI;
			float s = MathF.Sqrt(1 - VMRot.W * VMRot.W);
			Vector3 axis = s < 0.001f ? new Vector3(1, 0, 0) : new Vector3(VMRot.X / s, VMRot.Y / s, VMRot.Z / s);

			Raylib.DrawModelEx(VModel, ViewModelPos, axis, angle, new Vector3(1, 1, 1), Color.White);
		}
	}
}
