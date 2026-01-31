using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Engine {
	public struct AABB {
		public static readonly AABB Empty = new AABB(Vector3.Zero, Vector3.Zero);

		public Vector3 Position;
		public Vector3 Size;

		public bool IsEmpty {
			get; private set;
		}

		public AABB() {
			Position = Vector3.Zero;
			Size = Vector3.One;
			Calc();
		}

		public AABB(Vector3 Position, Vector3 Size) {
			this.Position = Position;
			this.Size = Size;
			Calc();
		}

		public AABB(BoundingBox BB) {
			Position = BB.Min;
			Size = BB.Max - BB.Min;
			Calc();
		}

		public AABB Offset(Vector3 Offset) {
			return new AABB(Position + Offset, Size);
		}

		public BoundingBox ToBoundingBox() {
			return new BoundingBox(Position, Position + Size);
		}

		public bool Contains(Vector3 Point) {
			return Point.X >= Position.X && Point.X <= Position.X + Size.X &&
				   Point.Y >= Position.Y && Point.Y <= Position.Y + Size.Y &&
				   Point.Z >= Position.Z && Point.Z <= Position.Z + Size.Z;
		}

		void Calc() {
			IsEmpty = Size.X == 0 && Size.Y == 0 && Size.Z == 0;
		}

		public Vector3[] GetCorners() {
			Vector3 min = Position;
			Vector3 max = Position + Size;

			Vector3[] Corners = new Vector3[8];

			Corners[0] = new Vector3(min.X, min.Y, min.Z);
			Corners[1] = new Vector3(max.X, min.Y, min.Z);
			Corners[2] = new Vector3(max.X, min.Y, max.Z);
			Corners[3] = new Vector3(min.X, min.Y, max.Z);
			Corners[4] = new Vector3(min.X, max.Y, min.Z);
			Corners[5] = new Vector3(max.X, max.Y, min.Z);
			Corners[6] = new Vector3(max.X, max.Y, max.Z);
			Corners[7] = new Vector3(min.X, max.Y, max.Z);

			return Corners;
		}

		public static AABB Union(AABB A, AABB B) {
			Vector3 min = Vector3.Min(A.Position, B.Position);
			Vector3 max = Vector3.Max(A.Position + A.Size, B.Position + B.Size);
			return new AABB(min, max - min);
		}

		/// <summary>
		/// Tests if two AABBs overlap.
		/// </summary>
		public bool Overlaps(AABB other) {
			Vector3 aMin = Position;
			Vector3 aMax = Position + Size;
			Vector3 bMin = other.Position;
			Vector3 bMax = other.Position + other.Size;

			return aMin.X <= bMax.X && aMax.X >= bMin.X &&
				   aMin.Y <= bMax.Y && aMax.Y >= bMin.Y &&
				   aMin.Z <= bMax.Z && aMax.Z >= bMin.Z;
		}

		/// <summary>
		/// Tests if two AABBs overlap (static version).
		/// </summary>
		public static bool Overlaps(AABB a, AABB b) {
			return a.Overlaps(b);
		}

		/// <summary>
		/// Creates an AABB from min/max corners.
		/// </summary>
		public static AABB FromMinMax(Vector3 min, Vector3 max) {
			return new AABB(min, max - min);
		}

		/// <summary>
		/// Gets the min corner of the AABB.
		/// </summary>
		public Vector3 Min => Position;

		/// <summary>
		/// Gets the max corner of the AABB.
		/// </summary>
		public Vector3 Max => Position + Size;

		/// <summary>
		/// Gets the center of the AABB.
		/// </summary>
		public Vector3 Center => Position + Size * 0.5f;
	}
}
