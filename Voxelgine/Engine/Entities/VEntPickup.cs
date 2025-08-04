using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Raylib_cs;

using RaylibGame.States;

using Voxelgine.Graphics;

namespace Voxelgine.Engine {
	public class VEntPickup : VoxEntity {
		// Up down movement
		public bool IsBobbing = false;
		public float BobAmplitude = 0.15f;
		public float BobSpeed = 2;
		LerpVec3 BobbingLerp;

		public VEntPickup() : base() {
			IsRotating = true;

			BobbingLerp = new LerpVec3();
			BobbingLerp.Loop = true;
			BobbingLerp.Easing = Easing.EaseInOutQuart;
			BobbingLerp.StartLerp(1, new Vector3(0, -BobAmplitude, 0), new Vector3(0, BobAmplitude, 0));
			IsBobbing = true;
		}

		public override void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr) {
			base.UpdateLockstep(TotalTime, Dt, InMgr);

			if (IsBobbing) {
				ModelOffset = new Vector3(0, BobbingLerp.GetVec3().Y, 0);
			}
		}
	}
}
