using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Client-side connection state, more granular than <see cref="ConnectionState"/>
	/// to track the full client lifecycle.
	/// </summary>
	public enum ClientState
	{
		/// <summary>
		/// Not connected to any server.
		/// </summary>
		Disconnected,

		/// <summary>
		/// Connect packet sent, awaiting <see cref="ConnectAcceptPacket"/> or <see cref="ConnectRejectPacket"/>.
		/// </summary>
		Connecting,

		/// <summary>
		/// Connection accepted, receiving world data from the server.
		/// </summary>
		Loading,

		/// <summary>
		/// World loaded, normal gameplay in progress.
		/// </summary>
		Playing,
	}

	/// <summary>
	/// Client-side network manager. Connects to a server endpoint, sends a
	/// <see cref="ConnectPacket"/>, handles connection acceptance/rejection,
	/// processes incoming packets, and provides a <see cref="Send"/> method
	/// for outgoing packets.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Incoming UDP datagrams are queued from the transport's background thread and
	/// processed on the game thread during <see cref="Tick"/>. This ensures all packet
	/// handling and event callbacks occur on the game thread.
	/// </para>
	/// <para>
	/// System packets (<see cref="ConnectAcceptPacket"/>, <see cref="ConnectRejectPacket"/>,
	/// <see cref="DisconnectPacket"/>, <see cref="PingPacket"/>, <see cref="PongPacket"/>)
	/// are handled internally. All other packets are forwarded via the
	/// <see cref="OnPacketReceived"/> event for the game loop to process.
	/// </para>
	/// </remarks>
	public class NetClient : IDisposable
	{
		private readonly UdpTransport _transport;
		private readonly ConcurrentQueue<byte[]> _receiveQueue = new();
		private readonly WorldReceiver _worldReceiver = new();

		private NetConnection _connection;
		private IPEndPoint _serverEndPoint;
		private bool _disposed;

		/// <summary>
		/// Fired when the server accepts the connection. The assigned player ID and
		/// server tick are available in the event args. Called on the game thread during <see cref="Tick"/>.
		/// </summary>
		public event Action<ConnectAcceptPacket> OnConnected;

		/// <summary>
		/// Fired when the connection is rejected by the server.
		/// Called on the game thread during <see cref="Tick"/>.
		/// </summary>
		public event Action<string> OnConnectionRejected;

		/// <summary>
		/// Fired when disconnected from the server (explicit disconnect, timeout, or rejection).
		/// Called on the game thread during <see cref="Tick"/>.
		/// </summary>
		public event Action<string> OnDisconnected;

		/// <summary>
		/// Fired for each non-system packet received from the server.
		/// System packets (ConnectAccept, ConnectReject, Disconnect, Ping, Pong) and
		/// world data packets (WorldData, WorldDataComplete) are handled internally.
		/// Called on the game thread during <see cref="Tick"/>.
		/// </summary>
		public event Action<Packet> OnPacketReceived;

		/// <summary>
		/// Fired when all world data fragments have been received and checksum-verified.
		/// The parameter is the GZip-compressed world data byte array, ready to be loaded
		/// via <c>ChunkMap.Read(new MemoryStream(data))</c>. The game loop should load the
		/// world and then call <see cref="FinishLoading"/> to transition to Playing state.
		/// Called on the game thread during <see cref="Tick"/>.
		/// </summary>
		public event Action<byte[]> OnWorldDataReady;

		/// <summary>
		/// Fired when the world data transfer fails (checksum mismatch, missing fragments, etc.).
		/// The parameter is a human-readable error message.
		/// Called on the game thread during <see cref="Tick"/>.
		/// </summary>
		public event Action<string> OnWorldTransferFailed;

		/// <summary>
		/// The current client connection state.
		/// </summary>
		public ClientState State { get; private set; } = ClientState.Disconnected;

		/// <summary>
		/// The player ID assigned by the server. -1 if not yet assigned.
		/// </summary>
		public int PlayerId => _connection?.PlayerId ?? -1;

		/// <summary>
		/// The local player name sent during connection.
		/// </summary>
		public string PlayerName => _connection?.PlayerName ?? string.Empty;

		/// <summary>
		/// Smoothed round-trip time to the server in seconds.
		/// Returns 0 until the first Pong is received.
		/// </summary>
		public float RoundTripTime => _connection?.RoundTripTime ?? 0f;

		/// <summary>
		/// Round-trip time to the server in milliseconds for display purposes.
		/// </summary>
		public int RoundTripTimeMs => _connection?.RoundTripTimeMs ?? 0;

		/// <summary>
		/// Whether the client is currently connected (any state other than Disconnected).
		/// </summary>
		public bool IsConnected => State != ClientState.Disconnected;

		/// <summary>
		/// The local tick counter. Initialized from the server tick on connection acceptance.
		/// The game loop should increment this each fixed timestep and use it to label
		/// outgoing <see cref="InputStatePacket"/> packets.
		/// </summary>
		public int LocalTick { get; set; }

		/// <summary>
		/// The world data receiver for tracking loading progress.
		/// Use <see cref="WorldReceiver.Progress"/> (0.0–1.0) and
		/// <see cref="WorldReceiver.FragmentsReceived"/> / <see cref="WorldReceiver.TotalFragments"/>
		/// to display loading status.
		/// </summary>
		public WorldReceiver WorldReceiver => _worldReceiver;

		public NetClient()
		{
			_transport = new UdpTransport();
			_worldReceiver.OnWorldDataReady += HandleWorldDataReady;
			_worldReceiver.OnTransferFailed += HandleWorldTransferFailed;
		}

		/// <summary>
		/// Initiates a connection to the specified server. Opens the UDP transport,
		/// creates a <see cref="NetConnection"/>, and sends a <see cref="ConnectPacket"/>.
		/// </summary>
		/// <param name="host">The server hostname or IP address.</param>
		/// <param name="port">The server UDP port.</param>
		/// <param name="playerName">The local player's display name.</param>
		/// <param name="currentTime">Current time in seconds.</param>
		public void Connect(string host, int port, string playerName, float currentTime)
		{
			if (State != ClientState.Disconnected)
				throw new InvalidOperationException($"Cannot connect while in state {State}.");

			_serverEndPoint = new IPEndPoint(
				Dns.GetHostAddresses(host)[0],
				port
			);

			_transport.OnDataReceived += QueueReceivedData;
			_transport.Open();

			_connection = new NetConnection(_serverEndPoint, currentTime);
			_connection.PlayerName = playerName;
			_connection.State = ConnectionState.Connecting;

			State = ClientState.Connecting;

			var connectPacket = new ConnectPacket
			{
				PlayerName = playerName,
				ProtocolVersion = NetServer.ProtocolVersion,
			};
			SendInternal(connectPacket, true, currentTime);
		}

		/// <summary>
		/// Disconnects from the server, sending a <see cref="DisconnectPacket"/> with the given reason.
		/// </summary>
		/// <param name="reason">The reason for disconnecting.</param>
		/// <param name="currentTime">Current time in seconds.</param>
		public void Disconnect(string reason, float currentTime)
		{
			if (State == ClientState.Disconnected)
				return;

			if (_connection != null && _connection.State != ConnectionState.Disconnected)
			{
				var disconnectPacket = new DisconnectPacket { Reason = reason };
				SendInternal(disconnectPacket, true, currentTime);
				_connection.Disconnect();
			}

			Cleanup();
			OnDisconnected?.Invoke(reason);
		}

		/// <summary>
		/// Sends a packet to the server.
		/// </summary>
		/// <param name="packet">The packet to send.</param>
		/// <param name="reliable">Whether the packet requires reliable delivery.</param>
		/// <param name="currentTime">Current time in seconds.</param>
		public void Send(Packet packet, bool reliable, float currentTime)
		{
			if (_connection == null || State == ClientState.Disconnected)
				return;

			SendInternal(packet, reliable, currentTime);
		}

		/// <summary>
		/// Processes all queued incoming packets, handles retransmissions, ping/pong,
		/// and timeout detection. Must be called once per tick on the game thread.
		/// </summary>
		/// <param name="currentTime">Current time in seconds.</param>
		public void Tick(float currentTime)
		{
			if (State == ClientState.Disconnected || _connection == null)
				return;

			_connection.Bandwidth.Update(currentTime);

			while (_receiveQueue.TryDequeue(out var data))
			{
				_connection.Bandwidth.RecordReceived(data.Length);

				var subPackets = PacketBatcher.UnbatchDatagram(data);
				foreach (var sub in subPackets)
				{
					Packet packet = _connection.UnwrapPacket(sub, currentTime);
					if (packet == null)
						continue;

					HandlePacket(packet, currentTime);

					// HandlePacket may trigger Cleanup() which nulls _connection
					if (_connection == null || State == ClientState.Disconnected)
						return;
				}
			}

			if (_connection.HasTimedOut(currentTime))
			{
				string reason = State == ClientState.Connecting
					? "Connection attempt timed out"
					: "Connection to server timed out";

				_connection.Disconnect();
				Cleanup();
				OnDisconnected?.Invoke(reason);
				return;
			}

			var retransmissions = _connection.Channel.GetRetransmissions(currentTime);
			foreach (var rawData in retransmissions)
			{
				_transport.SendTo(rawData, _serverEndPoint);
				_connection.Bandwidth.RecordSent(rawData.Length);
			}

			if (_connection.State == ConnectionState.Connected && _connection.ShouldSendPing(currentTime))
			{
				var ping = _connection.CreatePing(currentTime);
				SendInternal(ping, false, currentTime);
			}
		}

		/// <summary>
		/// Transitions the client from <see cref="ClientState.Loading"/> to
		/// <see cref="ClientState.Playing"/>. Called by the game loop when
		/// world data has been fully received and loaded.
		/// </summary>
		public void FinishLoading()
		{
			if (State == ClientState.Loading)
			{
				State = ClientState.Playing;
			}
		}

		public void Dispose()
		{
			if (_disposed)
				return;

			_disposed = true;

			if (State != ClientState.Disconnected)
			{
				_connection?.Disconnect();
				Cleanup();
			}

			_transport.Dispose();
		}

		private void QueueReceivedData(byte[] data, IPEndPoint sender)
		{
			if (_serverEndPoint != null && sender.Equals(_serverEndPoint))
			{
				_receiveQueue.Enqueue(data);
			}
		}

		private void HandlePacket(Packet packet, float currentTime)
		{
			switch (packet)
			{
				case ConnectAcceptPacket accept:
					HandleConnectAccept(accept);
					break;

				case ConnectRejectPacket reject:
					HandleConnectReject(reject);
					break;

				case DisconnectPacket disconnect:
					_connection.Disconnect();
					Cleanup();
					OnDisconnected?.Invoke(disconnect.Reason);
					break;

				case PingPacket ping:
					var pongReply = _connection.CreatePong(ping.Timestamp);
					SendInternal(pongReply, false, currentTime);
					break;

				case PongPacket pong:
					_connection.ProcessPong(pong);
					break;

				case WorldDataPacket worldData:
					_worldReceiver.HandleWorldData(worldData);
					// Try assembly after each fragment in case complete packet arrived first
					_worldReceiver.TryAssemble();
					break;

				case WorldDataCompletePacket worldComplete:
					_worldReceiver.HandleWorldDataComplete(worldComplete);
					break;

				default:
					OnPacketReceived?.Invoke(packet);
					break;
			}
		}

		private void HandleConnectAccept(ConnectAcceptPacket accept)
		{
			if (State != ClientState.Connecting)
				return;

			_connection.PlayerId = accept.PlayerId;
			_connection.State = ConnectionState.Connected;
			LocalTick = accept.ServerTick;
			State = ClientState.Loading;

			OnConnected?.Invoke(accept);
		}

		private void HandleConnectReject(ConnectRejectPacket reject)
		{
			_connection.Disconnect();
			Cleanup();
			OnConnectionRejected?.Invoke(reject.Reason);
			OnDisconnected?.Invoke(reject.Reason);
		}

		private void HandleWorldDataReady(byte[] worldData)
		{
			OnWorldDataReady?.Invoke(worldData);
		}

		private void HandleWorldTransferFailed(string error)
		{
			OnWorldTransferFailed?.Invoke(error);
		}

		private void SendInternal(Packet packet, bool reliable, float currentTime)
		{
			byte[] raw = _connection.WrapPacket(packet, reliable, currentTime);
			_transport.SendTo(raw, _serverEndPoint);
			_connection.Bandwidth.RecordSent(raw.Length);
		}

		private void Cleanup()
		{
			State = ClientState.Disconnected;
			LocalTick = 0;

			_transport.OnDataReceived -= QueueReceivedData;
			_transport.Close();

			_worldReceiver.Reset();

			_connection = null;
			_serverEndPoint = null;

			while (_receiveQueue.TryDequeue(out _)) { }
		}
	}
}
