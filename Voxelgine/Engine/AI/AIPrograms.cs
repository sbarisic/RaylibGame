namespace Voxelgine.Engine.AI
{
	/// <summary>
	/// Factory for common AI programs.
	/// </summary>
	public static class AIPrograms
	{
		/// <summary>
		/// Default NPC behavior: approach nearby players, otherwise wander randomly.
		/// Reacts to touch and attacks by looking at the player.
		/// <code>
		///  0: IS_PLAYER_NEARBY(15)  → fail: goto 4
		///  1: MOVE_TO_PLAYER(15)    → fail: goto 4
		///  2: LOOK_AT_PLAYER(15)
		///  3: GOTO(0)
		///  4: IDLE(5)
		///  5: MOVE_RANDOM(10)       → fail: goto 4
		///  6: GOTO(0)
		///  7: EVENT_HANDLER(OnPlayerTouch)
		///  8: LOOK_AT_PLAYER(15)
		///  9: IDLE(2)
		/// 10: GOTO(0)
		/// 11: EVENT_HANDLER(OnAttacked)
		/// 12: LOOK_AT_PLAYER(15)
		/// 13: GOTO(0)
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
			// OnPlayerTouch handler: look at player, pause, resume
			AIStep.Handler(AIEvent.OnPlayerTouch),
			new(AIInstruction.LookAtPlayer, 15f),
			new(AIInstruction.Idle, 2f),
			new(AIInstruction.Goto, 0),
			// OnAttacked handler: look at attacker direction, resume
			AIStep.Handler(AIEvent.OnAttacked),
			new(AIInstruction.LookAtPlayer, 15f),
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

		/// <summary>
		/// Funky NPC behavior: idle, wander, look at player, approach, greet.
		/// Reacts to damage by fleeing, and to touch by backing away.
		/// <code>
		///  0: WAIT(2)
		///  1: MOVE_RANDOM(5)                  → fail: goto 0
		///  2: LOOK_AT_PLAYER(15)              → fail: goto 0
		///  3: WAIT(3)
		///  4: MOVE_TO_PLAYER(15, stop=3)      → fail: goto 0
		///  5: ASYNC_SPEAK("Hello my dude!", 3)
		///  6: GOTO(0)
		///  7: EVENT_HANDLER(OnAttacked)
		///  8: ASYNC_SPEAK("Ouwie!", 2)
		///  9: MOVE_RANDOM(3)                  → fail: goto 0
		/// 10: GOTO(0)
		/// 11: EVENT_HANDLER(OnPlayerTouch)
		/// 12: ASYNC_SPEAK("Stop touching me, my dude", 3)
		/// 13: MOVE_RANDOM(1)                  → fail: goto 0
		/// 14: GOTO(0)
		/// </code>
		/// </summary>
		public static AIStep[] FunkyBehavior() =>
		[
			new(AIInstruction.Wait, 2f),
			new(AIInstruction.MoveRandom, 5f, onFailGoto: 0),
			new(AIInstruction.LookAtPlayer, 15f, onFailGoto: 0),
			new(AIInstruction.Wait, 3f),
			AIStep.MoveToPlayerAt(15f, 3f, onFailGoto: 0),
			AIStep.AsyncSpeakText("Hello my dude!", 3f),
			new(AIInstruction.Goto, 0),
			// OnAttacked handler: say "Ouwie!" and run away
			AIStep.Handler(AIEvent.OnAttacked),
			AIStep.AsyncSpeakText("Ouwie!", 2f),
			new(AIInstruction.MoveRandom, 3f, onFailGoto: 0),
			new(AIInstruction.Goto, 0),
			// OnPlayerTouch handler: say "Stop touching me" and walk away
			AIStep.Handler(AIEvent.OnPlayerTouch),
			AIStep.AsyncSpeakText("Stop touching me, my dude", 3f),
			new(AIInstruction.MoveRandom, 1f, onFailGoto: 0),
			new(AIInstruction.Goto, 0),
		];
	}
}
