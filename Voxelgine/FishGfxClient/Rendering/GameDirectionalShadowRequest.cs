#if WINDOWS
using FishGfx;
using FishGfx.Graphics;
using FishGfx.Graphics.Shadows;
using System.Collections.Generic;
using System.Numerics;

namespace Voxelgine.FishGfxClient.Rendering;

public readonly record struct GameDirectionalShadowRequest(
	Camera ViewCamera,
	Vector3 LightDirection,
	float Strength,
	DirectionalShadowOptions Options,
	long GeometryRevision,
	IReadOnlyList<AxisAlignedBoundingBox> StaticInvalidations,
	bool DynamicActorsChanged,
	bool GpuProfilingEnabled = false);
#endif
