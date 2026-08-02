#if WINDOWS
using System.Numerics;
using FishGfx;
using FishGfx.Voxels;
using Voxelgine.Engine;
using Voxelgine.Graphics;

namespace Voxelgine.FishGfxClient.Voxels;

/// <summary>
/// Builds stair display geometry from the same half-cell occupancy used by the shape catalog.
/// </summary>
internal static class StairVoxelModelBuilder
{
	private const int GridSize = 2;
	private const float CellSize = 1f / GridSize;
	private static readonly int[] TriangleOrder = { 0, 1, 2, 0, 2, 3 };

	public static VoxelModel Create(BlockValue value, int textureLayer)
	{
		bool[,,] occupied = BuildOccupancy(value);
		List<VoxelVertex> vertices = new(132);

		for (int x = 0; x < GridSize; x++)
		for (int y = 0; y < GridSize; y++)
		for (int z = 0; z < GridSize; z++)
		{
			if (!occupied[x, y, z])
				continue;

			Vector3 min = new(x * CellSize, y * CellSize, z * CellSize);
			Vector3 max = min + new Vector3(CellSize);
			if (!IsOccupied(occupied, x + 1, y, z))
				AppendFace(vertices, VoxelFace.PositiveX, min, max, textureLayer);
			if (!IsOccupied(occupied, x - 1, y, z))
				AppendFace(vertices, VoxelFace.NegativeX, min, max, textureLayer);
			if (!IsOccupied(occupied, x, y + 1, z))
				AppendFace(vertices, VoxelFace.PositiveY, min, max, textureLayer);
			if (!IsOccupied(occupied, x, y - 1, z))
				AppendFace(vertices, VoxelFace.NegativeY, min, max, textureLayer);
			if (!IsOccupied(occupied, x, y, z + 1))
				AppendFace(vertices, VoxelFace.PositiveZ, min, max, textureLayer);
			if (!IsOccupied(occupied, x, y, z - 1))
				AppendFace(vertices, VoxelFace.NegativeZ, min, max, textureLayer);
		}

		return new VoxelModel(vertices);
	}

	internal static VoxelModel CreateCube(VoxelFaceTiles tiles)
	{
		List<VoxelVertex> vertices = new(36);
		Vector3 min = Vector3.Zero;
		Vector3 max = Vector3.One;
		foreach (VoxelFace face in Enum.GetValues<VoxelFace>())
			AppendFace(vertices, face, min, max, tiles[face]);
		return new VoxelModel(vertices);
	}

	private static bool[,,] BuildOccupancy(BlockValue value)
	{
		bool[,,] result = new bool[GridSize, GridSize, GridSize];
		IReadOnlyList<AABB> boxes = BlockShapeCatalog.GetCollisionBoxes(value);
		for (int x = 0; x < GridSize; x++)
		for (int y = 0; y < GridSize; y++)
		for (int z = 0; z < GridSize; z++)
		{
			Vector3 center = new(
				(x + 0.5f) * CellSize,
				(y + 0.5f) * CellSize,
				(z + 0.5f) * CellSize);
			result[x, y, z] = boxes.Any(box =>
				center.X >= box.Min.X && center.X <= box.Max.X &&
				center.Y >= box.Min.Y && center.Y <= box.Max.Y &&
				center.Z >= box.Min.Z && center.Z <= box.Max.Z);
		}

		return result;
	}

	private static bool IsOccupied(bool[,,] occupied, int x, int y, int z) =>
		x >= 0 && x < GridSize && y >= 0 && y < GridSize && z >= 0 && z < GridSize && occupied[x, y, z];

	internal static void AppendFace(
		List<VoxelVertex> vertices,
		VoxelFace face,
		Vector3 min,
		Vector3 max,
		int textureLayer)
	{
		Span<Vector3> positions = stackalloc Vector3[4];
		Span<Vector2> uvs = stackalloc Vector2[4];
		Vector3 normal;

		switch (face)
		{
			case VoxelFace.PositiveX:
				normal = Vector3.UnitX;
				positions[0] = new(max.X, min.Y, min.Z);
				positions[1] = new(max.X, max.Y, min.Z);
				positions[2] = new(max.X, max.Y, max.Z);
				positions[3] = new(max.X, min.Y, max.Z);
				for (int index = 0; index < 4; index++) uvs[index] = new(1 - positions[index].Z, positions[index].Y);
				break;
			case VoxelFace.NegativeX:
				normal = -Vector3.UnitX;
				positions[0] = new(min.X, min.Y, max.Z);
				positions[1] = new(min.X, max.Y, max.Z);
				positions[2] = new(min.X, max.Y, min.Z);
				positions[3] = new(min.X, min.Y, min.Z);
				for (int index = 0; index < 4; index++) uvs[index] = new(positions[index].Z, positions[index].Y);
				break;
			case VoxelFace.PositiveY:
				normal = Vector3.UnitY;
				positions[0] = new(min.X, max.Y, max.Z);
				positions[1] = new(max.X, max.Y, max.Z);
				positions[2] = new(max.X, max.Y, min.Z);
				positions[3] = new(min.X, max.Y, min.Z);
				for (int index = 0; index < 4; index++) uvs[index] = new(positions[index].X, 1 - positions[index].Z);
				break;
			case VoxelFace.NegativeY:
				normal = -Vector3.UnitY;
				positions[0] = new(min.X, min.Y, min.Z);
				positions[1] = new(max.X, min.Y, min.Z);
				positions[2] = new(max.X, min.Y, max.Z);
				positions[3] = new(min.X, min.Y, max.Z);
				for (int index = 0; index < 4; index++) uvs[index] = new(1 - positions[index].X, 1 - positions[index].Z);
				break;
			case VoxelFace.PositiveZ:
				normal = Vector3.UnitZ;
				positions[0] = new(max.X, min.Y, max.Z);
				positions[1] = new(max.X, max.Y, max.Z);
				positions[2] = new(min.X, max.Y, max.Z);
				positions[3] = new(min.X, min.Y, max.Z);
				for (int index = 0; index < 4; index++) uvs[index] = new(positions[index].X, positions[index].Y);
				break;
			case VoxelFace.NegativeZ:
				normal = -Vector3.UnitZ;
				positions[0] = new(min.X, min.Y, min.Z);
				positions[1] = new(min.X, max.Y, min.Z);
				positions[2] = new(max.X, max.Y, min.Z);
				positions[3] = new(max.X, min.Y, min.Z);
				for (int index = 0; index < 4; index++) uvs[index] = new(1 - positions[index].X, positions[index].Y);
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(face));
		}

		Vector4 tangent = CalculateTangent(positions[0], positions[1], positions[2], uvs[0], uvs[1], uvs[2], normal);
		foreach (int corner in TriangleOrder)
		{
			VoxelVertex vertex = new(positions[corner], Color.White, uvs[corner], normal)
			{
				Tangent = tangent,
				TextureLayer = textureLayer,
			};
			vertices.Add(vertex);
		}
	}

	private static Vector4 CalculateTangent(
		Vector3 p0,
		Vector3 p1,
		Vector3 p2,
		Vector2 uv0,
		Vector2 uv1,
		Vector2 uv2,
		Vector3 normal)
	{
		Vector3 edge1 = p1 - p0;
		Vector3 edge2 = p2 - p0;
		Vector2 delta1 = uv1 - uv0;
		Vector2 delta2 = uv2 - uv0;
		float determinant = delta1.X * delta2.Y - delta1.Y * delta2.X;
		if (MathF.Abs(determinant) < 1e-6f)
			throw new InvalidOperationException("Stair face has degenerate texture coordinates.");

		float inverse = 1f / determinant;
		Vector3 tangent = Vector3.Normalize((edge1 * delta2.Y - edge2 * delta1.Y) * inverse);
		Vector3 bitangent = Vector3.Normalize((edge2 * delta1.X - edge1 * delta2.X) * inverse);
		float handedness = Vector3.Dot(Vector3.Cross(normal, tangent), bitangent) < 0 ? -1 : 1;
		return new Vector4(tangent, handedness);
	}
}
#endif
