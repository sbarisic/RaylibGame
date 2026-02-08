using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Voxelgine.Engine.DI;

namespace Voxelgine.Engine
{
	public class FishLogging : IFishLogging
	{
		string LogFolder;
		bool IsServer = false;

		static object Lck = new object();
		static FileStream FStream;
		static StreamWriter SWriter;

		public FishLogging(IFishConfig Cfg)
		{
			LogFolder = Cfg.LogFolder;
		}

		public void Init(bool IsServer = false)
		{
			string LogFileName = $"{LogFolder}/log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";
			this.IsServer = IsServer;

			if (!Directory.Exists(LogFolder))
				Directory.CreateDirectory(LogFolder);

			lock (Lck)
			{
				if (FStream == null)
				{
					FStream = File.OpenWrite(LogFileName);
					SWriter = new StreamWriter(FStream, Encoding.UTF8);
				}
			}
		}

		void Flush()
		{
			lock (Lck)
			{
				SWriter.Flush();
				FStream.Flush();
			}
		}

		string GetPrefix()
		{
			string prefix = $"[{DateTime.Now:HH:mm:ss.fff}]";

			if (IsServer)
				prefix = prefix + "[SERVER]";
			else
				prefix = prefix + "[CLIENT]";

			return prefix + " ";
		}

		public void WriteLine(string message)
		{
			lock (Lck)
			{
				Console.WriteLine(message);
				SWriter.WriteLine(GetPrefix() + message);
				Flush();
			}
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
	}
}
