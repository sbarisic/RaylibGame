using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine {
	public delegate float EasingFunc(float T);

	public abstract class AnimLerp {
		public float Duration;
		public float ElapsedTime;
		public float LerpVal;

		public bool Loop;

		public EasingFunc Easing;

		bool TriggeredOnComplete;
		public Action<AnimLerp> OnComplete;

		public AnimLerp() {
			Easing = Engine.Easing.Linear;
			Program.LerpMgr.AddLerp(this);
		}

		public virtual void StartLerp(float Duration, object StartVal, object EndVal) {
			TriggeredOnComplete = false;
		}

		public abstract void SwapStartAndEnd();

		public abstract object GetValue();

		public virtual void Update(float DeltaTime) {
			if (ElapsedTime < Duration) {
				ElapsedTime += DeltaTime;
				float T = ElapsedTime / Duration;

				if (T > 1) {
					T = 1;
				} else if (T < 0) {
					T = 0;
				}

				LerpVal = Easing(T);
			} else {
				if (!TriggeredOnComplete) {
					if (Loop) {
						ElapsedTime = 0;
						LerpVal = 0;
						TriggeredOnComplete = false;
						SwapStartAndEnd();
					} else {
						TriggeredOnComplete = true;
						OnComplete?.Invoke(this);
					}
				}
			}
		}
	}
}
