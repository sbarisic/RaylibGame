namespace Voxelgine.Engine
{
	/// <summary>
	/// Tracks bytes sent and received per second for network bandwidth diagnostics.
	/// Call <see cref="Update"/> once per tick to roll over per-second counters.
	/// </summary>
	public class BandwidthTracker
	{
		private long _bytesSentAccum;
		private long _bytesReceivedAccum;
		private float _lastResetTime;

		/// <summary>Bytes sent in the last completed one-second window.</summary>
		public long BytesSentPerSec { get; private set; }

		/// <summary>Bytes received in the last completed one-second window.</summary>
		public long BytesReceivedPerSec { get; private set; }

		/// <summary>Total bytes sent since tracking began.</summary>
		public long TotalBytesSent { get; private set; }

		/// <summary>Total bytes received since tracking began.</summary>
		public long TotalBytesReceived { get; private set; }

		/// <summary>Records outgoing bytes for bandwidth tracking.</summary>
		public void RecordSent(int bytes)
		{
			_bytesSentAccum += bytes;
			TotalBytesSent += bytes;
		}

		/// <summary>Records incoming bytes for bandwidth tracking.</summary>
		public void RecordReceived(int bytes)
		{
			_bytesReceivedAccum += bytes;
			TotalBytesReceived += bytes;
		}

		/// <summary>
		/// Rolls over per-second counters if a full second has elapsed since the last reset.
		/// Call once per tick to keep <see cref="BytesSentPerSec"/> and
		/// <see cref="BytesReceivedPerSec"/> up to date.
		/// </summary>
		public void Update(float currentTime)
		{
			if (currentTime - _lastResetTime >= 1f)
			{
				BytesSentPerSec = _bytesSentAccum;
				BytesReceivedPerSec = _bytesReceivedAccum;
				_bytesSentAccum = 0;
				_bytesReceivedAccum = 0;
				_lastResetTime = currentTime;
			}
		}
	}
}
