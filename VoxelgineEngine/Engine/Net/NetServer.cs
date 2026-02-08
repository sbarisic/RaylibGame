using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Voxelgine.Engine.DI;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Server-side network manager. Binds a UDP port, accepts client connections
	/// (validates protocol version, assigns player IDs, rejects if full), manages
	/// connected players, processes incoming packets, and provides methods to send
	/// packets to individual clients or broadcast to all.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Incoming UDP datagrams are queued from the transport's background thread and
	/// processed on the game thread during <see cref="Tick"/>. This ensures all packet
	/// handling and event callbacks occur on the game thread.
	/// </para>
	/// <para>
	/// System packets (<see cref="ConnectPacket"/>, <see cref="DisconnectPacket"/>,
	/// <see cref="PingPacket"/>, <see cref="PongPacket"/>) are handled internally.
	/// All other packets are forwarded via the <see cref="OnPacketReceived"/> event
	/// for the game loop to process.
	/// </para>
	/// </remarks>
	public class NetServer : IDisposable
	{
		/// <summary>
		/// Protocol version for client-server compatibility validation.
		/// Increment when packet formats change in an incompatible way.
		/// </summary>
		public const int ProtocolVersion = 1;

		/// <summary>
		/// Maximum number of concurrent player connections.
		/// </summary>
		public const int MaxPlayers = 10;

		private readonly UdpTransport _transport;
		private readonly Dictionary<IPEndPoint, NetConnection> _connections = new();
		private readonly Dictionary<int, NetConnection> _playerConnections = new();
		private readonly ConcurrentQueue<(byte[] Data, IPEndPoint Sender)> _receiveQueue = new();
		private readonly HashSet<int> _usedPlayerIds = new();
		private readonly IFishLogging _logging;

		private int _port;
		private bool _running;
		private bool _disposed;

		/// <summary>
		/// Fired when a client successfully connects (after protocol validation and ID assignment).
		/// The connection's <see cref="NetConnection.PlayerId"/> and <see cref="NetConnection.PlayerName"/>
		/// are set before this event fires. Called on the game thread during <see cref="Tick"/>.
		/// </summary>
		public event Action<NetConnection> OnClientConnected;

		/// <summary>
		/// Fired when a client disconnects (explicit disconnect or timeout).
		/// The reason string describes why the disconnect occurred.
		/// Called on the game thread during <see cref="Tick"/>.
		/// </summary>
		public event Action<NetConnection, string> OnClientDisconnected;

		/// <summary>
		/// Fired for each non-system packet received from a connected client.
		/// System packets (Connect, Disconnect, Ping, Pong) are handled internally.
		/// Called on the game thread during <see cref="Tick"/>.
		/// </summary>
		public event Action<NetConnection, Packet> OnPacketReceived;

		/// <summary>
		/// Whether the server is currently running and accepting connections.
		/// </summary>
		public bool IsRunning => _running;

		/// <summary>
		/// The number of currently connected clients.
		/// </summary>
		public int ConnectionCount => _connections.Count;

		/// <summary>
		/// The UDP port the server is bound to.
		/// </summary>
		public int Port => _port;

		/// <summary>
		/// The current server tick, incremented each <see cref="Tick"/> call.
		/// Used for snapshot sequencing and client synchronization.
		/// </summary>
		public int ServerTick { get; private set; }

		/// <summary>
		/// World generation seed sent to connecting clients in <see cref="ConnectAcceptPacket"/>.
		/// Set this before calling <see cref="Start"/> so clients receive the correct seed.
		/// </summary>
		public int WorldSeed { get; set; }

		public NetServer(IFishLogging logging = null)
		{
			_transport = new UdpTransport();
			_logging = logging;
		}

		/// <summary>
		/// Starts the server, binding to the specified UDP port and beginning to accept connections.
		/// </summary>
		/// <param name="port">The UDP port to listen on.</param>
		public void Start(int port)
		{
			if (_running)
				throw new InvalidOperationException("Server is already running.");

			_port = port;
			_transport.OnDataReceived += QueueReceivedData;
			_transport.Bind(port);
			_running = true;
			_logging?.ServerNetworkWriteLine($"Server started on port {port}");
		}

		/// <summary>
		/// Stops the server, disconnecting all clients and releasing the UDP port.
		/// </summary>
		/// <param name="currentTime">Current time in seconds for packet wrapping.</param>
		public void Stop(float currentTime)
		{
			if (!_running)
				return;

			_logging?.ServerNetworkWriteLine("Server stopping...");
			_running = false;

			foreach (var connection in _connections.Values.ToArray())
			{
				DisconnectClient(connection, "Server shutting down", currentTime);
			}

			_transport.OnDataReceived -= QueueReceivedData;
			_transport.Close();

			_connections.Clear();
			_playerConnections.Clear();
			_usedPlayerIds.Clear();

			while (_receiveQueue.TryDequeue(out _)) { }
		}

		/// <summary>
		/// Processes all queued incoming packets, handles retransmissions, ping/pong,
		/// and timeout detection. Must be called once per server tick on the game thread.
		/// </summary>
		/// <param name="currentTime">Current time in seconds.</param>
		public void Tick(float currentTime)
		{
			if (!_running)
				return;

			while (_receiveQueue.TryDequeue(out var item))
			{
				ProcessDatagram(item.Data, item.Sender, currentTime);
			}

			foreach (var connection in _connections.Values.ToArray())
			{
				if (connection.State == ConnectionState.Disconnected)
					continue;

				if (connection.HasTimedOut(currentTime))
				{
					_logging?.ServerNetworkWriteLine($"Client [{connection.PlayerId}] \"{connection.PlayerName}\" timed out");
					DisconnectClient(connection, "Connection timed out", currentTime);
					continue;
				}

				var retransmissions = connection.Channel.GetRetransmissions(currentTime);
				foreach (var rawData in retransmissions)
				{
					connection.QueueSend(rawData, true);
				}

				connection.CleanupStaleFragments(currentTime);

				if (connection.State == ConnectionState.Connected && connection.ShouldSendPing(currentTime))
				{
					var ping = connection.CreatePing(currentTime);
					QueuePacket(connection, ping, false, currentTime);
				}
			}

			FlushAllConnections(currentTime);
			ServerTick++;
		}

		/// <summary>
		/// Sends a packet to a specific connected player.
		/// </summary>
		/// <param name="playerId">The target player's ID.</param>
		/// <param name="packet">The packet to send.</param>
		/// <param name="reliable">Whether the packet requires reliable delivery.</param>
		/// <param name="currentTime">Current time in seconds.</param>
		public void SendTo(int playerId, Packet packet, bool reliable, float currentTime)
		{
			if (_playerConnections.TryGetValue(playerId, out var connection))
			{
				QueuePacket(connection, packet, reliable, currentTime);
			}
		}

		/// <summary>
		/// Sends a packet to all connected clients.
		/// </summary>
		/// <param name="packet">The packet to send.</param>
		/// <param name="reliable">Whether the packet requires reliable delivery.</param>
		/// <param name="currentTime">Current time in seconds.</param>
		public void Broadcast(Packet packet, bool reliable, float currentTime)
		{
			foreach (var connection in _connections.Values)
			{
				if (connection.State == ConnectionState.Connected)
				{
					QueuePacket(connection, packet, reliable, currentTime);
				}
			}
		}

		/// <summary>
		/// Sends a packet to all connected clients except the specified player.
		/// </summary>
		/// <param name="excludePlayerId">The player ID to exclude from the broadcast.</param>
		/// <param name="packet">The packet to send.</param>
		/// <param name="reliable">Whether the packet requires reliable delivery.</param>
		/// <param name="currentTime">Current time in seconds.</param>
		public void BroadcastExcept(int excludePlayerId, Packet packet, bool reliable, float currentTime)
		{
			foreach (var connection in _connections.Values)
			{
				if (connection.State == ConnectionState.Connected && connection.PlayerId != excludePlayerId)
				{
					QueuePacket(connection, packet, reliable, currentTime);
				}
			}
		}

		/// <summary>
		/// Returns the connection for the specified player ID, or null if not found.
		/// </summary>
		public NetConnection GetConnection(int playerId)
		{
			_playerConnections.TryGetValue(playerId, out var connection);
			return connection;
		}

		/// <summary>
		/// Returns all active connections. The returned collection should not be modified.
		/// </summary>
		public IReadOnlyCollection<NetConnection> GetConnections()
		{
			return _connections.Values;
		}

		/// <summary>
		/// Disconnects a specific client with a reason message.
		/// Sends a <see cref="DisconnectPacket"/> before removing the connection.
		/// </summary>
		/// <param name="playerId">The player ID to disconnect.</param>
		/// <param name="reason">The reason for disconnection.</param>
		/// <param name="currentTime">Current time in seconds.</param>
		public void Kick(int playerId, string reason, float currentTime)
		{
			if (_playerConnections.TryGetValue(playerId, out var connection))
			{
				DisconnectClient(connection, reason, currentTime);
			}
		}

		public void Dispose()
		{
			if (_disposed)
				return;

			_disposed = true;
			Stop(0f);
			_transport.Dispose();
		}

		private void QueueReceivedData(byte[] data, IPEndPoint sender)
		{
			_receiveQueue.Enqueue((data, sender));
		}

		private void ProcessDatagram(byte[] data, IPEndPoint sender, float currentTime)
		{
			if (_connections.TryGetValue(sender, out var connection))
			{
				connection.Bandwidth.RecordReceived(data.Length);

				var subPackets = PacketBatcher.UnbatchDatagram(data);
				foreach (var sub in subPackets)
				{
					Packet packet = connection.UnwrapPacket(sub, currentTime);
					if (packet == null)
						continue;

					HandlePacket(connection, packet, currentTime);

					if (connection.State == ConnectionState.Disconnected)
						break;
				}
			}
			else
			{
				HandleNewConnection(data, sender, currentTime);
			}
		}

		private void HandleNewConnection(byte[] data, IPEndPoint sender, float currentTime)
		{
			var tempConnection = new NetConnection(sender, currentTime);
			Packet packet = tempConnection.UnwrapPacket(data, currentTime);

			if (packet is not ConnectPacket connect)
				return;

			if (connect.ProtocolVersion != ProtocolVersion)
			{
				_logging?.ServerNetworkWriteLine($"Rejected connection from {sender}: protocol version mismatch (server: {ProtocolVersion}, client: {connect.ProtocolVersion})");
				var reject = new ConnectRejectPacket
				{
					Reason = $"Protocol version mismatch (server: {ProtocolVersion}, client: {connect.ProtocolVersion})"
				};
				byte[] raw = tempConnection.WrapPacket(reject, true, currentTime);
				_transport.SendTo(raw, sender);
				return;
			}

			if (_connections.Count >= MaxPlayers)
			{
				_logging?.ServerNetworkWriteLine($"Rejected connection from {sender}: server is full ({_connections.Count}/{MaxPlayers})");
				var reject = new ConnectRejectPacket
				{
					Reason = "Server is full"
				};
				byte[] raw = tempConnection.WrapPacket(reject, true, currentTime);
				_transport.SendTo(raw, sender);
				return;
			}

			int playerId = AllocatePlayerId();
			if (playerId < 0)
			{
				var reject = new ConnectRejectPacket
				{
					Reason = "No available player slots"
				};
				byte[] raw = tempConnection.WrapPacket(reject, true, currentTime);
				_transport.SendTo(raw, sender);
				return;
			}

			tempConnection.PlayerId = playerId;
			tempConnection.PlayerName = connect.PlayerName;
			tempConnection.State = ConnectionState.Connected;

			_connections[sender] = tempConnection;
			_playerConnections[playerId] = tempConnection;
			_usedPlayerIds.Add(playerId);

			_logging?.ServerNetworkWriteLine($"Accepted connection from {sender}: [{playerId}] \"{connect.PlayerName}\"");

			var accept = new ConnectAcceptPacket
				{
					PlayerId = playerId,
					ServerTick = ServerTick,
					WorldSeed = WorldSeed,
				};
			QueuePacket(tempConnection, accept, true, currentTime);

			OnClientConnected?.Invoke(tempConnection);
		}

		private void HandlePacket(NetConnection connection, Packet packet, float currentTime)
		{
			switch (packet)
			{
				case DisconnectPacket disconnect:
					DisconnectClient(connection, disconnect.Reason, currentTime);
					break;

				case PingPacket ping:
						var pongReply = connection.CreatePong(ping.Timestamp);
						QueuePacket(connection, pongReply, false, currentTime);
						break;

					case PongPacket pong:
					connection.ProcessPong(pong);
					break;

				default:
					OnPacketReceived?.Invoke(connection, packet);
					break;
			}
		}

		private void DisconnectClient(NetConnection connection, string reason, float currentTime)
		{
			if (connection.State == ConnectionState.Disconnected)
				return;

			_logging?.ServerNetworkWriteLine($"Disconnecting [{connection.PlayerId}] \"{connection.PlayerName}\": {reason}");

			var disconnectPacket = new DisconnectPacket { Reason = reason };
			SendDirect(connection, disconnectPacket, true, currentTime);

			connection.Disconnect();

			if (connection.PlayerId >= 0)
			{
				_usedPlayerIds.Remove(connection.PlayerId);
				_playerConnections.Remove(connection.PlayerId);
			}

			_connections.Remove(connection.RemoteEndPoint);

			OnClientDisconnected?.Invoke(connection, reason);
		}

		private int AllocatePlayerId()
		{
			for (int i = 0; i < MaxPlayers; i++)
			{
				if (!_usedPlayerIds.Contains(i))
					return i;
			}

			return -1;
		}

		/// <summary>
		/// Sends a packet immediately without batching. Used for disconnect packets
		/// and other cases where the connection may be removed before the next flush.
		/// </summary>
		private void SendDirect(NetConnection connection, Packet packet, bool reliable, float currentTime)
		{
			connection.SendImmediate(packet, reliable, currentTime, _transport);
		}

		/// <summary>
		/// Queues a packet for batched sending at the next <see cref="FlushAllConnections"/> call.
		/// </summary>
		private void QueuePacket(NetConnection connection, Packet packet, bool reliable, float currentTime)
		{
			connection.QueuePacket(packet, reliable, currentTime);
		}

		/// <summary>
		/// Flushes all queued outgoing packets for every connection as batched datagrams.
		/// Called at the end of each <see cref="Tick"/> to combine small packets into
		/// MTU-sized datagrams, reducing per-packet UDP/IP overhead.
		/// </summary>
		private void FlushAllConnections(float currentTime)
		{
			foreach (var connection in _connections.Values)
			{
				if (connection.State != ConnectionState.Disconnected)
				{
					connection.FlushOutgoing(_transport, currentTime);
				}
			}
		}
	}
}
