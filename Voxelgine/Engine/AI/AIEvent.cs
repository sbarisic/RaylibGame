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

		// ── Extended events ──

		/// <summary>A player sent a chat message.</summary>
		OnPlayerChat,

		/// <summary>A nearby NPC spoke.</summary>
		OnNPCChat,

		/// <summary>An ally (same non-zero team) was attacked.</summary>
		OnAllyAttacked,

		/// <summary>The current target entity was killed.</summary>
		OnEnemyKilled,

		/// <summary>NPC health dropped below 30%.</summary>
		OnLowHealth,

		/// <summary>NPC got stuck during navigation.</summary>
		OnStuck,

		/// <summary>A player entered proximity range (periodic check).</summary>
		OnPlayerInRange,

		/// <summary>An enemy entity entered proximity range.</summary>
		OnEnemyInRange,

		/// <summary>A friendly entity entered proximity range.</summary>
		OnFriendlyInRange,
	}
}
