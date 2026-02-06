using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Voxelgine.Engine.DI;

namespace Voxelgine.Engine
{
	public delegate float EasingFunc(float T);

	public abstract class AnimLerp
	{
		public float Duration;
		public float ElapsedTime;
		public float LerpVal;

		public bool Loop;
		public bool Completed;

		public EasingFunc Easing;

		bool TriggeredOnComplete;
		public Action<AnimLerp> OnComplete;

		public AnimLerp(IFishEngineRunner Eng)
		{
			Easing = Engine.Easing.Linear;
			Eng.DI.GetRequiredService<ILerpManager>().AddLerp(this);
		}

		public virtual void StartLerp(float Duration, object StartVal, object EndVal)
		{
			TriggeredOnComplete = false;
			Completed = false;
			ElapsedTime = 0;
			this.Duration = Duration;
		}

		public virtual void ContinueNew(float Duration, object EndVal)
		{
			if (!Completed)
				return;

			this.Duration = Duration;
			StartLerp(Duration, GetValue(), EndVal);
		}

		public abstract void SwapStartAndEnd();

		public abstract object GetValue();

		public virtual void Update(float DeltaTime)
		{
			if (ElapsedTime < Duration)
			{
				ElapsedTime += DeltaTime;
				float T = ElapsedTime / Duration;

				if (T > 1)
				{
					T = 1;
				}
				else if (T < 0)
				{
					T = 0;
				}

				LerpVal = Easing(T);
			}
			else
			{
				LerpVal = Easing(1);

				if (!TriggeredOnComplete)
				{
					if (Loop)
					{
						ElapsedTime = 0;
						LerpVal = 0;
						TriggeredOnComplete = false;
						SwapStartAndEnd();
					}
					else
					{
						TriggeredOnComplete = true;
						Completed = true;
						OnComplete?.Invoke(this);
					}
				}
			}
		}
	}
}
