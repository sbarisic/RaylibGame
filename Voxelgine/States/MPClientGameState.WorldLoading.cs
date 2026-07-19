using System.Diagnostics;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;

namespace Voxelgine.States;

public unsafe partial class MPClientGameState
{
	private enum ClientWorldLoadStage
	{
		None,
		Deserializing,
		Lighting,
		Ready,
	}

	private void BeginWorldLoad(byte[] compressedData)
	{
		if (compressedData == null || compressedData.Length == 0)
		{
			FailWorldLoad(new InvalidDataException("The transferred world is empty."));
			return;
		}

		if (_worldLoadTask != null)
		{
			_logging.Log(GameLogLevel.Warning, "Persistence", "Ignored duplicate world-load completion event.");
			return;
		}

		_logging.Log(GameLogLevel.Info, "Network", $"World transfer complete compressedBytes={compressedData.Length}");
		_statusText = "Building world: loading chunks...";
		_errorText = "";

		CancellationTokenSource cancellation = new();
		int generation = Interlocked.Increment(ref _worldLoadGeneration);
		_worldLoadCancellation = cancellation;
		Volatile.Write(ref _worldLoadStage, (int)ClientWorldLoadStage.Deserializing);
		_worldLoadTask = Task.Factory.StartNew(
			() => LoadWorldInBackground(compressedData, generation, cancellation.Token),
			cancellation.Token,
			TaskCreationOptions.LongRunning | TaskCreationOptions.DenyChildAttach,
			TaskScheduler.Default
		);
	}

	private GameSimulation LoadWorldInBackground(
		byte[] compressedData,
		int generation,
		CancellationToken cancellationToken)
	{
		Stopwatch timer = Stopwatch.StartNew();
		cancellationToken.ThrowIfCancellationRequested();

		_logging.Log(GameLogLevel.Debug, "Persistence", "Creating client simulation on world-load worker.");
		GameSimulation simulation = new(Eng);
		simulation.DayNight.IsAuthority = false;

		using (MemoryStream stream = new(compressedData, writable: false))
		{
			simulation.Map.Read(stream, cancellationToken);
		}

		_logging.Log(
			GameLogLevel.Info,
			"Persistence",
			$"Client world chunks loaded compressedBytes={compressedData.Length} chunks={simulation.Map.GetAllChunks().Length} durationMs={timer.Elapsed.TotalMilliseconds:F1}"
		);

		SetWorldLoadStage(generation, ClientWorldLoadStage.Lighting);
		timer.Restart();
		simulation.Map.ComputeLighting(cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();

		_logging.Log(
			GameLogLevel.Info,
			"Lighting",
			$"Client world lighting complete chunks={simulation.Map.GetAllChunks().Length} durationMs={timer.Elapsed.TotalMilliseconds:F1}"
		);
		SetWorldLoadStage(generation, ClientWorldLoadStage.Ready);
		return simulation;
	}

	private void SetWorldLoadStage(int generation, ClientWorldLoadStage stage)
	{
		if (Volatile.Read(ref _worldLoadGeneration) == generation)
			Volatile.Write(ref _worldLoadStage, (int)stage);
	}

	private string GetWorldLoadStatus()
	{
		return (ClientWorldLoadStage)Volatile.Read(ref _worldLoadStage) switch
		{
			ClientWorldLoadStage.Deserializing => "Building world: loading chunks...",
			ClientWorldLoadStage.Lighting => "Building world: computing lighting...",
			ClientWorldLoadStage.Ready => "Building world: finalizing...",
			_ => "Building world...",
		};
	}

	private void UpdateWorldLoad()
	{
		Task<GameSimulation> task = _worldLoadTask;
		if (task == null || !task.IsCompleted)
			return;

		CancellationTokenSource cancellation = _worldLoadCancellation;
		_worldLoadTask = null;
		_worldLoadCancellation = null;
		Volatile.Write(ref _worldLoadStage, (int)ClientWorldLoadStage.None);

		try
		{
			GameSimulation simulation = task.GetAwaiter().GetResult();
			if (_client?.State != ClientState.Loading)
			{
				_logging.Log(GameLogLevel.Debug, "Persistence", "Discarded completed client world because the connection is no longer loading.");
				return;
			}

			FinalizeWorldLoad(simulation);
		}
		catch (OperationCanceledException)
		{
			_logging.Log(GameLogLevel.Debug, "Persistence", "Client world loading was cancelled.");
		}
		catch (Exception exception)
		{
			FailWorldLoad(exception);
		}
		finally
		{
			cancellation?.Dispose();
		}
	}

	private void FinalizeWorldLoad(GameSimulation simulation)
	{
		_simulation = simulation ?? throw new ArgumentNullException(nameof(simulation));
		_simulation.Entities.IsAuthority = false;

		_logging.Log(GameLogLevel.Debug, "Persistence", "Creating client voxel presentation.");
		CreateFishGfxVoxelScene();

		_logging.Log(GameLogLevel.Debug, "UI", "Creating gameplay UI.");
		CreateGameplayUI();

		_logging.Log(GameLogLevel.Debug, "Audio", "Creating game audio sink.");
		_snd = CreateAudioSink();

		int playerId = _client.PlayerId;
		_logging.Log(GameLogLevel.Debug, "Entity", $"Creating local player playerId={playerId} name={_playerName}");
		ClientPlayer player = new(Eng, _gui, _playerName, true, _snd, playerId);
		_simulation.Players.AddLocalPlayer(playerId, player);
		player.InitGUI(_gameWindow, _gui);
		player.Init(_simulation.Map);
		player.SetPosition(new Vector3(32, 73, 19));

		ReplayPendingWorldPackets();

		_logging.Log(GameLogLevel.Debug, "Network", "Finishing client loading state.");
		_client.FinishLoading();
		_initialized = true;
		_statusText = "";
		_errorText = "";
		ApplyInputOwnership();

		_logging.Log(GameLogLevel.Info, "Persistence", "World loaded; entering gameplay.");
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
			_logging.Log(GameLogLevel.Debug, "Network", $"Replaying buffered world-load packets count={packets.Length}");
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
		DisposeFishGfxVoxelScene();
		_simulation?.LocalPlayer?.Dispose();
		_simulation = null;
		_logging.Log(GameLogLevel.Error, "Persistence", "Failed to load transferred world.", exception);

		if (_client?.IsConnected == true)
		{
			try
			{
				_client.Disconnect("Failed to load transferred world", GetClientTime());
			}
			catch (Exception disconnectException)
			{
				_logging.Log(GameLogLevel.Warning, "Network", "Failed to notify the server about a world-load failure.", disconnectException);
			}
		}

		_statusText = "";
		_errorText = $"Failed to load world: {exception.Message}";
	}

	private void CancelWorldLoad()
	{
		Interlocked.Increment(ref _worldLoadGeneration);
		Volatile.Write(ref _worldLoadStage, (int)ClientWorldLoadStage.None);

		Task<GameSimulation> task = _worldLoadTask;
		CancellationTokenSource cancellation = _worldLoadCancellation;
		_worldLoadTask = null;
		_worldLoadCancellation = null;

		if (cancellation == null)
			return;

		cancellation.Cancel();
		if (task == null)
		{
			cancellation.Dispose();
			return;
		}

		_ = task.ContinueWith(
			static (completed, state) =>
			{
				_ = completed.Exception;
				((CancellationTokenSource)state).Dispose();
			},
			cancellation,
			CancellationToken.None,
			TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default
		);
	}
}
