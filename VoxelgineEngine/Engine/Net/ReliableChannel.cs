using System;
using System.Collections.Generic;
using System.IO;

namespace Voxelgine.Engine
{
	[Flags]
	internal enum ReliablePacketFlags : byte
	{
		None = 0,
		Reliable = 1,
		AckOnly = 2,
	}

	public readonly struct ReliableReceiveResult
	{
		internal ReliableReceiveResult(bool isValid, bool isAckOnly, bool isDuplicate, byte[] payload)
		{
			IsValid = isValid;
			IsAckOnly = isAckOnly;
			IsDuplicate = isDuplicate;
			Payload = payload;
		}

		public bool IsValid { get; }
		public bool IsAckOnly { get; }
		public bool IsDuplicate { get; }
		public byte[] Payload { get; }
	}

	public readonly struct ReliableRetransmissionBatch
	{
		internal ReliableRetransmissionBatch(List<byte[]> packets, bool retryLimitExceeded, uint failedSequence)
		{
			Packets = packets;
			RetryLimitExceeded = retryLimitExceeded;
			FailedSequence = failedSequence;
		}

		public IReadOnlyList<byte[]> Packets { get; }
		public bool RetryLimitExceeded { get; }
		public uint FailedSequence { get; }
	}

	/// <summary>
	/// Selective-repeat reliability for one UDP peer. The send window is exactly the
	/// number of packets represented by the acknowledgement bitfield, so an accepted
	/// packet can never age out of the peer's acknowledgement history while outstanding.
	/// </summary>
	/// <remarks>
	/// Wire format: [flags:1][sequence:4][ack_sequence:4][ack_bits:8][payload...].
	/// Sequence zero is reserved for unreliable and acknowledgement-only datagrams.
	/// </remarks>
	public sealed class ReliableChannel
	{
		public const int HeaderSize = 17;
		public const int WindowSize = 64;
		public const int MaxRetries = 12;
		public const float InitialRetransmitTimeout = 0.2f;
		public const float MinimumRetransmitTimeout = 0.1f;
		public const float MaximumRetransmitTimeout = 1f;

		private uint localSequence;
		private readonly Dictionary<uint, PendingPacket> sendWindow = new();
		private uint remoteSequence;
		private ulong ackBitfield;
		private bool hasReceivedReliable;
		private bool ackDirty;

		public uint LocalSequence => localSequence;
		public uint RemoteSequence => remoteSequence;
		public int PendingCount => sendWindow.Count;
		public int AvailableSendSlots => WindowSize - sendWindow.Count;
		public bool HasPendingAcknowledgement => ackDirty;
		public long AckOnlyPacketsSent { get; private set; }
		public long RetransmissionsSent { get; private set; }
		public long RetryFailures { get; private set; }

		public bool TryWrapReliable(byte[] packetData, float currentTime, out byte[] rawData)
		{
			ArgumentNullException.ThrowIfNull(packetData);
			if (sendWindow.Count >= WindowSize)
			{
				rawData = null;
				return false;
			}

			localSequence++;
			if (localSequence == 0)
				localSequence = 1;

			uint sequence = localSequence;
			sendWindow.Add(sequence, new PendingPacket(sequence, packetData, currentTime));
			rawData = BuildRawPacket(
				ReliablePacketFlags.Reliable,
				sequence,
				remoteSequence,
				ackBitfield,
				packetData);
			return true;
		}

		public byte[] WrapUnreliable(byte[] packetData)
		{
			ArgumentNullException.ThrowIfNull(packetData);
			byte[] rawData = BuildRawPacket(
				ReliablePacketFlags.None,
				0,
				remoteSequence,
				ackBitfield,
				packetData);
			return rawData;
		}

		public byte[] CreateAcknowledgement()
		{
			byte[] rawData = BuildRawPacket(
				ReliablePacketFlags.AckOnly,
				0,
				remoteSequence,
				ackBitfield,
				Array.Empty<byte>());
			ackDirty = false;
			AckOnlyPacketsSent++;
			return rawData;
		}

		public ReliableReceiveResult Unwrap(byte[] rawData)
		{
			if (rawData == null || rawData.Length < HeaderSize)
				return default;

			using MemoryStream stream = new(rawData, writable: false);
			using BinaryReader reader = new(stream);
			ReliablePacketFlags flags = (ReliablePacketFlags)reader.ReadByte();
			if ((flags & ~(ReliablePacketFlags.Reliable | ReliablePacketFlags.AckOnly)) != 0)
				return default;

			uint sequence = reader.ReadUInt32();
			uint ackSequence = reader.ReadUInt32();
			ulong ackBits = reader.ReadUInt64();
			ProcessAck(ackSequence, ackBits);

			bool ackOnly = (flags & ReliablePacketFlags.AckOnly) != 0;
			bool reliable = (flags & ReliablePacketFlags.Reliable) != 0;
			if (ackOnly && (reliable || sequence != 0 || rawData.Length != HeaderSize))
				return default;

			if (!ackOnly && !reliable && sequence != 0)
				return default;
			if (reliable && sequence == 0)
				return default;

			if (ackOnly)
				return new ReliableReceiveResult(true, true, false, null);

			int payloadLength = rawData.Length - HeaderSize;
			if (payloadLength <= 0)
				return default;

			byte[] payload = reader.ReadBytes(payloadLength);
			if (!reliable)
				return new ReliableReceiveResult(true, false, false, payload);

			bool accepted = TrackReceivedSequence(sequence);
			ackDirty = true;
			return new ReliableReceiveResult(true, false, !accepted, accepted ? payload : null);
		}

		public ReliableRetransmissionBatch CollectRetransmissions(float currentTime, float roundTripTime)
		{
			List<byte[]> packets = new();
			float baseTimeout = roundTripTime > 0
				? Math.Clamp(roundTripTime * 2f, MinimumRetransmitTimeout, MaximumRetransmitTimeout)
				: InitialRetransmitTimeout;

			foreach (PendingPacket pending in sendWindow.Values)
			{
				float multiplier = MathF.Pow(2f, Math.Min(pending.RetryCount, 4));
				float timeout = Math.Min(MaximumRetransmitTimeout, baseTimeout * multiplier);
				if (currentTime - pending.SentTime < timeout)
					continue;

				if (pending.RetryCount >= MaxRetries)
				{
					RetryFailures++;
					return new ReliableRetransmissionBatch(packets, true, pending.Sequence);
				}

				pending.SentTime = currentTime;
				pending.RetryCount++;
				packets.Add(BuildRawPacket(
					ReliablePacketFlags.Reliable,
					pending.Sequence,
					remoteSequence,
					ackBitfield,
					pending.PacketData));
				RetransmissionsSent++;
			}

			return new ReliableRetransmissionBatch(packets, false, 0);
		}

		private bool TrackReceivedSequence(uint sequence)
		{
			if (!hasReceivedReliable)
			{
				remoteSequence = sequence;
				ackBitfield = 0;
				hasReceivedReliable = true;
				return true;
			}

			int difference = SequenceDifference(sequence, remoteSequence);
			if (difference == 0)
				return false;

			if (difference > 0)
			{
				ackBitfield = difference < WindowSize ? ackBitfield << difference : 0;
				if (difference <= WindowSize)
					ackBitfield |= 1UL << (difference - 1);
				remoteSequence = sequence;
				return true;
			}

			int bitIndex = -difference - 1;
			if (bitIndex >= WindowSize)
				return false;

			ulong mask = 1UL << bitIndex;
			if ((ackBitfield & mask) != 0)
				return false;

			ackBitfield |= mask;
			return true;
		}

		private void ProcessAck(uint ackSequence, ulong ackBits)
		{
			if (ackSequence == 0)
				return;

			sendWindow.Remove(ackSequence);
			for (int index = 0; index < WindowSize; index++)
			{
				if ((ackBits & (1UL << index)) == 0)
					continue;

				uint acknowledged = ackSequence - 1u - (uint)index;
				if (acknowledged != 0)
					sendWindow.Remove(acknowledged);
			}
		}

		private static byte[] BuildRawPacket(
			ReliablePacketFlags flags,
			uint sequence,
			uint ackSequence,
			ulong ackBits,
			byte[] packetData)
		{
			byte[] rawData = new byte[HeaderSize + packetData.Length];
			using MemoryStream stream = new(rawData);
			using BinaryWriter writer = new(stream);
			writer.Write((byte)flags);
			writer.Write(sequence);
			writer.Write(ackSequence);
			writer.Write(ackBits);
			writer.Write(packetData);
			return rawData;
		}

		private static int SequenceDifference(uint left, uint right) => unchecked((int)(left - right));

		private sealed class PendingPacket
		{
			internal PendingPacket(uint sequence, byte[] packetData, float sentTime)
			{
				Sequence = sequence;
				PacketData = packetData;
				SentTime = sentTime;
			}

			internal uint Sequence { get; }
			internal byte[] PacketData { get; }
			internal float SentTime { get; set; }
			internal int RetryCount { get; set; }
		}
	}
}
