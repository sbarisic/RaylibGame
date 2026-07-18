using Voxelgine.Engine.Audio;
using Voxelgine.Engine.DI;
using Voxelgine.FishGfxClient;
using Voxelgine.FishGfxClient.Viewmodel;
using Voxelgine.GUI;

namespace Voxelgine.Engine;

/// <summary>Windows client presentation and input shell around the shared player simulation.</summary>
public unsafe partial class ClientPlayer : Player
{
	private readonly FishUIManager GUI;
	private readonly bool LocalPlayer;
	private FishGfxViewModelRenderer FishGfxViewModelRenderer;

	public ViewModel ViewMdl;

	public ClientPlayer(
		IFishEngineRunner engine,
		FishUIManager gui,
		string modelName,
		bool localPlayer,
		IGameAudioSink audioSink,
		int playerId = 0
	)
		: base(
			engine,
			playerId,
			audioSink,
			(engine.DI.GetRequiredService<IFishConfig>() as GameConfig)?.MouseSensitivity ?? 0.35f
		)
	{
		GUI = gui;
		LocalPlayer = localPlayer;
		_ = modelName;

		IGameWindow gameWindow = engine.DI.GetRequiredService<IGameWindow>();
		ViewMdl = new ViewModel(engine);
		if (gameWindow is IFishGfxGameWindow fishGfxWindow)
		{
			try
			{
				FishGfxViewModelRenderer = new FishGfxViewModelRenderer(
					fishGfxWindow,
					Logging.WriteLine
				);
			}
			catch (Exception exception)
			{
				Logging.WriteLine(
					$"ViewModel: FishGfx assets are unavailable; continuing without a viewmodel: {exception.Message}"
				);
			}
		}

		ToggleMouse(true);
	}
}
