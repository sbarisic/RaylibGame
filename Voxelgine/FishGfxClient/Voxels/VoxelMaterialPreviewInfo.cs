#if WINDOWS
using FishGfx.Voxels;
using Voxelgine.Engine;

namespace Voxelgine.FishGfxClient.Voxels;

public readonly record struct VoxelMaterialPreviewInfo(
	BlockType BlockType,
	string Name,
	VoxelRenderMode RenderMode,
	bool IsCustomModel,
	bool SurfaceMapsEnabled,
	VoxelFaceTiles AtlasTiles);
#endif
