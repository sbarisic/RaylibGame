using System.Numerics;
using Voxelgine.Engine;

namespace Voxelgine.Graphics
{
	public unsafe partial class ChunkMap
	{
		public Vector3 RaycastPos(
			Vector3 origin,
			float distance,
			Vector3 direction,
			out Vector3 faceDirection)
		{
			Vector3 hitPosition = Vector3.Zero;
			Vector3 hitFace = Vector3.Zero;
			bool found = Utils.Raycast(origin, direction, distance, (x, y, z, face) =>
			{
				if (!BlockInfo.IsSolid(GetBlock(x, y, z)))
					return false;

				hitPosition = new Vector3(x, y, z);
				hitFace = face;
				return true;
			});

			faceDirection = hitFace;
			return found ? hitPosition : Vector3.Zero;
		}

		public bool RaycastPrecise(
			Vector3 origin,
			float distance,
			Vector3 direction,
			out Vector3 hitPoint,
			out Vector3 faceDirection)
		{
			Vector3 blockPosition = RaycastPos(origin, distance, direction, out faceDirection);
			if (blockPosition == Vector3.Zero)
			{
				hitPoint = Vector3.Zero;
				return false;
			}

			float planeValue;
			float directionComponent;
			float originComponent;
			if (MathF.Abs(faceDirection.X) > 0.5f)
			{
				planeValue = faceDirection.X > 0f ? blockPosition.X + 1f : blockPosition.X;
				directionComponent = direction.X;
				originComponent = origin.X;
			}
			else if (MathF.Abs(faceDirection.Y) > 0.5f)
			{
				planeValue = faceDirection.Y > 0f ? blockPosition.Y + 1f : blockPosition.Y;
				directionComponent = direction.Y;
				originComponent = origin.Y;
			}
			else
			{
				planeValue = faceDirection.Z > 0f ? blockPosition.Z + 1f : blockPosition.Z;
				directionComponent = direction.Z;
				originComponent = origin.Z;
			}

			if (MathF.Abs(directionComponent) < 1e-8f)
			{
				hitPoint = blockPosition + new Vector3(0.5f) + faceDirection * 0.5f;
				return true;
			}

			float amount = (planeValue - originComponent) / directionComponent;
			hitPoint = origin + direction * amount;
			return true;
		}

		public bool Collide(Vector3 position, Vector3 probeDirection, out Vector3 collisionNormal)
		{
			Vector3 probe = position + probeDirection * 0.1f;
			if (BlockInfo.IsSolid(GetBlock(
				(int)MathF.Floor(probe.X),
				(int)MathF.Floor(probe.Y),
				(int)MathF.Floor(probe.Z))))
			{
				collisionNormal = probeDirection == Vector3.Zero
					? Vector3.Zero
					: -Vector3.Normalize(probeDirection);
				return true;
			}

			collisionNormal = Vector3.Zero;
			return false;
		}

		public bool HasBlocksInBounds(Vector3 position, Vector3 size, bool solidOnly = true) =>
			HasBlocksInBoundsMinMax(position, position + size, solidOnly);

		public bool IsSolid(int x, int y, int z) => BlockInfo.IsSolid(GetBlock(x, y, z));

		public bool IsSolid(Vector3 position) => IsSolid(
			(int)MathF.Floor(position.X),
			(int)MathF.Floor(position.Y),
			(int)MathF.Floor(position.Z));

		public bool HasBlocksInBoundsMinMax(Vector3 minimum, Vector3 maximum, bool solidOnly = true)
		{
			int minimumX = (int)MathF.Floor(minimum.X);
			int minimumY = (int)MathF.Floor(minimum.Y);
			int minimumZ = (int)MathF.Floor(minimum.Z);
			int maximumX = (int)MathF.Floor(maximum.X);
			int maximumY = (int)MathF.Floor(maximum.Y);
			int maximumZ = (int)MathF.Floor(maximum.Z);

			for (int x = minimumX; x <= maximumX; x++)
			{
				for (int y = minimumY; y <= maximumY; y++)
				{
					for (int z = minimumZ; z <= maximumZ; z++)
					{
						if (solidOnly)
						{
							if (IsSolid(x, y, z))
								return true;
						}
						else if (GetBlock(x, y, z) != BlockType.None)
						{
							return true;
						}
					}
				}
			}

			return false;
		}

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
