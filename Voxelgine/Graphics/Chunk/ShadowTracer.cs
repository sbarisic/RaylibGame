using System;
using System.Numerics;
using Voxelgine.Engine;

namespace Voxelgine.Graphics
{
	/// <summary>
	/// Represents a point light source in the world with optional shadow casting.
	/// </summary>
	public struct PointLight
	{
		/// <summary>World position of the light source.</summary>
		public Vector3 Position;
		/// <summary>Light intensity (0-15).</summary>
		public byte Intensity;
		/// <summary>If true, this light traces rays to check for occlusion.</summary>
		public bool CastsShadows;

		public PointLight(Vector3 position, byte intensity, bool castsShadows = true)
		{
			Position = position;
			Intensity = intensity;
			CastsShadows = castsShadows;
		}
	}

	/// <summary>
	/// Provides ray-tracing utilities for shadow calculations.
	/// Uses 3D DDA (Digital Differential Analyzer) for fast voxel traversal.
	/// </summary>
	public static class ShadowTracer
	{
		/// <summary>
		/// Checks if there is line-of-sight between two points (no opaque blocks blocking).
		/// Uses 3D DDA algorithm for efficient voxel traversal.
		/// </summary>
		/// <param name="map">The chunk map to check against.</param>
		/// <param name="from">Start position (light source).</param>
		/// <param name="to">End position (target block).</param>
		/// <returns>True if line-of-sight exists (no occlusion), false if blocked.</returns>
		public static bool HasLineOfSight(ChunkMap map, Vector3 from, Vector3 to)
		{
			Vector3 dir = to - from;
			float distance = dir.Length();
			
			if (distance < 0.01f)
				return true; // Same position
				
			dir = Vector3.Normalize(dir);
			
			// Use 3D DDA (Amanatides & Woo algorithm)
			int x = (int)MathF.Floor(from.X);
			int y = (int)MathF.Floor(from.Y);
			int z = (int)MathF.Floor(from.Z);
			
			int endX = (int)MathF.Floor(to.X);
			int endY = (int)MathF.Floor(to.Y);
			int endZ = (int)MathF.Floor(to.Z);
			
			int stepX = dir.X > 0 ? 1 : (dir.X < 0 ? -1 : 0);
			int stepY = dir.Y > 0 ? 1 : (dir.Y < 0 ? -1 : 0);
			int stepZ = dir.Z > 0 ? 1 : (dir.Z < 0 ? -1 : 0);
			
			// Calculate tMax - distance to next voxel boundary
			float tMaxX = stepX != 0 ? ((stepX > 0 ? (x + 1 - from.X) : (from.X - x)) / MathF.Abs(dir.X)) : float.MaxValue;
			float tMaxY = stepY != 0 ? ((stepY > 0 ? (y + 1 - from.Y) : (from.Y - y)) / MathF.Abs(dir.Y)) : float.MaxValue;
			float tMaxZ = stepZ != 0 ? ((stepZ > 0 ? (z + 1 - from.Z) : (from.Z - z)) / MathF.Abs(dir.Z)) : float.MaxValue;
			
			// Calculate tDelta - distance to traverse one voxel
			float tDeltaX = stepX != 0 ? (1f / MathF.Abs(dir.X)) : float.MaxValue;
			float tDeltaY = stepY != 0 ? (1f / MathF.Abs(dir.Y)) : float.MaxValue;
			float tDeltaZ = stepZ != 0 ? (1f / MathF.Abs(dir.Z)) : float.MaxValue;
			
			// Maximum steps to prevent infinite loops
			int maxSteps = (int)(distance + 3);
			
			for (int i = 0; i < maxSteps; i++)
			{
				// Check if we've reached the destination
				if (x == endX && y == endY && z == endZ)
					return true;
				
				// Check current block (skip the source block on first iteration)
				if (i > 0)
				{
					BlockType block = map.GetBlock(x, y, z);
					if (BlockInfo.IsOpaque(block))
						return false; // Blocked by opaque block
				}
				
				// Step to next voxel
				if (tMaxX < tMaxY && tMaxX < tMaxZ)
				{
					x += stepX;
					tMaxX += tDeltaX;
				}
				else if (tMaxY < tMaxZ)
				{
					y += stepY;
					tMaxY += tDeltaY;
				}
				else
				{
					z += stepZ;
					tMaxZ += tDeltaZ;
				}
			}
			
			return true; // Reached max steps, assume visible
		}
		
		/// <summary>
		/// Fast check if a block position can receive light from a source.
		/// Uses distance check first for early-out, then ray trace if within range.
		/// </summary>
		public static bool CanReceiveLight(ChunkMap map, Vector3 lightSource, Vector3 blockPos, byte lightIntensity, bool castsShadows)
		{
			// Quick distance check - if too far, no light anyway
			float distSq = Vector3.DistanceSquared(lightSource, blockPos);
			float maxDist = lightIntensity + 1; // Light can travel at most intensity blocks
			if (distSq > maxDist * maxDist)
				return false;
				
			// If shadows disabled, skip ray trace
			if (!castsShadows)
				return true;
				
			// Check line of sight
			return HasLineOfSight(map, lightSource, blockPos);
		}
	}
}
