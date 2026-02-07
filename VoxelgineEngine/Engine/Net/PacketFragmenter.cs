using System;
using System.Collections.Generic;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Splits large packets into MTU-sized fragments for transmission and reassembles
	/// received fragments back into complete packets.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Fragment wire format prepended to each fragment's packet data:
	/// <c>[0xFE][groupId:2][index:1][total:1][payload...]</c>
	/// </para>
	/// <para>
	/// Non-fragmented packets pass through unchanged. The 0xFE marker byte does not
	/// conflict with any <see cref="PacketType"/> value (range 0x01–0x81) or the
	/// <see cref="PacketBatcher.BatchMarker"/> (0xFF).
	/// </para>
	/// <para>
	/// Each fragment is sent as an individually reliable packet through
	/// <see cref="ReliableChannel"/>, ensuring retransmission of lost fragments.
	/// The original packet is reassembled only when all fragments for a group arrive.
	/// </para>
	/// </remarks>
	public class PacketFragmenter
	{
		/// <summary>
		/// Marker byte identifying a fragment packet. Does not conflict with
		/// <see cref="PacketType"/> values (0x01–0x81) or <see cref="PacketBatcher.BatchMarker"/> (0xFF).
		/// </summary>
		public const byte FragmentMarker = 0xFE;

		/// <summary>
		/// Fragment header size in bytes: marker (1) + group ID (2) + fragment index (1) + total fragments (1).
		/// </summary>
		public const int FragmentHeaderSize = 5;

		/// <summary>
		/// Timeout in seconds for incomplete fragment groups. Groups with no new fragments
		/// for this duration are discarded.
		/// </summary>
		public const float StaleGroupTimeout = 10f;

		private ushort _nextGroupId;
		private readonly Dictionary<ushort, FragmentGroup> _pendingGroups = new();
		private readonly int _maxFragmentPayload;

		/// <summary>
		/// Maximum payload bytes per fragment (excluding the fragment header).
		/// Computed as MTU minus <see cref="ReliableChannel.HeaderSize"/> minus
		/// <see cref="FragmentHeaderSize"/> so each fragment fits in a single UDP datagram.
		/// </summary>
		public int MaxFragmentPayload => _maxFragmentPayload;

		/// <summary>
		/// Number of fragment groups currently awaiting completion on the receive side.
		/// </summary>
		public int PendingGroupCount => _pendingGroups.Count;

		/// <summary>
		/// Creates a new fragmenter.
		/// </summary>
		/// <param name="maxFragmentPayload">
		/// Maximum payload per fragment. Defaults to
		/// <c>PacketBatcher.DefaultMtu - ReliableChannel.HeaderSize - FragmentHeaderSize</c>.
		/// </param>
		public PacketFragmenter(int maxFragmentPayload = 0)
		{
			_maxFragmentPayload = maxFragmentPayload > 0
				? maxFragmentPayload
				: PacketBatcher.DefaultMtu - ReliableChannel.HeaderSize - FragmentHeaderSize;
		}

		/// <summary>
		/// Returns whether the given serialized packet data exceeds the maximum fragment
		/// payload size and needs to be split into multiple fragments.
		/// </summary>
		/// <param name="packetData">Serialized packet bytes (from <see cref="Packet.Serialize"/>).</param>
		public bool NeedsFragmentation(byte[] packetData)
		{
			return packetData != null && packetData.Length > _maxFragmentPayload;
		}

		/// <summary>
		/// Splits serialized packet data into numbered fragments. Each fragment has a
		/// fragment header prepended and should be sent as an individual reliable packet
		/// through <see cref="ReliableChannel.Wrap"/>.
		/// </summary>
		/// <param name="packetData">Serialized packet bytes to fragment.</param>
		/// <returns>List of fragment byte arrays, each with the fragment header prepended.</returns>
		/// <exception cref="InvalidOperationException">
		/// Thrown if the data would require more than 255 fragments.
		/// </exception>
		public List<byte[]> Split(byte[] packetData)
		{
			var fragments = new List<byte[]>();

			_nextGroupId++;
			if (_nextGroupId == 0)
				_nextGroupId = 1;

			ushort groupId = _nextGroupId;
			int totalFragments = (packetData.Length + _maxFragmentPayload - 1) / _maxFragmentPayload;

			if (totalFragments > 255)
				throw new InvalidOperationException(
					$"Packet too large to fragment: {packetData.Length} bytes requires {totalFragments} fragments (max 255).");

			int offset = 0;
			for (int i = 0; i < totalFragments; i++)
			{
				int chunkSize = Math.Min(_maxFragmentPayload, packetData.Length - offset);
				byte[] fragment = new byte[FragmentHeaderSize + chunkSize];

				fragment[0] = FragmentMarker;
				fragment[1] = (byte)(groupId & 0xFF);
				fragment[2] = (byte)((groupId >> 8) & 0xFF);
				fragment[3] = (byte)i;
				fragment[4] = (byte)totalFragments;

				Buffer.BlockCopy(packetData, offset, fragment, FragmentHeaderSize, chunkSize);
				fragments.Add(fragment);
				offset += chunkSize;
			}

			return fragments;
		}

		/// <summary>
		/// Processes received packet data after <see cref="ReliableChannel.Unwrap"/>.
		/// If the data is a fragment, it is buffered and the method returns the fully
		/// reassembled original packet data when all fragments for the group have arrived
		/// (returns null while waiting for more fragments). Non-fragment data is returned as-is.
		/// </summary>
		/// <param name="packetData">Unwrapped packet data from <see cref="ReliableChannel.Unwrap"/>.</param>
		/// <param name="currentTime">Current time in seconds for stale group tracking.</param>
		/// <returns>
		/// The complete packet data (ready for <see cref="Packet.Deserialize"/>),
		/// or null if still waiting for more fragments.
		/// </returns>
		public byte[] HandleReceived(byte[] packetData, float currentTime)
		{
			if (!IsFragment(packetData))
				return packetData;

			if (packetData.Length < FragmentHeaderSize)
				return null;

			ushort groupId = (ushort)(packetData[1] | (packetData[2] << 8));
			byte index = packetData[3];
			byte total = packetData[4];

			if (total == 0 || index >= total)
				return null;

			if (!_pendingGroups.TryGetValue(groupId, out var group))
			{
				group = new FragmentGroup(total);
				_pendingGroups[groupId] = group;
			}

			byte[] payload = new byte[packetData.Length - FragmentHeaderSize];
			Buffer.BlockCopy(packetData, FragmentHeaderSize, payload, 0, payload.Length);
			group.SetFragment(index, payload, currentTime);

			if (group.IsComplete)
			{
				_pendingGroups.Remove(groupId);
				return group.Assemble();
			}

			return null;
		}

		/// <summary>
		/// Removes incomplete fragment groups that have not received any new fragments
		/// within <see cref="StaleGroupTimeout"/> seconds.
		/// </summary>
		/// <param name="currentTime">Current time in seconds.</param>
		public void CleanupStaleGroups(float currentTime)
		{
			List<ushort> stale = null;

			foreach (var kvp in _pendingGroups)
			{
				if (currentTime - kvp.Value.LastFragmentTime >= StaleGroupTimeout)
				{
					stale ??= new List<ushort>();
					stale.Add(kvp.Key);
				}
			}

			if (stale != null)
			{
				foreach (var id in stale)
					_pendingGroups.Remove(id);
			}
		}

		/// <summary>
		/// Resets the fragmenter, clearing all pending fragment groups and the group ID counter.
		/// </summary>
		public void Reset()
		{
			_pendingGroups.Clear();
			_nextGroupId = 0;
		}

		/// <summary>
		/// Returns whether the given packet data starts with the <see cref="FragmentMarker"/> byte.
		/// </summary>
		public static bool IsFragment(byte[] data)
		{
			return data != null && data.Length > 0 && data[0] == FragmentMarker;
		}

		/// <summary>
		/// Tracks fragments for a single fragmented packet (one group ID).
		/// </summary>
		private class FragmentGroup
		{
			private readonly byte[][] _fragments;
			private int _receivedCount;

			/// <summary>
			/// The time when the most recent fragment in this group was received.
			/// Used for stale group cleanup.
			/// </summary>
			public float LastFragmentTime { get; private set; }

			/// <summary>
			/// Whether all fragments have been received.
			/// </summary>
			public bool IsComplete => _receivedCount == _fragments.Length;

			public FragmentGroup(int totalFragments)
			{
				_fragments = new byte[totalFragments][];
			}

			public void SetFragment(int index, byte[] payload, float currentTime)
			{
				if (index < 0 || index >= _fragments.Length)
					return;

				if (_fragments[index] == null)
					_receivedCount++;

				_fragments[index] = payload;
				LastFragmentTime = currentTime;
			}

			public byte[] Assemble()
			{
				int totalSize = 0;
				foreach (var f in _fragments)
					totalSize += f.Length;

				byte[] result = new byte[totalSize];
				int offset = 0;
				foreach (var f in _fragments)
				{
					Buffer.BlockCopy(f, 0, result, offset, f.Length);
					offset += f.Length;
				}

				return result;
			}
		}
	}
}
