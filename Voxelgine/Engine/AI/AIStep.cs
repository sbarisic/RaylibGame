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

		/// <summary>
		/// Step index to jump to when this instruction fails.
		/// -1 means fall through to the next step (default).
		/// </summary>
		public int OnFailGoto;

		public AIStep(AIInstruction instruction, float param = 0, int onFailGoto = -1)
		{
			Instruction = instruction;
			Param = param;
			OnFailGoto = onFailGoto;
		}

		/// <summary>
		/// Creates an event handler marker step for the given event type.
		/// </summary>
		public static AIStep Handler(AIEvent evt) => new(AIInstruction.EventHandler, (float)(int)evt);
	}
}
