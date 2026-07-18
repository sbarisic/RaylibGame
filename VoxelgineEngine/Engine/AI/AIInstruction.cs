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

		/// <summary>
		/// Event handler marker. Param = (float)(int)<see cref="AIEvent"/>.
		/// Skipped during normal sequential execution. When the matching event fires,
		/// the VM interrupts the current step and jumps here, then advances to the next instruction.
		/// The handler uses Goto to resume or branch.
		/// </summary>
		EventHandler,

		// ── Extended instructions ──

		/// <summary>Target nearest NPC entity within Param radius. Param2 selects type (0=any, 1=enemy, 2=friendly). Success if found.</summary>
		TargetEntity,

		/// <summary>Navigate toward the current target entity. Param2=stop distance. Success when in range, failure if no target or no path.</summary>
		MoveToTarget,

		/// <summary>Instantly face the current target entity. Failure if no target.</summary>
		LookAtTarget,

		/// <summary>Face and aim at the current target entity. Failure if no target.</summary>
		AimAtTarget,

		/// <summary>Melee attack on the current target. Param=damage, Param2=range. Failure if no target or out of range.</summary>
		PrimaryAttack,

		/// <summary>Secondary attack on the current target. Param=damage, Param2=range (defaults longer). Failure if no target or out of range.</summary>
		SecondaryAttack,

		/// <summary>Navigate to a position with block cover from the current target or nearest player. Param=search radius. Failure if no cover.</summary>
		MoveToCover,

		/// <summary>Set the NPC to crouching state. Always succeeds.</summary>
		Crouch,

		/// <summary>Set the NPC to standing state. Always succeeds.</summary>
		StandUp,

		/// <summary>Set movement speed mode. TextParam = "walk", "run", or "sprint". Always succeeds.</summary>
		SetMoveMode,

		/// <summary>Play an animation overlay for Param seconds. TextParam = animation name. Always succeeds.</summary>
		PlayAnimation,

		/// <summary>Set armed state on the NPC. Always succeeds.</summary>
		EquipWeapon,

		/// <summary>Clear armed state on the NPC. Always succeeds.</summary>
		UnequipWeapon,

		/// <summary>Check if recent player chat contains TextParam (case-insensitive). Success if found, failure otherwise.</summary>
		ChatMessageContains,
	}
}
