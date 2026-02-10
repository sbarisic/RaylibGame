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
	}
}
