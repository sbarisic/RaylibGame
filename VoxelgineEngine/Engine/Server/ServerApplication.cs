namespace Voxelgine.Engine.Server;

/// <summary>Owns one server loop and, for hosted use, its worker thread.</summary>
public sealed class ServerApplication : IDisposable
{
	private readonly ServerLoop server;
	private Thread thread;
	private bool disposed;

	public ServerApplication(RuntimePaths runtimePaths, DI.GameLogLevel logLevel)
	{
		ArgumentNullException.ThrowIfNull(runtimePaths);
		server = new ServerLoop(runtimePaths, logLevel);
	}

	public ServerLoop Server => server;

	public Task StartupTask => server.StartupTask;

	public Exception BackgroundFailure { get; private set; }

	public void StartHosted(int port, int seed, bool forceRegenerate)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		if (thread != null)
			throw new InvalidOperationException("The hosted server has already started.");

		thread = new Thread(() =>
		{
			try
			{
				server.Start(port, seed, forceRegenerate);
			}
			catch (Exception exception)
			{
				BackgroundFailure = exception;
			}
		})
		{
			IsBackground = true,
			Name = "HostedServer",
		};
		thread.Start();
	}

	public void Run(int port, int seed, bool forceRegenerate)
	{
		ObjectDisposedException.ThrowIf(disposed, this);
		server.Start(port, seed, forceRegenerate);
	}

	public void Stop()
	{
		if (disposed)
			return;
		server.Stop();
		if (thread != null && thread.IsAlive && !ReferenceEquals(Thread.CurrentThread, thread))
			thread.Join(TimeSpan.FromSeconds(5));
	}

	public void Dispose()
	{
		if (disposed)
			return;
		Stop();
		server.Dispose();
		disposed = true;
	}
}
