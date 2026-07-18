using System.Text;
using Voxelgine.Engine.DI;

namespace Voxelgine.Engine
{
	public sealed class FishLogging : IFishLogging, IDisposable
	{
		private const string ConsoleLogFileName = "console.log";
		private static readonly object ProcessLock = new();
		private static ConsoleLogSession processSession;
		private static int processSessionUsers;
		private readonly string logFolder;
		private ConsoleLogSession session;
		private string source = "CLIENT";
		private bool disposed;

		public FishLogging(IFishConfig config)
		{
			ArgumentNullException.ThrowIfNull(config);
			logFolder = config.LogFolder;
			MinimumLevel = config.LogLevel;
		}

		public GameLogLevel MinimumLevel { get; set; }

		public void Init(bool IsServer = false)
		{
			ObjectDisposedException.ThrowIf(disposed, this);
			source = IsServer ? "SERVER" : "CLIENT";

			lock (ProcessLock)
			{
				if (session is not null)
				{
					return;
				}

				if (processSession is null)
				{
					string folder = ResolveLogFolder(logFolder);
					Directory.CreateDirectory(folder);
					processSession = new ConsoleLogSession(Path.Combine(folder, ConsoleLogFileName));
				}

				session = processSession;
				processSessionUsers++;
			}
		}

		public void Log(
			GameLogLevel level,
			string category,
			string message,
			Exception exception = null
		)
		{
			if (level < MinimumLevel)
			{
				return;
			}

			ConsoleLogSession activeSession = session
				?? throw new InvalidOperationException("Logging must be initialized before use.");
			activeSession.WriteStructured(source, level, NormalizeCategory(category), message ?? string.Empty);
			if (exception is not null)
			{
				activeSession.WriteStructured(source, level, NormalizeCategory(category), exception.ToString());
			}
		}

		public void WriteLine(string message) => Log(GameLogLevel.Debug, "General", message);

		public void ServerWriteLine(string message) => Log(GameLogLevel.Debug, "Server", message);

		public void ClientWriteLine(string message) => Log(GameLogLevel.Debug, "Client", message);

		public void ServerNetworkWriteLine(string message) => Log(GameLogLevel.Trace, "Network", message);

		public void ClientNetworkWriteLine(string message) => Log(GameLogLevel.Trace, "Network", message);

		public void Dispose()
		{
			if (disposed)
			{
				return;
			}

			lock (ProcessLock)
			{
				if (session is not null)
				{
					processSessionUsers--;
					if (processSessionUsers == 0 && ReferenceEquals(processSession, session))
					{
						processSession.Dispose();
						processSession = null;
					}
				}

				session = null;
				disposed = true;
			}
		}

		private static string NormalizeCategory(string category)
		{
			return string.IsNullOrWhiteSpace(category) ? "General" : category.Trim();
		}

		private static string ResolveLogFolder(string configuredFolder)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(configuredFolder);
			return Path.IsPathRooted(configuredFolder)
				? Path.GetFullPath(configuredFolder)
				: Path.GetFullPath(configuredFolder, AppContext.BaseDirectory);
		}

		private sealed class ConsoleLogSession : IDisposable
		{
			private readonly object writeLock = new();
			private readonly TextWriter originalOutput;
			private readonly TextWriter originalError;
			private readonly StreamWriter logWriter;
			private bool disposed;

			public ConsoleLogSession(string path)
			{
				originalOutput = Console.Out;
				originalError = Console.Error;
				FileStream stream = new(path, FileMode.Create, FileAccess.Write, FileShare.Read);
				logWriter = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };

				try
				{
					Console.SetOut(new StructuredConsoleWriter(originalOutput, this));
					Console.SetError(new StructuredConsoleWriter(originalError, this));
				}
				catch
				{
					Console.SetOut(originalOutput);
					Console.SetError(originalError);
					logWriter.Dispose();
					throw;
				}
			}

			public void WriteStructured(string source, GameLogLevel level, string category, string text)
			{
				ObjectDisposedException.ThrowIf(disposed, this);
				string[] lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
				lock (writeLock)
				{
					foreach (string line in lines)
					{
						string formatted = $"[{DateTime.Now:HH:mm:ss.fff}][{source}][{level.ToString().ToUpperInvariant()}][{category}] {line}";
						originalOutput.WriteLine(formatted);
						logWriter.WriteLine(formatted);
					}
					originalOutput.Flush();
				}
			}

			public void Dispose()
			{
				if (disposed)
				{
					return;
				}

				Console.Out.Flush();
				Console.Error.Flush();
				Console.SetOut(originalOutput);
				Console.SetError(originalError);
				lock (writeLock)
				{
					logWriter.Dispose();
					disposed = true;
				}
			}
		}

		private sealed class StructuredConsoleWriter : TextWriter
		{
			private readonly TextWriter consoleWriter;
			private readonly ConsoleLogSession session;
			private readonly StringBuilder pending = new();

			public StructuredConsoleWriter(TextWriter consoleWriter, ConsoleLogSession session)
			{
				this.consoleWriter = consoleWriter;
				this.session = session;
			}

			public override Encoding Encoding => consoleWriter.Encoding;

			public override void Write(char value)
			{
				if (value == '\n')
				{
					FlushPending();
				}
				else if (value != '\r')
				{
					pending.Append(value);
				}
			}

			public override void Write(string value)
			{
				if (value is null)
				{
					return;
				}
				foreach (char character in value)
				{
					Write(character);
				}
			}

			public override void WriteLine(string value)
			{
				Write(value);
				FlushPending();
			}

			public override void WriteLine() => FlushPending();

			public override void Flush()
			{
				if (pending.Length > 0)
				{
					FlushPending();
				}
				consoleWriter.Flush();
			}

			private void FlushPending()
			{
				string line = pending.ToString();
				pending.Clear();
				session.WriteStructured("PROCESS", GameLogLevel.Trace, "Console", line);
			}
		}
	}
}
