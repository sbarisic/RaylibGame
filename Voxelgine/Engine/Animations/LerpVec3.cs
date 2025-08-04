using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine {
	public class LerpVec3 : AnimLerp {
		Vector3 Start;
		Vector3 End;

		public override void StartLerp(float Duration, object StartVal, object EndVal) {
			base.StartLerp(Duration, StartVal, EndVal);

			Start = (Vector3)StartVal;
			End = (Vector3)EndVal;
			this.Duration = Duration;
			ElapsedTime = 0;
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
}
