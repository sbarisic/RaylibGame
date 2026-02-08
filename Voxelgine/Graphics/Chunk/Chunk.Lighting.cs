using Raylib_cs;
using System.Numerics;

using Voxelgine.Engine;

using System;
using System.Collections.Generic;

namespace Voxelgine.Graphics
{
	public unsafe partial class Chunk
	{
		// Helper struct for queue (faster than Vector3 for ints)
		// BlockPos is used to represent integer block positions in the light propagation queue.
		struct BlockPos
		{
			public int X, Y, Z;
			public BlockPos(int x, int y, int z)
			{
				X = x;
				Y = y;
				Z = z;
			}
		}

		// Static direction arrays for light propagation (avoid allocation per call)
		static readonly int[] DirX = { 1, -1, 0, 0, 0, 0 };
		static readonly int[] DirY = { 0, 0, 1, -1, 0, 0 };
		static readonly int[] DirZ = { 0, 0, 0, 0, 1, -1 };

		// Cached sky exposure per column (16x16 = 256 bools)
		bool[] SkyExposureCache;
		bool SkyExposureCacheValid;

		// Reusable collections for lighting propagation (avoid allocations)
		Queue<BlockPos> _skylightQueue;
		Queue<(int x, int y, int z, byte level)> _blockLightQueue;
		List<PointLight> _lightSources;
		bool[] _visitedBlocks; // Flat array: idx = x + ChunkSize*(y + ChunkSize*z)

		/// <summary>
		/// Invalidates the sky exposure cache, forcing recalculation on next lighting compute.
		/// </summary>
		public void InvalidateSkyExposureCache()
		{
			SkyExposureCacheValid = false;
		}

		/// <summary>
		/// Resets all light values in this chunk to black.
		/// Called before computing lighting to ensure clean state.
		/// </summary>
		public void ResetLighting()
		{
			for (int i = 0; i < Blocks.Length; i++)
			{
				Blocks[i].SetAllLights(BlockLight.Black);
			}
		}

		/// <summary>
		/// Computes lighting for this chunk. Handles both skylight and block light separately.
		/// Skylight propagates from sky-exposed blocks, block light from light-emitting blocks.
		/// </summary>
		public void ComputeLighting()
		{
			ResetLighting();
			ComputeLightingWithoutReset();
		}

		/// <summary>
		/// Computes lighting without resetting light values first.
		/// Used when all chunks need to be reset before any propagation to avoid
		/// cross-chunk light values being overwritten.
		/// </summary>
		public void ComputeLightingWithoutReset()
		{
			// Compute skylight (from sky exposure)
			ComputeSkylight();

			// Compute block light (from light-emitting blocks)
			ComputeBlockLights();

			Dirty = true;
		}

		/// <summary>
		/// Computes skylight by propagating light from sky-exposed blocks.
		/// Sky-exposed means the block can see the sky directly above (no opaque blocks in the way).
		/// </summary>
		void ComputeSkylight()
		{
			// Reuse or allocate queue
			_skylightQueue ??= new Queue<BlockPos>(ChunkSize * ChunkSize);
			_skylightQueue.Clear();
			var skylightQueue = _skylightQueue;

			// Build or use cached sky exposure data
			if (!SkyExposureCacheValid || SkyExposureCache == null)
			{
				SkyExposureCache = new bool[ChunkSize * ChunkSize];
				for (int x = 0; x < ChunkSize; x++)
				{
					for (int z = 0; z < ChunkSize; z++)
					{
						SkyExposureCache[x + ChunkSize * z] = IsColumnSkyExposed(x, z);
					}
				}
				SkyExposureCacheValid = true;
			}

			// First pass: mark all sky-exposed air blocks with full skylight (15)
			// and add them to the propagation queue
			for (int x = 0; x < ChunkSize; x++)
			{
				for (int z = 0; z < ChunkSize; z++)
				{
					// Use cached sky exposure
					bool skyExposed = SkyExposureCache[x + ChunkSize * z];

					for (int y = ChunkSize - 1; y >= 0; y--)
					{
						int idx = x + ChunkSize * (y + ChunkSize * z);
						PlacedBlock block = Blocks[idx];

						if (skyExposed && (!BlockInfo.IsRendered(block.Type) || !BlockInfo.IsOpaque(block.Type)))
						{
							// This block receives full skylight
							Blocks[idx].SetSkylight(15);
							skylightQueue.Enqueue(new BlockPos(x, y, z));
						}
						else if (BlockInfo.IsOpaque(block.Type))
						{
							// Hit an opaque block, column below is no longer sky-exposed
							skyExposed = false;
						}
					}
				}
			}

			// Propagate skylight in all directions with attenuation
			PropagateSkylight(skylightQueue);
		}

		/// <summary>
		/// Checks if a column is sky-exposed by looking at blocks above this chunk.
		/// </summary>
		bool IsColumnSkyExposed(int localX, int localZ)
		{
			// Get world position of the top of this column
			WorldMap.GetWorldPos(localX, ChunkSize, localZ, GlobalChunkIndex, out Vector3 worldPos);

			// Check blocks above this chunk (up to a reasonable height)
			for (int y = (int)worldPos.Y; y < (int)worldPos.Y + 64; y++)
			{
				BlockType blockAbove = WorldMap.GetBlock((int)worldPos.X, y, (int)worldPos.Z);
				if (BlockInfo.IsOpaque(blockAbove))
				{
					return false; // There's an opaque block above
				}
			}
			return true; // Column is sky-exposed
		}

		/// <summary>
		/// Propagates skylight from bright blocks to darker neighbors.
		/// </summary>
		void PropagateSkylight(Queue<BlockPos> queue)
		{
			while (queue.Count > 0)
			{
				BlockPos pos = queue.Dequeue();

				if (pos.X < 0 || pos.X >= ChunkSize || pos.Y < 0 || pos.Y >= ChunkSize || pos.Z < 0 || pos.Z >= ChunkSize)
					continue;

				int idx = pos.X + ChunkSize * (pos.Y + ChunkSize * pos.Z);
				byte currentSkylight = Blocks[idx].GetMaxSkylight();

				if (currentSkylight <= 1)
					continue;

				for (int d = 0; d < 6; d++)
				{
					int nx = pos.X + DirX[d];
					int ny = pos.Y + DirY[d];
					int nz = pos.Z + DirZ[d];

					// Handle cross-chunk propagation
					if (nx < 0 || nx >= ChunkSize || ny < 0 || ny >= ChunkSize || nz < 0 || nz >= ChunkSize)
					{
						WorldMap.GetWorldPos(pos.X, pos.Y, pos.Z, GlobalChunkIndex, out Vector3 worldPos);
						int worldNx = (int)worldPos.X + DirX[d];
						int worldNy = (int)worldPos.Y + DirY[d];
						int worldNz = (int)worldPos.Z + DirZ[d];

						PlacedBlock neighborBlock = WorldMap.GetPlacedBlock(worldNx, worldNy, worldNz, out Chunk neighborChunk);
						if (neighborChunk == null || BlockInfo.IsOpaque(neighborBlock.Type))
							continue;

						byte neighborSkylight = neighborBlock.GetMaxSkylight();
						byte newSkylight = (byte)(currentSkylight - 1);
						if (newSkylight > neighborSkylight)
						{
							neighborBlock.SetSkylight(newSkylight);
							WorldMap.SetPlacedBlockNoLighting(worldNx, worldNy, worldNz, neighborBlock);
							neighborChunk.MarkDirty(); // Ensure neighbor chunk mesh is rebuilt
						}
						continue;
					}

					int nidx = nx + ChunkSize * (ny + ChunkSize * nz);
					PlacedBlock neighborBlock2 = Blocks[nidx];

					if (BlockInfo.IsOpaque(neighborBlock2.Type))
						continue;

					byte neighborSkylight2 = neighborBlock2.GetMaxSkylight();
					byte newSkylight2 = (byte)(currentSkylight - 1);

					if (newSkylight2 > neighborSkylight2)
					{
						Blocks[nidx].SetSkylight(newSkylight2);
						queue.Enqueue(new BlockPos(nx, ny, nz));
					}
				}
			}
		}

		/// <summary>
		/// Computes block light from light-emitting blocks (Glowstone, Campfire, etc.)
		/// Uses shadow tracing when enabled to create realistic shadows.
		/// Also considers light sources from neighboring chunks near borders.
		/// </summary>
		void ComputeBlockLights()
		{
			// Reuse light sources list to avoid allocation
			_lightSources ??= new List<PointLight>(64);
			_lightSources.Clear();
			var lightSources = _lightSources;

			for (int x = 0; x < ChunkSize; x++)
			{
				for (int y = 0; y < ChunkSize; y++)
				{
					for (int z = 0; z < ChunkSize; z++)
					{
						int idx = x + ChunkSize * (y + ChunkSize * z);
						PlacedBlock block = Blocks[idx];

						if (BlockInfo.EmitsLight(block.Type))
						{
							byte lightLevel = BlockInfo.GetLightEmission(block.Type);
							Blocks[idx].SetBlockLightLevel(lightLevel);

							// Get world position for this light source
							WorldMap.GetWorldPos(x, y, z, GlobalChunkIndex, out Vector3 worldPos);
							// Center the light in the block
							Vector3 lightPos = worldPos + new Vector3(0.5f, 0.5f, 0.5f);
							lightSources.Add(new PointLight(lightPos, lightLevel, castsShadows: true));
						}
					}
				}
			}

			// Collect light sources from neighboring chunks that can affect this chunk
			CollectNeighborLightSources(lightSources);

			// Propagate light from each source with shadow checking
			foreach (var light in lightSources)
			{
				PropagateBlockLightWithShadows(light);
			}
		}

		/// <summary>
		/// Collects light-emitting blocks from neighboring chunks that are within
		/// light propagation range of this chunk's borders.
		/// </summary>
		void CollectNeighborLightSources(List<PointLight> lightSources)
		{
			const int maxLightRange = 15; // Maximum light propagation distance

			// Check all 26 neighboring chunk directions (6 faces + 12 edges + 8 corners)
			for (int dx = -1; dx <= 1; dx++)
			{
				for (int dy = -1; dy <= 1; dy++)
				{
					for (int dz = -1; dz <= 1; dz++)
					{
						if (dx == 0 && dy == 0 && dz == 0)
							continue; // Skip self

						Vector3 neighborChunkIndex = GlobalChunkIndex + new Vector3(dx, dy, dz);
						Chunk neighborChunk = WorldMap.GetChunk(neighborChunkIndex);

						if (neighborChunk == null)
							continue;

						// Scan the border region of the neighbor chunk facing us
						ScanNeighborBorderForLights(neighborChunk, dx, dy, dz, maxLightRange, lightSources);
					}
				}
			}
		}

		/// <summary>
		/// Scans the border region of a neighbor chunk for light sources.
		/// Only scans blocks within light range of our chunk boundary.
		/// </summary>
		void ScanNeighborBorderForLights(Chunk neighbor, int dx, int dy, int dz, int range, List<PointLight> lightSources)
		{
			// Determine which region of the neighbor to scan based on direction
			// If dx == 1, neighbor is to our +X side, so we scan its low-X border (0 to range)
			// If dx == -1, neighbor is to our -X side, so we scan its high-X border (ChunkSize-range to ChunkSize)
			int xStart = dx == 1 ? 0 : (dx == -1 ? Math.Max(0, ChunkSize - range) : 0);
			int xEnd = dx == 1 ? Math.Min(range, ChunkSize) : (dx == -1 ? ChunkSize : ChunkSize);
			int yStart = dy == 1 ? 0 : (dy == -1 ? Math.Max(0, ChunkSize - range) : 0);
			int yEnd = dy == 1 ? Math.Min(range, ChunkSize) : (dy == -1 ? ChunkSize : ChunkSize);
			int zStart = dz == 1 ? 0 : (dz == -1 ? Math.Max(0, ChunkSize - range) : 0);
			int zEnd = dz == 1 ? Math.Min(range, ChunkSize) : (dz == -1 ? ChunkSize : ChunkSize);

			for (int x = xStart; x < xEnd; x++)
			{
				for (int y = yStart; y < yEnd; y++)
				{
					for (int z = zStart; z < zEnd; z++)
					{
						int idx = x + ChunkSize * (y + ChunkSize * z);
						PlacedBlock block = neighbor.Blocks[idx];

						if (BlockInfo.EmitsLight(block.Type))
						{
							byte lightLevel = BlockInfo.GetLightEmission(block.Type);
							WorldMap.GetWorldPos(x, y, z, neighbor.GlobalChunkIndex, out Vector3 worldPos);
							Vector3 lightPos = worldPos + new Vector3(0.5f, 0.5f, 0.5f);
							lightSources.Add(new PointLight(lightPos, lightLevel, castsShadows: true));
						}
					}
				}
			}
		}

		/// <summary>
		/// Propagates block light from a single source with shadow tracing.
		/// Uses BFS but checks line-of-sight to the source for each propagation step.
		/// Handles both internal and external (neighbor chunk) light sources.
		/// </summary>
		void PropagateBlockLightWithShadows(PointLight light)
		{
			// Reuse queue to avoid allocation
			_blockLightQueue ??= new Queue<(int, int, int, byte)>(512);
			_blockLightQueue.Clear();
			var queue = _blockLightQueue;

			// Reuse visited array - more efficient than HashSet for dense local access
			const int totalBlocks = ChunkSize * ChunkSize * ChunkSize;
			_visitedBlocks ??= new bool[totalBlocks];
			Array.Clear(_visitedBlocks, 0, totalBlocks);
			var visited = _visitedBlocks;

			// Start from the light source block
			int srcX = (int)MathF.Floor(light.Position.X);
			int srcY = (int)MathF.Floor(light.Position.Y);
			int srcZ = (int)MathF.Floor(light.Position.Z);

			// Cache chunk world position once
			WorldMap.GetWorldPos(0, 0, 0, GlobalChunkIndex, out Vector3 chunkWorldPos);
			int chunkWorldX = (int)chunkWorldPos.X;
			int chunkWorldY = (int)chunkWorldPos.Y;
			int chunkWorldZ = (int)chunkWorldPos.Z;

			int localX = srcX - chunkWorldX;
			int localY = srcY - chunkWorldY;
			int localZ = srcZ - chunkWorldZ;

			if (localX >= 0 && localX < ChunkSize && localY >= 0 && localY < ChunkSize && localZ >= 0 && localZ < ChunkSize)
			{
				// Light source is inside this chunk - start from its position
				queue.Enqueue((localX, localY, localZ, light.Intensity));
			}
			else
			{
				// Light source is in a neighbor chunk - find entry points at chunk boundary
				// Scan boundary blocks that could receive light from this source
				QueueBoundaryBlocksForExternalLight(light, chunkWorldX, chunkWorldY, chunkWorldZ, queue, visited);
			}

			while (queue.Count > 0)
			{
				var (x, y, z, level) = queue.Dequeue();

				if (level <= 1)
					continue;

				for (int d = 0; d < 6; d++)
				{
					int nx = x + DirX[d];
					int ny = y + DirY[d];
					int nz = z + DirZ[d];

					// Handle cross-chunk propagation
					if (nx < 0 || nx >= ChunkSize || ny < 0 || ny >= ChunkSize || nz < 0 || nz >= ChunkSize)
					{
						// Calculate world position using cached chunk coords
						int worldNx = chunkWorldX + x + DirX[d];
						int worldNy = chunkWorldY + y + DirY[d];
						int worldNz = chunkWorldZ + z + DirZ[d];

						PlacedBlock neighborBlock = WorldMap.GetPlacedBlock(worldNx, worldNy, worldNz, out Chunk neighborChunk);
						if (neighborChunk == null || BlockInfo.IsOpaque(neighborBlock.Type))
							continue;

						// Shadow check: verify line-of-sight to light source
						if (light.CastsShadows)
						{
							Vector3 neighborCenter = new Vector3(worldNx + 0.5f, worldNy + 0.5f, worldNz + 0.5f);
							if (!ShadowTracer.HasLineOfSight(WorldMap, light.Position, neighborCenter))
								continue; // In shadow, skip
						}

						byte neighborBlockLight = neighborBlock.GetMaxBlockLight();
						byte newBlockLight = (byte)(level - 1);
						if (newBlockLight > neighborBlockLight)
						{
							neighborBlock.SetBlockLightLevel(newBlockLight);
							WorldMap.SetPlacedBlockNoLighting(worldNx, worldNy, worldNz, neighborBlock);
							neighborChunk.MarkDirty(); // Ensure neighbor chunk mesh is rebuilt
						}
						continue;
					}

					// Within chunk bounds - use array index for visited check (faster than HashSet)
					int nidx = nx + ChunkSize * (ny + ChunkSize * nz);
					if (visited[nidx])
						continue;

					PlacedBlock neighborBlock2 = Blocks[nidx];

					if (BlockInfo.IsOpaque(neighborBlock2.Type))
						continue;

					// Shadow check: verify line-of-sight to light source
					if (light.CastsShadows)
					{
						// Calculate world position using cached chunk coords instead of calling GetWorldPos
						Vector3 neighborCenter = new Vector3(
							chunkWorldX + nx + 0.5f,
							chunkWorldY + ny + 0.5f,
							chunkWorldZ + nz + 0.5f);
						if (!ShadowTracer.HasLineOfSight(WorldMap, light.Position, neighborCenter))
							continue; // In shadow, skip
					}

					byte neighborBlockLight2 = neighborBlock2.GetMaxBlockLight();
					byte newBlockLight2 = (byte)(level - 1);

					if (newBlockLight2 > neighborBlockLight2)
					{
						Blocks[nidx].SetBlockLightLevel(newBlockLight2);
						visited[nidx] = true;
						queue.Enqueue((nx, ny, nz, newBlockLight2));
					}
				}
			}
		}

		/// <summary>
		/// Finds boundary blocks in this chunk that should receive light from an external source
		/// and queues them with the appropriate light level based on distance.
		/// Optimized to use integer chunk world coords and array-based visited tracking.
		/// </summary>
		void QueueBoundaryBlocksForExternalLight(PointLight light, int chunkWorldX, int chunkWorldY, int chunkWorldZ,
			Queue<(int x, int y, int z, byte level)> queue, bool[] visited)
		{
			int maxRange = light.Intensity;
			int lightX = (int)MathF.Floor(light.Position.X);
			int lightY = (int)MathF.Floor(light.Position.Y);
			int lightZ = (int)MathF.Floor(light.Position.Z);

			// Calculate bounding box intersection with chunk to limit iteration
			int minX = Math.Max(0, lightX - maxRange - chunkWorldX);
			int maxX = Math.Min(ChunkSize, lightX + maxRange + 1 - chunkWorldX);
			int minY = Math.Max(0, lightY - maxRange - chunkWorldY);
			int maxY = Math.Min(ChunkSize, lightY + maxRange + 1 - chunkWorldY);
			int minZ = Math.Max(0, lightZ - maxRange - chunkWorldZ);
			int maxZ = Math.Min(ChunkSize, lightZ + maxRange + 1 - chunkWorldZ);

			// Skip if bounding box doesn't intersect chunk
			if (minX >= maxX || minY >= maxY || minZ >= maxZ)
				return;

			// Scan only blocks within the light's bounding box
			for (int x = minX; x < maxX; x++)
			{
				for (int y = minY; y < maxY; y++)
				{
					for (int z = minZ; z < maxZ; z++)
					{
						// Calculate world position of block center
						float blockCenterX = chunkWorldX + x + 0.5f;
						float blockCenterY = chunkWorldY + y + 0.5f;
						float blockCenterZ = chunkWorldZ + z + 0.5f;

						// Calculate Manhattan distance to light source
						float dist = MathF.Abs(blockCenterX - light.Position.X) +
									 MathF.Abs(blockCenterY - light.Position.Y) +
									 MathF.Abs(blockCenterZ - light.Position.Z);

						// Skip if too far from light
						if (dist > maxRange)
							continue;

						// Calculate light level at this distance
						byte lightLevel = (byte)Math.Max(0, light.Intensity - (int)dist);
						if (lightLevel <= 1)
							continue;

						// Check if block is transparent (can receive light)
						int idx = x + ChunkSize * (y + ChunkSize * z);
						if (BlockInfo.IsOpaque(Blocks[idx].Type))
							continue;

						// Shadow check
						if (light.CastsShadows)
						{
							Vector3 blockCenter = new Vector3(blockCenterX, blockCenterY, blockCenterZ);
							if (!ShadowTracer.HasLineOfSight(WorldMap, light.Position, blockCenter))
								continue;
						}

						// Only queue if this light level is better than existing
						byte existingLight = Blocks[idx].GetMaxBlockLight();
						if (lightLevel > existingLight)
						{
							Blocks[idx].SetBlockLightLevel(lightLevel);
							if (!visited[idx])
							{
								visited[idx] = true;
								queue.Enqueue((x, y, z, lightLevel));
							}
						}
					}
				}
			}
		}

		/// <summary>
		/// Computes light from entity light sources with shadow support.
		/// Called by ChunkMap when entities are present.
		/// </summary>
		public void ComputeEntityLights(IEnumerable<PointLight> entityLights)
		{
			foreach (var light in entityLights)
			{
				PropagateBlockLightWithShadows(light);
			}
		}
	}
}
