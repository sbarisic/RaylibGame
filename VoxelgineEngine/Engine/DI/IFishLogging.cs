using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine.DI
{
	public enum GameLogLevel
	{
		Trace,
		Debug,
		Info,
		Warning,
		Error,
		Fatal,
	}

	public interface IFishLogging
	{
		GameLogLevel MinimumLevel
		{
			get => GameLogLevel.Trace;
			set { }
		}

		public void Init(bool IsServer = false);

		void Log(
			GameLogLevel level,
			string category,
			string message,
			Exception exception = null
		)
		{
			WriteLine(exception is null ? message : $"{message}{Environment.NewLine}{exception}");
		}

		public void WriteLine(string message);

		public void ServerWriteLine(string message);

		public void ClientWriteLine(string message);

		public void ServerNetworkWriteLine(string message);

		public void ClientNetworkWriteLine(string message);
	}
}
