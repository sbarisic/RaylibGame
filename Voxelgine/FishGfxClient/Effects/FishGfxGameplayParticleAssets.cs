#if WINDOWS
using FishGfx.Graphics;
using Voxelgine.FishGfxClient.Assets;

namespace Voxelgine.FishGfxClient.Effects;

/// <summary>
/// Process-lifetime particle texture handles. Register once in the game asset
/// store and share the handles with each world-scoped particle system.
/// </summary>
public readonly record struct FishGfxGameplayParticleAssets(
	AssetHandle<Texture> Smoke,
	AssetHandle<Texture> Fire,
	AssetHandle<Texture> Blood,
	AssetHandle<Texture> Spark)
{
	public static FishGfxGameplayParticleAssets Register(
		GameAssetStore assets,
		string idPrefix = "gameplay.particles")
	{
		ArgumentNullException.ThrowIfNull(assets);
		ArgumentException.ThrowIfNullOrWhiteSpace(idPrefix);

		return new FishGfxGameplayParticleAssets(
			assets.LoadColorTexture($"{idPrefix}.smoke", "data/textures/smoke/1.png"),
			assets.LoadColorTexture($"{idPrefix}.fire", "data/textures/fire/1.png"),
			assets.LoadColorTexture($"{idPrefix}.blood", "data/textures/blood/1.png"),
			assets.LoadColorTexture($"{idPrefix}.spark", "data/textures/spark/1.png"));
	}
}
#endif
