using System;
using System.Collections.Generic;
using System.Numerics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Stores the predicted player state for a single tick.
	/// Used to compare against server-authoritative snapshots for reconciliation.
	/// </summary>
	public struct PredictedState
	{
		/// <summary>The tick number this state corresponds to.</summary>
		public int TickNumber;

		/// <summary>The predicted eye-level position after physics for this tick.</summary>
		public Vector3 Position;

		/// <summary>The predicted velocity after physics for this tick.</summary>
		public Vector3 Velocity;
	}

	/// <summary>
	/// Client-side prediction and server reconciliation system.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Each tick, the client applies local input to the player using the same Quake-style
	/// physics as the server (<c>Player.UpdatePhysics()</c>). The predicted state (position
	/// and velocity) is stored in a circular buffer indexed by tick number via
	/// <see cref="RecordPrediction"/>.
	/// </para>
	/// <para>
	/// When a <see cref="WorldSnapshotPacket"/> arrives from the server containing the
	/// local player's authoritative state, <see cref="ProcessServerSnapshot"/> compares
	/// the server position with the predicted position at that tick. If the difference
	/// exceeds <see cref="CorrectionThreshold"/>, the method returns true indicating
	/// the caller must perform reconciliation: snap the player to the server state
	/// and replay all buffered inputs from the server tick to the current tick using
	/// <c>ClientInputBuffer.GetInputsInRange()</c>.
	/// </para>
	/// <para>
	/// Reconciliation replay is performed by the game code (which has access to
	/// <c>ChunkMap</c> and <c>Player</c>) rather than this class, keeping this class
	/// free of Raylib and game-specific dependencies.
	/// </para>
	/// </remarks>
	public class ClientPrediction
	{
		/// <summary>
		/// Position difference threshold (in world units) beyond which reconciliation is triggered.
		/// Small errors (below this) are ignored to avoid unnecessary corrections from floating-point drift.
		/// </summary>
		public const float CorrectionThreshold = 0.01f;

		/// <summary>
		/// Size of the prediction state buffer. Must match <see cref="ClientInputBuffer.BufferSize"/>.
		/// </summary>
		public const int BufferSize = ClientInputBuffer.BufferSize;

		private readonly PredictedState[] _stateBuffer = new PredictedState[BufferSize];

		/// <summary>
		/// The last server tick that was processed via <see cref="ProcessServerSnapshot"/>.
		/// Used to avoid reprocessing the same or older snapshots.
		/// </summary>
		public int LastServerTick { get; private set; } = -1;

		/// <summary>
		/// The number of reconciliations (corrections) performed since the last reset.
		/// Useful for network diagnostics.
		/// </summary>
		public int ReconciliationCount { get; private set; }

		/// <summary>
		/// The position error of the most recent server snapshot comparison.
		/// 0 if no correction was needed, or the distance in world units if corrected.
		/// </summary>
		public float LastCorrectionDistance { get; private set; }

		/// <summary>
		/// Records the predicted state after a tick's physics have been applied.
		/// Call this immediately after <c>Player.UpdatePhysics()</c> each tick.
		/// </summary>
		/// <param name="tickNumber">The client tick number.</param>
		/// <param name="position">The player's position after physics.</param>
		/// <param name="velocity">The player's velocity after physics.</param>
		public void RecordPrediction(int tickNumber, Vector3 position, Vector3 velocity)
		{
			int index = tickNumber % BufferSize;
			if (index < 0) index += BufferSize;

			_stateBuffer[index].TickNumber = tickNumber;
			_stateBuffer[index].Position = position;
			_stateBuffer[index].Velocity = velocity;
		}

		/// <summary>
		/// Processes a server-authoritative snapshot for the local player.
		/// Compares the server state with the predicted state at the snapshot's tick.
		/// </summary>
		/// <remarks>
		/// If this method returns true, the caller must:
		/// <list type="number">
		/// <item>Snap the player to <paramref name="serverPosition"/> and <paramref name="serverVelocity"/>.</item>
		/// <item>Retrieve buffered inputs via <c>ClientInputBuffer.GetInputsInRange(serverTick, currentTick)</c>.</item>
		/// <item>For each buffered input: set the camera angle, update direction vectors,
		/// feed the input into an <see cref="InputMgr"/> via <see cref="NetworkInputSource"/>,
		/// call <c>Player.UpdatePhysics()</c>, and call <see cref="RecordPrediction"/> with the result.</item>
		/// </list>
		/// </remarks>
		/// <param name="serverTick">The server tick number from the snapshot.</param>
		/// <param name="serverPosition">The server's authoritative position.</param>
		/// <param name="serverVelocity">The server's authoritative velocity.</param>
		/// <returns>True if the prediction was wrong and reconciliation is needed.</returns>
		public bool ProcessServerSnapshot(
			int serverTick,
			Vector3 serverPosition,
			Vector3 serverVelocity)
		{
			LastCorrectionDistance = 0f;

			// Ignore old or duplicate snapshots
			if (serverTick <= LastServerTick)
				return false;

			LastServerTick = serverTick;

			// Look up the predicted state at this tick
			int index = serverTick % BufferSize;
			if (index < 0) index += BufferSize;

			if (_stateBuffer[index].TickNumber != serverTick)
			{
				// No prediction stored for this tick (too old, overwritten).
				// Accept server state unconditionally.
				ReconciliationCount++;
				LastCorrectionDistance = float.MaxValue;
				return true;
			}

			// Compare predicted position with server position
			float error = Vector3.Distance(_stateBuffer[index].Position, serverPosition);
			LastCorrectionDistance = error;

			if (error > CorrectionThreshold)
			{
				ReconciliationCount++;
				return true;
			}

			// Prediction was accurate — no correction needed
			return false;
		}

		/// <summary>
		/// Resets the prediction system. Call on disconnect or reconnect.
		/// </summary>
		public void Reset()
		{
			Array.Clear(_stateBuffer, 0, BufferSize);
			LastServerTick = -1;
			ReconciliationCount = 0;
			LastCorrectionDistance = 0f;
		}
	}
}
