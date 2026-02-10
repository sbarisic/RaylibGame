namespace Voxelgine.Engine.AI
{
	/// <summary>
	/// Factory for common AI programs.
	/// </summary>
	public static class AIPrograms
	{
		/// <summary>
		/// Default NPC behavior: approach nearby players, otherwise wander randomly.
		/// <code>
		/// 0: IS_PLAYER_NEARBY(15)  → fail: goto 4
		/// 1: MOVE_TO_PLAYER(15)    → fail: goto 4
		/// 2: LOOK_AT_PLAYER(15)
		/// 3: GOTO(0)
		/// 4: IDLE(5)
		/// 5: MOVE_RANDOM(10)       → fail: goto 4
		/// 6: GOTO(0)
		/// </code>
		/// </summary>
		public static AIStep[] DefaultWander() =>
		[
			new(AIInstruction.IsPlayerNearby, 15f, onFailGoto: 4),
			new(AIInstruction.MoveToPlayer, 15f, onFailGoto: 4),
			new(AIInstruction.LookAtPlayer, 15f),
			new(AIInstruction.Goto, 0),
			new(AIInstruction.Idle, 5f),
			new(AIInstruction.MoveRandom, 10f, onFailGoto: 4),
			new(AIInstruction.Goto, 0),
		];

		/// <summary>
		/// Passive NPC: idle, wander, repeat. Never approaches players.
		/// </summary>
		public static AIStep[] PassiveWander() =>
		[
			new(AIInstruction.Idle, 8f),
			new(AIInstruction.MoveRandom, 8f, onFailGoto: 0),
			new(AIInstruction.Goto, 0),
		];

		/// <summary>
		/// Sentinel NPC: stands still, watches nearby players.
		/// </summary>
		public static AIStep[] Sentinel() =>
		[
			new(AIInstruction.LookAtPlayer, 20f),
			new(AIInstruction.Idle, 1f),
			new(AIInstruction.Goto, 0),
		];

		/*public static AIStep[] FunkyBehavior() => [
			new (AIInstruction.MoveRandom, 10f)
		];*/

		public static AIStep[] FunkyBehavior() => Sentinel();
	}
}
