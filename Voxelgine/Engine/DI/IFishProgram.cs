using Voxelgine.States;
using Voxelgine.Audio;
using Voxelgine.Engine.Server;

namespace Voxelgine.Engine.DI;

public interface IClientEngineRunner : IFishEngineRunner
{
	GameConfig Config { get; }

	IAudioSystem Audio { get; }

	RuntimePaths RuntimePaths { get; }

	/// <summary>Explicit Material Lab source data root, or null for bounded project discovery.</summary>
	string AssetSourceRoot => null;

	IGameWindow Window { get; }

	/// <summary>The hosted server loop, or null when this client is not hosting.</summary>
	ServerLoop HostedServer { get; }

	bool IsMultiplayerActive { get; }

	bool IsLocalPlayerSubmerged { get; }

	void RequestState(ClientStateKind state);

	void Connect(string address, int port, string playerName);

	ServerApplication StartHostedServer(int port, int seed, bool forceRegenerate);

	void StopHostedServer();

	void FireWeapon(System.Numerics.Vector3 start, System.Numerics.Vector3 direction, float maximumLength);
}

public static class ClientEngineRunnerExtensions
{
	public static IClientEngineRunner AsClient(this IFishEngineRunner engine)
	{
		return (IClientEngineRunner)engine;
	}
}

public enum ClientStateKind
{
	MainMenu,
	Multiplayer,
	NpcPreview,
	EffectsPreview,
	VoxelMaterialPreview,
}
