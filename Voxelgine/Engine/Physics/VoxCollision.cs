using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

using static System.Runtime.InteropServices.JavaScript.JSType;

using Windows.Devices.Radios;
using Voxelgine.Graphics;

namespace Voxelgine.Engine {
	delegate bool TraceFunc(Vector3 BlockPos);

	delegate bool VoxRaycastFunc(float X, float Y, float Z, int Counter, Vector3 Face);

	static class VoxCollision {
		public static IEnumerable<Vector3> Bresenham(Vector3 Start, Vector3 End) {
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

			if (dx > dy) {
				if (dx > dz) {
					accum = dx >> 1;
					accum2 = accum;
					do {
						yield return (new Vector3(startX, startY, startZ));

						accum -= dy;
						accum2 -= dz;
						if (accum < 0) {
							accum += dx;
							startY += sy;
						}
						if (accum2 < 0) {
							accum2 += dx;
							startZ += sz;
						}
						startX += sx;
					}
					while (startX != endX);
				} else {
					accum = dz >> 1;
					accum2 = accum;
					do {
						yield return (new Vector3(startX, startY, startZ));

						accum -= dy;
						accum2 -= dx;
						if (accum < 0) {
							accum += dz;
							startY += sy;
						}
						if (accum2 < 0) {
							accum2 += dz;
							startX += sx;
						}
						startZ += sz;
					}
					while (startZ != endZ);
				}
			} else {
				if (dy > dz) {
					accum = dy >> 1;
					accum2 = accum;
					do {
						yield return (new Vector3(startX, startY, startZ));

						accum -= dx;
						accum2 -= dz;
						if (accum < 0) {
							accum += dx;
							startX += sx;
						}
						if (accum2 < 0) {
							accum2 += dx;
							startZ += sz;
						}
						startY += sy;
					}
					while (startY != endY);
				} else {
					accum = dz >> 1;
					accum2 = accum;
					do {
						yield return (new Vector3(startX, startY, startZ));

						accum -= dx;
						accum2 -= dy;
						if (accum < 0) {
							accum += dx;
							startX += sx;
						}
						if (accum2 < 0) {
							accum2 += dx;
							startY += sy;
						}
						startZ += sz;
					}
					while (startZ != endZ);
				}
			}
		}

		public static IEnumerable<Vector3> BresenhamDir(Vector3 Start, Vector3 Dir, float Len) {
			return Bresenham(Start, Start + Dir * Len);
		}

		public static bool Trace(Vector3 Start, Vector3 End, TraceFunc Trace) {
			Vector3 TraceDir = Vector3.Normalize(End - Start);
			foreach (var Point in Bresenham(Start, End)) {
				if (Trace(Point))
					return true;
			}

			return false;
		}

		// Answer in https://gamedev.stackexchange.com/questions/47362/cast-ray-to-select-block-in-voxel-game

		public static void Raycast(Vector3 origin, Vector3 direction, float radius, VoxRaycastFunc callback) {
			// Cube containing origin point.
			var x = MathF.Floor(origin[0]);
			var y = MathF.Floor(origin[1]);
			var z = MathF.Floor(origin[2]);
			// Break out direction vector.
			var dx = direction[0];
			var dy = direction[1];
			var dz = direction[2];
			// Direction to increment x,y,z when stepping.
			var stepX = signum(dx);
			var stepY = signum(dy);
			var stepZ = signum(dz);
			// See description above. The initial values depend on the fractional
			// part of the origin.
			var tMaxX = intbound(origin[0], dx);
			var tMaxY = intbound(origin[1], dy);
			var tMaxZ = intbound(origin[2], dz);
			// The change in t when taking a step (always positive).
			var tDeltaX = stepX / dx;
			var tDeltaY = stepY / dy;
			var tDeltaZ = stepZ / dz;
			// Buffer for reporting faces to the callback.
			var face = new Vector3();

			// Avoids an infinite loop.
			if (dx == 0 && dy == 0 && dz == 0)
				throw new NotImplementedException("Range error");

			// Rescale from units of 1 cube-edge to units of 'direction' so we can
			// compare with 't'.
			radius /= MathF.Sqrt(dx * dx + dy * dy + dz * dz);

			int counter = 0;

			/* ray has not gone past bounds of world */
			while (true) {

				// Invoke the callback, unless we are not *yet* within the bounds of the
				// world.

				if (callback(x, y, z, counter++, face))
					break;

				// tMaxX stores the t-value at which we cross a cube boundary along the
				// X axis, and similarly for Y and Z. Therefore, choosing the least tMax
				// chooses the closest cube boundary. The following cases update the position and face normal accordingly.
				if (tMaxX < tMaxY) {
					if (tMaxX < tMaxZ) {
						if (tMaxX > radius)
							break;
						// Update which cube we are now in.
						x += stepX;
						// Adjust tMaxX to the next X-oriented boundary crossing.
						tMaxX += tDeltaX;
						// Record the normal vector of the cube face we entered.
						face[0] = -stepX;
						face[1] = 0;
						face[2] = 0;
					} else {
						if (tMaxZ > radius)
							break;
						z += stepZ;
						tMaxZ += tDeltaZ;
						face[0] = 0;
						face[1] = 0;
						face[2] = -stepZ;
					}
				} else {
					if (tMaxY < tMaxZ) {
						if (tMaxY > radius)
							break;
						y += stepY;
						tMaxY += tDeltaY;
						face[0] = 0;
						face[1] = -stepY;
						face[2] = 0;
					} else {
						// Identical to the second case, repeated for simplicity in
						// the conditionals.
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

		static float intbound(float s, float ds) {
			// Find the smallest positive t such that s+t*ds is an integer.
			if (ds < 0) {
				return intbound(-s, -ds);
			} else {
				s = mod(s, 1);
				// problem is now s+t*ds = 1
				return (1 - s) / ds;
			}
		}

		static float signum(float x) {
			return x > 0 ? 1 : x < 0 ? -1 : 0;
		}

		static float mod(float value, float modulus) {
			return (value % modulus + modulus) % modulus;
		}
	}
}
