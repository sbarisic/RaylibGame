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

		struct BlockLightEntry
		{
			public int X, Y, Z;
			public byte Level;
			public BlockLightEntry(int x, int y, int z, byte level)
			{
				X = x;
				Y = y;
				Z = z;
				Level = level;
			}
		}

		// Static direction arrays for light propagation (avoid allocation per call)
		static readonly int[] DirX = { 1, -1, 0, 0, 0, 0 };
		static readonly int[] DirY = { 0, 0, 1, -1, 0, 0 };
		static readonly int[] DirZ = { 0, 0, 0, 0, 1, -1 };

		// Cached sky exposure per column (16x16 = 256 bools)
		bool[] SkyExposureCache;
		bool SkyExposureCacheValid;

		// Reusable arrays for lighting propagation (avoid allocations)
		const int QueueCapacity = ChunkSize * ChunkSize * ChunkSize * 2;
		BlockPos[] _skylightQueue;
		int _skylightHead, _skylightTail;
		BlockLightEntry[] _blockLightQueue;
		int _blockLightHead, _blockLightTail;

		//List<PointLight> _lightSources;
		PointLight[] lightSources = new PointLight[32];


		int[] _visitedGeneration; // Generation-stamped visited: block is visited when _visitedGeneration[idx] == _currentGeneration
		int _currentGeneration;   // Incremented per light source to avoid clearing the array
		PointLight[] _neighborLightBuffer; // Preallocated buffer for neighbor border light scanning (max 32)

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
		/// Also seeds from pre-existing skylight at chunk boundaries (written by earlier
		/// parallel phases via cross-chunk propagation) to avoid light artefacts at seams.
		/// </summary>
		void ComputeSkylight()
		{
			// Reuse or allocate array
			_skylightQueue ??= new BlockPos[QueueCapacity];
			_skylightHead = 0;
			_skylightTail = 0;

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
							_skylightQueue[_skylightTail++] = new BlockPos(x, y, z);
						}
						else if (BlockInfo.IsOpaque(block.Type))
						{
							// Hit an opaque block, column below is no longer sky-exposed
							skyExposed = false;
						}
					}
				}
			}

			// Second pass: seed from pre-existing skylight at chunk boundaries.
			// Earlier parallel phases may have written skylight into this chunk's
			// boundary blocks via cross-chunk propagation. Those values need to be
			// added to the queue so they continue propagating inward.
			SeedFromBoundarySkylight();

			// Propagate skylight in all directions with attenuation
			PropagateSkylight();
		}

		/// <summary>
		/// Scans all 6 faces of the chunk for non-opaque blocks with pre-existing
		/// skylight values (from cross-chunk writes by earlier parallel phases).
		/// Adds them to the propagation queue so light continues spreading inward.
		/// </summary>
		void SeedFromBoundarySkylight()
		{
			const int last = ChunkSize - 1;

			for (int a = 0; a < ChunkSize; a++)
			{
				for (int b = 0; b < ChunkSize; b++)
				{
					// X = 0 and X = last faces
					TrySeedBoundarySkylight(0, a, b);
					TrySeedBoundarySkylight(last, a, b);
					// Y = 0 and Y = last faces
					TrySeedBoundarySkylight(a, 0, b);
					TrySeedBoundarySkylight(a, last, b);
					// Z = 0 and Z = last faces
					TrySeedBoundarySkylight(a, b, 0);
					TrySeedBoundarySkylight(a, b, last);
				}
			}
		}

		/// <summary>
		/// If the block at (x,y,z) has skylight > 1 and is not opaque, enqueue it
		/// for further propagation. Blocks already set to 15 by sky-exposure seeding
		/// are harmless duplicates — the BFS greater-than check prevents re-propagation.
		/// </summary>
		void TrySeedBoundarySkylight(int x, int y, int z)
		{
			int idx = x + ChunkSize * (y + ChunkSize * z);
			if (BlockInfo.IsOpaque(Blocks[idx].Type))
				return;
			byte skylight = Blocks[idx].GetMaxSkylight();
			if (skylight > 1)
				_skylightQueue[_skylightTail++] = new BlockPos(x, y, z);
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
		void PropagateSkylight()
		{
			// Cache chunk world origin once to avoid per-iteration GetWorldPos calls
			WorldMap.GetWorldPos(0, 0, 0, GlobalChunkIndex, out Vector3 chunkOrigin);
			int chunkWorldX = (int)chunkOrigin.X;
			int chunkWorldY = (int)chunkOrigin.Y;
			int chunkWorldZ = (int)chunkOrigin.Z;

			while (_skylightHead < _skylightTail)
			{
				BlockPos pos = _skylightQueue[_skylightHead++];

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
						int worldNx = chunkWorldX + pos.X + DirX[d];
						int worldNy = chunkWorldY + pos.Y + DirY[d];
						int worldNz = chunkWorldZ + pos.Z + DirZ[d];

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
						_skylightQueue[_skylightTail++] = new BlockPos(nx, ny, nz);
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
			//_lightSources ??= new List<PointLight>(64);
			//_lightSources.Clear();
			//var lightSources = _lightSources;
			int addedLightCount = 0;

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

							//lightSources.Add(new PointLight(lightPos, lightLevel, castsShadows: true));

							if (addedLightCount < lightSources.Length)
								lightSources[addedLightCount++] = new PointLight(lightPos, lightLevel, castsShadows: true);


							//addedLightCount++;
						}
					}
				}
			}

			// Collect light sources from neighboring chunks that can affect this chunk
			addedLightCount = CollectNeighborLightSources(addedLightCount, lightSources);

			for (int i = 0; i < addedLightCount; i++)
			{
				PropagateBlockLightWithShadows(lightSources[i]);
			}
		}

		/// <summary>
		/// Collects light-emitting blocks from neighboring chunks that are within
		/// light propagation range of this chunk's borders.
		/// </summary>
		int CollectNeighborLightSources(/*List<PointLight> lightSources*/ int startIdx, PointLight[] outLights)
		{
			const int maxLightRange = 15; // Maximum light propagation distance
			int outLightsIdx = startIdx;

			if (startIdx < 0)
				return 0;

			if (startIdx >= outLights.Length)
				return outLightsIdx;

			// Reuse preallocated buffer for neighbor border light scanning
			_neighborLightBuffer ??= new PointLight[32];
			var buffer = _neighborLightBuffer;

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
						int count = ScanNeighborBorderForLights(neighborChunk, dx, dy, dz, maxLightRange, buffer);

						for (int i = 0; i < count; i++)
						{
							outLights[outLightsIdx++] = buffer[i];

							if (outLightsIdx >= outLights.Length)
								return outLightsIdx;

							//lightSources.Add(buffer[i]);
						}
					}
				}
			}

			return outLightsIdx;
		}

		/// <summary>
		/// Scans the border region of a neighbor chunk for light sources.
		/// Only scans blocks within light range of our chunk boundary.
		/// Writes results into the preallocated <paramref name="lightSources"/> array (max 32).
		/// </summary>
		/// <returns>The number of light sources found (up to the array length).</returns>
		int ScanNeighborBorderForLights(Chunk neighbor, int dx, int dy, int dz, int range, PointLight[] lightSources)
		{
			int count = 0;
			int maxCount = lightSources.Length;

			// Determine which region of the neighbor to scan based on direction
			// If dx == 1, neighbor is to our +X side, so we scan its low-X border (0 to range)
			// If dx == -1, neighbor is to our -X side, so we scan its high-X border (ChunkSize-range to ChunkSize)
			int xStart = dx == 1 ? 0 : (dx == -1 ? Math.Max(0, ChunkSize - range) : 0);
			int xEnd = dx == 1 ? Math.Min(range, ChunkSize) : (dx == -1 ? ChunkSize : ChunkSize);
			int yStart = dy == 1 ? 0 : (dy == -1 ? Math.Max(0, ChunkSize - range) : 0);
			int yEnd = dy == 1 ? Math.Min(range, ChunkSize) : (dy == -1 ? ChunkSize : ChunkSize);
			int zStart = dz == 1 ? 0 : (dz == -1 ? Math.Max(0, ChunkSize - range) : 0);
			int zEnd = dz == 1 ? Math.Min(range, ChunkSize) : (dz == -1 ? ChunkSize : ChunkSize);

			// Cache neighbor blocks and chunk index locally to avoid repeated field access
			PlacedBlock[] neighborBlocks = neighbor.Blocks;
			Vector3 neighborChunkIndex = neighbor.GlobalChunkIndex;

			for (int x = xStart; x < xEnd; x++)
			{
				for (int y = yStart; y < yEnd; y++)
				{
					for (int z = zStart; z < zEnd; z++)
					{
						int idx = x + ChunkSize * (y + ChunkSize * z);
						BlockType type = neighborBlocks[idx].Type;

						if (BlockInfo.EmitsLight(type))
						{
							byte lightLevel = BlockInfo.GetLightEmission(type);
							WorldMap.GetWorldPos(x, y, z, neighborChunkIndex, out Vector3 worldPos);
							lightSources[count] = new PointLight(
								worldPos + new Vector3(0.5f, 0.5f, 0.5f),
								lightLevel,
								castsShadows: true);
							count++;

							if (count >= maxCount)
								return count;
						}
					}
				}
			}

			return count;
		}

		/// <summary>
		/// Propagates block light from a single source with shadow tracing.
		/// Uses BFS but checks line-of-sight to the source for each propagation step.
		/// Handles both internal and external (neighbor chunk) light sources.
		/// </summary>
		void PropagateBlockLightWithShadows(PointLight light)
		{
			// Reuse array to avoid allocation
			_blockLightQueue ??= new BlockLightEntry[QueueCapacity];
			_blockLightHead = 0;
			_blockLightTail = 0;

			// Reuse visited array - generation counter avoids costly Array.Clear per light source
			const int totalBlocks = ChunkSize * ChunkSize * ChunkSize;
			_visitedGeneration ??= new int[totalBlocks];
			_currentGeneration++;
			var visited = _visitedGeneration;
			int gen = _currentGeneration;

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
				_blockLightQueue[_blockLightTail++] = new BlockLightEntry(localX, localY, localZ, light.Intensity);
			}
			else
			{
				// Light source is in a neighbor chunk - find entry points at chunk boundary
				// Scan boundary blocks that could receive light from this source
				QueueBoundaryBlocksForExternalLight(light, chunkWorldX, chunkWorldY, chunkWorldZ, visited, gen);
			}

			while (_blockLightHead < _blockLightTail)
			{
				BlockLightEntry entry = _blockLightQueue[_blockLightHead++];
				int x = entry.X, y = entry.Y, z = entry.Z;
				byte level = entry.Level;

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
					if (visited[nidx] == gen)
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
						visited[nidx] = gen;
						_blockLightQueue[_blockLightTail++] = new BlockLightEntry(nx, ny, nz, newBlockLight2);
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
			int[] visited, int gen)
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
							if (visited[idx] != gen)
							{
								visited[idx] = gen;
								_blockLightQueue[_blockLightTail++] = new BlockLightEntry(x, y, z, lightLevel);
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
