using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine.DI
{
	public interface IFishLogging
	{
		public void Init(bool IsServer = false);

		public void WriteLine(string message);

		public void ServerWriteLine(string message);

		public void ClientWriteLine(string message);

		public void ServerNetworkWriteLine(string message);

		public void ClientNetworkWriteLine(string message);
	}
}
