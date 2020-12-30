using System.Numerics;

namespace RaylibSharp {
	public struct Vertex3 {
		public Vector3 Position;
		public Vector2 UV;
		public Color Color;

		public Vertex3(Vector3 Position, Vector2 UV, Color? Color = null) {
			this.Position = Position;
			this.UV = UV;
			this.Color = Color ?? new Color(255, 255, 255);
		}

		public override string ToString() {
			return string.Format("({0}, {1}, {2})", Position, UV, Color);
		}
	}
}