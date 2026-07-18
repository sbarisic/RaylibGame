using System.Numerics;

namespace Voxelgine.Engine.Geometry;

public readonly record struct Ray3(Vector3 Origin, Vector3 Direction)
{
	public Ray3 Normalized()
	{
		if (Direction.LengthSquared() <= float.Epsilon)
		{
			throw new InvalidOperationException("A ray direction cannot be zero.");
		}

		return this with { Direction = Vector3.Normalize(Direction) };
	}
}

public readonly record struct Triangle3(Vector3 A, Vector3 B, Vector3 C);

public readonly record struct TriangleHit(float Distance, Vector3 Position, Vector3 Normal);

public static class TriangleIntersection
{
	private const float Epsilon = 0.000001f;

	public static bool TryIntersect(
		in Ray3 ray,
		in Triangle3 triangle,
		out TriangleHit hit,
		bool cullBackFaces = false
	)
	{
		Vector3 edge1 = triangle.B - triangle.A;
		Vector3 edge2 = triangle.C - triangle.A;
		Vector3 cross = Vector3.Cross(ray.Direction, edge2);
		float determinant = Vector3.Dot(edge1, cross);

		if (cullBackFaces ? determinant <= Epsilon : MathF.Abs(determinant) <= Epsilon)
		{
			hit = default;
			return false;
		}

		float inverseDeterminant = 1 / determinant;
		Vector3 fromA = ray.Origin - triangle.A;
		float u = Vector3.Dot(fromA, cross) * inverseDeterminant;
		if (u < 0 || u > 1)
		{
			hit = default;
			return false;
		}

		Vector3 q = Vector3.Cross(fromA, edge1);
		float v = Vector3.Dot(ray.Direction, q) * inverseDeterminant;
		if (v < 0 || u + v > 1)
		{
			hit = default;
			return false;
		}

		float distance = Vector3.Dot(edge2, q) * inverseDeterminant;
		if (distance < 0)
		{
			hit = default;
			return false;
		}

		Vector3 normal = Vector3.Normalize(Vector3.Cross(edge1, edge2));
		hit = new TriangleHit(distance, ray.Origin + ray.Direction * distance, normal);
		return true;
	}

	public static bool TryIntersectClosest(
		in Ray3 ray,
		IReadOnlyList<Triangle3> triangles,
		out TriangleHit hit,
		bool cullBackFaces = false
	)
	{
		ArgumentNullException.ThrowIfNull(triangles);
		hit = default;
		bool found = false;
		float closest = float.PositiveInfinity;
		foreach (Triangle3 triangle in triangles)
		{
			if (TryIntersect(ray, triangle, out TriangleHit candidate, cullBackFaces)
				&& candidate.Distance < closest)
			{
				closest = candidate.Distance;
				hit = candidate;
				found = true;
			}
		}

		return found;
	}
}
