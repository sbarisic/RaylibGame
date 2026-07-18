using FishGfx.Graphics;
using Voxelgine.Engine;
using Voxelgine.FishGfxClient.Assets;

namespace Voxelgine.FishGfxClient;

public interface IFishGfxGameWindow : IGameWindow
{
	RenderWindow RenderWindow { get; }

	GameAssetStore Assets { get; }

	IReadOnlyList<MonitorInfo> Monitors { get; }

	void ApplyConfiguration();
}
