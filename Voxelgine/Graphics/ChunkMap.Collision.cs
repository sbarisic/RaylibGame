using System;
using System.Collections.Generic;
using System.Numerics;
using Raylib_cs;
using Voxelgine.Engine;

namespace Voxelgine.Graphics
{
	public unsafe partial class ChunkMap
	{
		// RaycastPos: Returns the first solid block hit by a block-based raycast, or Vector3.Zero if none is found.
		public Vector3 RaycastPos(Vector3 Origin, float Distance, Vector3 Dir, out Vector3 FaceDir)
		{
			// Block-based raycast: returns the first solid block hit, or Vector3.Zero if none
			Vector3 hitPos = Vector3.Zero;
			Vector3 hitFace = Vector3.Zero;
			bool found = Voxelgine.Utils.Raycast(Origin, Dir, Distance, (x, y, z, face) =>
			{

				if (BlockInfo.IsSolid(GetBlock(x, y, z)))
				{
					hitPos = new Vector3(x, y, z);
					hitFace = face;
					return true;
				}

				return false;
			});
			FaceDir = hitFace;
			return found ? hitPos : Vector3.Zero;
		}

		/// <summary>
		/// Raycasts against solid blocks and returns the precise intersection point on the block face,
		/// rather than the integer block position. Returns false if no block was hit.
		/// </summary>
		/// <param name="Origin">Ray origin.</param>
		/// <param name="Distance">Maximum ray distance.</param>
		/// <param name="Dir">Ray direction (does not need to be normalized).</param>
		/// <param name="HitPoint">Precise point on the block face where the ray intersects.</param>
		/// <param name="FaceDir">Normal of the face that was hit.</param>
		/// <returns>True if a solid block was hit.</returns>
		public bool RaycastPrecise(Vector3 Origin, float Distance, Vector3 Dir, out Vector3 HitPoint, out Vector3 FaceDir)
		{
			Vector3 blockPos = RaycastPos(Origin, Distance, Dir, out FaceDir);
			if (blockPos == Vector3.Zero)
			{
				HitPoint = Vector3.Zero;
				return false;
			}

			// Compute the precise intersection point on the block face plane.
			// The face normal tells us which axis-aligned plane was entered.
			// In the DDA, face = -Step, so:
			// face (-1,0,0) → ray was stepping +X, entered block through its -X face → plane at blockPos.X
			// face (1,0,0)  → ray was stepping -X, entered block through its +X face → plane at blockPos.X + 1
			// face (0,-1,0) → plane at blockPos.Y
			// face (0,1,0)  → plane at blockPos.Y + 1
			// face (0,0,-1) → plane at blockPos.Z
			// face (0,0,1)  → plane at blockPos.Z + 1
			float planeValue;
			float dirComponent;
			float originComponent;

			if (MathF.Abs(FaceDir.X) > 0.5f)
			{
				planeValue = FaceDir.X > 0 ? blockPos.X + 1f : blockPos.X;
				dirComponent = Dir.X;
				originComponent = Origin.X;
			}
			else if (MathF.Abs(FaceDir.Y) > 0.5f)
			{
				planeValue = FaceDir.Y > 0 ? blockPos.Y + 1f : blockPos.Y;
				dirComponent = Dir.Y;
				originComponent = Origin.Y;
			}
			else
			{
				planeValue = FaceDir.Z > 0 ? blockPos.Z + 1f : blockPos.Z;
				dirComponent = Dir.Z;
				originComponent = Origin.Z;
			}

			if (MathF.Abs(dirComponent) < 1e-8f)
			{
				// Ray is parallel to the face plane — fall back to block center on face
				HitPoint = blockPos + new Vector3(0.5f, 0.5f, 0.5f) + FaceDir * 0.5f;
				return true;
			}

			float t = (planeValue - originComponent) / dirComponent;
			HitPoint = Origin + Dir * t;
			return true;
		}

		// Collide: Checks if the position is inside a solid block, or if moving in ProbeDir hits a block. Returns true and the collision normal if a block is hit, otherwise false.
		public bool Collide(Vector3 Pos, Vector3 ProbeDir, out Vector3 PickNormal)
		{
			// Check if the position is inside a solid block, or if moving in ProbeDir hits a block
			Vector3 probe = Pos + ProbeDir * 0.1f;

			if (BlockInfo.IsSolid(GetBlock((int)MathF.Floor(probe.X), (int)MathF.Floor(probe.Y), (int)MathF.Floor(probe.Z))))
			{

				if (ProbeDir != Vector3.Zero)
					PickNormal = -Vector3.Normalize(ProbeDir);
				else
					PickNormal = Vector3.Zero;

				return true;
			}

			PickNormal = Vector3.Zero;
			return false;
		}

		public bool HasBlocksInBounds(Vector3 pos, Vector3 size, bool SolidOnly = true)
		{
			Vector3 min = pos;
			Vector3 max = pos + size;

			return HasBlocksInBoundsMinMax(min, max, SolidOnly);
		}

		public bool IsSolid(int X, int Y, int Z) => BlockInfo.IsSolid(GetBlock(X, Y, Z));

		public bool IsSolid(Vector3 Pos)
		{
			return IsSolid((int)MathF.Floor(Pos.X), (int)MathF.Floor(Pos.Y), (int)MathF.Floor(Pos.Z));
		}

		public bool HasBlocksInBoundsMinMax(Vector3 min, Vector3 max, bool SolidOnly = true)
		{
			int minX = (int)MathF.Floor(min.X);
			int minY = (int)MathF.Floor(min.Y);
			int minZ = (int)MathF.Floor(min.Z);
			int maxX = (int)MathF.Floor(max.X);
			int maxY = (int)MathF.Floor(max.Y);
			int maxZ = (int)MathF.Floor(max.Z);

			for (int x = minX; x <= maxX; x++)
				for (int y = minY; y <= maxY; y++)
					for (int z = minZ; z <= maxZ; z++)
					{
						if (SolidOnly)
						{
							if (IsSolid(x, y, z))
								return true;

						}
						else
						{
							if (GetBlock(x, y, z) != BlockType.None)
								return true;
						}
					}
			return false;
		}

		public RayCollision RaycastRay(Ray R, float MaxLen)
		{
			RayCollision closest = new RayCollision() { Hit = false };
			float closestDist = float.MaxValue;

			foreach (var KV in Chunks.Items)
			{
				Vector3 ChunkPos = KV.Key * new Vector3(Chunk.ChunkSize);

				RayCollision Col = KV.Value.Collide(ChunkPos, R);
				if (Col.Hit && Col.Distance <= MaxLen && Col.Distance < closestDist)
				{
					closest = Col;
					closestDist = Col.Distance;
				}
			}

			return closest;
		}

		/// <summary>
		/// Creates a new pathfinder instance for this map.
		/// </summary>
		/// <param name="entityHeight">Height of the entity in blocks (default 2).</param>
		/// <param name="entityWidth">Width of the entity in blocks (default 1).</param>
		/// <returns>A VoxelPathfinder configured for this map.</returns>
		public Voxelgine.Engine.Pathfinding.VoxelPathfinder CreatePathfinder(int entityHeight = 2, int entityWidth = 1)
		{
			return new Voxelgine.Engine.Pathfinding.VoxelPathfinder(this)
			{
				EntityHeight = entityHeight,
				EntityWidth = entityWidth
			};
		}

		/// <summary>
		/// Finds a path between two positions using A* pathfinding.
		/// Creates a temporary pathfinder - for repeated pathfinding, use CreatePathfinder() instead.
		/// </summary>
		/// <param name="start">Starting world position.</param>
		/// <param name="end">Target world position.</param>
		/// <param name="entityHeight">Height of the entity in blocks (default 2).</param>
		/// <returns>List of waypoints from start to end, or empty list if no path found.</returns>
		public List<Vector3> FindPath(Vector3 start, Vector3 end, int entityHeight = 2)
		{
			var pathfinder = new Voxelgine.Engine.Pathfinding.VoxelPathfinder(this)
			{
				EntityHeight = entityHeight
			};
			return pathfinder.FindPath(start, end);
		}
	}
}
