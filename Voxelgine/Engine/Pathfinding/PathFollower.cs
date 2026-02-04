using System;
using System.Collections.Generic;
using System.Numerics;
using Voxelgine.Graphics;

namespace Voxelgine.Engine.Pathfinding
{
	/// <summary>
	/// Component that enables an entity to follow paths calculated by VoxelPathfinder.
	/// Handles path following, waypoint management, and movement direction calculation.
	/// </summary>
	public class PathFollower
	{
		private readonly VoxelPathfinder _pathfinder;
		private List<Vector3> _currentPath = new();
		private int _currentWaypointIndex;
		private Vector3 _targetPosition;
		private bool _hasTarget;

		/// <summary>
		/// Distance threshold to consider a waypoint reached.
		/// </summary>
		public float WaypointReachDistance { get; set; } = 0.5f;

		/// <summary>
		/// Movement speed in blocks per second.
		/// </summary>
		public float MoveSpeed { get; set; } = 4.0f;

		/// <summary>
		/// Minimum distance to target before considering it reached.
		/// </summary>
		public float TargetReachDistance { get; set; } = 1.0f;

		/// <summary>
		/// Returns true if currently following a path.
		/// </summary>
		public bool IsFollowingPath => _currentPath.Count > 0 && _currentWaypointIndex < _currentPath.Count;

		/// <summary>
		/// Returns true if the final target has been reached.
		/// </summary>
		public bool HasReachedTarget { get; private set; }

		/// <summary>
		/// The current path being followed.
		/// </summary>
		public IReadOnlyList<Vector3> CurrentPath => _currentPath;

		/// <summary>
		/// The current waypoint index in the path.
		/// </summary>
		public int CurrentWaypointIndex => _currentWaypointIndex;

		/// <summary>
		/// The final target position.
		/// </summary>
		public Vector3 TargetPosition => _targetPosition;

		/// <summary>
		/// Returns true if a target has been set.
		/// </summary>
		public bool HasTarget => _hasTarget;

		public PathFollower(ChunkMap map, int entityHeight = 2, int entityWidth = 1)
		{
			_pathfinder = new VoxelPathfinder(map)
			{
				EntityHeight = entityHeight,
				EntityWidth = entityWidth
			};
		}

		public PathFollower(VoxelPathfinder pathfinder)
		{
			_pathfinder = pathfinder ?? throw new ArgumentNullException(nameof(pathfinder));
		}

		/// <summary>
		/// Sets a new target position and calculates a path to it.
		/// </summary>
		/// <param name="currentPosition">Current entity position.</param>
		/// <param name="targetPosition">Target position to navigate to.</param>
		/// <returns>True if a path was found, false otherwise.</returns>
		public bool SetTarget(Vector3 currentPosition, Vector3 targetPosition)
		{
			_targetPosition = targetPosition;
			_hasTarget = true;
			HasReachedTarget = false;

			_currentPath = _pathfinder.FindPath(currentPosition, targetPosition);
			_currentWaypointIndex = 0;

			return _currentPath.Count > 0;
		}

		/// <summary>
		/// Clears the current path and target.
		/// </summary>
		public void ClearPath()
		{
			_currentPath.Clear();
			_currentWaypointIndex = 0;
			_hasTarget = false;
			HasReachedTarget = false;
		}

		/// <summary>
		/// Updates the path follower and returns the movement direction.
		/// </summary>
		/// <param name="currentPosition">Current entity position.</param>
		/// <param name="deltaTime">Time since last update.</param>
		/// <returns>Normalized movement direction, or Vector3.Zero if not moving.</returns>
		public Vector3 Update(Vector3 currentPosition, float deltaTime)
		{
			if (!IsFollowingPath)
			{
				if (_hasTarget && Vector3.Distance(currentPosition, _targetPosition) < TargetReachDistance)
				{
					HasReachedTarget = true;
				}
				return Vector3.Zero;
			}

			Vector3 currentWaypoint = _currentPath[_currentWaypointIndex];

			// Check if we've reached the current waypoint (2D distance for horizontal movement)
			float horizontalDist = MathF.Sqrt(
				MathF.Pow(currentPosition.X - currentWaypoint.X, 2) +
				MathF.Pow(currentPosition.Z - currentWaypoint.Z, 2)
			);

			if (horizontalDist < WaypointReachDistance)
			{
				_currentWaypointIndex++;

				if (_currentWaypointIndex >= _currentPath.Count)
				{
					// Path complete
					HasReachedTarget = true;
					return Vector3.Zero;
				}

				currentWaypoint = _currentPath[_currentWaypointIndex];
			}

			// Calculate direction to current waypoint
			Vector3 direction = currentWaypoint - currentPosition;

			// Only use horizontal direction for ground movement
			// Vertical movement is handled by physics (jumping/falling)
			direction.Y = 0;

			if (direction.LengthSquared() > 0.001f)
			{
				return Vector3.Normalize(direction);
			}

			return Vector3.Zero;
		}

		/// <summary>
		/// Calculates the velocity to move toward the current waypoint.
		/// </summary>
		/// <param name="currentPosition">Current entity position.</param>
		/// <returns>Velocity vector for horizontal movement.</returns>
		public Vector3 GetMoveVelocity(Vector3 currentPosition)
		{
			Vector3 direction = Update(currentPosition, 0);
			return direction * MoveSpeed;
		}

		/// <summary>
		/// Returns true if the entity should jump (current waypoint is higher than position).
		/// </summary>
		/// <param name="currentPosition">Current entity position.</param>
		/// <returns>True if jump is needed to reach next waypoint.</returns>
		public bool ShouldJump(Vector3 currentPosition)
		{
			if (!IsFollowingPath)
				return false;

			Vector3 currentWaypoint = _currentPath[_currentWaypointIndex];

			// Jump if waypoint is above current position
			return currentWaypoint.Y > currentPosition.Y + 0.5f;
		}

		/// <summary>
		/// Gets the look direction toward the current waypoint (Y-axis rotation).
		/// </summary>
		/// <param name="currentPosition">Current entity position.</param>
		/// <returns>Normalized look direction on XZ plane.</returns>
		public Vector3 GetLookDirection(Vector3 currentPosition)
		{
			if (!IsFollowingPath)
				return Vector3.UnitZ;

			Vector3 currentWaypoint = _currentPath[_currentWaypointIndex];
			Vector3 direction = currentWaypoint - currentPosition;
			direction.Y = 0;

			if (direction.LengthSquared() > 0.001f)
				return Vector3.Normalize(direction);

			return Vector3.UnitZ;
		}

		/// <summary>
		/// Recalculates the path from current position to the existing target.
		/// Useful when the world has changed or entity got stuck.
		/// </summary>
		/// <param name="currentPosition">Current entity position.</param>
		/// <returns>True if a new path was found.</returns>
		public bool RecalculatePath(Vector3 currentPosition)
		{
			if (!_hasTarget)
				return false;

			return SetTarget(currentPosition, _targetPosition);
		}

		/// <summary>
		/// Checks if the current path is still valid (no blocked waypoints).
		/// </summary>
		/// <returns>True if path is valid or no path exists.</returns>
		public bool IsPathValid()
		{
			if (!IsFollowingPath)
				return true;

			// Check remaining waypoints are still walkable
			for (int i = _currentWaypointIndex; i < _currentPath.Count; i++)
			{
				if (!_pathfinder.IsWalkable(_currentPath[i]))
					return false;
			}

			return true;
		}
	}
}
