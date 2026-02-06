using System.Numerics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Input source for remote/networked players. Stores the last received input state
	/// from a network packet. In multiplayer, the server sets this each tick when it
	/// receives an InputState packet from a client.
	/// </summary>
	public unsafe class NetworkInputSource : IInputSource
	{
		private InputState _currentState;

		/// <summary>
		/// Sets the input state received from the network.
		/// Called by the server when processing a client's InputState packet.
		/// </summary>
		public void SetState(InputState state)
		{
			_currentState = state;
		}

		public InputState Poll(float gameTime)
		{
			// Return the last state received from the network.
			// GameTime is overridden to match server tick timing.
			_currentState.GameTime = gameTime;
			return _currentState;
		}
	}
}
