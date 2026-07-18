namespace Voxelgine.Engine
{
	/// <summary>
	/// Abstracts the source of input state. Local players use a client input adapter,
	/// remote/networked players receive input state from network packets.
	/// </summary>
	public interface IInputSource
	{
		/// <summary>
		/// Polls and returns the current input state for this tick.
		/// </summary>
		InputState Poll(float gameTime);
	}
}
