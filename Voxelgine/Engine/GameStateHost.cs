using Voxelgine.Engine.DI;

namespace Voxelgine.Engine;

/// <summary>Exclusively owns the active transient game state.</summary>
public sealed class GameStateHost : IDisposable
{
	private const int MaximumQueuedTransitions = 16;
	private readonly IGameWindow window;
	private readonly IFishLogging logging;
	private readonly Queue<TransitionRequest> pending = new();
	private readonly List<GameStateImpl> exceptionalRetirements = new();
	private GameStateImpl active;
	private bool transitioning;
	private bool disposed;

	public GameStateHost(IGameWindow window, IFishLogging logging)
	{
		this.window = window ?? throw new ArgumentNullException(nameof(window));
		this.logging = logging ?? throw new ArgumentNullException(nameof(logging));
	}

	/// <summary>The routed state, or null before startup and after disposal.</summary>
	public GameStateImpl ActiveState => active;

	public void Start(Func<GameStateImpl> factory)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		if (active != null)
			throw new InvalidOperationException("The state host has already started.");

		Transition(new TransitionRequest(factory, null));
	}

	public void Request(Func<GameStateImpl> factory)
	{
		Request(factory, null);
	}

	public void Request(Func<GameStateImpl> factory, Action<GameStateImpl> activated)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		ArgumentNullException.ThrowIfNull(factory);
		if (pending.Count >= MaximumQueuedTransitions)
			throw new InvalidOperationException("The game-state transition queue is full.");
		pending.Enqueue(new TransitionRequest(factory, activated));
	}

	/// <summary>Processes at most one queued transition at a frame boundary.</summary>
	public void ProcessPending()
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		if (transitioning || pending.Count == 0)
			return;
		Transition(pending.Dequeue());
	}

	private void Transition(TransitionRequest request)
	{
		ArgumentNullException.ThrowIfNull(request.Factory);
		transitioning = true;
		GameStateImpl candidate = null;
		GameStateImpl previous = active;
		try
		{
			candidate = request.Factory()
				?? throw new InvalidOperationException("A state factory returned null.");
			candidate.Prepare();
			try
			{
				window.RouteState(candidate);
				candidate.Activate();
				request.Activated?.Invoke(candidate);
			}
			catch
			{
				try
				{
					window.RouteState(previous);
				}
				catch (Exception rollbackException)
				{
					logging.Log(
						GameLogLevel.Error,
						"State",
						"Failed to restore the previous route after a transition failure.",
						rollbackException);
				}
				RetireFailedCandidate(candidate);
				throw;
			}

			active = candidate;
			logging.Log(
				GameLogLevel.Info,
				"State",
				$"Transition old={previous?.GetType().Name ?? "None"} new={candidate.GetType().Name}");
			if (previous != null)
				Retire(previous);
		}
		catch
		{
			if (candidate != null && !ReferenceEquals(candidate, active))
			{
				TryDispose(candidate, "candidate cleanup");
			}
			throw;
		}
		finally
		{
			transitioning = false;
		}
	}

	private void RetireFailedCandidate(GameStateImpl candidate)
	{
		try
		{
			candidate.Deactivate();
		}
		catch (Exception exception)
		{
			logging.Log(GameLogLevel.Error, "State", "Candidate deactivation failed.", exception);
			exceptionalRetirements.Add(candidate);
		}
	}

	private void Retire(GameStateImpl state)
	{
		try
		{
			state.Deactivate();
		}
		catch (Exception exception)
		{
			logging.Log(GameLogLevel.Error, "State", "Retired state deactivation failed; the new state remains active.", exception);
			exceptionalRetirements.Add(state);
		}
		TryDispose(state, "retired state disposal");
	}

	private void TryDispose(GameStateImpl state, string operation)
	{
		try
		{
			state.Dispose();
		}
		catch (Exception exception)
		{
			logging.Log(GameLogLevel.Error, "State", $"Failed during {operation}.", exception);
			if (!exceptionalRetirements.Contains(state))
				exceptionalRetirements.Add(state);
		}
	}

	public void Dispose()
	{
		if (disposed)
			return;
		disposed = true;
		pending.Clear();

		if (active != null)
		{
			try
			{
				active.Deactivate();
			}
			catch (Exception exception)
			{
				logging.Log(GameLogLevel.Error, "State", "Active state deactivation failed during shutdown.", exception);
			}
			TryDispose(active, "active state shutdown");
			active = null;
		}

		foreach (GameStateImpl state in exceptionalRetirements)
			TryDispose(state, "exceptional state shutdown retry");
		exceptionalRetirements.Clear();
		try
		{
			window.RouteState(null);
		}
		catch (Exception exception)
		{
			logging.Log(GameLogLevel.Error, "State", "Failed to clear state routing during shutdown.", exception);
		}
	}

	private readonly record struct TransitionRequest(
		Func<GameStateImpl> Factory,
		Action<GameStateImpl> Activated);
}
