using System.Numerics;
using Voxelgine.Engine.Geometry;

namespace VoxelgineEngine.Tests;

public class TriangleIntersectionTests
{
	private static readonly Triangle3 Triangle = new(
		new Vector3(-1, -1, 0),
		new Vector3(1, -1, 0),
		new Vector3(0, 1, 0)
	);

	[Fact]
	public void RayThroughTriangleReturnsDistancePositionAndNormal()
	{
		Ray3 ray = new(new Vector3(0, 0, 2), -Vector3.UnitZ);

		bool intersects = TriangleIntersection.TryIntersect(ray, Triangle, out TriangleHit hit);

		Assert.True(intersects);
		Assert.Equal(2, hit.Distance, 5);
		Assert.Equal(Vector3.Zero, hit.Position);
		Assert.Equal(Vector3.UnitZ, hit.Normal);
	}

	[Fact]
	public void RayOutsideTriangleMisses()
	{
		Ray3 ray = new(new Vector3(2, 2, 2), -Vector3.UnitZ);

		Assert.False(TriangleIntersection.TryIntersect(ray, Triangle, out _));
	}

	[Fact]
	public void ClosestIntersectionWinsRegardlessOfInputOrder()
	{
		Triangle3 near = Translate(Triangle, -1);
		Triangle3 far = Translate(Triangle, -4);
		Ray3 ray = new(new Vector3(0, 0, 1), -Vector3.UnitZ);

		Assert.True(TriangleIntersection.TryIntersectClosest(ray, [far, near], out TriangleHit hit));
		Assert.Equal(2, hit.Distance, 5);
	}

	private static Triangle3 Translate(Triangle3 triangle, float z)
	{
		Vector3 offset = new(0, 0, z);
		return new Triangle3(triangle.A + offset, triangle.B + offset, triangle.C + offset);
	}
}
