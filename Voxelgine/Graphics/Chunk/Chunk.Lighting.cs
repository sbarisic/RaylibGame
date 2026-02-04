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

		/// <summary>
		/// Invalidates the sky exposure cache, forcing recalculation on next lighting compute.
		/// </summary>
		public void InvalidateSkyExposureCache()
		{
			SkyExposureCacheValid = false;
		}

		/// <summary>
		/// Computes lighting for this chunk. Handles both skylight and block light separately.
		/// Skylight propagates from sky-exposed blocks, block light from light-emitting blocks.
		/// </summary>
		public void ComputeLighting()
		{
			// Reset all light values to zero
			for (int i = 0; i < Blocks.Length; i++)
			{
				Blocks[i].SetAllLights(BlockLight.Black);
			}

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
			//return;
			Queue<BlockPos> skylightQueue = new Queue<BlockPos>(ChunkSize * ChunkSize);

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

						if (skyExposed && (block.Type == BlockType.None || !BlockInfo.IsOpaque(block.Type)))
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
		/// </summary>
		void ComputeBlockLights()
		{
			// Collect all light sources in this chunk
			List<PointLight> lightSources = new List<PointLight>(32);

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

			// Propagate light from each source with shadow checking
			foreach (var light in lightSources)
			{
				PropagateBlockLightWithShadows(light);
			}
		}

		/// <summary>
		/// Propagates block light from a single source with shadow tracing.
		/// Uses BFS but checks line-of-sight to the source for each propagation step.
		/// </summary>
		void PropagateBlockLightWithShadows(PointLight light)
		{
			// Queue entry includes the block position and current light level
			Queue<(int x, int y, int z, byte level)> queue = new Queue<(int, int, int, byte)>(256);

			// Start from the light source block
			int srcX = (int)MathF.Floor(light.Position.X);
			int srcY = (int)MathF.Floor(light.Position.Y);
			int srcZ = (int)MathF.Floor(light.Position.Z);

			// Convert to local chunk coordinates if within this chunk
			WorldMap.GetWorldPos(0, 0, 0, GlobalChunkIndex, out Vector3 chunkWorldPos);
			int localX = srcX - (int)chunkWorldPos.X;
			int localY = srcY - (int)chunkWorldPos.Y;
			int localZ = srcZ - (int)chunkWorldPos.Z;

			if (localX >= 0 && localX < ChunkSize && localY >= 0 && localY < ChunkSize && localZ >= 0 && localZ < ChunkSize)
			{
				queue.Enqueue((localX, localY, localZ, light.Intensity));
			}

			// Visited set to avoid reprocessing (we may want to update with higher values though)
			HashSet<(int, int, int)> visited = new HashSet<(int, int, int)>();

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
						// Get world position and propagate to neighbor chunk
						WorldMap.GetWorldPos(x, y, z, GlobalChunkIndex, out Vector3 worldPos);
						int worldNx = (int)worldPos.X + DirX[d];
						int worldNy = (int)worldPos.Y + DirY[d];
						int worldNz = (int)worldPos.Z + DirZ[d];

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
						}
						continue;
					}

					// Within chunk bounds
					if (visited.Contains((nx, ny, nz)))
						continue;

					int nidx = nx + ChunkSize * (ny + ChunkSize * nz);
					PlacedBlock neighborBlock2 = Blocks[nidx];

					if (BlockInfo.IsOpaque(neighborBlock2.Type))
						continue;

					// Shadow check: verify line-of-sight to light source
					if (light.CastsShadows)
					{
						WorldMap.GetWorldPos(nx, ny, nz, GlobalChunkIndex, out Vector3 neighborWorldPos);
						Vector3 neighborCenter = neighborWorldPos + new Vector3(0.5f, 0.5f, 0.5f);
						if (!ShadowTracer.HasLineOfSight(WorldMap, light.Position, neighborCenter))
							continue; // In shadow, skip
					}

					byte neighborBlockLight2 = neighborBlock2.GetMaxBlockLight();
					byte newBlockLight2 = (byte)(level - 1);

					if (newBlockLight2 > neighborBlockLight2)
					{
						Blocks[nidx].SetBlockLightLevel(newBlockLight2);
						visited.Add((nx, ny, nz));
						queue.Enqueue((nx, ny, nz, newBlockLight2));
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
