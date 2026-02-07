using System;
using System.Net;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Connection lifecycle states.
	/// </summary>
	public enum ConnectionState
	{
		/// <summary>
		/// Connection handshake in progress (Connect sent, awaiting ConnectAccept/ConnectReject).
		/// </summary>
		Connecting,

		/// <summary>
		/// Fully connected and exchanging game data.
		/// </summary>
		Connected,

		/// <summary>
		/// Connection terminated (explicit disconnect or timeout).
		/// </summary>
		Disconnected,
	}

	/// <summary>
	/// Represents a network connection to a remote endpoint.
	/// Wraps a <see cref="ReliableChannel"/> for packet delivery and tracks connection
	/// metadata: round-trip time (via Ping/Pong), timeout detection, player identity,
	/// and connection state.
	/// </summary>
	/// <remarks>
	/// Server holds one <see cref="NetConnection"/> per connected client in a
	/// <c>Dictionary&lt;IPEndPoint, NetConnection&gt;</c>.
	/// Client holds a single <see cref="NetConnection"/> to the server.
	/// </remarks>
	public class NetConnection
	{
		/// <summary>
		/// Default timeout in seconds. If no data is received for this duration
		/// the connection is considered dead.
		/// </summary>
		public const float DefaultTimeout = 10f;

		/// <summary>
		/// Interval in seconds between automatic Ping packets.
		/// </summary>
		public const float PingInterval = 1f;

		/// <summary>
		/// Smoothing factor for RTT exponential moving average.
		/// Higher values weight the existing average more heavily.
		/// </summary>
		private const float RttSmoothFactor = 0.8f;

		private readonly ReliableChannel _channel;

		private float _lastReceiveTime;
		private float _lastPingSentTime;
		private long _lastPingTimestamp;
		private float _rtt;
		private bool _rttInitialized;

		/// <summary>
		/// The remote endpoint this connection communicates with.
		/// </summary>
		public IPEndPoint RemoteEndPoint { get; }

		/// <summary>
		/// Current connection state.
		/// </summary>
		public ConnectionState State { get; set; }

		/// <summary>
		/// The player ID assigned to this connection. -1 if not yet assigned.
		/// </summary>
		public int PlayerId { get; set; } = -1;

		/// <summary>
		/// The player display name associated with this connection.
		/// </summary>
		public string PlayerName { get; set; } = string.Empty;

		/// <summary>
		/// Smoothed round-trip time in seconds, measured via Ping/Pong.
		/// Returns 0 until the first Pong is received.
		/// </summary>
		public float RoundTripTime => _rtt;

		/// <summary>
		/// Round-trip time in milliseconds for display purposes.
		/// </summary>
		public int RoundTripTimeMs => (int)(_rtt * 1000f);

		/// <summary>
		/// The underlying reliability channel for this connection.
		/// </summary>
		public ReliableChannel Channel => _channel;

		/// <summary>
		/// Creates a new connection to the specified remote endpoint.
		/// </summary>
		/// <param name="remoteEndPoint">The remote IP endpoint.</param>
		/// <param name="currentTime">Current time in seconds for timeout tracking.</param>
		public NetConnection(IPEndPoint remoteEndPoint, float currentTime)
		{
			RemoteEndPoint = remoteEndPoint ?? throw new ArgumentNullException(nameof(remoteEndPoint));
			_channel = new ReliableChannel();
			_lastReceiveTime = currentTime;
			_lastPingSentTime = currentTime;
			State = ConnectionState.Connecting;
		}

		/// <summary>
		/// Wraps a packet for transmission through the reliable channel.
		/// </summary>
		/// <param name="packet">The packet to send.</param>
		/// <param name="reliable">Whether the packet requires reliable delivery.</param>
		/// <param name="currentTime">Current time in seconds.</param>
		/// <returns>Raw bytes ready for UDP transmission.</returns>
		public byte[] WrapPacket(Packet packet, bool reliable, float currentTime)
		{
			byte[] serialized = packet.Serialize();
			return _channel.Wrap(serialized, reliable, currentTime);
		}

		/// <summary>
		/// Unwraps a received raw datagram through the reliable channel and
		/// deserializes the contained packet.
		/// </summary>
		/// <param name="rawData">Raw bytes received from UDP.</param>
		/// <param name="currentTime">Current time in seconds for receive tracking.</param>
		/// <returns>The deserialized packet, or null if the data was a duplicate, malformed, or empty.</returns>
		public Packet UnwrapPacket(byte[] rawData, float currentTime)
		{
			byte[] packetData = _channel.Unwrap(rawData);
			if (packetData == null)
				return null;

			_lastReceiveTime = currentTime;

			try
			{
				return Packet.Deserialize(packetData);
			}
			catch
			{
				return null;
			}
		}

		/// <summary>
		/// Returns whether the connection has timed out (no data received for <see cref="DefaultTimeout"/> seconds).
		/// </summary>
		/// <param name="currentTime">Current time in seconds.</param>
		public bool HasTimedOut(float currentTime)
		{
			return currentTime - _lastReceiveTime >= DefaultTimeout;
		}

		/// <summary>
		/// Returns whether it is time to send a Ping packet based on <see cref="PingInterval"/>.
		/// </summary>
		/// <param name="currentTime">Current time in seconds.</param>
		public bool ShouldSendPing(float currentTime)
		{
			return currentTime - _lastPingSentTime >= PingInterval;
		}

		/// <summary>
		/// Creates a <see cref="PingPacket"/> and records the send time for RTT calculation.
		/// </summary>
		/// <param name="currentTime">Current time in seconds.</param>
		/// <returns>A Ping packet ready to be sent.</returns>
		public PingPacket CreatePing(float currentTime)
		{
			_lastPingSentTime = currentTime;
			_lastPingTimestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			return new PingPacket { Timestamp = _lastPingTimestamp };
		}

		/// <summary>
		/// Creates a <see cref="PongPacket"/> that echoes the timestamp from a received Ping.
		/// </summary>
		/// <param name="pingTimestamp">The timestamp from the received <see cref="PingPacket"/>.</param>
		/// <returns>A Pong packet ready to be sent.</returns>
		public PongPacket CreatePong(long pingTimestamp)
		{
			return new PongPacket { Timestamp = pingTimestamp };
		}

		/// <summary>
		/// Processes a received <see cref="PongPacket"/> and updates the smoothed RTT.
		/// </summary>
		/// <param name="pong">The received Pong packet.</param>
		public void ProcessPong(PongPacket pong)
		{
			long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			long elapsedMs = now - pong.Timestamp;

			if (elapsedMs < 0)
				return;

			float sample = elapsedMs / 1000f;

			if (!_rttInitialized)
			{
				_rtt = sample;
				_rttInitialized = true;
			}
			else
			{
				_rtt = _rtt * RttSmoothFactor + sample * (1f - RttSmoothFactor);
			}
		}

		/// <summary>
		/// Disconnects this connection by setting the state to <see cref="ConnectionState.Disconnected"/>.
		/// </summary>
		public void Disconnect()
		{
			State = ConnectionState.Disconnected;
		}

		public override string ToString()
		{
			return $"[NetConnection {RemoteEndPoint} PlayerId={PlayerId} State={State} RTT={RoundTripTimeMs}ms Pending={_channel.PendingCount}]";
		}
	}
}
