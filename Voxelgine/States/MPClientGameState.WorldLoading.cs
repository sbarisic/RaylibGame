using Voxelgine.Engine;
using Voxelgine.Engine.Audio;
using Voxelgine.Engine.DI;

namespace Voxelgine.States;

public partial class MPClientGameState
{
	private ClientWorldStream _worldStream;

	internal int WorldStreamId => _worldStream?.StreamId ?? 0;
	internal System.Numerics.Vector3 WorldStreamFocus => _worldStream?.Focus ?? default;
	internal int WorldDecodeQueueDepth => _worldStream?.DecodeQueueDepth ?? 0;
	internal int WorldApplyQueueDepth => _worldStream?.ApplyQueueDepth ?? 0;
	internal int WorldDeferredAcknowledgements => _worldStream?.DeferredAcknowledgements ?? 0;
	internal bool WorldStreamingBackpressured => _worldStream?.IsBackpressured ?? false;
	internal float WorldLoadingProgress => _worldStream?.LoadingProgress ?? 0;
	internal int WorldCoreReceived => _worldStream?.CoreReceived ?? 0;
	internal int WorldCoreApplied => _worldStream?.CoreApplied ?? 0;
	internal int WorldHaloReceived => _worldStream?.HaloReceived ?? 0;
	internal int WorldHaloApplied => _worldStream?.HaloApplied ?? 0;
	internal int WorldOrdinaryReceived => _worldStream?.OrdinaryReceived ?? 0;
	internal int WorldOrdinaryApplied => _worldStream?.OrdinaryApplied ?? 0;
	internal int WorldCachedColumns => _worldStream?.CachedColumns ?? 0;
	internal int WorldCoreLit => _worldStream?.CoreLit ?? 0;
	internal int WorldCoreMeshed => _worldStream?.CoreMeshed ?? 0;
	internal int WorldHaloLit => _worldStream?.HaloLit ?? 0;
	internal int WorldHaloMeshed => _worldStream?.HaloMeshed ?? 0;
	internal double AverageColumnDecodeMilliseconds => _worldStream?.AverageDecodeMilliseconds ?? 0;
	internal double AverageColumnApplyMilliseconds => _worldStream?.AverageApplyMilliseconds ?? 0;

	private void EnsureWorldStream()
	{
		_worldStream ??= new ClientWorldStream(
			_logging,
			() => _client,
			() => _simulation,
			() => _fishVoxelScene,
			() => _initialized,
			() => Client.Config.MaxChunkDrawDistance,
			GetClientTime,
			FailWorldLoad);
	}

	private void BeginWorldStream(WorldStreamBeginPacket packet)
	{
		CancelWorldLoad();
		_objectAssemblies.Clear();
		DisposeFishGfxVoxelScene();
		_simulation?.LocalPlayer?.Dispose();

		_errorText = string.Empty;
		_statusText = "Receiving bootstrap columns";

		_simulation = new GameSimulation(Eng);
		_simulation.DayNight.IsAuthority = false;
		_simulation.Entities.IsAuthority = false;
		_simulation.Map.UnknownColumnsAreBoundaries = true;
		CreateFishGfxVoxelScene(synchronizeExisting: false);

		EnsureWorldStream();
		_worldStream.Begin(packet);
	}

	private void ReceiveWorldColumn(WorldColumnPacket packet)
	{
		_worldStream?.ReceiveColumn(packet);
	}

	private void UpdateWorldStream()
	{
		_worldStream?.Update();
		if (!_initialized && _worldStream != null)
			_statusText = _worldStream.Status;
	}

	private void FinishWorldStart(ClientWorldStartPacket packet)
	{
		if (_worldStream == null || packet.StreamId != _worldStream.StreamId || _simulation == null || _initialized)
			return;

		CreateGameplayUI();
		_snd = CreateAudioSink();
		ClientPlayer player = new(Eng, _gui, _playerName, true, _snd, _client.PlayerId);
		player.BindInventoryModel(_inventoryModel);
		player.ItemUseRequested += HandleLocalItemUseRequested;
		_simulation.Players.AddLocalPlayer(_client.PlayerId, player);
		player.InitGUI(_gameWindow, _gui);
		player.Init(_simulation.Map);
		player.Health = packet.Health;
		player.ApplyPhysicsState(packet.PhysicsState);

		ReplayPendingWorldPackets();
		_client.FinishLoading();
		_initialized = true;
		_statusText = string.Empty;
		_errorText = string.Empty;
		ApplyInputOwnership();
		_worldStream.SendInterest(force: true);
		_logging.Log(GameLogLevel.Info, "WorldStream", $"started streamId={packet.StreamId} serverTick={packet.ServerTick} position={player.Position}");
	}

	private void ReplayPendingWorldPackets()
	{
		if (_pendingWorldPackets.Count == 0)
			return;
		Packet[] packets = _pendingWorldPackets.ToArray();
		_pendingWorldPackets.Clear();
		_replayingPendingWorldPackets = true;
		try
		{
			foreach (Packet packet in packets)
				OnPacketReceived(packet);
		}
		finally
		{
			_replayingPendingWorldPackets = false;
		}
	}

	private void FailWorldLoad(Exception exception)
	{
		_logging.Log(GameLogLevel.Error, "WorldStream", "Client world streaming failed.", exception);
		_statusText = string.Empty;
		_errorText = $"Failed to load world: {exception.Message}";
		if (_client?.IsConnected == true)
			_client.Disconnect("Failed to stream world", GetClientTime());
	}

	private void CancelWorldLoad()
	{
		_worldStream?.Cancel();
		_objectAssemblies.Clear();
	}

	private void OnPreparedRenderColumnApplied(int x, int z, long revision)
	{
		_worldStream?.MarkRenderColumnApplied(x, z, revision);
	}
}
