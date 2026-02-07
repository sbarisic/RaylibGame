using System;
using System.Collections.Generic;
using System.IO;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Manages server-side world data transfers to connecting clients. On client connect,
	/// the server serializes the <c>ChunkMap</c> into a GZip-compressed byte array, then
	/// this manager fragments it into <see cref="WorldDataPacket"/>s (~1KB each) and sends
	/// them reliably at a rate-limited pace to avoid flooding the connection. Once all
	/// fragments are sent, a <see cref="WorldDataCompletePacket"/> with a checksum is sent
	/// so the client can verify integrity and transition to gameplay.
	/// </summary>
	public class WorldTransferManager
	{
		/// <summary>
		/// Size of each world data fragment in bytes. Chosen to stay well under
		/// typical MTU (~1200 bytes) after accounting for reliable delivery headers.
		/// </summary>
		public const int FragmentSize = 1024;

		/// <summary>
		/// Maximum number of fragments sent per tick per player. At 66.6 Hz tick rate,
		/// 8 fragments × 1024 bytes = ~546 KB/s transfer rate per player.
		/// </summary>
		public const int FragmentsPerTick = 8;

		private readonly NetServer _server;
		private readonly Dictionary<int, PendingTransfer> _transfers = new();

		/// <summary>
		/// Fired when a world transfer completes (all fragments + complete packet sent).
		/// The parameter is the player ID.
		/// </summary>
		public event Action<int> OnTransferComplete;

		public WorldTransferManager(NetServer server)
		{
			_server = server;
		}

		/// <summary>
		/// Begins a rate-limited world data transfer to the specified player.
		/// The compressed data is split into fragments and queued for sending.
		/// </summary>
		/// <param name="playerId">The target player's ID.</param>
		/// <param name="compressedWorldData">The GZip-compressed world data bytes
		/// (output of <c>ChunkMap.Write()</c> into a <see cref="MemoryStream"/>).</param>
		public void BeginTransfer(int playerId, byte[] compressedWorldData)
		{
			if (_transfers.ContainsKey(playerId))
			{
				_transfers.Remove(playerId);
			}

			int totalFragments = (compressedWorldData.Length + FragmentSize - 1) / FragmentSize;
			uint checksum = ComputeChecksum(compressedWorldData);

			_transfers[playerId] = new PendingTransfer
			{
				PlayerId = playerId,
				Data = compressedWorldData,
				TotalFragments = totalFragments,
				NextFragment = 0,
				Checksum = checksum,
			};
		}

		/// <summary>
		/// Sends the next batch of fragments for all pending transfers. Must be called
		/// once per server tick on the game thread.
		/// </summary>
		/// <param name="currentTime">Current time in seconds for packet wrapping.</param>
		public void Tick(float currentTime)
		{
			if (_transfers.Count == 0)
				return;

			// Process in a list to allow removal during iteration
			var completed = (List<int>)null;

			foreach (var kvp in _transfers)
			{
				PendingTransfer transfer = kvp.Value;
				int sent = 0;

				while (sent < FragmentsPerTick && transfer.NextFragment < transfer.TotalFragments)
				{
					int fragmentIndex = transfer.NextFragment;
					int offset = fragmentIndex * FragmentSize;
					int length = Math.Min(FragmentSize, transfer.Data.Length - offset);

					byte[] fragmentData = new byte[length];
					Buffer.BlockCopy(transfer.Data, offset, fragmentData, 0, length);

					var packet = new WorldDataPacket
					{
						FragmentIndex = fragmentIndex,
						Data = fragmentData,
					};

					_server.SendTo(transfer.PlayerId, packet, true, currentTime);

					transfer.NextFragment++;
					sent++;
				}

				// All fragments sent — send completion packet
				if (transfer.NextFragment >= transfer.TotalFragments)
				{
					var completePacket = new WorldDataCompletePacket
					{
						TotalFragments = transfer.TotalFragments,
						Checksum = transfer.Checksum,
					};

					_server.SendTo(transfer.PlayerId, completePacket, true, currentTime);

					completed ??= new List<int>();
					completed.Add(kvp.Key);
				}
			}

			if (completed != null)
			{
				foreach (int playerId in completed)
				{
					_transfers.Remove(playerId);
					OnTransferComplete?.Invoke(playerId);
				}
			}
		}

		/// <summary>
		/// Cancels a pending transfer for a player (e.g., when they disconnect during loading).
		/// </summary>
		/// <param name="playerId">The player ID whose transfer to cancel.</param>
		public void CancelTransfer(int playerId)
		{
			_transfers.Remove(playerId);
		}

		/// <summary>
		/// Whether there is a pending world transfer for the specified player.
		/// </summary>
		public bool HasPendingTransfer(int playerId) => _transfers.ContainsKey(playerId);

		/// <summary>
		/// The number of currently active world transfers.
		/// </summary>
		public int ActiveTransferCount => _transfers.Count;

		/// <summary>
		/// Computes a 32-bit FNV-1a hash of the data for integrity verification.
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

		private class PendingTransfer
		{
			public int PlayerId;
			public byte[] Data;
			public int TotalFragments;
			public int NextFragment;
			public uint Checksum;
		}
	}
}
