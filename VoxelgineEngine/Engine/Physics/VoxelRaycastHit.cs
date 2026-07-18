using System.Numerics;

namespace Voxelgine.Engine;

/// <summary>
/// Describes a ray hit against a solid voxel.
/// </summary>
public readonly record struct VoxelRaycastHit(
	int X,
	int Y,
	int Z,
	Vector3 Point,
	Vector3 Normal,
	float Distance)
{
	public Vector3 BlockPosition => new(X, Y, Z);
}
