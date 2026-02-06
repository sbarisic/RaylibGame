using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Voxelgine.Engine.DI;

namespace Voxelgine.Engine {
	public class LerpVec3 : AnimLerp {
		Vector3 Start;
		Vector3 End;

		public LerpVec3(IFishEngineRunner Eng) : base(Eng)
		{
		}

		public override void StartLerp(float Duration, object StartVal, object EndVal) {
			base.StartLerp(Duration, StartVal, EndVal);

			Start = (Vector3)StartVal;
			End = (Vector3)EndVal;
		}

		public virtual Vector3 GetVec3() {
			return Vector3.Lerp(Start, End, LerpVal);
		}

		public override object GetValue() {
			return GetVec3();
		}

		public override void SwapStartAndEnd() {
			Vector3 Tmp = End;
			End = Start;
			Start = Tmp;
		}
	}

	public class LerpQuat : AnimLerp {
		Quaternion Start;
		Quaternion End;

		public LerpQuat(IFishEngineRunner Eng) : base(Eng)
		{
		}

		public override void StartLerp(float Duration, object StartVal, object EndVal) {
			base.StartLerp(Duration, StartVal, EndVal);

			Start = (Quaternion)StartVal;
			End = (Quaternion)EndVal;
		}

		public virtual Quaternion GetQuat() {
			return Quaternion.Slerp(Start, End, LerpVal);
		}

		public override object GetValue() {
			return GetQuat();
		}

		public override void SwapStartAndEnd() {
			Quaternion Tmp = End;
			End = Start;
			Start = Tmp;
		}
	}

	public class LerpFloat : AnimLerp {
		float Start;
		float End;

		public LerpFloat(IFishEngineRunner Eng) : base(Eng)
		{

		}

		public override void StartLerp(float Duration, object StartVal, object EndVal) {
			base.StartLerp(Duration, StartVal, EndVal);

			Start = (float)StartVal;
			End = (float)EndVal;
		}

		public virtual float GetFloat() {
			return Start + (End - Start) * LerpVal;
		}

		public override object GetValue() {
			return GetFloat();
		}

		public override void SwapStartAndEnd() {
			float Tmp = End;
			End = Start;
			Start = Tmp;
		}
	}
}
