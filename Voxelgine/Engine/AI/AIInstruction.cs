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

		/// <summary>Display a speech bubble with TextParam for Param seconds and immediately advance to the next instruction.</summary>
		AsyncSpeak,

		// TODO: Add instructions for MoveToEntity, LookAtEntity (entity from event handler), PlayAnimation (with overlay support, like crouching + walking + attacking/shooting)
		// TargetEntity (stores entity for future Target instructions)
		// PointAtTarget, MoveToTarget (with configurable stop distance), LookAtTarget, AimAtTarget, PrimaryAttack, SecondaryAttack
		// MoveToCover(dangerFromPosition) (find cover with no line of sight to the danger position, and move there), Crouch, StandUp, SetMoveMode(walk/walk+jump/run+jump)
		// Idle should play random available idle anim
		// ChatMessageContains(text, onFalse: relative jump, onTrue: relative jump (1 by default)) (for events where you get chat either from player or npc)
		// EquipWeapon, UnequipWeapon

		/// <summary>
		/// Event handler marker. Param = (float)(int)<see cref="AIEvent"/>.
		/// Skipped during normal sequential execution. When the matching event fires,
		/// the VM interrupts the current step and jumps here, then advances to the next instruction.
		/// The handler uses Goto to resume or branch.
		/// </summary>
		EventHandler,
	}
}
