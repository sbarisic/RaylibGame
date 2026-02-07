using System;
using System.Collections.Generic;
using System.IO;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Manages reliable and unreliable packet delivery over UDP for a single connection.
	/// Wraps outgoing packets with a protocol header and tracks reliable packets for
	/// retransmission. Processes incoming ACK data and detects duplicate reliable packets.
	/// </summary>
	/// <remarks>
	/// Wire format: [reliable:1][seq:2][ack_seq:2][ack_bits:4][packet_data...]
	/// <para/>
	/// Packet data starts with the <see cref="PacketType"/> byte from <see cref="Packet.Serialize"/>.
	/// All packets (reliable and unreliable) carry piggybacked ACK data.
	/// Sequence number 0 is reserved for unreliable packets (no tracking).
	/// </remarks>
	public class ReliableChannel
	{
		/// <summary>
		/// Protocol header size in bytes:
		/// reliable flag (1) + sequence (2) + ack sequence (2) + ack bitfield (4).
		/// </summary>
		public const int HeaderSize = 9;

		/// <summary>
		/// Default retransmission timeout in seconds.
		/// </summary>
		public const float DefaultRetransmitTimeout = 0.2f;

		// --- Outgoing state ---
		private ushort _localSequence;
		private readonly Dictionary<ushort, PendingPacket> _sendBuffer = new();

		// --- Incoming state ---
		private ushort _remoteSequence;
		private uint _ackBitfield;
		private bool _hasReceivedReliable;

		/// <summary>
		/// The last assigned local reliable sequence number.
		/// </summary>
		public ushort LocalSequence => _localSequence;

		/// <summary>
		/// The highest received remote reliable sequence number.
		/// </summary>
		public ushort RemoteSequence => _remoteSequence;

		/// <summary>
		/// Number of reliable packets awaiting acknowledgment.
		/// </summary>
		public int PendingCount => _sendBuffer.Count;

		/// <summary>
		/// Wraps packet data with the protocol header for transmission.
		/// Reliable packets are assigned a sequence number and stored for retransmission.
		/// Unreliable packets use sequence 0 and are not tracked.
		/// All packets carry piggybacked ACK data for the remote side.
		/// </summary>
		/// <param name="packetData">Serialized packet bytes (from <see cref="Packet.Serialize"/>).</param>
		/// <param name="reliable">Whether this packet requires reliable delivery.</param>
		/// <param name="currentTime">Current time in seconds for retransmission tracking.</param>
		/// <returns>Raw bytes ready for UDP transmission (header + packet data).</returns>
		public byte[] Wrap(byte[] packetData, bool reliable, float currentTime)
		{
			ushort sequence = 0;

			if (reliable)
			{
				_localSequence++;
				if (_localSequence == 0)
					_localSequence = 1;

				sequence = _localSequence;

				_sendBuffer[sequence] = new PendingPacket
				{
					Sequence = sequence,
					PacketData = packetData,
					SentTime = currentTime,
				};
			}

			return BuildRawPacket(reliable ? (byte)1 : (byte)0, sequence, _remoteSequence, _ackBitfield, packetData);
		}

		/// <summary>
		/// Processes a received raw packet: strips the protocol header, processes piggybacked
		/// ACK data to remove acknowledged packets from the send buffer, and checks incoming
		/// reliable packets for duplicates.
		/// </summary>
		/// <param name="rawData">Raw bytes received from UDP (header + packet data).</param>
		/// <returns>
		/// The packet data payload (suitable for <see cref="Packet.Deserialize"/>),
		/// or null if the packet is a duplicate, malformed, or has no payload.
		/// </returns>
		public byte[] Unwrap(byte[] rawData)
		{
			if (rawData == null || rawData.Length < HeaderSize)
				return null;

			using var ms = new MemoryStream(rawData);
			using var reader = new BinaryReader(ms);

			byte reliableFlag = reader.ReadByte();
			ushort sequence = reader.ReadUInt16();
			ushort ackSequence = reader.ReadUInt16();
			uint ackBitfield = reader.ReadUInt32();

			ProcessAck(ackSequence, ackBitfield);

			int payloadLength = rawData.Length - HeaderSize;
			if (payloadLength == 0)
				return null;

			byte[] packetData = reader.ReadBytes(payloadLength);

			if (reliableFlag != 0 && sequence != 0)
			{
				if (!TrackReceivedSequence(sequence))
					return null;
			}

			return packetData;
		}

		/// <summary>
		/// Collects reliable packets that have not been acknowledged within the retransmission
		/// timeout and re-wraps them with current ACK data for retransmission.
		/// </summary>
		/// <param name="currentTime">Current time in seconds.</param>
		/// <param name="retransmitTimeout">Seconds before retransmitting an unACKed packet.</param>
		/// <returns>List of raw byte arrays ready for UDP retransmission.</returns>
		public List<byte[]> GetRetransmissions(float currentTime, float retransmitTimeout = DefaultRetransmitTimeout)
		{
			var result = new List<byte[]>();

			foreach (var kvp in _sendBuffer)
			{
				var pending = kvp.Value;
				if (currentTime - pending.SentTime >= retransmitTimeout)
				{
					pending.SentTime = currentTime;

					byte[] rewrapped = BuildRawPacket(1, pending.Sequence, _remoteSequence, _ackBitfield, pending.PacketData);
					result.Add(rewrapped);
				}
			}

			return result;
		}

		/// <summary>
		/// Tracks a received reliable sequence number in the receive window.
		/// Returns false if the sequence is a duplicate or too old to track (more than 32 behind).
		/// </summary>
		private bool TrackReceivedSequence(ushort sequence)
		{
			if (!_hasReceivedReliable)
			{
				_remoteSequence = sequence;
				_ackBitfield = 0;
				_hasReceivedReliable = true;
				return true;
			}

			int diff = SequenceDiff(sequence, _remoteSequence);

			if (diff == 0)
				return false;

			if (diff > 0)
			{
				// New sequence ahead of current — shift the bitfield and mark old head as received.
				if (diff < 32)
					_ackBitfield <<= diff;
				else
					_ackBitfield = 0;

				if (diff <= 32)
					_ackBitfield |= 1U << (diff - 1);

				_remoteSequence = sequence;
				return true;
			}

			// diff < 0: older packet arrived out of order.
			int bitIndex = -diff - 1;
			if (bitIndex >= 32)
				return false;

			if ((_ackBitfield & (1U << bitIndex)) != 0)
				return false;

			_ackBitfield |= 1U << bitIndex;
			return true;
		}

		/// <summary>
		/// Processes piggybacked ACK data from a remote packet, removing
		/// acknowledged packets from the send buffer.
		/// </summary>
		private void ProcessAck(ushort ackSequence, uint ackBitfield)
		{
			if (ackSequence == 0)
				return;

			_sendBuffer.Remove(ackSequence);

			for (int i = 0; i < 32; i++)
			{
				if ((ackBitfield & (1U << i)) != 0)
				{
					ushort ackedSeq = (ushort)(ackSequence - 1 - i);
					if (ackedSeq != 0)
						_sendBuffer.Remove(ackedSeq);
				}
			}
		}

		/// <summary>
		/// Builds a raw packet by prepending the protocol header to the packet data.
		/// </summary>
		private static byte[] BuildRawPacket(byte reliableFlag, ushort sequence, ushort ackSequence, uint ackBitfield, byte[] packetData)
		{
			byte[] raw = new byte[HeaderSize + packetData.Length];

			using var ms = new MemoryStream(raw);
			using var writer = new BinaryWriter(ms);

			writer.Write(reliableFlag);
			writer.Write(sequence);
			writer.Write(ackSequence);
			writer.Write(ackBitfield);
			writer.Write(packetData);

			return raw;
		}

		/// <summary>
		/// Computes the signed difference between two sequence numbers with ushort wraparound.
		/// Positive means <paramref name="a"/> is ahead of <paramref name="b"/>.
		/// </summary>
		private static int SequenceDiff(ushort a, ushort b)
		{
			return (short)(a - b);
		}

		private class PendingPacket
		{
			public ushort Sequence;
			public byte[] PacketData;
			public float SentTime;
		}
	}
}
