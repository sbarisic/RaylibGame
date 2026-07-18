using System.Numerics;

namespace Voxelgine.Engine;

/// <summary>
/// Complete deterministic player movement state for snapshots and prediction replay.
/// </summary>
public readonly record struct PlayerPhysicsState(
	Vector3 Position,
	Vector3 Velocity,
	float GroundGraceRemaining,
	float JumpCooldownRemaining,
	float RecentJumpRemaining,
	float HeadBumpCooldownRemaining,
	Vector3 LastWallNormal,
	bool WasGrounded,
	bool WasInWater,
	bool NoClip = false);
