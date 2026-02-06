using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Voxelgine.Engine.DI;

namespace Voxelgine.Engine
{
	public abstract class GameStateImpl
	{
		public IGameWindow Window;
		protected IFishEngineRunner Eng;

		public GameStateImpl(IGameWindow window, IFishEngineRunner Eng)
		{
			Window = window;
			this.Eng = Eng;
		}

		public virtual void SwapTo()
		{
		}

		public virtual void OnResize(GameWindow Window)
		{
		}

		public virtual void Tick(float GameTime)
		{
			// Once per frame
		}

		public virtual void UpdateLockstep(float TotalTime, float Dt, InputMgr InMgr)
		{
			// Multiple times per frame, fixed delta
		}

		public virtual void Draw(float TimeAlpha, ref GameFrameInfo LastFrame, ref GameFrameInfo FInfo)
		{
		}

		public virtual void Draw2D()
		{
		}
	}
}
