using RaylibGame.States;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine.DI
{
	public interface IFishEngineRunner
	{
		public FishDI DI { get; set; }

		public int ChunkDrawCalls { get; set; }
		public bool DebugMode { get; set; }
		public float TotalTime { get; set; }

		public MainMenuStateFishUI MainMenuState { get; set; }

		public GameState GameState { get; set; }

		public NPCPreviewState NPCPreviewState { get; set; }

		public void Run();
	}
}
