using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading.Tasks;
using Voxelgine.Engine;

namespace Voxelgine.Graphics
{
	public unsafe partial class ChunkMap
	{
		public void GenerateFloatingIsland(int Width, int Length, int Seed = 666)
		{
			Noise.Seed = Seed;
			const int CS = Chunk.ChunkSize;
			int WorldHeight = 64;

			// Step 1 — Create chunk grid
			Chunk[,,] chunkGrid = CreateChunkGrid(Width, Length, WorldHeight, CS);

			// Step 2 — Island shape (stone with caves)
			GenerateIslandShape(chunkGrid, Width, Length, WorldHeight, CS);

			// Step 3 — Surface layer (grass/dirt) and record surface heights
			int[] surfaceHeight = ApplySurfaceLayer(chunkGrid, Width, Length, WorldHeight, CS);

			// Step 4 — Water bodies (ponds with sand shoreline)
			PlaceWaterBodies(chunkGrid, surfaceHeight, Width, Length, WorldHeight, CS, Seed);

			// Step 5 — Roads (plank paths between points of interest)
			GenerateRoads(chunkGrid, surfaceHeight, Width, Length, WorldHeight, CS, Seed);

			// Step 6 — Trees (on remaining grass)
			PlaceTrees(chunkGrid, surfaceHeight, Width, Length, WorldHeight, CS, Seed);

			// Step 7 — Foliage (grass plants on remaining grass blocks)
			PlaceFoliage(chunkGrid, surfaceHeight, Width, Length, WorldHeight, CS, Seed);

			// Step 8 — Lighting
			ComputeLighting();
		}

		/// <summary>
		/// Pre-creates all chunks for direct O(1) access during generation,
		/// bypassing SetPlacedBlock overhead (neighbor tracking, change logging,
		/// and lighting updates per block).
		/// </summary>
		Chunk[,,] CreateChunkGrid(int width, int length, int worldHeight, int cs)
		{
			int chunksX = (width + cs - 1) / cs;
			int chunksY = (worldHeight + cs - 1) / cs + 1; // +1 for an empty air chunk above the terrain
			int chunksZ = (length + cs - 1) / cs;

			Chunk[,,] chunkGrid = new Chunk[chunksX, chunksY, chunksZ];
			for (int cx = 0; cx < chunksX; cx++)
				for (int cy = 0; cy < chunksY; cy++)
					for (int cz = 0; cz < chunksZ; cz++)
					{
						Vector3 chunkIndex = new Vector3(cx, cy, cz);
						Chunk chunk = new Chunk(Eng, chunkIndex, this);
						chunkGrid[cx, cy, cz] = chunk;
						Chunks.Add(chunkIndex, chunk);
					}

			return chunkGrid;
		}

		/// <summary>
		/// Generates the island's base shape using 3D simplex noise with center and height falloff.
		/// A 2D heightmap noise layer displaces the surface per XZ column to create gentle hills and valleys.
		/// Fills solid areas with stone and carves cave pockets using a secondary noise layer.
		/// Parallelized across the X axis — each XZ column is independent.
		/// </summary>
		void GenerateIslandShape(Chunk[,,] chunkGrid, int width, int length, int worldHeight, int cs)
		{
			float scale = 0.02f;
			Vector3 center = new Vector3(width, 0, length) / 2;
			float centerRadius = Math.Min(width / 2, length / 2);

			const float HillScale = 0.012f;
			const float HillAmplitude = 6.0f;

			Parallel.For(0, width, x =>
			{
				for (int z = 0; z < length; z++)
				{
					// 2D heightmap displacement — gentle hills and valleys (±HillAmplitude blocks)
					float hillNoise = Noise.CalcPixel2D(x, z, HillScale) / 255f; // 0..1
					float hillOffset = (hillNoise - 0.5f) * 2.0f * HillAmplitude; // -HillAmplitude..+HillAmplitude

					for (int y = 0; y < worldHeight; y++)
					{
						Vector3 Pos = new Vector3(x, (worldHeight - y), z);

						float CenterFalloff = 1.0f - Utils.Clamp(((center - Pos).Length() / centerRadius) / 1.2f, 0, 1);
						float Height = (float)(y - hillOffset) / worldHeight;

						const float HeightFallStart = 0.8f;
						const float HeightFallEnd = 1.0f;
						const float HeightFallRange = HeightFallEnd - HeightFallStart;

						float HeightFalloff = Height <= HeightFallStart ? 1.0f : (Height > HeightFallStart && Height < HeightFallEnd ? 1.0f - (Height - HeightFallStart) * (HeightFallRange * 10) : 0);
						float Density = Simplex(2, x, y * 0.5f, z, scale) * CenterFalloff * HeightFalloff;

						if (Density > 0.1f)
						{
							float Caves = Simplex(1, x, y, z, scale * 4) * HeightFalloff;
							if (Caves < 0.65f)
								chunkGrid[x / cs, y / cs, z / cs].SetBlock(x % cs, y % cs, z % cs, new PlacedBlock(BlockType.Stone));
						}
					}
				}
			});
		}

		/// <summary>
		/// Replaces the topmost stone blocks with grass (surface) and dirt (subsurface, up to 4 deep).
		/// Builds and returns a surface height map used by subsequent generation steps.
		/// Parallelized across the X axis.
		/// </summary>
		int[] ApplySurfaceLayer(Chunk[,,] chunkGrid, int width, int length, int worldHeight, int cs)
		{
			int[] surfaceHeight = new int[width * length];
			Array.Fill(surfaceHeight, -1);

			Parallel.For(0, width, x =>
			{
				for (int z = 0; z < length; z++)
				{
					int DownRayHits = 0;
					for (int y = worldHeight - 1; y >= 0; y--)
					{
						if (chunkGrid[x / cs, y / cs, z / cs].GetBlock(x % cs, y % cs, z % cs).Type != BlockType.None)
						{
							DownRayHits++;

							if (DownRayHits == 1)
							{
								chunkGrid[x / cs, y / cs, z / cs].SetBlock(x % cs, y % cs, z % cs, new PlacedBlock(BlockType.Grass));
								surfaceHeight[x * length + z] = y;
							}
							else if (DownRayHits < 5)
								chunkGrid[x / cs, y / cs, z / cs].SetBlock(x % cs, y % cs, z % cs, new PlacedBlock(BlockType.Dirt));

						}
						else if (DownRayHits != 0)
							break;
					}
				}
			});

			return surfaceHeight;
		}

		/// <summary>
		/// Generates plank roads connecting points of interest across the island.
		/// Uses noise-seeded waypoints on the surface and connects them with
		/// Bresenham-style paths, placing Plank blocks on the ground.
		/// </summary>
		void GenerateRoads(Chunk[,,] chunkGrid, int[] surfaceHeight, int width, int length, int worldHeight, int cs, int seed)
		{
			Random rng = new Random(seed + 3);
			const float WaypointNoiseScale = 0.025f;
			const float WaypointThreshold = 0.78f;
			const int MinWaypointSpacing = 20;
			const int EdgeMargin = 10;
			const int RoadHalfWidth = 1;

			// Find waypoints using noise
			List<(int x, int z)> waypoints = new();

			for (int x = EdgeMargin; x < width - EdgeMargin; x += 4)
			{
				for (int z = EdgeMargin; z < length - EdgeMargin; z += 4)
				{
					int surfY = surfaceHeight[x * length + z];
					if (surfY < 0)
						continue;

					BlockType surfBlock = GridGetBlock(chunkGrid, x, surfY, z, width, worldHeight, length, cs);
					if (surfBlock != BlockType.Grass)
						continue;

					float wpNoise = Noise.CalcPixel2D(x + seed * 11, z + seed * 11, WaypointNoiseScale) / 255f;
					if (wpNoise < WaypointThreshold)
						continue;

					bool tooClose = false;
					foreach (var (wx, wz) in waypoints)
					{
						int dx = x - wx;
						int dz = z - wz;
						if (dx * dx + dz * dz < MinWaypointSpacing * MinWaypointSpacing)
						{
							tooClose = true;
							break;
						}
					}
					if (tooClose)
						continue;

					waypoints.Add((x, z));
				}
			}

			if (waypoints.Count < 2)
				return;

			// Connect each waypoint to its nearest neighbor that isn't already connected
			HashSet<(int, int)> connectedPairs = new();

			foreach (var (ax, az) in waypoints)
			{
				// Find nearest unconnected waypoint
				int bestIdx = -1;
				int bestDistSq = int.MaxValue;

				for (int i = 0; i < waypoints.Count; i++)
				{
					var (bx, bz) = waypoints[i];
					if (bx == ax && bz == az)
						continue;

					int pairA = Math.Min(ax * 10000 + az, bx * 10000 + bz);
					int pairB = Math.Max(ax * 10000 + az, bx * 10000 + bz);
					if (connectedPairs.Contains((pairA, pairB)))
						continue;

					int distSq = (ax - bx) * (ax - bx) + (az - bz) * (az - bz);
					if (distSq < bestDistSq)
					{
						bestDistSq = distSq;
						bestIdx = i;
					}
				}

				if (bestIdx < 0)
					continue;

				var (tx, tz) = waypoints[bestIdx];
				int pkA = Math.Min(ax * 10000 + az, tx * 10000 + tz);
				int pkB = Math.Max(ax * 10000 + az, tx * 10000 + tz);
				connectedPairs.Add((pkA, pkB));

				// Lay plank path using Bresenham line between (ax,az) and (tx,tz)
				PlaceRoadSegment(chunkGrid, surfaceHeight, ax, az, tx, tz, width, length, worldHeight, cs, RoadHalfWidth);
			}
		}

		/// <summary>
		/// Places a plank road segment between two XZ points, following the terrain surface.
		/// Uses Bresenham's line algorithm with configurable half-width for road thickness.
		/// </summary>
		void PlaceRoadSegment(Chunk[,,] chunkGrid, int[] surfaceHeight, int x0, int z0, int x1, int z1, int width, int length, int worldHeight, int cs, int halfWidth)
		{
			int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
			int dz = Math.Abs(z1 - z0), sz = z0 < z1 ? 1 : -1;
			int err = dx - dz;

			while (true)
			{
				// Place road blocks in a cross pattern around the path center
				for (int ox = -halfWidth; ox <= halfWidth; ox++)
				{
					for (int oz = -halfWidth; oz <= halfWidth; oz++)
					{
						int bx = x0 + ox;
						int bz = z0 + oz;
						if (bx < 0 || bx >= width || bz < 0 || bz >= length)
							continue;

						int surfY = surfaceHeight[bx * length + bz];
						if (surfY < 0)
							continue;

						BlockType surfBlock = GridGetBlock(chunkGrid, bx, surfY, bz, width, worldHeight, length, cs);
						if (surfBlock == BlockType.Grass || surfBlock == BlockType.Dirt)
							GridSetBlock(chunkGrid, bx, surfY, bz, BlockType.Plank, width, worldHeight, length, cs);
					}
				}

				if (x0 == x1 && z0 == z1)
					break;

				int e2 = 2 * err;
				if (e2 > -dz) { err -= dz; x0 += sx; }
				if (e2 < dx) { err += dx; z0 += sz; }
			}
		}

		/// <summary>
		/// Scans the world for valid spawn points on the surface.
		/// A valid spawn point has a grass surface block on a flat 3×3 area
		/// (all 9 blocks at the same height ±1) with at least 3 air blocks above.
		/// Points are selected with minimum spacing, prioritized by proximity to the world center.
		/// </summary>
		/// <param name="count">Number of spawn points to find.</param>
		/// <param name="minSpacing">Minimum distance in blocks between spawn points.</param>
		/// <returns>List of world positions suitable for spawning (above ground surface).</returns>
		public List<Vector3> FindSpawnPoints(int count, int minSpacing = 5)
		{
			// Compute world bounds from loaded chunks
			int minX = int.MaxValue, maxX = int.MinValue;
			int minY = int.MaxValue, maxY = int.MinValue;
			int minZ = int.MaxValue, maxZ = int.MinValue;

			foreach (var kvp in Chunks.Items)
			{
				int cx = (int)kvp.Key.X, cy = (int)kvp.Key.Y, cz = (int)kvp.Key.Z;
				int bx = cx * Chunk.ChunkSize;
				int by = cy * Chunk.ChunkSize;
				int bz = cz * Chunk.ChunkSize;

				if (bx < minX) minX = bx;
				if (bx + Chunk.ChunkSize > maxX) maxX = bx + Chunk.ChunkSize;
				if (by < minY) minY = by;
				if (by + Chunk.ChunkSize > maxY) maxY = by + Chunk.ChunkSize;
				if (bz < minZ) minZ = bz;
				if (bz + Chunk.ChunkSize > maxZ) maxZ = bz + Chunk.ChunkSize;
			}

			if (minX == int.MaxValue)
				return new List<Vector3>();

			float centerX = (minX + maxX) / 2f;
			float centerZ = (minZ + maxZ) / 2f;

			var candidates = new List<Vector3>();

			// Scan each XZ column top-down for the topmost grass block on a flat 3x3 surface
			for (int x = minX + 1; x < maxX - 1; x++)
				for (int z = minZ + 1; z < maxZ - 1; z++)
				{
					for (int y = maxY - 1; y >= minY; y--)
					{
						// Must be a grass block
						if (GetBlock(x, y, z) != BlockType.Grass)
							continue;

						// Need 3 air blocks above for player clearance
						if (GetBlock(x, y + 1, z) != BlockType.None ||
							GetBlock(x, y + 2, z) != BlockType.None ||
							GetBlock(x, y + 3, z) != BlockType.None)
							continue;

						// Check 3x3 flatness: all surrounding surface blocks must be solid
						// and within ±1 of the center height
						bool flat = true;
						for (int ddx = -1; ddx <= 1 && flat; ddx++)
						{
							for (int dz = -1; dz <= 1 && flat; dz++)
							{
								if (ddx == 0 && dz == 0)
									continue;

								// Find topmost solid block in this neighbor column (within ±1 of y)
								bool neighborOk = false;
								for (int ny = y + 1; ny >= y - 1; ny--)
								{
									if (BlockInfo.IsSolid(GetBlock(x + ddx, ny, z + dz)) &&
										GetBlock(x + ddx, ny + 1, z + dz) == BlockType.None)
									{
										neighborOk = true;
										break;
									}
								}
								if (!neighborOk)
									flat = false;
							}
						}

						if (!flat)
							continue;

						candidates.Add(new Vector3(x, y + 3, z));
						break;
					}
				}

			if (candidates.Count == 0)
				return new List<Vector3>();

			// Sort by distance to world center (XZ plane)
			candidates.Sort((a, b) =>
			{
				float distA = (a.X - centerX) * (a.X - centerX) + (a.Z - centerZ) * (a.Z - centerZ);
				float distB = (b.X - centerX) * (b.X - centerX) + (b.Z - centerZ) * (b.Z - centerZ);
				return distA.CompareTo(distB);
			});

			// Select points with minimum spacing
			var result = new List<Vector3>();
			float minSpacingSq = minSpacing * minSpacing;

			foreach (var candidate in candidates)
			{
				bool tooClose = false;
				foreach (var selected in result)
				{
					float ddx = candidate.X - selected.X;
					float dz = candidate.Z - selected.Z;
					if (ddx * ddx + dz * dz < minSpacingSq)
					{
						tooClose = true;
						break;
					}
				}

				if (!tooClose)
				{
					result.Add(candidate);
					if (result.Count >= count)
						break;
				}
			}

			return result;
		}

		float Simplex(int Octaves, float X, float Y, float Z, float Scale)
		{
			float Val = 0.0f;

			for (int i = 0; i < Octaves; i++)
			{
				float freq = 1 << i;
				Val += Noise.CalcPixel3D(X * freq, Y * freq, Z * freq, Scale);
			}

			return (Val / Octaves) / 255;
		}

		/// <summary>
		/// Inline block set using pre-created chunk grid. Bounds-checked.
		/// </summary>
		static void GridSetBlock(Chunk[,,] grid, int x, int y, int z, BlockType type, int width, int height, int length, int cs)
		{
			if (x < 0 || x >= width || y < 0 || y >= height || z < 0 || z >= length)
				return;
			int cx = x / cs, cy = y / cs, cz = z / cs;
			if (cx >= grid.GetLength(0) || cy >= grid.GetLength(1) || cz >= grid.GetLength(2))
				return;
			grid[cx, cy, cz].SetBlock(x % cs, y % cs, z % cs, new PlacedBlock(type));
		}

		/// <summary>
		/// Inline block get using pre-created chunk grid. Returns None for out-of-bounds.
		/// </summary>
		static BlockType GridGetBlock(Chunk[,,] grid, int x, int y, int z, int width, int height, int length, int cs)
		{
			if (x < 0 || x >= width || y < 0 || y >= height || z < 0 || z >= length)
				return BlockType.None;
			int cx = x / cs, cy = y / cs, cz = z / cs;
			if (cx >= grid.GetLength(0) || cy >= grid.GetLength(1) || cz >= grid.GetLength(2))
				return BlockType.None;
			return grid[cx, cy, cz].GetBlock(x % cs, y % cs, z % cs).Type;
		}

		/// <summary>
		/// Places trees on grass blocks using noise-based distribution.
		/// Uses a 2D noise layer to select tree positions, enforcing minimum spacing.
		/// </summary>
		void PlaceTrees(Chunk[,,] chunkGrid, int[] surfaceHeight, int width, int length, int worldHeight, int cs, int seed)
		{
			Random rng = new Random(seed + 1);
			const float TreeNoiseScale = 0.08f;
			const float TreeThreshold = 0.62f;
			const int MinTreeSpacing = 5;
			const int EdgeMargin = 4; // Keep trees away from world edges

			// Use actual grid extent — the chunk grid has an extra air chunk above worldHeight
			int gridHeight = chunkGrid.GetLength(1) * cs;

			// Collect tree positions using noise, then place sequentially
			List<(int x, int z, int surfY)> treePositions = new();

			for (int x = EdgeMargin; x < width - EdgeMargin; x++)
			{
				for (int z = EdgeMargin; z < length - EdgeMargin; z++)
				{
					int surfY = surfaceHeight[x * length + z];
					if (surfY < 0 || surfY + 12 >= gridHeight)
						continue;

					// Only place trees on grass
					if (GridGetBlock(chunkGrid, x, surfY, z, width, gridHeight, length, cs) != BlockType.Grass)
						continue;

					// Noise-based placement
					float treeNoise = Noise.CalcPixel2D(x + seed * 3, z + seed * 3, TreeNoiseScale) / 255f;
					if (treeNoise < TreeThreshold)
						continue;

					// Check minimum spacing against already-selected trees
					bool tooClose = false;
					for (int i = treePositions.Count - 1; i >= 0 && i >= treePositions.Count - 50; i--)
					{
						int ddx = x - treePositions[i].x;
						int dz = z - treePositions[i].z;
						if (ddx * ddx + dz * dz < MinTreeSpacing * MinTreeSpacing)
						{
							tooClose = true;
							break;
						}
					}
					if (tooClose)
						continue;

					treePositions.Add((x, z, surfY));
				}
			}

			// Place each tree
			foreach (var (tx, tz, surfY) in treePositions)
			{
				int trunkHeight = 6 + rng.Next(5); // 6-10
				int canopyRadius = 2 + rng.Next(2); // 2-3
				int canopyHeight = 3 + rng.Next(3); // 3-5
				int canopyBase = surfY + trunkHeight - canopyHeight + 1;

				// Trunk
				for (int y = surfY + 1; y <= surfY + trunkHeight; y++)
					GridSetBlock(chunkGrid, tx, y, tz, BlockType.Wood, width, gridHeight, length, cs);

				// Replace grass under trunk with dirt
				GridSetBlock(chunkGrid, tx, surfY, tz, BlockType.Dirt, width, gridHeight, length, cs);

				// Leaf canopy (roughly spherical)
				for (int ly = canopyBase; ly <= surfY + trunkHeight + 1; ly++)
				{
					// Canopy narrows at top and bottom
					int layerFromCenter = Math.Abs(ly - (canopyBase + canopyHeight / 2));
					int layerRadius = Math.Max(1, canopyRadius - layerFromCenter / 2);

					for (int lx = -layerRadius; lx <= layerRadius; lx++)
					{
						for (int lz = -layerRadius; lz <= layerRadius; lz++)
						{
							// Skip corners for rounder shape
							if (lx * lx + lz * lz > layerRadius * layerRadius + 1)
								continue;

							int bx = tx + lx;
							int bz = tz + lz;

							// Don't overwrite trunk
							if (lx == 0 && lz == 0 && ly <= surfY + trunkHeight)
								continue;

							if (GridGetBlock(chunkGrid, bx, ly, bz, width, gridHeight, length, cs) == BlockType.None)
								GridSetBlock(chunkGrid, bx, ly, bz, BlockType.Leaf, width, gridHeight, length, cs);
						}
					}
				}
			}
		}

		/// <summary>
		/// Places foliage (grass plant) blocks on grass surface blocks using noise-based distribution.
		/// Skips positions already occupied by trees, roads, water, or other blocks.
		/// </summary>
		void PlaceFoliage(Chunk[,,] chunkGrid, int[] surfaceHeight, int width, int length, int worldHeight, int cs, int seed)
		{
			int gridHeight = chunkGrid.GetLength(1) * cs;
			const float FoliageNoiseScale = 0.12f;
			const float FoliageThreshold = 0.35f;
			const int EdgeMargin = 2;

			for (int x = EdgeMargin; x < width - EdgeMargin; x++)
			{
				for (int z = EdgeMargin; z < length - EdgeMargin; z++)
				{
					int surfY = surfaceHeight[x * length + z];
					if (surfY < 0 || surfY + 1 >= gridHeight)
						continue;

					// Only place foliage on grass blocks
					if (GridGetBlock(chunkGrid, x, surfY, z, width, gridHeight, length, cs) != BlockType.Grass)
						continue;

					// Only place if the block above is empty
					if (GridGetBlock(chunkGrid, x, surfY + 1, z, width, gridHeight, length, cs) != BlockType.None)
						continue;

					// Noise-based placement for natural distribution
					float foliageNoise = Noise.CalcPixel2D(x + seed * 7, z + seed * 7, FoliageNoiseScale) / 255f;
					if (foliageNoise < FoliageThreshold)
						continue;

					GridSetBlock(chunkGrid, x, surfY + 1, z, BlockType.Foliage, width, gridHeight, length, cs);
				}
			}
		}

		/// <summary>
		/// Places water bodies in terrain depressions with irregular, noise-based shapes.
		/// Carves shallow basins, lines them with stone/sand for containment, and fills with water.
		/// </summary>
		void PlaceWaterBodies(Chunk[,,] chunkGrid, int[] surfaceHeight, int width, int length, int worldHeight, int cs, int seed)
		{
			Random rng = new Random(seed + 2);
			const float PondNoiseScale = 0.015f;
			const float PondThreshold = 0.72f;
			const int PondMinSpacing = 40;
			const int EdgeMargin = 12;
			const float ShapeNoiseScale = 0.18f;

			// Find potential pond centers using noise
			List<(int x, int z)> pondCenters = new();

			for (int x = EdgeMargin; x < width - EdgeMargin; x += 3)
			{
				for (int z = EdgeMargin; z < length - EdgeMargin; z += 3)
				{
					int surfY = surfaceHeight[x * length + z];
					if (surfY < 2)
						continue;

					float pondNoise = Noise.CalcPixel2D(x + seed * 7, z + seed * 7, PondNoiseScale) / 255f;
					if (pondNoise < PondThreshold)
						continue;

					// Check spacing
					bool tooClose = false;
					foreach (var (px, pz) in pondCenters)
					{
						int ddx = x - px;
						int dz = z - pz;
						if (ddx * ddx + dz * dz < PondMinSpacing * PondMinSpacing)
						{
							tooClose = true;
							break;
						}
					}
					if (tooClose)
						continue;

					pondCenters.Add((x, z));
				}
			}

			// Carve and fill each pond
			foreach (var (cx, cz) in pondCenters)
			{
				int surfY = surfaceHeight[cx * length + cz];
				int pondRadius = 5 + rng.Next(6); // 5-10 blocks radius
				int pondDepth = 2 + rng.Next(3);  // 2-4 blocks deep
				int waterLevel = surfY;
				int outerRadius = pondRadius + 2;  // Extra margin for containment walls and shoreline

				// Pass 1: Carve basin with noise-based irregular shape
				for (int ddx = -pondRadius; ddx <= pondRadius; ddx++)
				{
					for (int dz = -pondRadius; dz <= pondRadius; dz++)
					{
						int bx = cx + ddx;
						int bz = cz + dz;
						if (bx < 0 || bx >= width || bz < 0 || bz >= length)
							continue;

						float distSq = ddx * ddx + dz * dz;
						float radiusSq = pondRadius * pondRadius;

						// Noise-modulated radius for irregular shape
						float shapeNoise = Noise.CalcPixel2D(bx + seed * 13, bz + seed * 13, ShapeNoiseScale) / 255f;
						float localRadiusFactor = 0.55f + shapeNoise * 0.45f; // 0.55-1.0 of radius
						float localRadiusSq = radiusSq * localRadiusFactor * localRadiusFactor;
						if (distSq > localRadiusSq)
							continue;

						int localSurfY = surfaceHeight[bx * length + bz];
						if (localSurfY < 0)
							continue;

						// Depth tapers toward edges (deeper in center)
						float edgeFactor = 1f - distSq / localRadiusSq;
						int localDepth = Math.Max(1, (int)(pondDepth * edgeFactor + 0.5f));

						// Carve terrain and fill with water
						for (int dy = 0; dy < localDepth; dy++)
						{
							int carveY = localSurfY - dy;
							if (carveY < 1)
								break;

							if (carveY <= waterLevel)
								GridSetBlock(chunkGrid, bx, carveY, bz, BlockType.Water, width, worldHeight, length, cs);
						}

						// Fill any remaining space up to water level with water
						for (int wy = localSurfY + 1; wy <= waterLevel; wy++)
						{
							if (GridGetBlock(chunkGrid, bx, wy, bz, width, worldHeight, length, cs) == BlockType.None)
								GridSetBlock(chunkGrid, bx, wy, bz, BlockType.Water, width, worldHeight, length, cs);
						}

						// Update surface height to reflect water surface
						if (localSurfY <= waterLevel)
							surfaceHeight[bx * length + bz] = waterLevel;
					}
				}

				// Pass 2: Seal the basin — ensure every water block has solid neighbors on sides and bottom
				for (int ddx = -outerRadius; ddx <= outerRadius; ddx++)
				{
					for (int dz = -outerRadius; dz <= outerRadius; dz++)
					{
						int bx = cx + ddx;
						int bz = cz + dz;
						if (bx < 0 || bx >= width || bz < 0 || bz >= length)
							continue;

						for (int y = waterLevel; y >= Math.Max(1, waterLevel - pondDepth); y--)
						{
							if (GridGetBlock(chunkGrid, bx, y, bz, width, worldHeight, length, cs) != BlockType.Water)
								continue;

							// Check bottom — seal with stone if not solid
							BlockType below = GridGetBlock(chunkGrid, bx, y - 1, bz, width, worldHeight, length, cs);
							if (!BlockInfo.IsSolid(below) && below != BlockType.Water)
								GridSetBlock(chunkGrid, bx, y - 1, bz, BlockType.Stone, width, worldHeight, length, cs);

							// Check 4 horizontal neighbors — seal with sand if not solid and not water
							ReadOnlySpan<(int nx, int nz)> neighbors = [(1, 0), (-1, 0), (0, 1), (0, -1)];
							foreach (var (nx, nz) in neighbors)
							{
								int sx = bx + nx;
								int sz = bz + nz;
								BlockType side = GridGetBlock(chunkGrid, sx, y, sz, width, worldHeight, length, cs);
								if (!BlockInfo.IsSolid(side) && side != BlockType.Water)
									GridSetBlock(chunkGrid, sx, y, sz, BlockType.Sand, width, worldHeight, length, cs);
							}
						}
					}
				}

				// Pass 3: Sand shoreline around the pond perimeter
				for (int ddx = -outerRadius; ddx <= outerRadius; ddx++)
				{
					for (int dz = -outerRadius; dz <= outerRadius; dz++)
					{
						float distSq = ddx * ddx + dz * dz;
						float outerSq = (pondRadius + 2.5f) * (pondRadius + 2.5f);
						float innerSq = (pondRadius - 1.0f) * (pondRadius - 1.0f);
						if (distSq > outerSq || distSq < innerSq)
							continue;

						int bx = cx + ddx;
						int bz = cz + dz;
						if (bx < 0 || bx >= width || bz < 0 || bz >= length)
							continue;

						int localSurfY = surfaceHeight[bx * length + bz];
						if (localSurfY < 0)
							continue;

						BlockType surfBlock = GridGetBlock(chunkGrid, bx, localSurfY, bz, width, worldHeight, length, cs);
						if (surfBlock == BlockType.Grass || surfBlock == BlockType.Dirt)
							GridSetBlock(chunkGrid, bx, localSurfY, bz, BlockType.Sand, width, worldHeight, length, cs);
					}
				}
			}
		}
	}
}
