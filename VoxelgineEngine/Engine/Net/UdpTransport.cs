using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Low-level UDP transport layer wrapping <see cref="UdpClient"/>.
	/// Supports server mode (bind to port, receive from any endpoint) and
	/// client mode (open on ephemeral port to communicate with a server).
	/// Thread-safe for concurrent send operations.
	/// </summary>
	public class UdpTransport : IDisposable
	{
		private UdpClient _udpClient;
		private CancellationTokenSource _cts;
		private Task _receiveTask;
		private bool _disposed;
		private readonly object _sendLock = new object();

		/// <summary>
		/// Fired when data is received from a remote endpoint.
		/// Parameters: received byte data, sender endpoint.
		/// Called on the receive task thread — handlers must be thread-safe.
		/// </summary>
		public event Action<byte[], IPEndPoint> OnDataReceived;

		/// <summary>
		/// Whether the transport is currently active and listening.
		/// </summary>
		public bool IsActive => _udpClient != null && !_disposed;

		/// <summary>
		/// The local endpoint this transport is bound to, or null if not active.
		/// </summary>
		public IPEndPoint LocalEndPoint => _udpClient?.Client?.LocalEndPoint as IPEndPoint;

		/// <summary>
		/// Binds to the specified port and starts listening for incoming datagrams.
		/// Used by the server to accept data from any remote endpoint.
		/// </summary>
		/// <param name="port">The UDP port to bind to.</param>
		public void Bind(int port)
		{
			if (_udpClient != null)
				throw new InvalidOperationException("Transport is already active.");

			_udpClient = new UdpClient(port);
			StartReceiveLoop();
		}

		/// <summary>
		/// Opens the transport on an ephemeral port for sending and receiving.
		/// Used by the client to communicate with a server.
		/// </summary>
		public void Open()
		{
			if (_udpClient != null)
				throw new InvalidOperationException("Transport is already active.");

			_udpClient = new UdpClient(0);
			StartReceiveLoop();
		}

		/// <summary>
		/// Sends data to a specific remote endpoint.
		/// Thread-safe — can be called from any thread.
		/// </summary>
		/// <param name="data">The byte array to send.</param>
		/// <param name="target">The destination endpoint.</param>
		public void SendTo(byte[] data, IPEndPoint target)
		{
			if (_disposed || _udpClient == null)
				return;

			try
			{
				lock (_sendLock)
				{
					_udpClient.Send(data, data.Length, target);
				}
			}
			catch (SocketException)
			{
				// Send failures are expected under poor network conditions.
				// Reliability is handled at a higher layer.
			}
			catch (ObjectDisposedException)
			{
				// Transport was closed between the null check and the send call.
			}
		}

		/// <summary>
		/// Stops listening and releases the underlying socket.
		/// Safe to call multiple times.
		/// </summary>
		public void Close()
		{
			if (_disposed)
				return;

			_disposed = true;

			_cts?.Cancel();
			_udpClient?.Close();
			_udpClient?.Dispose();
			_udpClient = null;

			try
			{
				_receiveTask?.Wait(TimeSpan.FromSeconds(1));
			}
			catch (AggregateException)
			{
				// Expected when the receive loop is cancelled during shutdown.
			}

			_cts?.Dispose();
			_cts = null;
			_receiveTask = null;
		}

		/// <summary>
		/// Disposes the transport, stopping all activity.
		/// </summary>
		public void Dispose()
		{
			Close();
		}

		private void StartReceiveLoop()
		{
			_cts = new CancellationTokenSource();
			_receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
		}

		private async Task ReceiveLoopAsync(CancellationToken ct)
		{
			while (!ct.IsCancellationRequested)
			{
				try
				{
					UdpReceiveResult result = await _udpClient.ReceiveAsync(ct);
					OnDataReceived?.Invoke(result.Buffer, result.RemoteEndPoint);
				}
				catch (OperationCanceledException)
				{
					break;
				}
				catch (SocketException)
				{
					// ICMP unreachable or socket closing — continue unless cancelled.
					if (ct.IsCancellationRequested)
						break;
				}
				catch (ObjectDisposedException)
				{
					break;
				}
			}
		}
	}
}
