using Raylib_cs;
using System.Numerics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Extension methods for converting between <see cref="AABB"/> and Raylib <see cref="BoundingBox"/>.
	/// </summary>
	public static class AABBExtensions
	{
		/// <summary>
		/// Creates an AABB from a Raylib BoundingBox.
		/// </summary>
		public static AABB ToAABB(this BoundingBox bb)
		{
			return new AABB(bb.Min, bb.Max - bb.Min);
		}

		/// <summary>
		/// Converts this AABB to a Raylib BoundingBox.
		/// </summary>
		public static BoundingBox ToBoundingBox(this AABB aabb)
		{
			return new BoundingBox(aabb.Position, aabb.Position + aabb.Size);
		}
	}
}
