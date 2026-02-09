using System.Numerics;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Raylib_cs;
using Voxelgine.Engine;
using System.Data;
using Voxelgine.Engine.DI;

namespace Voxelgine.Graphics
{
	/// <summary>
	/// Manages the voxel world as a collection of chunks stored in a spatial hash grid.
	/// Provides world generation, block access, collision queries, lighting computation,
	/// and rendering with frustum culling.
	/// </summary>
	/// <remarks>
	/// Each chunk is 16³ blocks. The ChunkMap handles:
	/// - Procedural floating island generation via simplex noise
	/// - Block placement/removal with automatic lighting updates
	/// - Serialization (save/load) with GZip compression
	/// - Transparent block rendering with depth sorting
	/// </remarks>
	public unsafe class ChunkMap
	{
		SpatialHashGrid<Chunk> Chunks;
		Random Rnd = new Random();

		// Reusable buffer for transparent face sorting
		List<TransparentFace> TransparentFaceBuffer = new List<TransparentFace>(4096);
		float[] DistanceBuffer = new float[4096];
		int[] IndexBuffer = new int[4096];

		// Persistent mesh buffers for sorted transparent rendering (avoid per-frame allocations)
		int TransparentMeshCapacity = 0;
		Vector3[] TransparentVertices;
		Vector3[] TransparentNormals;
		Vector2[] TransparentTexCoords;
		Color[] TransparentColors;
		Mesh TransparentMesh;
		Material TransparentMaterial;
		bool TransparentMeshInitialized = false;

		IFishEngineRunner Eng;

		/// <summary>
		/// Maximum render distance in blocks. Chunks whose center is farther than this
		/// from the camera are skipped entirely (before frustum testing).
		/// </summary>
		public float RenderDistanceBlocks = 52;

		/// <summary>
		/// Last known camera position, updated each <see cref="Draw"/> call.
		/// Used by <see cref="SetPlacedBlock"/> to skip lighting for chunks outside render distance.
		/// </summary>
		private Vector3 _cameraPosition;

		/// <summary>Countdown timer for periodic block particle emission (campfire fire particles, etc.).</summary>
		private float _blockParticleTimer;

		/// <summary>
		/// Log of block changes since last clear. Used for network delta sync —
		/// server reads and broadcasts pending changes each tick.
		/// </summary>
		private readonly List<BlockChange> _blockChangeLog = new();

		public ChunkMap(IFishEngineRunner Eng)
		{
			this.Eng = Eng;
			Chunks = new SpatialHashGrid<Chunk>(1);
		}

		/// <summary>
		/// Returns all block changes recorded since the last call to <see cref="ClearPendingChanges"/>.
		/// </summary>
		public IReadOnlyList<BlockChange> GetPendingChanges() => _blockChangeLog;

		/// <summary>
		/// Clears the block change log. Called by the server after broadcasting deltas each tick.
		/// </summary>
		public void ClearPendingChanges() => _blockChangeLog.Clear();

		public void Write(Stream Output)
		{
			using (GZipStream ZipStream = new GZipStream(Output, CompressionMode.Compress, true))
			using (var Writer = new System.IO.BinaryWriter(ZipStream))
			{
				var ChunksArray = Chunks.Items.ToArray();
				Writer.Write(ChunksArray.Length);

				foreach (var chunk in ChunksArray)
				{
					Writer.Write((int)chunk.Key.X);
					Writer.Write((int)chunk.Key.Y);
					Writer.Write((int)chunk.Key.Z);

					chunk.Value.Write(Writer);
				}
			}
		}

		public void Read(System.IO.Stream Input)
		{
			using (GZipStream ZipStream = new GZipStream(Input, CompressionMode.Decompress, true))
			using (var Reader = new System.IO.BinaryReader(ZipStream))
			{
				int Count = Reader.ReadInt32();

				for (int i = 0; i < Count; i++)
				{
					int CX = Reader.ReadInt32();
					int CY = Reader.ReadInt32();
					int CZ = Reader.ReadInt32();

					Vector3 ChunkIndex = new Vector3(CX, CY, CZ);

					Chunk Chk = new Chunk(Eng, ChunkIndex, this);
					Chk.Read(Reader);

					Chunks.Add(ChunkIndex, Chk);
				}
			}
		}

		public Chunk[] GetAllChunks() => Chunks.Values.ToArray();

		/// <summary>
		/// Marks all chunks as dirty, forcing mesh rebuild on next draw.
		/// Useful when global rendering settings change (e.g., fullbright mode).
		/// </summary>
		public void MarkAllChunksDirty()
		{
			foreach (var chunk in Chunks.Values)
			{
				chunk.MarkDirty();
			}
		}

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
						for (int dx = -1; dx <= 1 && flat; dx++)
						{
							for (int dz = -1; dz <= 1 && flat; dz++)
							{
								if (dx == 0 && dz == 0)
									continue;

								// Find topmost solid block in this neighbor column (within ±1 of y)
								bool neighborOk = false;
								for (int ny = y + 1; ny >= y - 1; ny--)
								{
									if (BlockInfo.IsSolid(GetBlock(x + dx, ny, z + dz)) &&
										GetBlock(x + dx, ny + 1, z + dz) == BlockType.None)
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
					float dx = candidate.X - selected.X;
					float dz = candidate.Z - selected.Z;
					if (dx * dx + dz * dz < minSpacingSq)
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
						int dx = x - treePositions[i].x;
						int dz = z - treePositions[i].z;
						if (dx * dx + dz * dz < MinTreeSpacing * MinTreeSpacing)
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
						int dx = x - px;
						int dz = z - pz;
						if (dx * dx + dz * dz < PondMinSpacing * PondMinSpacing)
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
				for (int dx = -pondRadius; dx <= pondRadius; dx++)
				{
					for (int dz = -pondRadius; dz <= pondRadius; dz++)
					{
						int bx = cx + dx;
						int bz = cz + dz;
						if (bx < 0 || bx >= width || bz < 0 || bz >= length)
							continue;

						float distSq = dx * dx + dz * dz;
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
				for (int dx = -outerRadius; dx <= outerRadius; dx++)
				{
					for (int dz = -outerRadius; dz <= outerRadius; dz++)
					{
						int bx = cx + dx;
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
				for (int dx = -outerRadius; dx <= outerRadius; dx++)
				{
					for (int dz = -outerRadius; dz <= outerRadius; dz++)
					{
						float distSq = dx * dx + dz * dz;
						float outerSq = (pondRadius + 2.5f) * (pondRadius + 2.5f);
						float innerSq = (pondRadius - 1.0f) * (pondRadius - 1.0f);
						if (distSq > outerSq || distSq < innerSq)
							continue;

						int bx = cx + dx;
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

		public void SetPlacedBlock(int X, int Y, int Z, PlacedBlock Block)
		{
			TranslateChunkPos(X, Y, Z, out Vector3 ChunkIndex, out Vector3 BlockPos);

			int XX = (int)BlockPos.X, YY = (int)BlockPos.Y, ZZ = (int)BlockPos.Z;
			const int MaxBlock = Chunk.ChunkSize - 1;

			// Use stackalloc-style approach with fixed arrays to avoid allocations
			Span<Vector3> affectedChunks = stackalloc Vector3[8]; // Max 8 chunks can be affected (corner case)
			int affectedCount = 0;

			// Calculate which neighbor chunks are affected based on block position
			int xMin = XX == 0 ? -1 : 0;
			int xMax = XX == MaxBlock ? 1 : 0;
			int yMin = YY == 0 ? -1 : 0;
			int yMax = YY == MaxBlock ? 1 : 0;
			int zMin = ZZ == 0 ? -1 : 0;
			int zMax = ZZ == MaxBlock ? 1 : 0;

			for (int xOff = xMin; xOff <= xMax; xOff++)
			{
				for (int yOff = yMin; yOff <= yMax; yOff++)
				{
					for (int zOff = zMin; zOff <= zMax; zOff++)
					{
						Vector3 chunkPos = ChunkIndex + new Vector3(xOff, yOff, zOff);
						// Check if already added (simple linear search for small array)
						bool found = false;
						for (int i = 0; i < affectedCount; i++)
						{
							if (affectedChunks[i] == chunkPos)
							{
								found = true;
								break;
							}
						}
						if (!found)
						{
							affectedChunks[affectedCount++] = chunkPos;
						}
					}
				}
			}

			// Mark affected chunks dirty
			for (int i = 0; i < affectedCount; i++)
			{
				if (Chunks.TryGetValue(affectedChunks[i], out var chunk))
					chunk.MarkDirty();
			}

			if (!Chunks.ContainsKey(ChunkIndex))
				Chunks.Add(ChunkIndex, new Chunk(Eng, ChunkIndex, this));

			Chunks.TryGetValue(ChunkIndex, out var targetChunk);

			// Record the change for network delta sync
			BlockType oldType = targetChunk.GetBlock(XX, YY, ZZ).Type;
			if (oldType != Block.Type)
				_blockChangeLog.Add(new BlockChange(X, Y, Z, oldType, Block.Type));

			targetChunk.SetBlock(XX, YY, ZZ, Block);

			// Recompute lighting if a light-emitting or light-blocking block was placed/removed
			bool needsLightingUpdate = BlockInfo.EmitsLight(Block.Type) ||
									   !BlockInfo.IsRendered(Block.Type) || // Block removed
									   BlockInfo.IsOpaque(Block.Type); // Opaque block affects light propagation

			if (needsLightingUpdate)
			{
				// For light sources, we need to update all chunks within light propagation range
				// Light can travel up to 15 blocks, which is almost 1 full chunk in each direction
				const int lightRangeInChunks = 1; // 15 blocks / 16 blocks per chunk, rounded up

				// Collect all chunks within light range, split by render-distance visibility
				float halfChunk = Chunk.ChunkSize * 0.5f;
				float renderDistSq = RenderDistanceBlocks * RenderDistanceBlocks;

				List<Chunk> chunksToUpdate = new List<Chunk>();
				for (int cx = -lightRangeInChunks; cx <= lightRangeInChunks; cx++)
				{
					for (int cy = -lightRangeInChunks; cy <= lightRangeInChunks; cy++)
					{
						for (int cz = -lightRangeInChunks; cz <= lightRangeInChunks; cz++)
						{
							Vector3 neighborIdx = ChunkIndex + new Vector3(cx, cy, cz);
							if (Chunks.TryGetValue(neighborIdx, out var chunk))
							{
								Vector3 chunkCenter = neighborIdx * Chunk.ChunkSize + new Vector3(halfChunk);
								if (Vector3.DistanceSquared(_cameraPosition, chunkCenter) <= renderDistSq)
								{
									chunksToUpdate.Add(chunk);
								}
								else
								{
									// Defer lighting for chunks outside render distance
									chunk.NeedsRelighting = true;
									chunk.MarkDirty();
								}
							}
						}
					}
				}

				if (chunksToUpdate.Count > 0)
				{
					// Reset all affected chunks first (prevents stale cross-chunk light values)
					foreach (var chunk in chunksToUpdate)
						chunk.ResetLighting();

					// Compute lighting in parallel using 8-phase coloring
					ComputeLightingParallel(chunksToUpdate.ToArray());

					// Mark all as dirty for mesh rebuild
					foreach (var chunk in chunksToUpdate)
						chunk.MarkDirty();
				}
			}
		}

		/// <summary>
		/// Sets a block without triggering lighting recalculation.
		/// Used during light propagation to avoid infinite recursion.
		/// </summary>
		public void SetPlacedBlockNoLighting(int X, int Y, int Z, PlacedBlock Block)
		{
			TranslateChunkPos(X, Y, Z, out Vector3 ChunkIndex, out Vector3 BlockPos);

			if (!Chunks.ContainsKey(ChunkIndex))
				return; // Don't create chunks during light propagation

			Chunks.TryGetValue(ChunkIndex, out var targetChunk);
			targetChunk.SetBlock((int)BlockPos.X, (int)BlockPos.Y, (int)BlockPos.Z, Block);
		}

		public void SetBlock(int X, int Y, int Z, BlockType T) => SetPlacedBlock(X, Y, Z, new PlacedBlock(T));

		public PlacedBlock GetPlacedBlock(int X, int Y, int Z, out Chunk Chk)
		{
			TranslateChunkPos(X, Y, Z, out Vector3 ChunkIndex, out Vector3 BlockPos);
			if (Chunks.TryGetValue(ChunkIndex, out Chk))
				return Chk.GetBlock((int)BlockPos.X, (int)BlockPos.Y, (int)BlockPos.Z);
			Chk = null;
			return new PlacedBlock(BlockType.None);
		}

		public BlockType GetBlock(int X, int Y, int Z) => GetPlacedBlock(X, Y, Z, out _).Type;
		public BlockType GetBlock(Vector3 Pos) => GetBlock((int)Pos.X, (int)Pos.Y, (int)Pos.Z);

		/// <summary>
		/// Gets a chunk by its global chunk index, or null if not loaded.
		/// </summary>
		public Chunk GetChunk(Vector3 chunkIndex)
		{
			Chunks.TryGetValue(chunkIndex, out var chunk);
			return chunk;
		}

		/// <summary>
		/// Returns true if the block at the given position is water.
		/// </summary>
		public bool IsWaterAt(Vector3 Pos) => BlockInfo.IsWater(GetBlock(Pos));
		public bool IsWaterAt(int X, int Y, int Z) => BlockInfo.IsWater(GetBlock(X, Y, Z));

		/// <summary>
		/// Gets the effective light level at a world position as a normalized value (0.0 to 1.0).
		/// Samples the block at the position and returns the maximum light from all faces.
		/// </summary>
		public float GetLightLevel(Vector3 Pos)
		{
			return GetLightLevel((int)MathF.Floor(Pos.X), (int)MathF.Floor(Pos.Y), (int)MathF.Floor(Pos.Z));
		}

		/// <summary>
		/// Gets the effective light level at a world position as a normalized value (0.0 to 1.0).
		/// </summary>
		public float GetLightLevel(int X, int Y, int Z)
		{
			var block = GetPlacedBlock(X, Y, Z, out _);
			// Get max of skylight and block light
			byte maxSky = block.GetMaxSkylight();
			byte maxBlock = block.GetMaxBlockLight();
			// Apply sky multiplier
			float skyContrib = maxSky * BlockLight.SkyLightMultiplier;
			float combined = MathF.Max(skyContrib, maxBlock);
			// Apply ambient minimum
			combined = MathF.Max(combined, BlockLight.AmbientLight);
			// Normalize from 0-15 to 0.0-1.0
			return combined / 15f;
		}

		/// <summary>
		/// Gets the light color at a world position for rendering.
		/// </summary>
		public Color GetLightColor(Vector3 Pos)
		{
			float level = GetLightLevel(Pos);
			byte val = (byte)(level * 255);
			return new Color(val, val, val, (byte)255);
		}

		void TranslateChunkPos(int X, int Y, int Z, out Vector3 ChunkIndex, out Vector3 BlockPos)
		{
			TransPosScalar(X, out int ChkX, out int BlkX);
			TransPosScalar(Y, out int ChkY, out int BlkY);
			TransPosScalar(Z, out int ChkZ, out int BlkZ);
			ChunkIndex = new Vector3(ChkX, ChkY, ChkZ);
			BlockPos = new Vector3(BlkX, BlkY, BlkZ);
		}
		void TransPosScalar(int S, out int ChunkIndex, out int BlockPos)
		{
			ChunkIndex = (int)Math.Floor((float)S / Chunk.ChunkSize);
			BlockPos = Utils.Mod(S, Chunk.ChunkSize);
		}
		public void GetWorldPos(int X, int Y, int Z, Vector3 ChunkIndex, out Vector3 GlobalPos)
		{
			GlobalPos = ChunkIndex * Chunk.ChunkSize + new Vector3(X, Y, Z);
		}

		public void ComputeLighting()
		{
			var allChunks = GetAllChunks();

			// Reset all chunks in parallel — purely per-chunk, no cross-chunk dependencies
			Parallel.ForEach(allChunks, c => c.ResetLighting());

			// Compute lighting in parallel using 8-phase coloring
			ComputeLightingParallel(allChunks);

			// Mark all dirty in parallel
			Parallel.ForEach(allChunks, c => c.MarkDirty());
		}

		/// <summary>
		/// Groups chunks into 8 phases using 2×2×2 index parity coloring and computes
		/// lighting for each phase in parallel. Within each phase, chunks are ≥2 apart
		/// on every axis, so cross-chunk border writes (which extend at most 1 block into
		/// face-neighbors) target non-overlapping blocks and cannot race.
		/// </summary>
		private void ComputeLightingParallel(Chunk[] chunks)
		{
			var phases = new List<Chunk>[8];
			for (int i = 0; i < 8; i++)
				phases[i] = new List<Chunk>(chunks.Length / 8 + 1);

			foreach (var c in chunks)
			{
				int cx = ((int)c.GlobalChunkIndex.X % 2 + 2) % 2;
				int cy = ((int)c.GlobalChunkIndex.Y % 2 + 2) % 2;
				int cz = ((int)c.GlobalChunkIndex.Z % 2 + 2) % 2;
				phases[cx + cy * 2 + cz * 4].Add(c);
			}

			for (int phase = 0; phase < 8; phase++)
				if (phases[phase].Count > 0)
					Parallel.ForEach(phases[phase], c => c.ComputeLightingWithoutReset());
		}

		/// <summary>
		/// Computes lighting including entity light sources with shadow support.
		/// </summary>
		/// <param name="entityLights">Collection of point lights from entities.</param>
		public void ComputeLightingWithEntities(IEnumerable<PointLight> entityLights)
		{
			var allChunks = GetAllChunks();

			// Reset all chunks in parallel
			Parallel.ForEach(allChunks, c => c.ResetLighting());

			// Compute standard block-based lighting in parallel
			ComputeLightingParallel(allChunks);

			// Then add entity lights with shadows (uses same cross-chunk write pattern)
			if (entityLights != null)
			{
				var lightList = entityLights.ToList();
				if (lightList.Count > 0)
				{
					foreach (Chunk C in allChunks)
						C.ComputeEntityLights(lightList);
				}
			}

			// Mark all dirty in parallel
			Parallel.ForEach(allChunks, c => c.MarkDirty());
		}

		public void Tick()
		{
		}

		/// <summary>
		/// Emits particles for blocks that produce them (e.g. campfire fire particles).
		/// Called each frame; internally throttled to emit every ~0.25 seconds.
		/// Uses <see cref="_cameraPosition"/> from the previous Draw call for distance filtering.
		/// </summary>
		public void EmitBlockParticles(ParticleSystem particle, float dt)
		{
			const float EmitInterval = 0.25f;

			_blockParticleTimer -= dt;
			if (_blockParticleTimer > 0f)
				return;
			_blockParticleTimer = EmitInterval;

			float halfChunk = Chunk.ChunkSize * 0.5f;
			float renderDistSq = RenderDistanceBlocks * RenderDistanceBlocks;

			foreach (var KV in Chunks.Items)
			{
				Chunk chunk = KV.Value;
				if (!chunk.HasCustomModelBlocks)
					continue;

				Vector3 chunkPos = KV.Key * new Vector3(Chunk.ChunkSize);
				Vector3 chunkCenter = chunkPos + new Vector3(halfChunk);
				if (Vector3.DistanceSquared(_cameraPosition, chunkCenter) > renderDistSq)
					continue;

				for (int i = 0; i < chunk.CachedCustomModelBlocks.Count; i++)
				{
					var cmb = chunk.CachedCustomModelBlocks[i];
					if (cmb.Type == BlockType.Campfire)
					{
						Vector3 worldPos = chunkPos + new Vector3(cmb.X + 0.5f, cmb.Y + 0.6f, cmb.Z + 0.5f);

						float forceFactor = 1.8f;
						float randomUnitFactor = 0.6f;
						Vector3 hitNormal = new Vector3(0, 1, 0);
						if (hitNormal.Y == 0)
						{
							forceFactor *= 2;
							randomUnitFactor = 0.4f;
						}
						Vector3 rndDir = Vector3.Normalize(hitNormal + Utils.GetRandomUnitVector() * randomUnitFactor);

						particle.SpawnFire(worldPos, rndDir * forceFactor, Color.White, (float)(Random.Shared.NextDouble() + 0.5), noCollide: true, lifetime: 0.9f);
					}
				}
			}
		}

		public void Draw(ref Frustum Fr)
		{
			_cameraPosition = Fr.CamPos;
			float halfChunk = Chunk.ChunkSize * 0.5f;
			float renderDistSq = RenderDistanceBlocks * RenderDistanceBlocks;

			// Collect chunks that entered render distance and need deferred relighting
			List<Chunk> relightChunks = null;
			foreach (var KV in Chunks.Items)
			{
				if (!KV.Value.NeedsRelighting)
					continue;

				Vector3 ChunkCenter = KV.Key * new Vector3(Chunk.ChunkSize) + new Vector3(halfChunk);
				if (Vector3.DistanceSquared(Fr.CamPos, ChunkCenter) <= renderDistSq)
				{
					relightChunks ??= new List<Chunk>();
					relightChunks.Add(KV.Value);
					KV.Value.NeedsRelighting = false;
				}
			}

			if (relightChunks != null)
			{
				foreach (var chunk in relightChunks)
					chunk.ResetLighting();
				ComputeLightingParallel(relightChunks.ToArray());
				foreach (var chunk in relightChunks)
					chunk.MarkDirty();
			}

			foreach (var KV in Chunks.Items)
			{
				Vector3 ChunkPos = KV.Key * new Vector3(Chunk.ChunkSize);
				Vector3 ChunkCenter = ChunkPos + new Vector3(halfChunk);
				if (Vector3.DistanceSquared(Fr.CamPos, ChunkCenter) > renderDistSq)
					continue;

				KV.Value.Draw(ChunkPos, ref Fr);
			}

			Utils.DrawRaycastRecord();
		}

		public void DrawTransparent(ref Frustum Fr, Vector3 cameraPos)
		{
			// Collect all transparent faces from visible chunks within render distance
			TransparentFaceBuffer.Clear();

			float halfChunk = Chunk.ChunkSize * 0.5f;
			float renderDistSq = RenderDistanceBlocks * RenderDistanceBlocks;

			foreach (var KV in Chunks.Items)
			{
				Vector3 ChunkPos = KV.Key * new Vector3(Chunk.ChunkSize);
				Vector3 ChunkCenter = ChunkPos + new Vector3(halfChunk);
				if (Vector3.DistanceSquared(cameraPos, ChunkCenter) > renderDistSq)
					continue;

				if (KV.Value.HasTransparentFaces())
				{
					var faces = KV.Value.GetTransparentFaces(ref Fr);
					TransparentFaceBuffer.AddRange(faces);
				}
			}

			if (TransparentFaceBuffer.Count == 0)
				return;

			int faceCount = TransparentFaceBuffer.Count;
			int vertexCount = faceCount * 6;

			// Ensure sorting buffers are large enough
			if (DistanceBuffer.Length < faceCount)
			{
				int newSize = faceCount * 2;
				DistanceBuffer = new float[newSize];
				IndexBuffer = new int[newSize];
			}

			// Ensure mesh buffers are large enough (only reallocate when capacity exceeded)
			if (vertexCount > TransparentMeshCapacity)
			{
				int newCapacity = Math.Max(vertexCount * 2, 6144); // Start with reasonable size
				TransparentMeshCapacity = newCapacity;
				TransparentVertices = new Vector3[newCapacity];
				TransparentNormals = new Vector3[newCapacity];
				TransparentTexCoords = new Vector2[newCapacity];
				TransparentColors = new Color[newCapacity];

				// Recreate mesh with new capacity
				if (TransparentMeshInitialized)
				{
					Raylib.UnloadMesh(TransparentMesh);
				}
				TransparentMesh = CreateTransparentMesh(newCapacity);
				TransparentMaterial = Raylib.LoadMaterialDefault();
				Raylib.SetMaterialTexture(ref TransparentMaterial, MaterialMapIndex.Albedo, ResMgr.AtlasTexture);
				TransparentMeshInitialized = true;
			}

			// Calculate distances and build index array
			for (int i = 0; i < faceCount; i++)
			{
				DistanceBuffer[i] = Vector3.DistanceSquared(cameraPos, TransparentFaceBuffer[i].Center);
				IndexBuffer[i] = i;
			}

			// Sort indices by distance (back-to-front)
			Array.Sort(IndexBuffer, 0, faceCount,
				Comparer<int>.Create((a, b) => DistanceBuffer[b].CompareTo(DistanceBuffer[a])));

			// Fill buffers with sorted face data
			int vIdx = 0;
			for (int i = 0; i < faceCount; i++)
			{
				var face = TransparentFaceBuffer[IndexBuffer[i]];
				for (int j = 0; j < 6; j++)
				{
					var v = face.Vertices[j];
					TransparentVertices[vIdx] = v.Position;
					TransparentNormals[vIdx] = v.Normal;
					TransparentTexCoords[vIdx] = v.UV;
					TransparentColors[vIdx] = v.Color;
					vIdx++;
				}
			}

			// Update mesh buffers on GPU (much faster than recreating mesh)
			fixed (Vector3* verts = TransparentVertices)
			fixed (Vector3* norms = TransparentNormals)
			fixed (Vector2* uvs = TransparentTexCoords)
			fixed (Color* cols = TransparentColors)
			{
				Raylib.UpdateMeshBuffer(TransparentMesh, 0, verts, vertexCount * sizeof(Vector3), 0); // vertices
				Raylib.UpdateMeshBuffer(TransparentMesh, 1, uvs, vertexCount * sizeof(Vector2), 0);   // texcoords
				Raylib.UpdateMeshBuffer(TransparentMesh, 2, norms, vertexCount * sizeof(Vector3), 0); // normals
				Raylib.UpdateMeshBuffer(TransparentMesh, 3, cols, vertexCount * sizeof(Color), 0);    // colors
			}

			// Update vertex count for this frame's draw
			TransparentMesh.VertexCount = vertexCount;
			TransparentMesh.TriangleCount = vertexCount / 3;

			// Draw
			Raylib.BeginBlendMode(BlendMode.Alpha);
			Rlgl.DisableDepthMask();
			Raylib.DrawMesh(TransparentMesh, TransparentMaterial, Matrix4x4.Identity);
			Rlgl.EnableDepthMask();
			Raylib.EndBlendMode();
		}

		Mesh CreateTransparentMesh(int capacity)
		{
			Mesh mesh = new Mesh();
			mesh.VertexCount = capacity;
			mesh.TriangleCount = capacity / 3;

			// Allocate GPU-side buffers
			mesh.Vertices = (float*)NativeMemory.AllocZeroed((nuint)(sizeof(Vector3) * capacity));
			mesh.Normals = (float*)NativeMemory.AllocZeroed((nuint)(sizeof(Vector3) * capacity));
			mesh.TexCoords = (float*)NativeMemory.AllocZeroed((nuint)(sizeof(Vector2) * capacity));
			mesh.Colors = (byte*)NativeMemory.AllocZeroed((nuint)(sizeof(Color) * capacity));

			Raylib.UploadMesh(ref mesh, true); // dynamic = true for frequent updates
			return mesh;
		}

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

		public bool IsSolid(int X, int Y, int Z)
		{
			if (BlockInfo.IsSolid(GetBlock(X, Y, Z)))
				return true;

			return false;
		}

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
			List<RayCollision> Cols = new List<RayCollision>();

			foreach (var KV in Chunks.Items)
			{
				Vector3 ChunkPos = KV.Key * new Vector3(Chunk.ChunkSize);

				RayCollision Col = KV.Value.Collide(ChunkPos, R);
				if (Col.Hit)
				{
					Cols.Add(Col);
				}
			}

			Cols = Cols.Where(c => c.Distance <= MaxLen).ToList();

			if (Cols.Count == 0)
				return new RayCollision() { Hit = false };

			return Cols.OrderBy(c => c.Distance).FirstOrDefault();
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
