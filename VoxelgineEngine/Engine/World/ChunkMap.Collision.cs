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
			if (IsSolid(x, y, z))
			{
				hit = new VoxelRaycastHit(x, y, z, origin, Vector3.Zero, 0f);
				return true;
			}

			int stepX = Math.Sign(direction.X);
			int stepY = Math.Sign(direction.Y);
			int stepZ = Math.Sign(direction.Z);
			float deltaX = stepX == 0 ? float.PositiveInfinity : MathF.Abs(1f / direction.X);
			float deltaY = stepY == 0 ? float.PositiveInfinity : MathF.Abs(1f / direction.Y);
			float deltaZ = stepZ == 0 ? float.PositiveInfinity : MathF.Abs(1f / direction.Z);
			float nextX = InitialBoundaryDistance(origin.X, direction.X, x, stepX);
			float nextY = InitialBoundaryDistance(origin.Y, direction.Y, y, stepY);
			float nextZ = InitialBoundaryDistance(origin.Z, direction.Z, z, stepZ);

			while (true)
			{
				float distance;
				Vector3 normal;
				if (nextX <= nextY && nextX <= nextZ)
				{
					distance = nextX;
					if (distance > maximumDistance)
						return false;
					x += stepX;
					nextX += deltaX;
					normal = new Vector3(-stepX, 0f, 0f);
				}
				else if (nextY <= nextZ)
				{
					distance = nextY;
					if (distance > maximumDistance)
						return false;
					y += stepY;
					nextY += deltaY;
					normal = new Vector3(0f, -stepY, 0f);
				}
				else
				{
					distance = nextZ;
					if (distance > maximumDistance)
						return false;
					z += stepZ;
					nextZ += deltaZ;
					normal = new Vector3(0f, 0f, -stepZ);
				}

				if (IsSolid(x, y, z))
				{
					hit = new VoxelRaycastHit(x, y, z, origin + direction * distance, normal, distance);
					return true;
				}
			}
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

		public bool IsSolid(int x, int y, int z) => BlockInfo.IsSolid(GetBlock(x, y, z));

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
