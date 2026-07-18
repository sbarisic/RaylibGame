using System.Numerics;

namespace Voxelgine.Engine;

public readonly record struct FrameTiming(
	float TotalTime,
	float DeltaTime,
	float FixedDeltaTime,
	float InterpolationAlpha);

public enum CameraProjectionKind
{
	Perspective,
	Orthographic,
}

public readonly record struct GameCameraState(
	Vector3 Position,
	Vector3 Target,
	Vector3 Up,
	float FieldOfView,
	CameraProjectionKind Projection)
{
	public Vector3 Forward
	{
		get
		{
			Vector3 direction = Target - Position;
			return direction.LengthSquared() > 0f
				? Vector3.Normalize(direction)
				: Vector3.UnitZ;
		}
	}
}

public readonly record struct Rgba32(byte R, byte G, byte B, byte A = byte.MaxValue)
{
	public static Rgba32 White => new(byte.MaxValue, byte.MaxValue, byte.MaxValue);
	public static Rgba32 Black => new(0, 0, 0);
	public static Rgba32 Transparent => new(0, 0, 0, 0);
}
