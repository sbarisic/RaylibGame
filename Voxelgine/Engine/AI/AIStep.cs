namespace Voxelgine.Engine.AI
{
	/// <summary>
	/// A single step in an NPC AI program.
	/// The VM executes each step to completion, then advances to the next (or jumps on failure).
	/// </summary>
	public struct AIStep
	{
		/// <summary>The instruction to execute.</summary>
		public AIInstruction Instruction;

		/// <summary>Instruction-specific parameter (radius, duration, jump target, etc.).</summary>
		public float Param;

		/// <summary>Secondary parameter (stop distance for MoveToPlayer, etc.). 0 = use default.</summary>
		public float Param2;

		/// <summary>
		/// Step index to jump to when this instruction fails.
		/// -1 means fall through to the next step (default).
		/// </summary>
		public int OnFailGoto;

		/// <summary>Optional text parameter (used by Speak instruction).</summary>
		public string TextParam;

		public AIStep(AIInstruction instruction, float param = 0, int onFailGoto = -1)
		{
			Instruction = instruction;
			Param = param;
			Param2 = 0;
			OnFailGoto = onFailGoto;
			TextParam = null;
		}

		/// <summary>
		/// Creates an event handler marker step for the given event type.
		/// </summary>
		public static AIStep Handler(AIEvent evt) => new(AIInstruction.EventHandler, (float)(int)evt);

		/// <summary>
		/// Creates a Speak instruction with the given text and duration.
		/// </summary>
		public static AIStep SpeakText(string text, float duration) => new(AIInstruction.Speak, duration) { TextParam = text };

		/// <summary>
		/// Creates an async Speak instruction — displays speech and immediately advances.
		/// </summary>
		public static AIStep AsyncSpeakText(string text, float duration) => new(AIInstruction.AsyncSpeak, duration) { TextParam = text };

		/// <summary>
		/// Creates a MoveToPlayer instruction with a custom stop distance.
		/// </summary>
		public static AIStep MoveToPlayerAt(float searchRadius, float stopDistance, int onFailGoto = -1) =>
			new(AIInstruction.MoveToPlayer, searchRadius, onFailGoto) { Param2 = stopDistance };
	}
}
