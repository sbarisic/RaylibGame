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

	public static class Easing {
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

		public static float EaseInBounce(float x) {
			return 1 - EaseOutBounce(1 - x);

		}

		public static float EaseOutBounce(float x) {
			const float n1 = 7.5625f;
			const float d1 = 2.75f;

			if (x < 1 / d1) {
				return n1 * x * x;
			} else if (x < 2 / d1) {
				return n1 * (x -= 1.5f / d1) * x + 0.75f;
			} else if (x < 2.5 / d1) {
				return n1 * (x -= 2.25f / d1) * x + 0.9375f;
			} else {
				return n1 * (x -= 2.625f / d1) * x + 0.984375f;
			}
		}

		public static float EaseInOutBounce(float x) {
			return x < 0.5
			  ? (1 - EaseOutBounce(1 - 2 * x)) / 2
			  : (1 + EaseOutBounce(2 * x - 1)) / 2;
		}
	}
}
