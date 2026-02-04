using System;
using System.Collections.Generic;
using System.Numerics;
using Voxelgine.Graphics;

namespace Voxelgine.Engine.Pathfinding
{
	/// <summary>
	/// A* pathfinder for 3D voxel terrain navigation.
	/// Finds paths through walkable spaces (non-solid blocks with solid ground below).
	/// </summary>
	public class VoxelPathfinder
	{
		private readonly ChunkMap _map;

		// Movement costs
		private const float HorizontalCost = 1.0f;
		private const float DiagonalCost = 1.414f; // sqrt(2)
		private const float VerticalCost = 1.5f;   // Slightly discourage vertical movement

		// Search limits
		private const int MaxIterations = 10000;
		private const int MaxPathLength = 500;

		/// <summary>
		/// Height of the entity in blocks (default 2 for humanoid).
		/// Used to check if entity can fit through spaces.
		/// </summary>
		public int EntityHeight { get; set; } = 2;

		/// <summary>
		/// Width of the entity in blocks (default 1).
		/// </summary>
		public int EntityWidth { get; set; } = 1;

		/// <summary>
		/// Maximum fall distance the entity can handle (default 3 blocks).
		/// </summary>
		public int MaxFallDistance { get; set; } = 3;

		/// <summary>
		/// Maximum jump height the entity can handle (default 1 block).
		/// </summary>
		public int MaxJumpHeight { get; set; } = 1;

		/// <summary>
		/// Whether to allow diagonal movement (default true).
		/// </summary>
		public bool AllowDiagonal { get; set; } = true;

		public VoxelPathfinder(ChunkMap map)
		{
			_map = map ?? throw new ArgumentNullException(nameof(map));
		}

		/// <summary>
		/// Finds a path from start to end position using A* algorithm.
		/// </summary>
		/// <param name="start">Starting world position (will be floored to block coords).</param>
		/// <param name="end">Target world position (will be floored to block coords).</param>
		/// <returns>List of waypoints from start to end, or empty list if no path found.</returns>
		public List<Vector3> FindPath(Vector3 start, Vector3 end)
		{
			// Convert to integer block coordinates
			Vector3Int startBlock = ToBlockCoord(start);
			Vector3Int endBlock = ToBlockCoord(end);

			// Snap to walkable positions
			startBlock = FindNearestWalkable(startBlock);
			endBlock = FindNearestWalkable(endBlock);

			if (!IsWalkable(startBlock) || !IsWalkable(endBlock))
				return new List<Vector3>();

			// A* data structures
			var openSet = new PriorityQueue<Vector3Int, float>();
			var cameFrom = new Dictionary<Vector3Int, Vector3Int>();
			var gScore = new Dictionary<Vector3Int, float>();
			var fScore = new Dictionary<Vector3Int, float>();
			var closedSet = new HashSet<Vector3Int>();

			gScore[startBlock] = 0;
			fScore[startBlock] = Heuristic(startBlock, endBlock);
			openSet.Enqueue(startBlock, fScore[startBlock]);

			int iterations = 0;

			while (openSet.Count > 0 && iterations < MaxIterations)
			{
				iterations++;
				var current = openSet.Dequeue();

				// Skip if already processed
				if (closedSet.Contains(current))
					continue;

				// Reached goal
				if (current == endBlock)
					return ReconstructPath(cameFrom, current);

				closedSet.Add(current);

				// Explore neighbors
				foreach (var neighbor in GetNeighbors(current))
				{
					if (closedSet.Contains(neighbor))
						continue;

					float moveCost = GetMovementCost(current, neighbor);
					float tentativeG = gScore.GetValueOrDefault(current, float.MaxValue) + moveCost;

					if (tentativeG < gScore.GetValueOrDefault(neighbor, float.MaxValue))
					{
						cameFrom[neighbor] = current;
						gScore[neighbor] = tentativeG;
						fScore[neighbor] = tentativeG + Heuristic(neighbor, endBlock);
						openSet.Enqueue(neighbor, fScore[neighbor]);
					}
				}
			}

			// No path found
			return new List<Vector3>();
		}

		/// <summary>
		/// Checks if a position is walkable (entity can stand there).
		/// </summary>
		public bool IsWalkable(Vector3Int pos)
		{
			// Check ground below - must be solid
			if (!_map.IsSolid(pos.X, pos.Y - 1, pos.Z))
				return false;

			// Check space for entity body - must be non-solid
			for (int y = 0; y < EntityHeight; y++)
			{
				if (_map.IsSolid(pos.X, pos.Y + y, pos.Z))
					return false;
			}

			// For wider entities, check adjacent blocks too
			if (EntityWidth > 1)
			{
				int halfWidth = EntityWidth / 2;
				for (int dx = -halfWidth; dx <= halfWidth; dx++)
				{
					for (int dz = -halfWidth; dz <= halfWidth; dz++)
					{
						if (dx == 0 && dz == 0) continue;

						// Ground must be solid
						if (!_map.IsSolid(pos.X + dx, pos.Y - 1, pos.Z + dz))
							return false;

						// Body space must be clear
						for (int y = 0; y < EntityHeight; y++)
						{
							if (_map.IsSolid(pos.X + dx, pos.Y + y, pos.Z + dz))
								return false;
						}
					}
				}
			}

			return true;
		}

		/// <summary>
		/// Checks if a position is walkable using Vector3.
		/// </summary>
		public bool IsWalkable(Vector3 pos) => IsWalkable(ToBlockCoord(pos));

		private IEnumerable<Vector3Int> GetNeighbors(Vector3Int pos)
		{
			// Cardinal directions (horizontal)
			int[] dx = { 1, -1, 0, 0 };
			int[] dz = { 0, 0, 1, -1 };

			for (int i = 0; i < 4; i++)
			{
				// Same level
				var neighbor = new Vector3Int(pos.X + dx[i], pos.Y, pos.Z + dz[i]);
				if (IsWalkable(neighbor))
				{
					yield return neighbor;
					continue;
				}

				// Step up (jump)
				for (int jumpY = 1; jumpY <= MaxJumpHeight; jumpY++)
				{
					var upNeighbor = new Vector3Int(pos.X + dx[i], pos.Y + jumpY, pos.Z + dz[i]);
					if (IsWalkable(upNeighbor) && CanJumpTo(pos, upNeighbor))
					{
						yield return upNeighbor;
						break;
					}
				}

				// Step down (fall)
				for (int fallY = 1; fallY <= MaxFallDistance; fallY++)
				{
					var downNeighbor = new Vector3Int(pos.X + dx[i], pos.Y - fallY, pos.Z + dz[i]);
					if (IsWalkable(downNeighbor))
					{
						yield return downNeighbor;
						break;
					}
				}
			}

			// Diagonal directions (if allowed)
			if (AllowDiagonal)
			{
				int[] ddx = { 1, 1, -1, -1 };
				int[] ddz = { 1, -1, 1, -1 };

				for (int i = 0; i < 4; i++)
				{
					var neighbor = new Vector3Int(pos.X + ddx[i], pos.Y, pos.Z + ddz[i]);

					// For diagonals, also check that both cardinal directions are passable
					// to prevent corner-cutting through walls
					var cardinalX = new Vector3Int(pos.X + ddx[i], pos.Y, pos.Z);
					var cardinalZ = new Vector3Int(pos.X, pos.Y, pos.Z + ddz[i]);

					if (IsWalkable(neighbor) && IsPassable(cardinalX) && IsPassable(cardinalZ))
					{
						yield return neighbor;
					}
				}
			}
		}

		/// <summary>
		/// Checks if entity can pass through a position (doesn't need ground).
		/// </summary>
		private bool IsPassable(Vector3Int pos)
		{
			for (int y = 0; y < EntityHeight; y++)
			{
				if (_map.IsSolid(pos.X, pos.Y + y, pos.Z))
					return false;
			}
			return true;
		}

		/// <summary>
		/// Checks if entity can jump from current position to target.
		/// </summary>
		private bool CanJumpTo(Vector3Int from, Vector3Int to)
		{
			int heightDiff = to.Y - from.Y;
			if (heightDiff <= 0 || heightDiff > MaxJumpHeight)
				return false;

			// Check head clearance during jump
			for (int y = EntityHeight; y < EntityHeight + heightDiff; y++)
			{
				if (_map.IsSolid(from.X, from.Y + y, from.Z))
					return false;
			}

			return true;
		}

		private float GetMovementCost(Vector3Int from, Vector3Int to)
		{
			int dx = Math.Abs(to.X - from.X);
			int dy = Math.Abs(to.Y - from.Y);
			int dz = Math.Abs(to.Z - from.Z);

			float cost = 0;

			// Horizontal movement
			if (dx + dz == 2) // Diagonal
				cost = DiagonalCost;
			else // Cardinal
				cost = HorizontalCost;

			// Vertical movement adds cost
			if (dy > 0)
				cost += dy * VerticalCost;

			return cost;
		}

		private float Heuristic(Vector3Int a, Vector3Int b)
		{
			// Euclidean distance with slight preference for straight lines
			float dx = Math.Abs(a.X - b.X);
			float dy = Math.Abs(a.Y - b.Y);
			float dz = Math.Abs(a.Z - b.Z);
			return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
		}

		private Vector3Int FindNearestWalkable(Vector3Int pos)
		{
			// Already walkable
			if (IsWalkable(pos))
				return pos;

			// Search in expanding radius
			for (int radius = 1; radius <= 5; radius++)
			{
				for (int dy = -radius; dy <= radius; dy++)
				{
					for (int dx = -radius; dx <= radius; dx++)
					{
						for (int dz = -radius; dz <= radius; dz++)
						{
							var check = new Vector3Int(pos.X + dx, pos.Y + dy, pos.Z + dz);
							if (IsWalkable(check))
								return check;
						}
					}
				}
			}

			return pos; // Return original if nothing found
		}

		private List<Vector3> ReconstructPath(Dictionary<Vector3Int, Vector3Int> cameFrom, Vector3Int current)
		{
			var path = new List<Vector3>();
			path.Add(ToWorldPos(current));

			while (cameFrom.ContainsKey(current))
			{
				current = cameFrom[current];
				path.Add(ToWorldPos(current));

				if (path.Count > MaxPathLength)
					break;
			}

			path.Reverse();
			return path;
		}

		private static Vector3Int ToBlockCoord(Vector3 pos)
		{
			return new Vector3Int(
				(int)MathF.Floor(pos.X),
				(int)MathF.Floor(pos.Y),
				(int)MathF.Floor(pos.Z)
			);
		}

		private static Vector3 ToWorldPos(Vector3Int block)
		{
			// Return center of block at foot level
			return new Vector3(block.X + 0.5f, block.Y, block.Z + 0.5f);
		}
	}

	/// <summary>
	/// Simple integer 3D vector for pathfinding grid coordinates.
	/// </summary>
	public readonly struct Vector3Int : IEquatable<Vector3Int>
	{
		public readonly int X;
		public readonly int Y;
		public readonly int Z;

		public Vector3Int(int x, int y, int z)
		{
			X = x;
			Y = y;
			Z = z;
		}

		public static bool operator ==(Vector3Int a, Vector3Int b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
		public static bool operator !=(Vector3Int a, Vector3Int b) => !(a == b);

		public override bool Equals(object obj) => obj is Vector3Int other && Equals(other);
		public bool Equals(Vector3Int other) => this == other;
		public override int GetHashCode() => HashCode.Combine(X, Y, Z);
		public override string ToString() => $"({X}, {Y}, {Z})";

		public static implicit operator Vector3(Vector3Int v) => new Vector3(v.X, v.Y, v.Z);
	}
}
