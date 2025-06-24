using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine.Physics {
	struct AABB {
		public Vector3 Position;
		public Vector3 Size;

		public AABB() {
			Position = Vector3.Zero;
			Size = Vector3.One;
		}
	}
}
