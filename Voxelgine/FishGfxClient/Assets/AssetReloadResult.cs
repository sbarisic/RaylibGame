#if WINDOWS
namespace Voxelgine.FishGfxClient.Assets;

public readonly record struct AssetReloadResult(
	string AssetId,
	bool Succeeded,
	string Message);
#endif
