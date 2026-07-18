using System.Text;
using Voxelgine.Engine.DI;

namespace Voxelgine.Engine
{
	public sealed class FishLogging : IFishLogging, IDisposable
	{
		private const string ConsoleLogFileName = "console.log";
		private static readonly object ProcessLock = new();
		private static ConsoleLogSession processSession;
		private readonly string logFolder;
		private ConsoleLogSession session;
		private bool isServer;
		private bool ownsProcessSession;
		private bool disposed;

		public FishLogging(IFishConfig config)
		{
			ArgumentNullException.ThrowIfNull(config);
			logFolder = config.LogFolder;
		}

		public void Init(bool isServer = false)
		{
			ObjectDisposedException.ThrowIf(disposed, this);
			this.isServer = isServer;

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
					processSession = new ConsoleLogSession(
						Path.Combine(folder, ConsoleLogFileName)
					);
					ownsProcessSession = true;
				}

				session = processSession;
			}
		}

		public void WriteLine(string message)
		{
			ConsoleLogSession activeSession = session
				?? throw new InvalidOperationException("Logging must be initialized before use.");
			activeSession.WriteEngineLine(message, GetPrefix() + message);
		}

		public void ServerWriteLine(string message)
		{
			WriteLine(message);
		}

		public void ClientWriteLine(string message)
		{
			WriteLine(message);
		}

		public void ServerNetworkWriteLine(string message)
		{
			WriteLine("[NETWORK] " + message);
		}

		public void ClientNetworkWriteLine(string message)
		{
			WriteLine("[NETWORK] " + message);
		}

		public void Dispose()
		{
			if (disposed)
			{
				return;
			}

			lock (ProcessLock)
			{
				if (ownsProcessSession && ReferenceEquals(processSession, session))
				{
					processSession.Dispose();
					processSession = null;
				}

				session = null;
				ownsProcessSession = false;
				disposed = true;
			}
		}

		private string GetPrefix()
		{
			string source = isServer ? "[SERVER]" : "[CLIENT]";
			return $"[{DateTime.Now:HH:mm:ss.fff}]{source} ";
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
				FileStream stream = new(
					path,
					FileMode.Create,
					FileAccess.Write,
					FileShare.Read
				);
				logWriter = new StreamWriter(stream, new UTF8Encoding(false))
				{
					AutoFlush = true,
				};

				try
				{
					Console.SetOut(new TeeTextWriter(originalOutput, logWriter, writeLock));
					Console.SetError(new TeeTextWriter(originalError, logWriter, writeLock));
				}
				catch
				{
					Console.SetOut(originalOutput);
					Console.SetError(originalError);
					logWriter.Dispose();
					throw;
				}
			}

			public void WriteEngineLine(string consoleMessage, string fileMessage)
			{
				ObjectDisposedException.ThrowIf(disposed, this);
				lock (writeLock)
				{
					originalOutput.WriteLine(consoleMessage);
					originalOutput.Flush();
					logWriter.WriteLine(fileMessage);
				}
			}

			public void Dispose()
			{
				if (disposed)
				{
					return;
				}

				Console.SetOut(originalOutput);
				Console.SetError(originalError);
				lock (writeLock)
				{
					logWriter.Dispose();
					disposed = true;
				}
			}
		}

		private sealed class TeeTextWriter : TextWriter
		{
			private readonly TextWriter consoleWriter;
			private readonly TextWriter logWriter;
			private readonly object writeLock;

			public TeeTextWriter(
				TextWriter consoleWriter,
				TextWriter logWriter,
				object writeLock
			)
			{
				this.consoleWriter = consoleWriter;
				this.logWriter = logWriter;
				this.writeLock = writeLock;
			}

			public override Encoding Encoding => consoleWriter.Encoding;

			public override void Write(char value)
			{
				lock (writeLock)
				{
					consoleWriter.Write(value);
					logWriter.Write(value);
				}
			}

			public override void Write(string value)
			{
				lock (writeLock)
				{
					consoleWriter.Write(value);
					logWriter.Write(value);
				}
			}

			public override void Write(char[] buffer, int index, int count)
			{
				lock (writeLock)
				{
					consoleWriter.Write(buffer, index, count);
					logWriter.Write(buffer, index, count);
				}
			}

			public override void WriteLine()
			{
				lock (writeLock)
				{
					consoleWriter.WriteLine();
					logWriter.WriteLine();
				}
			}

			public override void WriteLine(string value)
			{
				lock (writeLock)
				{
					consoleWriter.WriteLine(value);
					logWriter.WriteLine(value);
				}
			}

			public override void Flush()
			{
				lock (writeLock)
				{
					consoleWriter.Flush();
					logWriter.Flush();
				}
			}
		}
	}
}
