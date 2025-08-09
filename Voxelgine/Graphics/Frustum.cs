using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Graphics {
	public struct Frustum {
		public Vector4 Left;
		public Vector4 Right;
		public Vector4 Top;
		public Vector4 Bottom;
		public Vector4 Near;
		public Vector4 Far;
	}
}
