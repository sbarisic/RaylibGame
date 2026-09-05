using System.Security.Cryptography;
using System.Text.Json;

namespace Voxelgine.Engine;

/// <summary>A public identity, independent of a player's editable display name.</summary>
public readonly record struct PlayerIdentity(string Fingerprint)
{
	public static PlayerIdentity FromPublicKey(byte[] key) => new(Convert.ToHexStringLower(SHA256.HashData(key)));
	public override string ToString() => Fingerprint;
}

public sealed class NetworkIdentity : IDisposable
{
	private readonly ECDsa key;
	public NetworkIdentity() => key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
	public byte[] PublicKey => key.ExportSubjectPublicKeyInfo();
	public PlayerIdentity Identity => PlayerIdentity.FromPublicKey(PublicKey);
	public byte[] Sign(byte[] transcript) => key.SignData(transcript, HashAlgorithmName.SHA256);
	public static bool Verify(byte[] publicKey, byte[] transcript, byte[] signature)
	{
		try
		{
			using ECDsa verifier = ECDsa.Create();
			verifier.ImportSubjectPublicKeyInfo(publicKey, out int read);
			return read == publicKey.Length && verifier.KeySize == 256 &&
				verifier.ExportParameters(false).Curve.Oid.Value == ECCurve.NamedCurves.nistP256.Oid.Value &&
				verifier.VerifyData(transcript, signature, HashAlgorithmName.SHA256);
		}
		catch (CryptographicException) { return false; }
	}
	public static NetworkIdentity LoadOrCreate(string path)
	{
		NetworkIdentity identity = new();
		try
		{
			if (File.Exists(path))
			{
				byte[] bytes = File.ReadAllBytes(path);
				try { identity.key.ImportPkcs8PrivateKey(bytes, out int read); if (read != bytes.Length || identity.key.KeySize != 256 || identity.key.ExportParameters(false).Curve.Oid.Value != ECCurve.NamedCurves.nistP256.Oid.Value) throw new InvalidDataException("Identity key must be P-256."); }
				finally { CryptographicOperations.ZeroMemory(bytes); }
			}
			else
			{
				Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
				byte[] bytes = identity.key.ExportPkcs8PrivateKey();
				try { AtomicFile.Write(path, bytes, overwrite: false, privateFile: true); }
				catch (IOException) when (File.Exists(path)) { identity.Dispose(); return LoadOrCreate(path); }
				finally { CryptographicOperations.ZeroMemory(bytes); }
				if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
			}
			return identity;
		}
		catch { identity.Dispose(); throw; }
	}
	public void Dispose() => key.Dispose();
}

internal static class AtomicFile
{
	internal static void Write(string path, byte[] bytes, bool overwrite = true, bool privateFile = false)
	{
		string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
		try
		{
			FileStreamOptions options = new() { Mode=FileMode.CreateNew, Access=FileAccess.Write, Share=FileShare.None };
			if (privateFile && !OperatingSystem.IsWindows()) options.UnixCreateMode=UnixFileMode.UserRead|UnixFileMode.UserWrite;
			using (FileStream stream = new(temporary, options))
			{ stream.Write(bytes); stream.Flush(true); }
			File.Move(temporary, path, overwrite);
		}
		finally { if (File.Exists(temporary)) File.Delete(temporary); }
	}
}

/// <summary>Trust on first authenticated connection. Reset requires an explicit local action.</summary>
public sealed class ServerTrustStore
{
	private readonly string path;
	private Dictionary<string, string> pins;
	public ServerTrustStore(string path = null)
	{
		this.path = path;
		pins = path != null && File.Exists(path)
			? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? new()
			: new();
	}
	public bool Matches(string endpoint, PlayerIdentity identity) => !pins.TryGetValue(endpoint, out string pin) || pin == identity.Fingerprint;
	public void Confirm(string endpoint, PlayerIdentity identity)
	{
		if (!Matches(endpoint, identity)) throw new InvalidOperationException("Server identity changed. Reset its trust explicitly before reconnecting.");
		if (pins.ContainsKey(endpoint)) return;
		var updated = new Dictionary<string, string>(pins) { [endpoint] = identity.Fingerprint };
		Save(updated); pins = updated;
	}
	public bool Reset(string endpoint)
	{
		var updated = new Dictionary<string, string>(pins);
		if (!updated.Remove(endpoint)) return false;
		Save(updated); pins = updated; return true;
	}
	private void Save(Dictionary<string, string> updated)
	{
		if (path == null) return;
		Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
		AtomicFile.Write(path, JsonSerializer.SerializeToUtf8Bytes(updated));
	}
}
