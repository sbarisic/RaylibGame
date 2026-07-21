using System;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Voxelgine.Engine.DI;

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
		private readonly IFishLogging _logging;
		private int _callbackThreadId;
		private int _receiveLoopFailed;

		public UdpTransport(IFishLogging logging = null)
		{
			_logging = logging;
		}

		/// <summary>
		/// Fired when data is received from a remote endpoint.
		/// Parameters: received byte data, sender endpoint.
		/// Called on the receive task thread — handlers must be thread-safe.
		/// </summary>
		public event Action<byte[], IPEndPoint> OnDataReceived;

		/// <summary>
		/// Whether the transport is currently active and listening.
		/// </summary>
		public bool IsActive => _udpClient != null
			&& !_disposed
			&& Volatile.Read(ref _receiveLoopFailed) == 0
			&& _receiveTask?.IsFaulted != true;

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
			ObjectDisposedException.ThrowIf(_disposed, this);
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
			ObjectDisposedException.ThrowIf(_disposed, this);
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
			if (_udpClient == null)
				return;

			_cts?.Cancel();
			_udpClient?.Close();
			_udpClient?.Dispose();
			_udpClient = null;

			Task receiveTask = _receiveTask;
			try
			{
				if (receiveTask != null
					&& Environment.CurrentManagedThreadId != Volatile.Read(ref _callbackThreadId))
				{
					receiveTask.Wait(TimeSpan.FromSeconds(1));
				}
			}
			catch (AggregateException exception)
			{
				Exception failure = exception.Flatten().InnerExceptions.FirstOrDefault(
					static inner => inner is not OperationCanceledException
						and not ObjectDisposedException
						and not SocketException
				);
				if (failure != null)
					_logging?.Log(GameLogLevel.Error, "Transport", "UDP receive loop failed during shutdown.", failure);
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
			if (_disposed)
				return;
			Close();
			_disposed = true;
		}

		private void StartReceiveLoop()
		{
			Volatile.Write(ref _receiveLoopFailed, 0);
			_cts = new CancellationTokenSource();
			_receiveTask = Task.Run(() => ReceiveLoopAsync(_cts.Token));
		}

		private async Task ReceiveLoopAsync(CancellationToken ct)
		{
			while (!ct.IsCancellationRequested)
			{
				try
				{
					UdpClient client = _udpClient;
					if (client == null)
						break;
					UdpReceiveResult result = await client.ReceiveAsync(ct);
					try
					{
						Volatile.Write(ref _callbackThreadId, Environment.CurrentManagedThreadId);
						OnDataReceived?.Invoke(result.Buffer, result.RemoteEndPoint);
					}
					catch (Exception exception)
					{
						_logging?.Log(GameLogLevel.Error, "Transport", "UDP receive handler rejected a datagram.", exception);
					}
					finally
					{
						Volatile.Write(ref _callbackThreadId, 0);
					}
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
				catch (Exception exception)
				{
					Volatile.Write(ref _receiveLoopFailed, 1);
					_logging?.Log(GameLogLevel.Error, "Transport", "UDP receive loop terminated unexpectedly.", exception);
					break;
				}
			}
		}
	}
}
