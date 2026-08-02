using System.Numerics;
using Voxelgine.Engine;

namespace Voxelgine.Graphics
{
	public unsafe partial class ChunkMap
	{
		public bool TryRaycast(
			Vector3 origin,
			Vector3 direction,
			float maximumDistance,
			out VoxelRaycastHit hit)
		{
			hit = default;
			if (!IsFinite(origin) ||
				!IsFinite(direction) ||
				!float.IsFinite(maximumDistance) ||
				maximumDistance < 0f)
			{
				return false;
			}

			float directionLengthSquared = direction.LengthSquared();
			if (directionLengthSquared <= 1e-12f)
				return false;

			direction /= MathF.Sqrt(directionLengthSquared);
			int x = (int)MathF.Floor(origin.X);
			int y = (int)MathF.Floor(origin.Y);
			int z = (int)MathF.Floor(origin.Z);
			int stepX = Math.Sign(direction.X);
			int stepY = Math.Sign(direction.Y);
			int stepZ = Math.Sign(direction.Z);
			float deltaX = stepX == 0 ? float.PositiveInfinity : MathF.Abs(1f / direction.X);
			float deltaY = stepY == 0 ? float.PositiveInfinity : MathF.Abs(1f / direction.Y);
			float deltaZ = stepZ == 0 ? float.PositiveInfinity : MathF.Abs(1f / direction.Z);
			float nextX = InitialBoundaryDistance(origin.X, direction.X, x, stepX);
			float nextY = InitialBoundaryDistance(origin.Y, direction.Y, y, stepY);
			float nextZ = InitialBoundaryDistance(origin.Z, direction.Z, z, stepZ);

			float cellEntryDistance = 0;
			while (cellEntryDistance <= maximumDistance)
			{
				if (UnknownColumnsAreBoundaries && !IsWorldColumnResident(x, z))
					return false;
				if (TryRaycastBlockShape(x, y, z, origin, direction, cellEntryDistance, maximumDistance, out float shapeDistance, out Vector3 shapeNormal))
				{
					hit = new VoxelRaycastHit(x, y, z, origin + direction * shapeDistance, shapeNormal, shapeDistance);
					return true;
				}

				float distance;
				if (nextX <= nextY && nextX <= nextZ)
				{
					distance = nextX;
					if (distance > maximumDistance)
						return false;
					x += stepX;
					nextX += deltaX;
				}
				else if (nextY <= nextZ)
				{
					distance = nextY;
					if (distance > maximumDistance)
						return false;
					y += stepY;
					nextY += deltaY;
				}
				else
				{
					distance = nextZ;
					if (distance > maximumDistance)
						return false;
					z += stepZ;
					nextZ += deltaZ;
				}
				cellEntryDistance = distance;
			}
			return false;
		}

		private bool TryRaycastBlockShape(
			int x, int y, int z, Vector3 origin, Vector3 direction,
			float minimumDistance, float maximumDistance,
			out float distance, out Vector3 normal)
		{
			distance = float.PositiveInfinity;
			normal = Vector3.Zero;
			BlockValue value = GetBlockValue(x, y, z);
			foreach (AABB local in BlockShapeCatalog.GetCollisionBoxes(value))
			{
				AABB world = local.Offset(new Vector3(x, y, z));
				if (!TryIntersectRayAabb(origin, direction, world, out float candidate, out Vector3 candidateNormal) ||
					candidate + 1e-5f < minimumDistance || candidate > maximumDistance || candidate >= distance)
					continue;
				distance = candidate;
				normal = candidateNormal;
			}
			return !float.IsPositiveInfinity(distance);
		}

		private static bool TryIntersectRayAabb(
			Vector3 origin, Vector3 direction, AABB bounds,
			out float distance, out Vector3 normal)
		{
			float near = float.NegativeInfinity;
			float far = float.PositiveInfinity;
			normal = Vector3.Zero;
			for (int axis = 0; axis < 3; axis++)
			{
				float originAxis = axis == 0 ? origin.X : axis == 1 ? origin.Y : origin.Z;
				float directionAxis = axis == 0 ? direction.X : axis == 1 ? direction.Y : direction.Z;
				float minimum = axis == 0 ? bounds.Min.X : axis == 1 ? bounds.Min.Y : bounds.Min.Z;
				float maximum = axis == 0 ? bounds.Max.X : axis == 1 ? bounds.Max.Y : bounds.Max.Z;
				if (MathF.Abs(directionAxis) < 1e-8f)
				{
					if (originAxis < minimum || originAxis > maximum) { distance = 0; return false; }
					continue;
				}
				float first = (minimum - originAxis) / directionAxis;
				float second = (maximum - originAxis) / directionAxis;
				float axisNear = MathF.Min(first, second);
				float axisFar = MathF.Max(first, second);
				if (axisNear > near)
				{
					near = axisNear;
					float sign = first < second ? -1 : 1;
					normal = axis == 0 ? new Vector3(sign, 0, 0) : axis == 1 ? new Vector3(0, sign, 0) : new Vector3(0, 0, sign);
				}
				far = MathF.Min(far, axisFar);
				if (near > far) { distance = 0; return false; }
			}
			if (far < 0) { distance = 0; return false; }
			if (near < 0) { distance = 0; normal = Vector3.Zero; return true; }
			distance = near;
			return true;
		}

		private static bool IsFinite(Vector3 value) =>
			float.IsFinite(value.X) &&
			float.IsFinite(value.Y) &&
			float.IsFinite(value.Z);

		private static float InitialBoundaryDistance(float origin, float direction, int cell, int step)
		{
			if (step == 0)
				return float.PositiveInfinity;
			float boundary = step > 0 ? cell + 1f : cell;
			return (boundary - origin) / direction;
		}

		public bool IsSolid(int x, int y, int z) =>
			(UnknownColumnsAreBoundaries && !IsWorldColumnResident(x, z)) ||
			BlockInfo.IsSolid(GetBlock(x, y, z));

		private bool IsWorldColumnResident(int worldX, int worldZ) => IsColumnResident(
			(int)Math.Floor((double)worldX / Chunk.ChunkSize),
			(int)Math.Floor((double)worldZ / Chunk.ChunkSize));

		public bool IsSolid(Vector3 position) => IsSolid(
			(int)MathF.Floor(position.X),
			(int)MathF.Floor(position.Y),
			(int)MathF.Floor(position.Z));

		public Engine.Pathfinding.VoxelPathfinder CreatePathfinder(
			int entityHeight = 2,
			int entityWidth = 1) => new(this)
		{
			EntityHeight = entityHeight,
			EntityWidth = entityWidth,
		};

		public List<Vector3> FindPath(Vector3 start, Vector3 end, int entityHeight = 2)
		{
			Engine.Pathfinding.VoxelPathfinder pathfinder = new(this)
			{
				EntityHeight = entityHeight,
			};
			return pathfinder.FindPath(start, end);
		}
	}
}
