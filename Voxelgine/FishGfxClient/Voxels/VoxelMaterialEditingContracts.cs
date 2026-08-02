#if WINDOWS
using FishGfx.Voxels;
using Voxelgine.Graphics;

namespace Voxelgine.FishGfxClient.Voxels;

internal readonly record struct VoxelMaterialPaintGeometry(
	BlockValue Value,
	ushort MaterialId,
	VoxelMaterial Material,
	VoxelModel Model);

internal sealed class OwnedVoxelSurfaceTextureSet : IDisposable
{
	private VoxelSurfaceTextureSet textures;

	internal OwnedVoxelSurfaceTextureSet(VoxelSurfaceTextureSet textures)
	{
		this.textures = textures ?? throw new ArgumentNullException(nameof(textures));
	}

	internal VoxelSurfaceTextureSet Textures => textures
		?? throw new ObjectDisposedException(nameof(OwnedVoxelSurfaceTextureSet));

	public void Dispose()
	{
		VoxelSurfaceTextureSet current = Interlocked.Exchange(ref textures, null);
		if (current == null)
			return;
		current.PackedSurface.Dispose();
		current.CubeBaseColor.Dispose();
		current.ModelAtlas.Dispose();
	}
}
#endif
