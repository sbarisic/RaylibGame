using System;
using System.Collections.Generic;
using System.Net;

namespace Voxelgine.Engine
{
	public enum ConnectionState
	{
		Connecting,
		Connected,
		Disconnected,
	}

	public enum ReliableSendClass
	{
		Control,
		Gameplay,
		Bulk,
	}

	public readonly record struct NetConnectionDiagnostics(
		int ReliableInFlight,
		int ControlQueued,
		int GameplayQueued,
		int BulkQueued,
		long AcknowledgementsSent,
		long RetransmissionsSent,
		long RetryFailures,
		float AcknowledgementsPerSecond,
		float RetransmissionsPerSecond);

	/// <summary>
	/// One UDP peer with bounded, priority-aware reliable delivery and fragment reassembly.
	/// </summary>
	public sealed class NetConnection
	{
		public const float DefaultTimeout = 10f;
		public const float PingInterval = 1f;
		public const int ControlAndGameplayQueueCapacity = 256;
		public const int BulkQueueCapacity = 128;
		public const int BulkInFlightLimit = 48;

		private const float RttSmoothFactor = 0.8f;
		private readonly ReliableChannel channel = new();
		private readonly PacketFragmenter fragmenter = new(PacketBatcher.DefaultMtu - AuthenticatedDatagrams.Overhead - ReliableChannel.HeaderSize - PacketFragmenter.FragmentHeaderSize);
		private readonly BandwidthTracker bandwidth = new();
		private readonly Queue<byte[]> controlQueue = new();
		private readonly Queue<byte[]> gameplayQueue = new();
		private readonly Queue<byte[]> bulkQueue = new();
		private readonly Queue<byte[]> unreliableQueue = new();
		private readonly List<byte[]> rawPriorityQueue = new();

		private double lastReceiveTime;
		private double lastPingSentTime;
		private long lastPingTimestamp;
		private float roundTripTime;
		private bool roundTripTimeInitialized;
		private double diagnosticsRateSampleTime;
		private long diagnosticsRateAcknowledgements;
		private long diagnosticsRateRetransmissions;
		private float acknowledgementsPerSecond;
		private float retransmissionsPerSecond;

		public NetConnection(IPEndPoint remoteEndPoint, double currentTime)
		{
			RemoteEndPoint = remoteEndPoint ?? throw new ArgumentNullException(nameof(remoteEndPoint));
			lastReceiveTime = currentTime;
			lastPingSentTime = currentTime;
			diagnosticsRateSampleTime = currentTime;
			State = ConnectionState.Connecting;
		}

		public IPEndPoint RemoteEndPoint { get; }
		public PlayerIdentity Identity { get; set; }
		public AuthenticatedDatagrams Authentication { get; set; }
		public ConnectionState State { get; set; }
		public int PlayerId { get; set; } = -1;
		public string PlayerName { get; set; } = string.Empty;
		public bool IsGameplayActive { get; set; }
		public float RoundTripTime => roundTripTime;
		public int RoundTripTimeMs => (int)(roundTripTime * 1000f);
		public ReliableChannel Channel => channel;
		public PacketFragmenter Fragmenter => fragmenter;
		public BandwidthTracker Bandwidth => bandwidth;
		public int BulkQueueSlots => BulkQueueCapacity - bulkQueue.Count;
		public int ReliableQueueCount => controlQueue.Count + gameplayQueue.Count + bulkQueue.Count;

		public NetConnectionDiagnostics Diagnostics => new(
			channel.PendingCount,
			controlQueue.Count,
			gameplayQueue.Count,
			bulkQueue.Count,
			channel.AckOnlyPacketsSent,
			channel.RetransmissionsSent,
			channel.RetryFailures,
			acknowledgementsPerSecond,
			retransmissionsPerSecond);

		public Packet UnwrapPacket(byte[] rawData, double currentTime)
		{
			ReliableReceiveResult result = channel.Unwrap(rawData);
			if (!result.IsValid)
				return null;

			lastReceiveTime = currentTime;
			if (result.IsAckOnly || result.IsDuplicate || result.Payload == null)
				return null;

			byte[] reassembled = fragmenter.HandleReceived(result.Payload, currentTime);
			if (reassembled == null)
				return null;

			try
			{
				return Packet.Deserialize(reassembled);
			}
			catch
			{
				return null;
			}
		}

		public bool HasTimedOut(double currentTime) => currentTime - lastReceiveTime >= DefaultTimeout;

		public double TimeSinceLastReceive(double currentTime) => currentTime - lastReceiveTime;

		public bool ShouldSendPing(double currentTime) => currentTime - lastPingSentTime >= PingInterval;

		public PingPacket CreatePing(double currentTime)
		{
			lastPingSentTime = currentTime;
			lastPingTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
			return new PingPacket { Timestamp = lastPingTimestamp };
		}

		public PongPacket CreatePong(long pingTimestamp) => new() { Timestamp = pingTimestamp };

		public void ProcessPong(PongPacket pong)
		{
			if (pong.Timestamp != lastPingTimestamp) return;
			double elapsedSeconds = System.Diagnostics.Stopwatch.GetElapsedTime(pong.Timestamp).TotalSeconds;
			if (elapsedSeconds < 0)
				return;

			float sample = (float)elapsedSeconds;
			if (!roundTripTimeInitialized)
			{
				roundTripTime = sample;
				roundTripTimeInitialized = true;
			}
			else
			{
				roundTripTime = roundTripTime * RttSmoothFactor + sample * (1f - RttSmoothFactor);
			}
		}

		public void Disconnect()
		{
			State = ConnectionState.Disconnected;
			IsGameplayActive = false;
		}

		public bool QueuePacket(
			Packet packet,
			bool reliable,
			double currentTime,
			ReliableSendClass sendClass = ReliableSendClass.Gameplay)
		{
			ArgumentNullException.ThrowIfNull(packet);
			if (reliable && (sendClass == ReliableSendClass.Bulk
				? bulkQueue.Count >= BulkQueueCapacity
				: controlQueue.Count + gameplayQueue.Count >= ControlAndGameplayQueueCapacity)) return false;
			byte[] serialized = packet.Serialize();
			if (!reliable)
			{
				unreliableQueue.Enqueue(serialized);
				return true;
			}

			List<byte[]> payloads = fragmenter.NeedsFragmentation(serialized)
				? fragmenter.Split(serialized)
				: new List<byte[]> { serialized };
			Queue<byte[]> target = GetQueue(sendClass);
			bool queueFull = sendClass == ReliableSendClass.Bulk
				? bulkQueue.Count + payloads.Count > BulkQueueCapacity
				: controlQueue.Count + gameplayQueue.Count + payloads.Count > ControlAndGameplayQueueCapacity;
			if (queueFull)
				return false;

			foreach (byte[] payload in payloads)
				target.Enqueue(payload);
			return true;
		}

		public bool SendImmediate(
			Packet packet,
			bool reliable,
			double currentTime,
			UdpTransport transport,
			ReliableSendClass sendClass = ReliableSendClass.Control)
		{
			if (!QueuePacket(packet, reliable, currentTime, sendClass))
				return false;
			FlushOutgoing(transport, currentTime);
			return true;
		}

		public bool CollectRetransmissions(double currentTime, out uint failedSequence)
		{
			ReliableRetransmissionBatch batch = channel.CollectRetransmissions(currentTime, roundTripTime);
			foreach (byte[] packet in batch.Packets)
				rawPriorityQueue.Add(packet);
			failedSequence = batch.FailedSequence;
			return !batch.RetryLimitExceeded;
		}

		public void CleanupStaleFragments(double currentTime) => fragmenter.CleanupStaleGroups(currentTime);

		public void FlushOutgoing(UdpTransport transport, double currentTime, int maxBytesPerTick = 0)
		{
			ArgumentNullException.ThrowIfNull(transport);
			bandwidth.Update(currentTime);
			List<byte[]> packets = new(rawPriorityQueue.Count + ReliableChannel.WindowSize + unreliableQueue.Count + 1);
			packets.AddRange(rawPriorityQueue);
			rawPriorityQueue.Clear();

			DrainReliable(controlQueue, packets, currentTime);
			DrainReliable(gameplayQueue, packets, currentTime);
			DrainReliable(bulkQueue, packets, currentTime, BulkInFlightLimit);

			int reliableBytes = 0;
			foreach (byte[] packet in packets)
				reliableBytes += packet.Length;
			int byteBudget = maxBytesPerTick > 0 ? maxBytesPerTick : int.MaxValue;
			while (unreliableQueue.Count != 0)
			{
				byte[] rawData = channel.WrapUnreliable(unreliableQueue.Dequeue());
				if (reliableBytes + rawData.Length <= byteBudget)
				{
					packets.Add(rawData);
					reliableBytes += rawData.Length;
				}
			}

			if (channel.HasPendingAcknowledgement)
				packets.Add(channel.CreateAcknowledgement());
			if (packets.Count == 0)
			{
				UpdateDiagnosticRates(currentTime);
				return;
			}

			foreach (byte[] datagram in PacketBatcher.CreateBatchedDatagrams(packets, PacketBatcher.DefaultMtu - AuthenticatedDatagrams.Overhead))
			{
				byte[] outgoing = Authentication?.Protect(datagram) ?? datagram;
				transport.SendTo(outgoing, RemoteEndPoint);
				bandwidth.RecordSent(outgoing.Length);
			}
			UpdateDiagnosticRates(currentTime);
		}

		public void Reset()
		{
			Authentication?.Dispose();
			Authentication = null;
			controlQueue.Clear();
			gameplayQueue.Clear();
			bulkQueue.Clear();
			unreliableQueue.Clear();
			rawPriorityQueue.Clear();
			fragmenter.Reset();
			IsGameplayActive = false;
			diagnosticsRateSampleTime = 0;
			diagnosticsRateAcknowledgements = 0;
			diagnosticsRateRetransmissions = 0;
			acknowledgementsPerSecond = 0;
			retransmissionsPerSecond = 0;
		}

		private void UpdateDiagnosticRates(double currentTime)
		{
			double elapsed = currentTime - diagnosticsRateSampleTime;
			if (elapsed < 1)
				return;

			long acknowledgements = channel.AckOnlyPacketsSent;
			long retransmissions = channel.RetransmissionsSent;
			acknowledgementsPerSecond = (float)((acknowledgements - diagnosticsRateAcknowledgements) / elapsed);
			retransmissionsPerSecond = (float)((retransmissions - diagnosticsRateRetransmissions) / elapsed);
			diagnosticsRateAcknowledgements = acknowledgements;
			diagnosticsRateRetransmissions = retransmissions;
			diagnosticsRateSampleTime = currentTime;
		}

		private Queue<byte[]> GetQueue(ReliableSendClass sendClass) => sendClass switch
		{
			ReliableSendClass.Control => controlQueue,
			ReliableSendClass.Gameplay => gameplayQueue,
			ReliableSendClass.Bulk => bulkQueue,
			_ => throw new ArgumentOutOfRangeException(nameof(sendClass)),
		};

		private void DrainReliable(
			Queue<byte[]> source,
			List<byte[]> destination,
			double currentTime,
			int inFlightLimit = ReliableChannel.WindowSize)
		{
			while (source.Count != 0 &&
				channel.AvailableSendSlots != 0 &&
				channel.PendingCount < inFlightLimit)
			{
				byte[] payload = source.Peek();
				if (!channel.TryWrapReliable(payload, currentTime, out byte[] rawData))
					break;
				source.Dequeue();
				destination.Add(rawData);
			}
		}

		public override string ToString()
		{
			NetConnectionDiagnostics diagnostics = Diagnostics;
			return $"[NetConnection {RemoteEndPoint} PlayerId={PlayerId} State={State} RTT={RoundTripTimeMs}ms InFlight={diagnostics.ReliableInFlight} Queued={ReliableQueueCount} Out={bandwidth.BytesSentPerSec}B/s In={bandwidth.BytesReceivedPerSec}B/s]";
		}
	}
}
