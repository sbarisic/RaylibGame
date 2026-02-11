using Voxelgine.Engine.DI;
using Voxelgine.Graphics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Owns the authoritative game simulation state: world, players, entities, day/night cycle, and physics data.
	/// Can run independently of any rendering/presentation layer (Raylib).
	/// On the client, <see cref="Voxelgine.States.MPClientGameState"/> creates and owns a GameSimulation.
	/// On a dedicated server, GameSimulation runs without any presentation layer.
	/// </summary>
	public class GameSimulation
	{
		/// <summary>The voxel world chunk manager.</summary>
		public ChunkMap Map { get; }

		/// <summary>Manages all players in the game.</summary>
		public PlayerManager Players { get; }

		/// <summary>Convenience property to get the local player instance.</summary>
		public Player LocalPlayer => Players.LocalPlayer;

		/// <summary>Manages all non-player entities.</summary>
		public EntityManager Entities { get; }

		/// <summary>Day/night cycle time and lighting.</summary>
		public DayNightCycle DayNight { get; }

		/// <summary>Physics constants (movement, gravity, friction, etc.).</summary>
		public PhysData PhysicsData { get; }

		public GameSimulation(IFishEngineRunner eng)
		{
			PhysicsData = new PhysData();
			DayNight = new DayNightCycle();
			Players = new PlayerManager();
			Entities = new EntityManager(eng);
			Map = new ChunkMap(eng);
		}
	}
}
