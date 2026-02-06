using Voxelgine.Engine;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Voxelgine.Engine.DI;

namespace Voxelgine.States
{
	class OptionsState : GameStateImpl
	{
		public OptionsState(GameWindow window, IFishEngineRunner Eng) : base(window, Eng)
		{
		}
	}
}
