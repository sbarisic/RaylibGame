using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Voxelgine.Engine;

public partial class NetServer
{
	private readonly Dictionary<IPEndPoint,PendingHandshake> handshakes=new();
	private NetworkIdentity serverIdentity=new();
	private Func<PlayerIdentity,bool> isBanned = _=>false;
	private double lastHandshakeDiagnostic=double.NegativeInfinity;
	public void ConfigureIdentity(NetworkIdentity identity,Func<PlayerIdentity,bool> banned=null)
	{
		if(_running) throw new InvalidOperationException("Configure identity before starting.");
		serverIdentity.Dispose(); serverIdentity=identity ?? throw new ArgumentNullException(nameof(identity)); isBanned=banned??(_=>false);
	}
	private void ExpireHandshakes(double time)
	{
		foreach(var pair in handshakes.Where(pair=>time-pair.Value.CreatedAt>=10).ToArray()) { pair.Value.Authentication.Dispose(); handshakes.Remove(pair.Key); }
	}
	private void HandleNewConnection(byte[] data,IPEndPoint sender,double time)
	{
		if(data.Length<2 || data[0]!=ConnectionHandshake.Marker)
		{
			// Rejection only: legacy clients can read why they must upgrade. No state,
			// reliability acknowledgements, slot, or profile is created for them.
			if (data.Length > ReliableChannel.HeaderSize && data.Length <= PacketBatcher.DefaultMtu &&
				data[0] == 1 && data[ReliableChannel.HeaderSize] == (byte)PacketType.Connect && time-lastHandshakeDiagnostic>=1)
			{
				lastHandshakeDiagnostic=time;
				byte[] rejection = new ConnectRejectPacket { Reason="Incompatible protocol. Update client and server together; authenticated protocol 2 is required." }.Serialize();
				byte[] datagram = new byte[ReliableChannel.HeaderSize+rejection.Length];
				rejection.CopyTo(datagram,ReliableChannel.HeaderSize); _transport.SendTo(datagram,sender);
			}
			return;
		}
		try
		{
			if(data[1]==ConnectionHandshake.Hello)
			{
				byte[][] fields=ConnectionHandshake.Decode(data,ConnectionHandshake.Hello,4,192,256,256,32);
				if(fields[0].Length!=4 || fields[4].Length!=32) return;
				if(BitConverter.ToInt32(fields[0])!=ProtocolVersion) { RejectHandshake(sender,fields[4],"Incompatible protocol. Update client and server together."); return; }
				string name=ConnectionHandshake.ReadName(fields[1]);
				if(handshakes.TryGetValue(sender,out PendingHandshake existing))
				{
					if(existing.Hello.AsSpan().SequenceEqual(data)) _transport.SendTo(existing.Challenge,sender);
					return;
				}
				if(handshakes.Count>=256 || handshakes.Keys.Count(peer=>peer.Address.Equals(sender.Address))>=4) return;
				using ECDiffieHellman ephemeral=ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
				byte[] nonce=RandomNumberGenerator.GetBytes(32);
				byte[] unsigned=ConnectionHandshake.Encode(ConnectionHandshake.Challenge,fields[4],serverIdentity.PublicKey,ephemeral.ExportSubjectPublicKeyInfo(),nonce);
				byte[] transcript=ConnectionHandshake.Transcript(data,unsigned);
				// Validate both public keys before retaining handshake state.
				using ECDsa publicIdentity=ECDsa.Create(); publicIdentity.ImportSubjectPublicKeyInfo(fields[2],out int consumed);
				if(consumed!=fields[2].Length || publicIdentity.KeySize!=256) return;
				AuthenticatedDatagrams authentication=ConnectionHandshake.Derive(ephemeral,fields[3],transcript,true);
				byte[] challenge=ConnectionHandshake.Encode(ConnectionHandshake.Challenge,fields[4],serverIdentity.PublicKey,ephemeral.ExportSubjectPublicKeyInfo(),nonce,serverIdentity.Sign(transcript));
				handshakes.Add(sender,new(time,data,challenge,transcript,fields[2],fields[4],name,authentication));
				_transport.SendTo(challenge,sender);
			}
			else if(data[1]==ConnectionHandshake.Proof && handshakes.TryGetValue(sender,out PendingHandshake pending))
			{
				byte[][] fields=ConnectionHandshake.Decode(data,ConnectionHandshake.Proof,32,64);
				if(!fields[0].AsSpan().SequenceEqual(pending.Nonce) || !NetworkIdentity.Verify(pending.PublicKey,pending.Transcript,fields[1])) return;
				PlayerIdentity identity=PlayerIdentity.FromPublicKey(pending.PublicKey);
				string rejection=isBanned(identity)?"Banned by server":_connections.Count>=MaxPlayers?"Server is full":
					_connections.Values.Any(peer=>peer.Identity==identity)?"Identity is already connected":
					_connections.Values.Any(peer=>string.Equals(peer.PlayerName,pending.Name,StringComparison.OrdinalIgnoreCase))?"Player name is already in use":null;
				handshakes.Remove(sender);
				if(rejection!=null) { RejectHandshake(sender,pending.Nonce,rejection); pending.Authentication.Dispose(); return; }
				int playerId=AllocatePlayerId();
				NetConnection connection=new(sender,time) { Identity=identity,Authentication=pending.Authentication,PlayerId=playerId,PlayerName=pending.Name,State=ConnectionState.Connected };
				_connections.Add(sender,connection); _playerConnections.Add(playerId,connection); _usedPlayerIds.Add(playerId);
				QueuePacket(connection,new ConnectAcceptPacket { PlayerId=playerId,ServerTick=ServerTick,WorldSeed=WorldSeed },true,time);
				OnClientConnected?.Invoke(connection);
			}
		}
		catch(Exception exception) when(exception is CryptographicException or IOException or ArgumentException)
		{
			if(time-lastHandshakeDiagnostic>=1) { lastHandshakeDiagnostic=time; _logging?.Log(DI.GameLogLevel.Warning,"Network","Rejected malformed handshake."); }
		}
	}
	private void RejectHandshake(IPEndPoint endpoint,byte[] nonce,string reason) => _transport.SendTo(ConnectionHandshake.Encode(ConnectionHandshake.Rejected,nonce,Encoding.UTF8.GetBytes(reason)),endpoint);
	private sealed record PendingHandshake(double CreatedAt,byte[] Hello,byte[] Challenge,byte[] Transcript,byte[] PublicKey,byte[] Nonce,string Name,AuthenticatedDatagrams Authentication);
}
