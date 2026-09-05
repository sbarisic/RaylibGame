using System.Diagnostics;
using System.Net;
using System.Reflection;
using Voxelgine.Engine;

namespace VoxelgineEngine.Tests;

public sealed class SecureTransportIntegrationTests
{
	[Fact]
	public void ValidProofCannotBeReplayedIntoANewChallenge()
	{
		using NetServer server=new();using NetworkIdentity identity=new();using ClientHandshake first=new(identity,"Player");
		IPEndPoint peer=new(IPAddress.Loopback,12345);MethodInfo receive=typeof(NetServer).GetMethod("HandleNewConnection",BindingFlags.Instance|BindingFlags.NonPublic)!;
		void Send(byte[] data,double time)=>receive.Invoke(server,new object[]{data,peer,time});
		Send(first.Hello,0);
		var pending=(System.Collections.IDictionary)typeof(NetServer).GetField("handshakes",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(server)!;
		object state=pending[peer]!;byte[] challenge=(byte[])state.GetType().GetProperty("Challenge")!.GetValue(state)!;
		using AuthenticatedDatagrams authentication=first.AcceptChallenge(challenge,new(),"server:1");
		Send(first.Outgoing,.1);Assert.Single(server.GetConnections());server.Kick(server.GetConnections().Single().PlayerId,"test",.2);
		using ClientHandshake second=new(identity,"Player");Send(second.Hello,.3);Send(first.Outgoing,.4);
		Assert.Empty(server.GetConnections());Assert.Equal(1,server.PendingHandshakeCount);
	}

	[Fact]
	public void AuthenticatedFragmentationStaysInsideMtu()
	{
		using UdpTransport receiver=new(), sender=new();receiver.Bind(0);sender.Open();
		System.Collections.Concurrent.ConcurrentQueue<byte[]> received=new();receiver.OnDataReceived+=(data,_)=>received.Enqueue(data);
		NetConnection connection=new(new(IPAddress.Loopback,receiver.LocalEndPoint.Port),0) { Authentication=new(new byte[32],new byte[64],false) };
		Assert.True(connection.QueuePacket(new ChatMessagePacket{Message=new string('x',10000)},true,0));connection.FlushOutgoing(sender,0);
		Assert.True(SpinWait.SpinUntil(()=>received.Count>=9,2000));Assert.All(received,data=>Assert.InRange(data.Length,AuthenticatedDatagrams.Overhead+1,PacketBatcher.DefaultMtu));connection.Reset();
	}

	[Fact]
	public void LoopbackStillRequiresProofAndRejectsDuplicateNamesIdentitiesAndBans()
	{
		string root = Path.Combine(Path.GetTempPath(), "voxel-wire-" + Guid.NewGuid()); Directory.CreateDirectory(root);
		try
		{
			using NetServer server = new(); bool banned = false; server.ConfigureIdentity(new NetworkIdentity(), _ => banned); server.Start(0);
			using NetClient first = new(); first.ConfigureIdentity(NetworkIdentity.LoadOrCreate(Path.Combine(root, "identity.pk8")), new());
			first.Connect("127.0.0.1", server.Port, "First", 0); Pump(server, first, () => first.State == ClientState.Loading);
			Assert.Single(server.GetConnections()); Assert.Equal(first.Identity, server.GetConnections().Single().Identity);
			using NetClient duplicateIdentity = new(); duplicateIdentity.ConfigureIdentity(NetworkIdentity.LoadOrCreate(Path.Combine(root, "identity.pk8")), new());
			string rejection = null; duplicateIdentity.OnConnectionRejected += reason => rejection = reason;
			duplicateIdentity.Connect("127.0.0.1", server.Port, "ChangedName", 0); Pump(server, duplicateIdentity, () => rejection != null);
			Assert.Contains("Identity", rejection); Assert.Single(server.GetConnections());
			using NetClient duplicateName = new(); rejection = null; duplicateName.OnConnectionRejected += reason => rejection = reason;
			duplicateName.Connect("127.0.0.1", server.Port, "FIRST", 0); Pump(server, duplicateName, () => rejection != null); Assert.Contains("name", rejection);
			using NetClient denied = new(); banned = true; rejection = null; denied.OnConnectionRejected += reason => rejection = reason;
			denied.Connect("127.0.0.1", server.Port, "Other", 0); Pump(server, denied, () => rejection != null); Assert.Contains("Banned", rejection);
		}
		finally { Directory.Delete(root, true); }
	}

	[Fact]
	public void HandshakesBoundPerIpAndGloballyAndExpireWithoutAllocatingPlayers()
	{
		using NetServer server = new(); using NetworkIdentity identity = new(); using ClientHandshake client = new(identity, "Player");
		MethodInfo receive = typeof(NetServer).GetMethod("HandleNewConnection", BindingFlags.Instance | BindingFlags.NonPublic)!;
		for (int ip = 1; ip <= 70; ip++)
			for (int port = 1; port <= 5; port++) receive.Invoke(server, new object[] { client.Hello, new IPEndPoint(IPAddress.Parse($"10.0.0.{ip}"), port), 0d });
		Assert.Equal(256, server.PendingHandshakeCount); Assert.Empty(server.GetConnections());
		byte[] invalid = ConnectionHandshake.Encode(ConnectionHandshake.Proof, client.Nonce, new byte[64]);
		receive.Invoke(server, new object[] { invalid, new IPEndPoint(IPAddress.Parse("10.0.0.1"), 1), 1d }); Assert.Empty(server.GetConnections());
		typeof(NetServer).GetMethod("ExpireHandshakes", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(server, new object[] { 10d });
		Assert.Equal(0, server.PendingHandshakeCount);
		receive.Invoke(server, new object[] { invalid, new IPEndPoint(IPAddress.Parse("10.0.0.1"), 1), 11d }); Assert.Empty(server.GetConnections());
	}

	[Fact]
	public void OldUnauthenticatedConnectCannotAllocateSlot()
	{
		using NetServer server = new();
		typeof(NetServer).GetMethod("HandleNewConnection", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(server,
			new object[] { new ConnectPacket { PlayerName = "Legacy", ProtocolVersion = 1 }.Serialize(), new IPEndPoint(IPAddress.Loopback, 9), 0d });
		Assert.Empty(server.GetConnections()); Assert.Equal(0, server.PendingHandshakeCount);
	}

	private static void Pump(NetServer server, NetClient client, Func<bool> complete)
	{
		Stopwatch timer = Stopwatch.StartNew();
		while (!complete() && timer.Elapsed.TotalSeconds < 3)
		{
			server.Tick(timer.Elapsed.TotalSeconds); client.Tick(timer.Elapsed.TotalSeconds); Thread.Sleep(1);
		}
		Assert.True(complete(), "UDP handshake did not reach the expected state.");
	}
}
