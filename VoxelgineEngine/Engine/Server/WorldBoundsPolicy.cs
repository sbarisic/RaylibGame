using System.Numerics;

namespace Voxelgine.Engine.Server;

internal static class WorldBoundsPolicy
{
	/// <summary>Y position below which a world object is considered lost to the void.</summary>
	internal const float VoidThreshold = -50f;

	internal static bool IsBelowVoid(Vector3 position)
	{
		return position.Y < VoidThreshold;
	}

	/// <summary>
	/// Marks fallen NPCs dead and returns a stable snapshot for authoritative removal.
	/// </summary>
	internal static VEntNPC[] KillFallenNpcs(IEnumerable<VoxEntity> entities)
	{
		ArgumentNullException.ThrowIfNull(entities);

		VEntNPC[] fallen = entities
			.OfType<VEntNPC>()
			.Where(static npc => IsBelowVoid(npc.Position))
			.ToArray();
		foreach (VEntNPC npc in fallen)
		{
			npc.TakeDamage(Math.Max(npc.Health, npc.MaxHealth));
		}

		return fallen;
	}
}
