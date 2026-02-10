namespace Voxelgine.Engine.AI
{
	/// <summary>
	/// Instructions available to the NPC behavior tree VM.
	/// Each instruction runs to completion (Success/Failure) before the VM advances.
	/// </summary>
	public enum AIInstruction : byte
	{
		/// <summary>Wait a random duration (Param .. Param*2 seconds). Always succeeds.</summary>
		Idle,

		/// <summary>Navigate to a random walkable point within Param radius. Success when arrived, failure if no path or stuck.</summary>
		MoveRandom,

		/// <summary>Navigate toward the nearest player within Param radius. Success when within 2 blocks, failure if no player or no path.</summary>
		MoveToPlayer,

		/// <summary>Instant check: is any player within Param radius? Success/failure.</summary>
		IsPlayerNearby,

		/// <summary>Instantly face the nearest player within Param radius. Success if found, failure otherwise.</summary>
		LookAtPlayer,

		/// <summary>Unconditional jump to step index (int)Param.</summary>
		Goto,

		/// <summary>Wait exactly Param seconds. Always succeeds. Unlike Idle, no randomness.</summary>
		Wait,

		/// <summary>Display a speech bubble with TextParam for Param seconds. Always succeeds after the duration.</summary>
		Speak,

		/// <summary>
		/// Event handler marker. Param = (float)(int)<see cref="AIEvent"/>.
		/// Skipped during normal sequential execution. When the matching event fires,
		/// the VM interrupts the current step and jumps here, then advances to the next instruction.
		/// The handler uses Goto to resume or branch.
		/// </summary>
		EventHandler,
	}
}
