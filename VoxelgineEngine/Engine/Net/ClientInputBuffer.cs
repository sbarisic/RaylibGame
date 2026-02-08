using System;
using System.Collections.Generic;
using System.Numerics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Stores a buffered input entry: the tick number, input state, and camera angle
	/// at the time the input was sent to the server.
	/// </summary>
	public unsafe struct BufferedInput
	{
		/// <summary>The client tick number when this input was recorded.</summary>
		public int TickNumber;

		/// <summary>The full input state (keys, mouse position, wheel) at this tick.</summary>
		public InputState State;

		/// <summary>The camera yaw/pitch angle at this tick.</summary>
		public Vector2 CameraAngle;
	}

	/// <summary>
	/// Circular buffer storing the last <see cref="BufferSize"/> ticks of sent input for
	/// client-side prediction reconciliation. Each tick, the client records its local input
	/// via <see cref="Record"/> which stores the input and returns a ready-to-send
	/// <see cref="InputStatePacket"/>. When the server sends an authoritative player snapshot
	/// for tick T, the client retrieves all inputs after T via <see cref="GetInputsInRange"/>
	/// and replays them to reconcile the predicted position with the server's authoritative state.
	/// </summary>
	public unsafe class ClientInputBuffer
	{
		/// <summary>
		/// Maximum number of buffered input entries. Inputs older than this are discarded.
		/// Must be a power of two is not required, but modulo works with any positive value.
		/// </summary>
		public const int BufferSize = 128;

		private readonly BufferedInput[] _buffer = new BufferedInput[BufferSize];
		private int _count;

		// Pre-allocated list reused by GetInputsInRange to avoid per-reconciliation allocation
		private readonly List<BufferedInput> _replayList = new List<BufferedInput>(BufferSize);

		/// <summary>
		/// The number of inputs currently stored in the buffer (up to <see cref="BufferSize"/>).
		/// </summary>
		public int Count => _count;

		/// <summary>
		/// Records an input for the given tick and returns a ready-to-send <see cref="InputStatePacket"/>.
		/// The input is stored in the circular buffer for later reconciliation.
		/// </summary>
		/// <param name="tickNumber">The client's local tick number for this input.</param>
		/// <param name="state">The polled input state (keys, mouse, wheel).</param>
		/// <param name="cameraAngle">The camera yaw (X) and pitch (Y) in degrees.</param>
		/// <returns>An <see cref="InputStatePacket"/> ready to be sent unreliably to the server.</returns>
		public InputStatePacket Record(int tickNumber, InputState state, Vector2 cameraAngle)
		{
			int index = tickNumber % BufferSize;
			if (index < 0) index += BufferSize;

			_buffer[index].TickNumber = tickNumber;
			_buffer[index].State = state;
			_buffer[index].CameraAngle = cameraAngle;

			if (_count < BufferSize)
				_count++;

			var packet = new InputStatePacket
			{
				TickNumber = tickNumber,
				CameraAngle = cameraAngle,
				MouseWheel = state.MouseWheel,
			};
			packet.PackKeys(state);
			return packet;
		}

		/// <summary>
		/// Tries to retrieve a buffered input by tick number.
		/// Returns false if the tick has been overwritten or was never recorded.
		/// </summary>
		public bool TryGetInput(int tickNumber, out BufferedInput input)
		{
			int index = tickNumber % BufferSize;
			if (index < 0) index += BufferSize;

			if (_buffer[index].TickNumber == tickNumber)
			{
				input = _buffer[index];
				return true;
			}

			input = default;
			return false;
		}

		/// <summary>
		/// Returns all buffered inputs with tick numbers strictly greater than
		/// <paramref name="afterTick"/> up to and including <paramref name="upToTick"/>,
		/// ordered by tick number. Used for prediction reconciliation: replay these
		/// inputs from the server-confirmed state.
		/// </summary>
		/// <remarks>
		/// Returns a shared list that is reused across calls — do not hold references to it
		/// beyond the current reconciliation cycle.
		/// </remarks>
		/// <param name="afterTick">The last server-acknowledged tick (exclusive).</param>
		/// <param name="upToTick">The current client tick (inclusive).</param>
		public List<BufferedInput> GetInputsInRange(int afterTick, int upToTick)
		{
			_replayList.Clear();

			for (int tick = afterTick + 1; tick <= upToTick; tick++)
			{
				int index = tick % BufferSize;
				if (index < 0) index += BufferSize;

				if (_buffer[index].TickNumber == tick)
				{
					_replayList.Add(_buffer[index]);
				}
			}

			return _replayList;
		}

		/// <summary>
		/// Clears all buffered inputs and resets the count.
		/// </summary>
		public void Clear()
		{
			Array.Clear(_buffer, 0, BufferSize);
			_count = 0;
		}
	}
}
