using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Voxelgine.Engine {
	class LerpManager {
		List<AnimLerp> LerpList = new List<AnimLerp>();

		public void AddLerp(AnimLerp Lerp) {
			LerpList.Add(Lerp);
		}

		public void Update(float Dt) {
			foreach (var L in LerpList) {
				L.Update(Dt);
			}
		}
	}

	static class Easing {
		public static float Linear(float T) {
			return T;
		}

		public static float EaseInSine(float T) {
			return 1 - MathF.Cos((T * MathF.PI) / 2);
		}

		public static float EaseInOutCubic(float T) {
			return T < 0.5f ? 4 * T * T * T : 1 - MathF.Pow(-2 * T + 2, 3) / 2;
		}

		public static float EaseInOutQuint(float T) {
			return T < 0.5 ? 16 * T * T * T * T * T : 1 - MathF.Pow(-2 * T + 2, 5) / 2;
		}

		public static float EaseInOutQuart(float T) {
			return T < 0.5 ? 8 * T * T * T * T : 1 - MathF.Pow(-2 * T + 2, 4) / 2;
		}
	}
}
