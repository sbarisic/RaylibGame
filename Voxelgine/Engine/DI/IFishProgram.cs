using Voxelgine.States;

namespace Voxelgine.Engine.DI;

public interface IClientEngineRunner : IFishEngineRunner
{
	MainMenuStateFishUI MainMenuState { get; set; }

	NPCPreviewState NPCPreviewState { get; set; }

	EffectsPreviewState EffectsPreviewState { get; set; }

	MPClientGameState MultiplayerGameState { get; set; }
}

public static class ClientEngineRunnerExtensions
{
	public static IClientEngineRunner AsClient(this IFishEngineRunner engine)
	{
		return (IClientEngineRunner)engine;
	}
}
