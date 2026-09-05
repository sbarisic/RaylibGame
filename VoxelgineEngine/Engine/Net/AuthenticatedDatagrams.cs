using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Voxelgine.Engine;

/// <summary>Directional MACs authenticate the entire datagram before ACK/reliability processing.</summary>
public sealed class AuthenticatedDatagrams : IDisposable
{
	public const byte Marker = 0xFB;
	public const int Overhead = 41;
	private readonly byte[] sendKey, receiveKey, sessionId;
	private readonly ulong[] received = new ulong[1024];
	private ulong nextSend, highestReceived;
	public AuthenticatedDatagrams(byte[] sharedSecret, byte[] transcript, bool server)
	{
		byte[] hash = SHA256.HashData(transcript);
		sessionId = hash[..16];
		byte[] material = HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, 64, hash, "Voxelgine authenticated UDP v2"u8.ToArray());
		sendKey = server ? material[..32] : material[32..];
		receiveKey = server ? material[32..] : material[..32];
		CryptographicOperations.ZeroMemory(material);
	}
	public byte[] Protect(byte[] payload)
	{
		byte[] output = new byte[payload.Length + Overhead];
		output[0] = Marker; sessionId.CopyTo(output, 1);
		BinaryPrimitives.WriteUInt64LittleEndian(output.AsSpan(17), checked(++nextSend));
		payload.CopyTo(output, 25);
		Span<byte> tag = stackalloc byte[32];
		HMACSHA256.HashData(sendKey, output.AsSpan(0, output.Length - 16), tag);
		tag[..16].CopyTo(output.AsSpan(output.Length - 16));
		return output;
	}
	public bool TryUnprotect(byte[] datagram, out byte[] payload)
	{
		payload = null;
		if (datagram.Length <= Overhead || datagram[0] != Marker || !datagram.AsSpan(1,16).SequenceEqual(sessionId)) return false;
		Span<byte> tag = stackalloc byte[32];
		HMACSHA256.HashData(receiveKey, datagram.AsSpan(0,datagram.Length-16),tag);
		if (!CryptographicOperations.FixedTimeEquals(tag[..16],datagram.AsSpan(datagram.Length-16))) return false;
		ulong sequence = BinaryPrimitives.ReadUInt64LittleEndian(datagram.AsSpan(17));
		if (sequence == 0 || (sequence <= highestReceived && highestReceived - sequence >= 1024) || received[sequence % 1024] == sequence) return false;
		received[sequence % 1024] = sequence;
		highestReceived = Math.Max(highestReceived,sequence);
		payload = datagram.AsSpan(25,datagram.Length-Overhead).ToArray(); return true;
	}
	public void Dispose() { CryptographicOperations.ZeroMemory(sendKey); CryptographicOperations.ZeroMemory(receiveKey); }
}
