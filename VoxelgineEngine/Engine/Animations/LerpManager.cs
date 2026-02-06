using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Voxelgine.Engine
{
	public interface ILerpManager
	{
		public void AddLerp(AnimLerp Lerp);
		public void Update(float Dt);
	}

	public class LerpManager : ILerpManager
	{
		List<AnimLerp> LerpList = new List<AnimLerp>();

		public void AddLerp(AnimLerp Lerp)
		{
			LerpList.Add(Lerp);
		}

		public void Update(float Dt)
		{
			foreach (var L in LerpList)
			{
				L.Update(Dt);
			}
		}
	}

	public static class Easing
	{
		// Linear
		public static float Linear(float T)
		{
			return T;
		}

		// Quadratic
		public static float EaseInQuad(float T)
		{
			return T * T;
		}

		public static float EaseOutQuad(float T)
		{
			return 1 - (1 - T) * (1 - T);
		}

		public static float EaseInOutQuad(float T)
		{
			return T < 0.5f ? 2 * T * T : 1 - MathF.Pow(-2 * T + 2, 2) / 2;
		}

		// Cubic
		public static float EaseInCubic(float T)
		{
			return T * T * T;
		}

		public static float EaseOutCubic(float T)
		{
			return 1 - MathF.Pow(1 - T, 3);
		}

		public static float EaseInOutCubic(float T)
		{
			return T < 0.5f ? 4 * T * T * T : 1 - MathF.Pow(-2 * T + 2, 3) / 2;
		}

		// Quartic
		public static float EaseInQuart(float T)
		{
			return T * T * T * T;
		}

		public static float EaseOutQuart(float T)
		{
			return 1 - MathF.Pow(1 - T, 4);
		}

		public static float EaseInOutQuart(float T)
		{
			return T < 0.5f ? 8 * T * T * T * T : 1 - MathF.Pow(-2 * T + 2, 4) / 2;
		}

		// Quintic
		public static float EaseInQuint(float T)
		{
			return T * T * T * T * T;
		}

		public static float EaseOutQuint(float T)
		{
			return 1 - MathF.Pow(1 - T, 5);
		}

		public static float EaseInOutQuint(float T)
		{
			return T < 0.5f ? 16 * T * T * T * T * T : 1 - MathF.Pow(-2 * T + 2, 5) / 2;
		}

		// Sine
		public static float EaseInSine(float T)
		{
			return 1 - MathF.Cos((T * MathF.PI) / 2);
		}

		public static float EaseOutSine(float T)
		{
			return MathF.Sin((T * MathF.PI) / 2);
		}

		public static float EaseInOutSine(float T)
		{
			return -(MathF.Cos(MathF.PI * T) - 1) / 2;
		}

		// Exponential
		public static float EaseInExpo(float T)
		{
			return T == 0 ? 0 : MathF.Pow(2, 10 * T - 10);
		}

		public static float EaseOutExpo(float T)
		{
			return T == 1 ? 1 : 1 - MathF.Pow(2, -10 * T);
		}

		public static float EaseInOutExpo(float T)
		{
			return T == 0 ? 0 : T == 1 ? 1 : T < 0.5f
				? MathF.Pow(2, 20 * T - 10) / 2
				: (2 - MathF.Pow(2, -20 * T + 10)) / 2;
		}

		// Circular
		public static float EaseInCirc(float T)
		{
			return 1 - MathF.Sqrt(1 - MathF.Pow(T, 2));
		}

		public static float EaseOutCirc(float T)
		{
			return MathF.Sqrt(1 - MathF.Pow(T - 1, 2));
		}

		public static float EaseInOutCirc(float T)
		{
			return T < 0.5f
				? (1 - MathF.Sqrt(1 - MathF.Pow(2 * T, 2))) / 2
				: (MathF.Sqrt(1 - MathF.Pow(-2 * T + 2, 2)) + 1) / 2;
		}

		// Back (overshoots then returns)
		const float BackC1 = 1.70158f;
		const float BackC2 = BackC1 * 1.525f;
		const float BackC3 = BackC1 + 1;

		public static float EaseInBack(float T)
		{
			return BackC3 * T * T * T - BackC1 * T * T;
		}

		public static float EaseOutBack(float T)
		{
			return 1 + BackC3 * MathF.Pow(T - 1, 3) + BackC1 * MathF.Pow(T - 1, 2);
		}

		public static float EaseInOutBack(float T)
		{
			return T < 0.5f
				? (MathF.Pow(2 * T, 2) * ((BackC2 + 1) * 2 * T - BackC2)) / 2
				: (MathF.Pow(2 * T - 2, 2) * ((BackC2 + 1) * (T * 2 - 2) + BackC2) + 2) / 2;
		}

		// Elastic (spring-like overshoot)
		const float ElasticC4 = (2 * MathF.PI) / 3;
		const float ElasticC5 = (2 * MathF.PI) / 4.5f;

		public static float EaseInElastic(float T)
		{
			return T == 0 ? 0 : T == 1 ? 1
				: -MathF.Pow(2, 10 * T - 10) * MathF.Sin((T * 10 - 10.75f) * ElasticC4);
		}

		public static float EaseOutElastic(float T)
		{
			return T == 0 ? 0 : T == 1 ? 1
				: MathF.Pow(2, -10 * T) * MathF.Sin((T * 10 - 0.75f) * ElasticC4) + 1;
		}

		public static float EaseInOutElastic(float T)
		{
			return T == 0 ? 0 : T == 1 ? 1 : T < 0.5f
				? -(MathF.Pow(2, 20 * T - 10) * MathF.Sin((20 * T - 11.125f) * ElasticC5)) / 2
				: (MathF.Pow(2, -20 * T + 10) * MathF.Sin((20 * T - 11.125f) * ElasticC5)) / 2 + 1;
		}

		// Bounce
		public static float EaseInBounce(float T)
		{
			return 1 - EaseOutBounce(1 - T);
		}

		public static float EaseOutBounce(float T)
		{
			const float n1 = 7.5625f;
			const float d1 = 2.75f;

			if (T < 1 / d1)
			{
				return n1 * T * T;
			}
			else if (T < 2 / d1)
			{
				return n1 * (T -= 1.5f / d1) * T + 0.75f;
			}
			else if (T < 2.5f / d1)
			{
				return n1 * (T -= 2.25f / d1) * T + 0.9375f;
			}
			else
			{
				return n1 * (T -= 2.625f / d1) * T + 0.984375f;
			}
		}

		public static float EaseInOutBounce(float T)
		{
			return T < 0.5f
				? (1 - EaseOutBounce(1 - 2 * T)) / 2
				: (1 + EaseOutBounce(2 * T - 1)) / 2;
		}
	}
}
