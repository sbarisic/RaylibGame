using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine.DI
{
	public interface IFishConfig
	{
		public int WindowWidth { get; set; }
		public int WindowHeight { get; set; }

		public string Title { get; set; }

		public string LogFolder { get; set; }

		public void LoadFromJson();
	}
}
