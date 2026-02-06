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

		FileStream FStream;
		StreamWriter SWriter;

		public FishLogging(IFishConfig Cfg)
		{
			LogFolder = Cfg.LogFolder;
		}

		public void Init()
		{
			string LogFileName = $"{LogFolder}/log_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.txt";

			if (!Directory.Exists(LogFolder))
				Directory.CreateDirectory(LogFolder);

			FStream = File.OpenWrite(LogFileName);
			SWriter = new StreamWriter(FStream, Encoding.UTF8);
		}

		void Flush()
		{
			SWriter.Flush();
			FStream.Flush();
		}

		string GetPrefix()
		{
			string prefix = $"[{DateTime.Now:HH:mm:ss.fff}] ";
			return prefix;
		}

		public void WriteLine(string message)
		{
			SWriter.WriteLine(GetPrefix() + message);
			Flush();
		}
	}
}
