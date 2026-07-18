using System.Collections.Generic;
using System.Numerics;
using Voxelgine.Graphics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Performs client-side prediction reconciliation by replaying buffered inputs
	/// against the world after a server correction. Lives in the Voxelgine project
	/// because it requires <see cref="ChunkMap"/> for collision detection.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Usage flow each time a <see cref="WorldSnapshotPacket"/> arrives:
	/// <list type="number">
	/// <item>Call <see cref="ClientPrediction.ProcessServerSnapshot"/> to check if correction is needed.</item>
	/// <item>If true, call <see cref="Reconcile"/> which snaps the player to server state
	/// and replays all buffered inputs from the server tick to the current tick.</item>
	/// </list>
	/// </para>
	/// </remarks>
	public sealed class PredictionReconciler
	{
		private readonly NetworkInputSource _replayInputSource = new();
		private readonly InputMgr _replayInputMgr;

		public PredictionReconciler()
		{
			_replayInputMgr = new InputMgr(_replayInputSource);
		}

		public void Reset()
		{
			_replayInputSource.SetState(default);
			_replayInputMgr.Reset();
		}

		/// <summary>
		/// Snaps the player to the server-authoritative state and replays all buffered
		/// inputs from <paramref name="lastInputTick"/> to <paramref name="currentTick"/>
		/// using the same Quake-style physics as the server.
		/// </summary>
		/// <param name="player">The local player to reconcile.</param>
		/// <param name="serverState">The server's complete authoritative physics state.</param>
		/// <param name="lastInputTick">The client tick of the last input the server processed.</param>
		/// <param name="currentTick">The current client tick (inclusive).</param>
		/// <param name="inputBuffer">The client input buffer containing inputs for replay.</param>
		/// <param name="prediction">The prediction system to update with replayed states.</param>
		/// <param name="world">The shared voxel/entity collision view used during replay.</param>
		/// <param name="physData">Physics constants.</param>
		/// <param name="dt">Fixed timestep delta time (0.015s).</param>
		public void Reconcile(
			Player player,
			in PlayerPhysicsState serverState,
			int lastInputTick,
			int currentTick,
			ClientInputBuffer inputBuffer,
			ClientPrediction prediction,
			PhysicsWorld world,
			PhysData physData,
			float dt)
		{
			// Snap to server state
			player.ApplyPhysicsState(serverState);

			// Get all inputs that need to be replayed (after last-processed input tick, up to current)
			List<BufferedInput> inputs = inputBuffer.GetInputsInRange(lastInputTick, currentTick);

			foreach (var input in inputs)
			{
				// Restore camera angle for this tick's input
				player.SetCamAngle(new Vector3(input.CameraAngle.X, input.CameraAngle.Y, 0));
				player.UpdateDirectionVectors();

				// Feed the input state into the replay InputMgr
				_replayInputSource.SetState(input.State);
				_replayInputMgr.Tick(0f);

				// Run physics with this input (same code as server)
				player.UpdatePhysics(world, physData, dt, _replayInputMgr);

				// Record the new predicted state
				prediction.RecordPrediction(input.TickNumber, player.CapturePhysicsState());
			}
		}
	}
}
