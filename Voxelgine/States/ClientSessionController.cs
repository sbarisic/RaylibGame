using Voxelgine.Engine;
using Voxelgine.Engine.DI;

namespace Voxelgine.States;

/// <summary>
/// Owns the transport-facing multiplayer client and routes decoded session events
/// to the active gameplay state. Disposing the controller removes every route
/// before releasing the socket.
/// </summary>
internal sealed class ClientSessionController : IDisposable
{
	private readonly Action<ConnectAcceptPacket> connected;
	private readonly Action<string> disconnected;
	private readonly Action<string> rejected;
	private readonly Action<Packet> packetReceived;
	private bool disposed;

	internal ClientSessionController(
		IFishLogging logging,
		Action<ConnectAcceptPacket> connected,
		Action<string> disconnected,
		Action<string> rejected,
		Action<Packet> packetReceived)
	{
		this.connected = connected ?? throw new ArgumentNullException(nameof(connected));
		this.disconnected = disconnected ?? throw new ArgumentNullException(nameof(disconnected));
		this.rejected = rejected ?? throw new ArgumentNullException(nameof(rejected));
		this.packetReceived = packetReceived ?? throw new ArgumentNullException(nameof(packetReceived));

		Client = new NetClient(logging ?? throw new ArgumentNullException(nameof(logging)));
		Client.OnConnected += connected;
		Client.OnDisconnected += disconnected;
		Client.OnConnectionRejected += rejected;
		Client.OnPacketReceived += packetReceived;
	}

	internal NetClient Client { get; }

	internal void Connect(string host, int port, string playerName, float time)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		Client.Connect(host, port, playerName, time);
	}

	public void Dispose()
	{
		if (disposed)
			return;
		disposed = true;

		Client.OnConnected -= connected;
		Client.OnDisconnected -= disconnected;
		Client.OnConnectionRejected -= rejected;
		Client.OnPacketReceived -= packetReceived;
		Client.Dispose();
	}
}
