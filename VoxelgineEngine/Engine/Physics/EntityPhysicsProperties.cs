namespace Voxelgine.Engine;

/// <summary>
/// Declares how an entity participates in authoritative physics.
/// </summary>
public readonly record struct EntityPhysicsProperties(
	bool SimulateMotion,
	bool AffectedByGravity,
	bool CollidesWithVoxels,
	bool BlocksPlayers,
	bool BlocksEntities,
	bool GeneratesTouchEvents)
{
	public static EntityPhysicsProperties DynamicTrigger => new(
		SimulateMotion: true,
		AffectedByGravity: true,
		CollidesWithVoxels: true,
		BlocksPlayers: false,
		BlocksEntities: false,
		GeneratesTouchEvents: true
	);
}
