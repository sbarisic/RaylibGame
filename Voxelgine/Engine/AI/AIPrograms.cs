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
		/// Funky NPC behavior: wander, greet players, react to chat, flee from damage,
		/// take cover, defend allies, and exercise all available instructions and events.
		/// <code>
		///  0: SET_MOVE_MODE("run")
		///  1: WAIT(2)
		///  2: IS_PLAYER_NEARBY(15)              → fail: goto 7
		///  3: MOVE_TO_PLAYER(15, stop=3)        → fail: goto 7
		///  4: LOOK_AT_PLAYER(15)
		///  5: ASYNC_SPEAK("Hello my dude!", 3)
		///  6: GOTO(0)
		///  7: TARGET_ENTITY(15, any)            → fail: goto 11
		///  8: MOVE_TO_TARGET(stop=3)            → fail: goto 11
		///  9: LOOK_AT_TARGET                    → fail: goto 11
		/// 10: ASYNC_SPEAK("Hey there, NPC!", 2)
		/// 11: CHAT_CONTAINS("hello")            → fail: goto 13
		/// 12: ASYNC_SPEAK("Someone said hello!", 3)
		/// 13: IDLE(3)
		/// 14: MOVE_RANDOM(8)                    → fail: goto 13
		/// 15: GOTO(0)
		/// -- Event handlers --
		/// 16: EVENT_HANDLER(OnAttacked)
		/// 17: ASYNC_SPEAK("Ouwie!", 2)
		/// 18: CROUCH
		/// 19: WAIT(1)
		/// 20: STAND_UP
		/// 21: SET_MOVE_MODE("sprint")
		/// 22: MOVE_TO_COVER(8)                  → fail: goto 24
		/// 23: WAIT(2)
		/// 24: SET_MOVE_MODE("run")
		/// 25: GOTO(0)
		/// 26: EVENT_HANDLER(OnPlayerTouch)
		/// 27: ASYNC_SPEAK("Stop touching me, my dude", 3)
		/// 28: MOVE_RANDOM(1)                    → fail: goto 0
		/// 29: GOTO(0)
		/// 30: EVENT_HANDLER(OnPlayerChat)
		/// 31: CHAT_CONTAINS("hi")               → fail: goto 34
		/// 32: ASYNC_SPEAK("Hi there!", 2)
		/// 33: LOOK_AT_PLAYER(15)
		/// 34: GOTO(0)
		/// 35: EVENT_HANDLER(OnStuck)
		/// 36: ASYNC_SPEAK("I'm stuck!", 2)
		/// 37: GOTO(0)
		/// 38: EVENT_HANDLER(OnPlayerInRange)
		/// 39: LOOK_AT_PLAYER(15)
		/// 40: EQUIP_WEAPON
		/// 41: PLAY_ANIMATION("idle", 2)
		/// 42: WAIT(2)
		/// 43: UNEQUIP_WEAPON
		/// 44: GOTO(0)
		/// 45: EVENT_HANDLER(OnLowHealth)
		/// 46: ASYNC_SPEAK("I need healing!", 3)
		/// 47: SET_MOVE_MODE("sprint")
		/// 48: MOVE_TO_COVER(10)                 → fail: goto 51
		/// 49: CROUCH
		/// 50: WAIT(3)
		/// 51: STAND_UP
		/// 52: SET_MOVE_MODE("run")
		/// 53: GOTO(0)
		/// 54: EVENT_HANDLER(OnNPCChat)
		/// 55: ASYNC_SPEAK("I heard you!", 2)
		/// 56: GOTO(0)
		/// 57: EVENT_HANDLER(OnAllyAttacked)
		/// 58: ASYNC_SPEAK("My friend is hurt!", 2)
		/// 59: TARGET_ENTITY(15, enemy)          → fail: goto 64
		/// 60: MOVE_TO_TARGET(stop=3)            → fail: goto 64
		/// 61: AIM_AT_TARGET                     → fail: goto 64
		/// 62: PRIMARY_ATTACK(15, range=3)       → fail: goto 64
		/// 63: SECONDARY_ATTACK(10, range=5)     → fail: goto 64
		/// 64: GOTO(0)
		/// 65: EVENT_HANDLER(OnEnemyInRange)
		/// 66: EQUIP_WEAPON
		/// 67: TARGET_ENTITY(15, enemy)          → fail: goto 69
		/// 68: LOOK_AT_TARGET                    → fail: goto 69
		/// 69: GOTO(0)
		/// 70: EVENT_HANDLER(OnFriendlyInRange)
		/// 71: ASYNC_SPEAK("Hey friend!", 2)
		/// 72: GOTO(0)
		/// 73: EVENT_HANDLER(OnEnemyKilled)
		/// 74: UNEQUIP_WEAPON
		/// 75: ASYNC_SPEAK("Victory!", 2)
		/// 76: GOTO(0)
		/// </code>
		/// </summary>
		public static AIStep[] FunkyBehavior() =>
		[
			// Main loop: set speed, check for players, greet them
			AIStep.SetMode("run"),                                              //  0
			new(AIInstruction.Wait, 2f),                                        //  1
			new(AIInstruction.IsPlayerNearby, 15f, onFailGoto: 7),              //  2
			AIStep.MoveToPlayerAt(15f, 3f, onFailGoto: 7),                      //  3
			new(AIInstruction.LookAtPlayer, 15f),                               //  4
			AIStep.AsyncSpeakText("Hello my dude!", 3f),                        //  5
			new(AIInstruction.Goto, 0),                                         //  6
			// Check for nearby NPC entities
			new(AIInstruction.TargetEntity, 15f, onFailGoto: 11),               //  7
			AIStep.MoveToTargetAt(3f, onFailGoto: 11),                          //  8
			new(AIInstruction.LookAtTarget, 0, onFailGoto: 11),                 //  9
			AIStep.AsyncSpeakText("Hey there, NPC!", 2f),                       // 10
			// Check if anyone said "hello" in chat
			AIStep.ChatContains("hello", onFailGoto: 13),                       // 11
			AIStep.AsyncSpeakText("Someone said hello!", 3f),                   // 12
			// Otherwise just idle and wander
			new(AIInstruction.Idle, 3f),                                        // 13
			new(AIInstruction.MoveRandom, 8f, onFailGoto: 13),                  // 14
			new(AIInstruction.Goto, 0),                                         // 15

			// ── Event handlers ──

			// OnAttacked: cry out, crouch, flee to cover
			AIStep.Handler(AIEvent.OnAttacked),                                 // 16
			AIStep.AsyncSpeakText("Ouwie!", 2f),                                // 17
			new(AIInstruction.Crouch),                                          // 18
			new(AIInstruction.Wait, 1f),                                        // 19
			new(AIInstruction.StandUp),                                         // 20
			AIStep.SetMode("sprint"),                                           // 21
			new(AIInstruction.MoveToCover, 8f, onFailGoto: 24),                 // 22
			new(AIInstruction.Wait, 2f),                                        // 23
			AIStep.SetMode("run"),                                              // 24
			new(AIInstruction.Goto, 0),                                         // 25

			// OnPlayerTouch: complain and walk away
			AIStep.Handler(AIEvent.OnPlayerTouch),                              // 26
			AIStep.AsyncSpeakText("Stop touching me, my dude", 3f),             // 27
			new(AIInstruction.MoveRandom, 1f, onFailGoto: 0),                   // 28
			new(AIInstruction.Goto, 0),                                         // 29

			// OnPlayerChat: respond if chat contains "hi"
			AIStep.Handler(AIEvent.OnPlayerChat),                               // 30
			AIStep.ChatContains("hi", onFailGoto: 34),                          // 31
			AIStep.AsyncSpeakText("Hi there!", 2f),                             // 32
			new(AIInstruction.LookAtPlayer, 15f),                               // 33
			new(AIInstruction.Goto, 0),                                         // 34

			// OnStuck: complain
			AIStep.Handler(AIEvent.OnStuck),                                    // 35
			AIStep.AsyncSpeakText("I'm stuck!", 2f),                            // 36
			new(AIInstruction.Goto, 0),                                         // 37

			// OnPlayerInRange: look, equip weapon briefly, then unequip
			AIStep.Handler(AIEvent.OnPlayerInRange),                            // 38
			new(AIInstruction.LookAtPlayer, 15f),                               // 39
			new(AIInstruction.EquipWeapon),                                     // 40
			AIStep.PlayAnim("idle", 2f),                                        // 41
			new(AIInstruction.Wait, 2f),                                        // 42
			new(AIInstruction.UnequipWeapon),                                   // 43
			new(AIInstruction.Goto, 0),                                         // 44

			// OnLowHealth: flee to cover and hide
			AIStep.Handler(AIEvent.OnLowHealth),                                // 45
			AIStep.AsyncSpeakText("I need healing!", 3f),                       // 46
			AIStep.SetMode("sprint"),                                           // 47
			new(AIInstruction.MoveToCover, 10f, onFailGoto: 51),                // 48
			new(AIInstruction.Crouch),                                          // 49
			new(AIInstruction.Wait, 3f),                                        // 50
			new(AIInstruction.StandUp),                                         // 51
			AIStep.SetMode("run"),                                              // 52
			new(AIInstruction.Goto, 0),                                         // 53

			// OnNPCChat: acknowledge
			AIStep.Handler(AIEvent.OnNPCChat),                                  // 54
			AIStep.AsyncSpeakText("I heard you!", 2f),                          // 55
			new(AIInstruction.Goto, 0),                                         // 56

			// OnAllyAttacked: rush to defend ally, attack the enemy
			AIStep.Handler(AIEvent.OnAllyAttacked),                             // 57
			AIStep.AsyncSpeakText("My friend is hurt!", 2f),                    // 58
			new(AIInstruction.TargetEntity, 15f, onFailGoto: 64) { Param2 = 1 },// 59 (enemy)
			AIStep.MoveToTargetAt(3f, onFailGoto: 64),                          // 60
			new(AIInstruction.AimAtTarget, 0, onFailGoto: 64),                  // 61
			AIStep.Attack(15f, 3f, onFailGoto: 64),                             // 62
			AIStep.SecondaryAttack(10f, 5f, onFailGoto: 64),                    // 63
			new(AIInstruction.Goto, 0),                                         // 64

			// OnEnemyInRange: equip and look at enemy
			AIStep.Handler(AIEvent.OnEnemyInRange),                             // 65
			new(AIInstruction.EquipWeapon),                                     // 66
			new(AIInstruction.TargetEntity, 15f, onFailGoto: 69) { Param2 = 1 },// 67 (enemy)
			new(AIInstruction.LookAtTarget, 0, onFailGoto: 69),                 // 68
			new(AIInstruction.Goto, 0),                                         // 69

			// OnFriendlyInRange: greet
			AIStep.Handler(AIEvent.OnFriendlyInRange),                          // 70
			AIStep.AsyncSpeakText("Hey friend!", 2f),                           // 71
			new(AIInstruction.Goto, 0),                                         // 72

			// OnEnemyKilled: unequip and celebrate
			AIStep.Handler(AIEvent.OnEnemyKilled),                              // 73
			new(AIInstruction.UnequipWeapon),                                   // 74
			AIStep.AsyncSpeakText("Victory!", 2f),                              // 75
			new(AIInstruction.Goto, 0),                                         // 76
		];
	}
}
