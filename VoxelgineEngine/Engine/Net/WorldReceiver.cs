using System;
using System.Collections.Generic;
using System.IO;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Client-side world data receiver. Collects <see cref="WorldDataPacket"/> fragments
	/// from the server, reassembles them into a contiguous byte array, verifies integrity
	/// via FNV-1a checksum, and signals completion. This is the client-side counterpart to
	/// <see cref="WorldTransferManager"/>.
	/// </summary>
	/// <remarks>
	/// Fragments may arrive out of order (due to reliable retransmission scheduling), so they
	/// are stored in a sparse dictionary keyed by fragment index. When a
	/// <see cref="WorldDataCompletePacket"/> is received, all fragments must be present.
	/// The reassembled data is GZip-compressed world data ready to pass to
	/// <c>ChunkMap.Read(new MemoryStream(data))</c>.
	/// </remarks>
	public class WorldReceiver
	{
		private readonly Dictionary<int, byte[]> _fragments = new();
		private int _totalFragments = -1;
		private uint _expectedChecksum;
		private bool _completed;

		/// <summary>
		/// Fired when all fragments have been received, reassembled, and checksum-verified.
		/// The parameter is the GZip-compressed world data byte array.
		/// </summary>
		public event Action<byte[]> OnWorldDataReady;

		/// <summary>
		/// Fired when the transfer fails (checksum mismatch, missing fragments, etc.).
		/// The parameter is a human-readable error message.
		/// </summary>
		public event Action<string> OnTransferFailed;

		/// <summary>
		/// The number of fragments received so far.
		/// </summary>
		public int FragmentsReceived => _fragments.Count;

		/// <summary>
		/// The total number of fragments expected, or -1 if not yet known
		/// (set when <see cref="WorldDataCompletePacket"/> arrives).
		/// </summary>
		public int TotalFragments => _totalFragments;

		/// <summary>
		/// Loading progress as a fraction (0.0 to 1.0). Returns 0 if total is unknown.
		/// </summary>
		public float Progress
		{
			get
			{
				if (_totalFragments <= 0)
					return _fragments.Count > 0 ? 0f : 0f;
				return Math.Min(1f, (float)_fragments.Count / _totalFragments);
			}
		}

		/// <summary>
		/// Whether the world data transfer has completed successfully.
		/// </summary>
		public bool IsComplete => _completed;

		/// <summary>
		/// Whether a transfer is currently in progress (at least one fragment received,
		/// not yet complete or failed).
		/// </summary>
		public bool IsReceiving => _fragments.Count > 0 && !_completed;

		/// <summary>
		/// Processes a <see cref="WorldDataPacket"/> fragment from the server.
		/// Stores the fragment data keyed by index. Duplicate fragments are silently ignored.
		/// </summary>
		public void HandleWorldData(WorldDataPacket packet)
		{
			if (_completed)
				return;

			// Store fragment (ignore duplicates from reliable retransmission)
			if (!_fragments.ContainsKey(packet.FragmentIndex))
			{
				_fragments[packet.FragmentIndex] = packet.Data;
			}
		}

		/// <summary>
		/// Processes a <see cref="WorldDataCompletePacket"/> from the server.
		/// Verifies all fragments are present, reassembles the data, validates the
		/// FNV-1a checksum, and fires <see cref="OnWorldDataReady"/> on success
		/// or <see cref="OnTransferFailed"/> on failure.
		/// </summary>
		public void HandleWorldDataComplete(WorldDataCompletePacket packet)
		{
			if (_completed)
				return;

			_totalFragments = packet.TotalFragments;
			_expectedChecksum = packet.Checksum;

			// Check that all fragments have arrived
			if (_fragments.Count < _totalFragments)
			{
				// Not all fragments received yet — this can happen if the complete packet
				// arrives before all reliable fragments have been delivered (unlikely but possible).
				// Store the expected values and wait; the caller can check again later.
				return;
			}

			TryAssemble();
		}

		/// <summary>
		/// Attempts to assemble the world data if all fragments and the completion info are available.
		/// Called after each fragment and after the complete packet. Allows for the case where
		/// the complete packet arrives before all fragments.
		/// </summary>
		public void TryAssemble()
		{
			if (_completed || _totalFragments < 0 || _fragments.Count < _totalFragments)
				return;

			// Reassemble fragments in order
			using var ms = new MemoryStream();
			for (int i = 0; i < _totalFragments; i++)
			{
				if (!_fragments.TryGetValue(i, out byte[] fragmentData))
				{
					OnTransferFailed?.Invoke($"Missing world data fragment {i} of {_totalFragments}.");
					return;
				}
				ms.Write(fragmentData, 0, fragmentData.Length);
			}

			byte[] worldData = ms.ToArray();

			// Verify checksum
			uint actualChecksum = ComputeChecksum(worldData);
			if (actualChecksum != _expectedChecksum)
			{
				OnTransferFailed?.Invoke(
					$"World data checksum mismatch: expected 0x{_expectedChecksum:X8}, got 0x{actualChecksum:X8}.");
				return;
			}

			_completed = true;

			// Clear fragment storage to free memory
			_fragments.Clear();

			OnWorldDataReady?.Invoke(worldData);
		}

		/// <summary>
		/// Resets the receiver to its initial state. Called on disconnect or to retry a transfer.
		/// </summary>
		public void Reset()
		{
			_fragments.Clear();
			_totalFragments = -1;
			_expectedChecksum = 0;
			_completed = false;
		}

		/// <summary>
		/// Computes a 32-bit FNV-1a hash matching <see cref="WorldTransferManager"/>'s checksum.
		/// </summary>
		private static uint ComputeChecksum(byte[] data)
		{
			uint hash = 2166136261u; // FNV-1a offset basis
			for (int i = 0; i < data.Length; i++)
			{
				hash ^= data[i];
				hash *= 16777619u; // FNV-1a prime
			}
			return hash;
		}
	}
}
