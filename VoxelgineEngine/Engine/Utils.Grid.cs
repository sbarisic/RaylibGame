using System;
using System.Collections.Generic;
using System.Numerics;

namespace Voxelgine
{
	public static partial class Utils
	{
		public static bool Raycast(Vector3 Origin, Vector3 Direction, float Length, RaycastCallbackFunc Callback)
		{
			// Cube containing origin point.
			float X = (float)Math.Floor(Origin.X);
			float Y = (float)Math.Floor(Origin.Y);
			float Z = (float)Math.Floor(Origin.Z);

			// Break out direction vector.
			float Dx = Direction.X;
			float Dy = Direction.Y;
			float Dz = Direction.Z;

			// Direction to increment x,y,z when stepping.
			float StepX = Dx > 0 ? 1 : Dx < 0 ? -1 : 0;
			float StepY = Dy > 0 ? 1 : Dy < 0 ? -1 : 0;
			float StepZ = Dz > 0 ? 1 : Dz < 0 ? -1 : 0;

			/*const float StepScale = 0.2f;
			StepX *= StepScale;
			StepY *= StepScale;
			StepZ *= StepScale;*/

			// See description above. The initial values depend on the fractional
			// part of the origin. tMaxX, tMaxY, and tMaxZ store the t-value at which we cross a cube boundary along each axis.
			// The following logic chooses the closest cube boundary and updates the position and face normal accordingly.
			float tMaxX = IntBound(Origin.X, Dx);
			float tMaxY = IntBound(Origin.Y, Dy);
			float tMaxZ = IntBound(Origin.Z, Dz);

			// The change in t when taking a step (always positive).
			float tDeltaX = StepX / Dx;
			float tDeltaY = StepY / Dy;
			float tDeltaZ = StepZ / Dz;

			// Buffer for reporting faces to the callback.
			var face = new Vector3();

			// Avoids an infinite loop.
			if (Dx == 0 && Dy == 0 && Dz == 0)
				throw new Exception("Raycast in zero direction!");

			// Rescale from units of 1 cube-edge to units of 'direction' so we can
			// compare with 't'.
			Length /= (float)Math.Sqrt(Dx * Dx + Dy * Dy + Dz * Dz);

			while (true)
			{
				if (Callback((int)X, (int)Y, (int)Z, face))
					return true;

				// tMaxX stores the t-value at which we cross a cube boundary along the
				// X axis, and similarly for Y and Z. Therefore, choosing the least tMax
				// chooses the closest cube boundary. Only the first case of the four
				// has been commented in detail.
				if (tMaxX < tMaxY)
				{
					if (tMaxX < tMaxZ)
					{
						if (tMaxX > Length)
							break;
						// Update which cube we are now in.
						X += StepX;
						// Adjust tMaxX to the next X-oriented boundary crossing.
						tMaxX += tDeltaX;
						// Record the normal vector of the cube face we entered.
						face.X = -StepX;
						face.Y = 0;
						face.Z = 0;
					}
					else
					{
						if (tMaxZ > Length)
							break;
						Z += StepZ;
						tMaxZ += tDeltaZ;
						face.X = 0;
						face.Y = 0;
						face.Z = -StepZ;
					}
				}
				else
				{
					if (tMaxY < tMaxZ)
					{
						if (tMaxY > Length)
							break;
						Y += StepY;
						tMaxY += tDeltaY;
						face.X = 0;
						face.Y = -StepY;
						face.Z = 0;
					}
					else
					{
						// Identical to the second case, repeated for simplicity in
						// the conditionals.
						if (tMaxZ > Length)
							break;
						Z += StepZ;
						tMaxZ += tDeltaZ;
						face.X = 0;
						face.Y = 0;
						face.Z = -StepZ;
					}
				}
			}

			return false;
		}

		// Legacy debug-recording hooks remain no-ops in the renderer-free engine.
		public static void ClearRaycastRecord() { }
		public static void BeginRaycastRecord() { }
		public static void EndRaycastRecord() { }
		public static bool HasRecord() => false;

		// --- BEGIN MOVED FROM VoxCollision ---
		public static IEnumerable<Vector3> Bresenham(Vector3 Start, Vector3 End)
		{
			int startX = (int)Start.X, startY = (int)Start.Y, startZ = (int)Start.Z;
			int endX = (int)End.X, endY = (int)End.Y, endZ = (int)End.Z;

			int dx, dy, dz;
			int sx, sy, sz;
			int accum, accum2;

			dx = endX - startX;
			dy = endY - startY;
			dz = endZ - startZ;

			sx = ((dx) < 0 ? -1 : ((dx) > 0 ? 1 : 0));
			sy = ((dy) < 0 ? -1 : ((dy) > 0 ? 1 : 0));
			sz = ((dz) < 0 ? -1 : ((dz) > 0 ? 1 : 0));

			dx = Math.Abs(dx);
			dy = Math.Abs(dy);
			dz = Math.Abs(dz);

			endX += sx;
			endY += sy;
			endZ += sz;

			if (dx > dy)
			{
				if (dx > dz)
				{
					accum = dx >> 1;
					accum2 = accum;
					do
					{
						yield return (new Vector3(startX, startY, startZ));

						accum -= dy;
						accum2 -= dz;
						if (accum < 0)
						{
							accum += dx;
							startY += sy;
						}
						if (accum2 < 0)
						{
							accum2 += dx;
							startZ += sz;
						}
						startX += sx;
					}
					while (startX != endX);
				}
				else
				{
					accum = dz >> 1;
					accum2 = accum;
					do
					{
						yield return (new Vector3(startX, startY, startZ));

						accum -= dy;
						accum2 -= dx;
						if (accum < 0)
						{
							accum += dz;
							startY += sy;
						}
						if (accum2 < 0)
						{
							accum2 += dz;
							startX += sx;
						}
						startZ += sz;
					}
					while (startZ != endZ);
				}
			}
			else
			{
				if (dy > dz)
				{
					accum = dy >> 1;
					accum2 = accum;
					do
					{
						yield return (new Vector3(startX, startY, startZ));

						accum -= dx;
						accum2 -= dz;
						if (accum < 0)
						{
							accum += dx;
							startX += sx;
						}
						if (accum2 < 0)
						{
							accum2 += dx;
							startZ += sz;
						}
						startY += sy;
					}
					while (startY != endY);
				}
				else
				{
					accum = dz >> 1;
					accum2 = accum;
					do
					{
						yield return (new Vector3(startX, startY, startZ));

						accum -= dx;
						accum2 -= dy;
						if (accum < 0)
						{
							accum += dx;
							startX += sx;
						}
						if (accum2 < 0)
						{
							accum2 += dx;
							startY += sy;
						}
						startZ += sz;
					}
					while (startZ != endZ);
				}
			}
		}

		public static IEnumerable<Vector3> BresenhamDir(Vector3 Start, Vector3 Dir, float Len)
		{
			return Bresenham(Start, Start + Dir * Len);
		}

		public static bool Trace(Vector3 Start, Vector3 End, Func<Vector3, bool> Trace)
		{
			ArgumentNullException.ThrowIfNull(Trace);
			foreach (var Point in Bresenham(Start, End))
			{
				if (Trace(Point))
					return true;
			}
			return false;
		}

		public static void RaycastVoxel(Vector3 origin, Vector3 direction, float radius, Func<float, float, float, int, Vector3, bool> callback)
		{
			ArgumentNullException.ThrowIfNull(callback);
			if (!float.IsFinite(origin.X) || !float.IsFinite(origin.Y) || !float.IsFinite(origin.Z))
				throw new ArgumentOutOfRangeException(nameof(origin));
			if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Y) || !float.IsFinite(direction.Z))
				throw new ArgumentOutOfRangeException(nameof(direction));
			if (!float.IsFinite(radius) || radius < 0)
				throw new ArgumentOutOfRangeException(nameof(radius));
			// Cube containing origin point.
			var x = MathF.Floor(origin[0]);
			var y = MathF.Floor(origin[1]);
			var z = MathF.Floor(origin[2]);
			// Break out direction vector.
			var dx = direction[0];
			var dy = direction[1];
			var dz = direction[2];
			// Direction to increment x,y,z when stepping.
			var stepX = Signum(dx);
			var stepY = Signum(dy);
			var stepZ = Signum(dz);
			// See description above. The initial values depend on the fractional
			// part of the origin.
			var tMaxX = dx == 0 ? float.PositiveInfinity : IntBound(origin[0], dx);
			var tMaxY = dy == 0 ? float.PositiveInfinity : IntBound(origin[1], dy);
			var tMaxZ = dz == 0 ? float.PositiveInfinity : IntBound(origin[2], dz);
			// The change in t when taking a step (always positive).
			var tDeltaX = dx == 0 ? float.PositiveInfinity : MathF.Abs(1 / dx);
			var tDeltaY = dy == 0 ? float.PositiveInfinity : MathF.Abs(1 / dy);
			var tDeltaZ = dz == 0 ? float.PositiveInfinity : MathF.Abs(1 / dz);
			// Buffer for reporting faces to the callback.
			var face = new Vector3();

			// Avoids an infinite loop.
			if (dx == 0 && dy == 0 && dz == 0)
				throw new ArgumentException("Ray direction cannot be zero.", nameof(direction));

			// Rescale from units of 1 cube-edge to units of 'direction' so we can
			// compare with 't'.
			radius /= MathF.Sqrt(dx * dx + dy * dy + dz * dz);

			int counter = 0;

			while (true)
			{
				if (callback(x, y, z, counter++, face))
					break;
				if (tMaxX < tMaxY)
				{
					if (tMaxX < tMaxZ)
					{
						if (tMaxX > radius)
							break;
						x += stepX;
						tMaxX += tDeltaX;
						face[0] = -stepX;
						face[1] = 0;
						face[2] = 0;
					}
					else
					{
						if (tMaxZ > radius)
							break;
						z += stepZ;
						tMaxZ += tDeltaZ;
						face[0] = 0;
						face[1] = 0;
						face[2] = -stepZ;
					}
				}
				else
				{
					if (tMaxY < tMaxZ)
					{
						if (tMaxY > radius)
							break;
						y += stepY;
						tMaxY += tDeltaY;
						face[0] = 0;
						face[1] = -stepY;
						face[2] = 0;
					}
					else
					{
						if (tMaxZ > radius)
							break;
						z += stepZ;
						tMaxZ += tDeltaZ;
						face[0] = 0;
						face[1] = 0;
						face[2] = -stepZ;
					}
				}
			}
		}

		public static float IntBound(float s, float ds)
		{
			if (ds < 0)
			{
				return IntBound(-s, -ds);
			}
			else
			{
				s = Mod(s, 1);
				return (1 - s) / ds;
			}
		}

		public static float Signum(float x)
		{
			return x > 0 ? 1 : x < 0 ? -1 : 0;
		}

		public static float Mod(float value, float modulus)
		{
			return (value % modulus + modulus) % modulus;
		}
		// --- END MOVED FROM VoxCollision ---

	}
}

