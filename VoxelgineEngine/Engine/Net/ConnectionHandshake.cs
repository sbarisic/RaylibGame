using System.Security.Cryptography;
using System.Text;

namespace Voxelgine.Engine;

internal static class ConnectionHandshake
{
	internal const byte Marker = 0xFA, Hello = 1, Challenge = 2, Proof = 3, Rejected = 4;
	internal static byte[] Encode(byte kind, params byte[][] fields)
	{
		using MemoryStream stream = new(); using BinaryWriter writer = new(stream);
		writer.Write(Marker); writer.Write(kind);
		foreach(byte[] field in fields) { writer.Write(checked((ushort)field.Length)); writer.Write(field); }
		return stream.ToArray();
	}
	internal static byte[][] Decode(byte[] data, byte kind, params int[] limits)
	{
		if(data.Length > PacketBatcher.DefaultMtu || data.Length < 2 || data[0]!=Marker || data[1]!=kind) throw new InvalidDataException("Invalid handshake.");
		using BinaryReader reader=new(new MemoryStream(data,2,data.Length-2,false));
		byte[][] fields=new byte[limits.Length][];
		for(int i=0;i<fields.Length;i++) { int length=reader.ReadUInt16(); if(length>limits[i]) throw new InvalidDataException("Handshake field exceeds limit."); fields[i]=reader.ReadBytes(length); if(fields[i].Length!=length) throw new EndOfStreamException(); }
		if(reader.BaseStream.Position!=reader.BaseStream.Length) throw new InvalidDataException("Trailing handshake bytes.");
		return fields;
	}
	internal static string ReadName(byte[] data)
	{
		string name = new UTF8Encoding(false,true).GetString(data).Trim();
		if(name.Length is < 1 or > 48 || name.Any(char.IsControl)) throw new InvalidDataException("Player name must contain 1-48 printable characters.");
		return name;
	}
	internal static byte[] Transcript(byte[] hello, byte[] challenge) => Encode(0, "Voxelgine handshake v2"u8.ToArray(), hello, challenge);
	internal static AuthenticatedDatagrams Derive(ECDiffieHellman key, byte[] remotePublicKey, byte[] transcript, bool server)
	{
		using ECDiffieHellman remote=ECDiffieHellman.Create();
		remote.ImportSubjectPublicKeyInfo(remotePublicKey,out int read);
		if(read!=remotePublicKey.Length || remote.KeySize!=256 || remote.ExportParameters(false).Curve.Oid.Value!=ECCurve.NamedCurves.nistP256.Oid.Value) throw new CryptographicException("Invalid ephemeral key.");
		byte[] secret=key.DeriveRawSecretAgreement(remote.PublicKey);
		try { return new(secret,transcript,server); } finally { CryptographicOperations.ZeroMemory(secret); }
	}
}

internal sealed class ClientHandshake : IDisposable
{
	private readonly NetworkIdentity identity;
	private readonly ECDiffieHellman ephemeral=ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
	internal readonly byte[] Nonce=RandomNumberGenerator.GetBytes(32);
	internal byte[] Hello { get; }
	internal byte[] Outgoing { get; private set; }
	internal PlayerIdentity ServerIdentity { get; private set; }
	private byte[] challenge;
	internal ClientHandshake(NetworkIdentity identity,string name)
	{
		this.identity=identity;
		byte[] nameBytes=Encoding.UTF8.GetBytes(name); _=ConnectionHandshake.ReadName(nameBytes);
		Outgoing=Hello=ConnectionHandshake.Encode(ConnectionHandshake.Hello, BitConverter.GetBytes(NetServer.ProtocolVersion), nameBytes, identity.PublicKey, ephemeral.ExportSubjectPublicKeyInfo(), Nonce);
	}
	internal AuthenticatedDatagrams AcceptChallenge(byte[] data,ServerTrustStore trust,string endpoint)
	{
		if(challenge!=null) { if(!challenge.AsSpan().SequenceEqual(data)) throw new InvalidDataException("Handshake challenge changed."); return null; }
		byte[][] fields=ConnectionHandshake.Decode(data,ConnectionHandshake.Challenge,32,256,256,32,64);
		if(!fields[0].AsSpan().SequenceEqual(Nonce) || fields[3].Length!=32) throw new InvalidDataException("Invalid challenge nonce.");
		byte[] unsigned=ConnectionHandshake.Encode(ConnectionHandshake.Challenge,fields[..4]);
		byte[] transcript=ConnectionHandshake.Transcript(Hello,unsigned);
		if(!NetworkIdentity.Verify(fields[1],transcript,fields[4])) throw new CryptographicException("Invalid server proof.");
		ServerIdentity=PlayerIdentity.FromPublicKey(fields[1]);
		if(!trust.Matches(endpoint,ServerIdentity)) throw new InvalidOperationException("Server identity changed. Use --reset-server-trust <address:port> before reconnecting only if the change is expected.");
		AuthenticatedDatagrams authentication=ConnectionHandshake.Derive(ephemeral,fields[2],transcript,false);
		Outgoing=ConnectionHandshake.Encode(ConnectionHandshake.Proof,Nonce,identity.Sign(transcript));
		challenge=data;
		return authentication;
	}
	public void Dispose()=>ephemeral.Dispose();
}
