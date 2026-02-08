using System;
using System.Collections.Generic;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Simulates adverse network conditions for testing: artificial latency,
	/// packet loss, and jitter. Incoming raw datagrams are buffered and released
	/// after a configurable delay. A percentage of datagrams are randomly dropped.
	/// </summary>
	public class NetworkSimulation
	{
		private readonly List<DelayedPacket> _delayed = new();
		private readonly Random _rng = new();

		/// <summary>
		/// Whether the simulation is active. When false, all packets pass through immediately.
		/// </summary>
		public bool Enabled { get; set; }

		/// <summary>
		/// One-way artificial latency in milliseconds added to each packet.
		/// </summary>
		public int LatencyMs { get; set; }

		/// <summary>
		/// Packet loss percentage (0–100). Each incoming packet has this chance of being dropped.
		/// </summary>
		public int PacketLossPercent { get; set; }

		/// <summary>
		/// Maximum additional random delay in milliseconds added on top of <see cref="LatencyMs"/>.
		/// The actual jitter for each packet is uniformly distributed in [0, JitterMs].
		/// </summary>
		public int JitterMs { get; set; }

		/// <summary>
		/// Submits a raw datagram into the simulation pipeline. The datagram may be
		/// dropped (packet loss) or delayed (latency + jitter).
		/// </summary>
		/// <param name="data">Raw UDP datagram bytes.</param>
		/// <param name="currentTime">Current time in seconds.</param>
		public void Submit(byte[] data, float currentTime)
		{
			if (!Enabled)
			{
				_delayed.Add(new DelayedPacket(data, currentTime));
				return;
			}

			// Packet loss
			if (PacketLossPercent > 0 && _rng.Next(100) < PacketLossPercent)
				return;

			// Calculate delivery time
			float delaySeconds = LatencyMs / 1000f;
			if (JitterMs > 0)
				delaySeconds += _rng.Next(JitterMs + 1) / 1000f;

			_delayed.Add(new DelayedPacket(data, currentTime + delaySeconds));
		}

		/// <summary>
		/// Retrieves all packets whose delivery time has arrived, preserving submission order.
		/// </summary>
		/// <param name="currentTime">Current time in seconds.</param>
		/// <param name="output">List to append ready packets to.</param>
		public void Collect(float currentTime, List<byte[]> output)
		{
			int writeIdx = 0;
			for (int i = 0; i < _delayed.Count; i++)
			{
				if (currentTime >= _delayed[i].DeliverAt)
				{
					output.Add(_delayed[i].Data);
				}
				else
				{
					_delayed[writeIdx++] = _delayed[i];
				}
			}
			_delayed.RemoveRange(writeIdx, _delayed.Count - writeIdx);
		}

		/// <summary>
		/// Discards all buffered packets.
		/// </summary>
		public void Clear()
		{
			_delayed.Clear();
		}

		private readonly struct DelayedPacket
		{
			public readonly byte[] Data;
			public readonly float DeliverAt;

			public DelayedPacket(byte[] data, float deliverAt)
			{
				Data = data;
				DeliverAt = deliverAt;
			}
		}
	}
}
