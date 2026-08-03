using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.Engine.Server;
using Voxelgine.States;

namespace Voxelgine;

internal sealed partial class ClientApplication
{
	private GameStateImpl CreateInitialState()
	{
		if (arguments.Contains("--fishgfx-auto-gameplay", StringComparer.OrdinalIgnoreCase))
			return new FishGfxGameplaySmokeState(Window, this);
		if (arguments.Contains("--fishgfx-auto-transition", StringComparer.OrdinalIgnoreCase))
			return new FishGfxStateTransitionSmokeState(Window, this);
		if (arguments.Contains("--fishgfx-auto-npc", StringComparer.OrdinalIgnoreCase))
			return CreateState(ClientStateKind.NpcPreview);
		if (arguments.Contains("--fishgfx-auto-effects", StringComparer.OrdinalIgnoreCase))
			return CreateState(ClientStateKind.EffectsPreview);
		if (arguments.Contains("--fishgfx-auto-voxel-material", StringComparer.OrdinalIgnoreCase))
		{
			VoxelMaterialPreviewState state = (VoxelMaterialPreviewState)CreateState(ClientStateKind.VoxelMaterialPreview);
			state.EnableAutomaticValidation();
			return state;
		}
		if (arguments.Contains("--fishgfx-auto-world-preview", StringComparer.OrdinalIgnoreCase))
		{
			WorldPreviewState state = (WorldPreviewState)CreateState(ClientStateKind.WorldPreview);
			state.EnableAutomaticValidation();
			return state;
		}
		if (arguments.Contains("--fishgfx-auto-village-prefab-lab", StringComparer.OrdinalIgnoreCase))
		{
			VillagePrefabLabState state = (VillagePrefabLabState)CreateState(ClientStateKind.VillagePrefabLab);
			state.EnableAutomaticValidation();
			return state;
		}
		return CreateState(ClientStateKind.MainMenu);
	}

	private GameStateImpl CreateState(ClientStateKind state)
	{
		return state switch
		{
			ClientStateKind.MainMenu => new MainMenuStateFishUI(Window, this),
			ClientStateKind.Multiplayer => new MPClientGameState(Window, this),
			ClientStateKind.NpcPreview => new NPCPreviewState(Window, this),
			ClientStateKind.EffectsPreview => new EffectsPreviewState(Window, this),
			ClientStateKind.VoxelMaterialPreview => new VoxelMaterialPreviewState(Window, this),
			ClientStateKind.WorldPreview => new WorldPreviewState(Window, this),
			ClientStateKind.VillagePrefabLab => new VillagePrefabLabState(Window, this),
			_ => throw new ArgumentOutOfRangeException(nameof(state)),
		};
	}

	public void RequestState(ClientStateKind state)
	{
		stateHost.Request(() => CreateState(state));
	}

	public void Connect(string address, int port, string playerName)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(address);
		ArgumentException.ThrowIfNullOrWhiteSpace(playerName);
		IsCurrentConnectionHosted = hostedServer is not null
			&& port == hostedServerPort
			&& IsLoopbackAddress(address);
		stateHost.Request(
			() => CreateState(ClientStateKind.Multiplayer),
			state => ((MPClientGameState)state).Connect(address, port, playerName));
	}

	public ServerApplication StartHostedServer(int port, int seed, bool forceRegenerate, string worldPlanDirectory = null)
	{
		StopHostedServer();
		RuntimePaths serverPaths = RuntimePathResolver.ResolveRuntimePaths(
			ApplicationKind.HostedServer,
			Path.Combine(RuntimePaths.Root, "hosted-server"),
			Environment.CurrentDirectory);
		ServerApplication application = new(serverPaths, Config.LogLevel);
		try
		{
			application.StartHosted(port, seed, forceRegenerate, worldPlanDirectory);
			hostedServer = application;
			hostedServerPort = port;
			return application;
		}
		catch
		{
			application.Dispose();
			throw;
		}
	}

	public void StopHostedServer()
	{
		ServerApplication application = hostedServer;
		hostedServer = null;
		hostedServerPort = 0;
		IsCurrentConnectionHosted = false;
		application?.Dispose();
	}

	private static bool IsLoopbackAddress(string address)
	{
		if (string.Equals(address, "localhost", StringComparison.OrdinalIgnoreCase))
			return true;
		return System.Net.IPAddress.TryParse(address, out System.Net.IPAddress parsed)
			&& System.Net.IPAddress.IsLoopback(parsed);
	}

	public void FireWeapon(Vector3 start, Vector3 direction, float maximumLength)
	{
		if (stateHost.ActiveState is not MPClientGameState { IsActive: true } multiplayer)
			return;
		multiplayer.SendWeaponFire(start, direction);
		multiplayer.SpawnPredictedFireEffects(start, direction, maximumLength);
	}
}
