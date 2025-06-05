using Voxelgine;
using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine {
	public struct Vertex3 {
		public Vector3 Position;
		public Vector3 Normal;
		public Vector2 UV;
		public Color Color;

		public Vertex3(Vector3 Position, Vector2 UV, Vector3 Normal, Color? Color = null) {
			this.Position = Position;
			this.UV = UV;
			this.Color = Color ?? Utils.Color(1.0f);
			this.Normal = Normal;
		}

		public override string ToString() {
			return string.Format("({0}, {1}, {2})", Position, UV, Color);
		}
	}
}
