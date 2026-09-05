using System.Text.Json;

namespace Voxelgine.Engine.Server;

public sealed class ServerAccessPolicy
{
	private readonly string path;
	private readonly PlayerIdentity? host;
	private Policy state;
	public ServerAccessPolicy(string path, PlayerIdentity? host = null)
	{
		this.path=path; this.host=host;
		state=File.Exists(path)?JsonSerializer.Deserialize<Policy>(File.ReadAllText(path)) ?? throw new InvalidDataException("Empty server access policy."):new();
		if(state.Administrators==null || state.Bans==null || state.Administrators.Concat(state.Bans).Any(value=>!IsFingerprint(value))) throw new InvalidDataException("Invalid identity in access policy.");
	}
	public static bool IsFingerprint(string value)=>value?.Length==64 && value.All(character=>character is >= '0' and <= '9' or >= 'a' and <= 'f');
	public bool IsAdministrator(PlayerIdentity identity)=>!IsBanned(identity) && (host==identity || state.Administrators.Contains(identity.Fingerprint??""));
	public bool IsBanned(PlayerIdentity identity)=>state.Bans.Contains(identity.Fingerprint??"");
	public void SetAdministrator(PlayerIdentity identity,bool enabled)=>Change(identity,enabled,false);
	public void SetBanned(PlayerIdentity identity,bool enabled)=>Change(identity,enabled,true);
	private void Change(PlayerIdentity identity,bool enabled,bool ban)
	{
		if(!IsFingerprint(identity.Fingerprint)) throw new ArgumentException("Expected a verified identity fingerprint.");
		Policy updated=new() { Administrators=new(state.Administrators),Bans=new(state.Bans) };
		HashSet<string> target=ban?updated.Bans:updated.Administrators;
		if(enabled) target.Add(identity.Fingerprint); else target.Remove(identity.Fingerprint);
		AtomicFile.Write(path,JsonSerializer.SerializeToUtf8Bytes(updated)); state=updated;
	}
	public sealed class Policy
	{
		public HashSet<string> Administrators { get; set; }=new();
		public HashSet<string> Bans { get; set; }=new();
	}
}
