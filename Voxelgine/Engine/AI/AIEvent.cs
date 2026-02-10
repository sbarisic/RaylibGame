namespace Voxelgine.Engine.AI
{
	/// <summary>
	/// Events that can interrupt the AI program and jump to a registered handler.
	/// Register handlers using <see cref="AIInstruction.EventHandler"/> steps in the program.
	/// </summary>
	public enum AIEvent : byte
	{
		/// <summary>A player entered the NPC's collision box.</summary>
		OnPlayerTouch,

		/// <summary>A player entered the NPC's sight radius (checked each tick).</summary>
		OnPlayerSight,

		/// <summary>The NPC was hit by a weapon.</summary>
		OnAttacked,

		// TODO: Add events for entity in range, other npc in range (OnEnemyInRange, OnFriendlyInRange, OnNeutralInRange, OnPlayerInRange), low health, on stuck
		// OnPlayerChat (when player types something within 2 blocks of NPC), OnNPCChat (same but NPC), OnAllyAttacked (when an allied NPC is attacked), OnEnemyKilled (when an enemy NPC or player is killed), OnLowHealth (when health drops below a threshold), OnStuck (when pathfinding fails repeatedly)
	}
}
