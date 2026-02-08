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
			float Scale = 0.02f;
			int WorldHeight = 64;

			Vector3 Center = new Vector3(Width, 0, Length) / 2;
			float CenterRadius = Math.Min(Width / 2, Length / 2);

			// Pre-create all chunks for direct O(1) access during generation,
			// bypassing SetPlacedBlock overhead (neighbor tracking, change logging,
			// and lighting updates per block).
			const int CS = Chunk.ChunkSize;
			int chunksX = (Width + CS - 1) / CS;
			int chunksY = (WorldHeight + CS - 1) / CS + 1; // +1 for an empty air chunk above the terrain
			int chunksZ = (Length + CS - 1) / CS;

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

			// Noise pass — each XZ column is independent, parallelized across X
			Parallel.For(0, Width, x =>
			{
				for (int z = 0; z < Length; z++)
					for (int y = 0; y < WorldHeight; y++)
					{
						Vector3 Pos = new Vector3(x, (WorldHeight - y), z);

						float CenterFalloff = 1.0f - Utils.Clamp(((Center - Pos).Length() / CenterRadius) / 1.2f, 0, 1);
						float Height = (float)y / WorldHeight;

						const float HeightFallStart = 0.8f;
						const float HeightFallEnd = 1.0f;
						const float HeightFallRange = HeightFallEnd - HeightFallStart;

						float HeightFalloff = Height <= HeightFallStart ? 1.0f : (Height > HeightFallStart && Height < HeightFallEnd ? 1.0f - (Height - HeightFallStart) * (HeightFallRange * 10) : 0);
						float Density = Simplex(2, x, y * 0.5f, z, Scale) * CenterFalloff * HeightFalloff;

						if (Density > 0.1f)
						{
							float Caves = Simplex(1, x, y, z, Scale * 4) * HeightFalloff;
							if (Caves < 0.65f)
								chunkGrid[x / CS, y / CS, z / CS].SetBlock(x % CS, y % CS, z % CS, new PlacedBlock(BlockType.Stone));
						}
					}
			});

			// Surface pass — replace top stone with grass/dirt
			Parallel.For(0, Width, x =>
			{
				for (int z = 0; z < Length; z++)
				{
					int DownRayHits = 0;
					for (int y = WorldHeight - 1; y >= 0; y--)
					{
						if (chunkGrid[x / CS, y / CS, z / CS].GetBlock(x % CS, y % CS, z % CS).Type != BlockType.None)
						{
							DownRayHits++;

							if (DownRayHits == 1)
								chunkGrid[x / CS, y / CS, z / CS].SetBlock(x % CS, y % CS, z % CS, new PlacedBlock(BlockType.Grass));
							else if (DownRayHits < 5)
								chunkGrid[x / CS, y / CS, z / CS].SetBlock(x % CS, y % CS, z % CS, new PlacedBlock(BlockType.Dirt));

						}
						else if (DownRayHits != 0)
							break;
					}
				}
			});

			ComputeLighting();
		}

		/// <summary>
		/// Scans the world for valid spawn points on the surface.
		/// A valid spawn point has a solid ground block with at least 2 air blocks above it.
		/// Points are selected with minimum spacing, prioritized by proximity to the world center.
		/// </summary>
		/// <param name="count">Number of spawn points to find.</param>
		/// <param name="minSpacing">Minimum distance in blocks between spawn points.</param>
		/// <returns>List of world positions suitable for spawning (3 blocks above ground surface).</returns>
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

			// Scan each XZ column top-down for the topmost surface block
			for (int x = minX; x < maxX; x++)
				for (int z = minZ; z < maxZ; z++)
				{
					for (int y = maxY - 1; y >= minY; y--)
					{
						if (BlockInfo.IsSolid(GetBlock(x, y, z)) &&
							GetBlock(x, y + 1, z) == BlockType.None &&
							GetBlock(x, y + 2, z) == BlockType.None)
						{
							candidates.Add(new Vector3(x, y + 3, z));
							break;
						}
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
									   Block.Type == BlockType.None || // Block removed
									   BlockInfo.IsOpaque(Block.Type); // Opaque block affects light propagation

			if (needsLightingUpdate)
			{
				// For light sources, we need to update all chunks within light propagation range
				// Light can travel up to 15 blocks, which is almost 1 full chunk in each direction
				const int lightRangeInChunks = 1; // 15 blocks / 16 blocks per chunk, rounded up

				// Collect all chunks within light range
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
								chunksToUpdate.Add(chunk);
							}
						}
					}
				}

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

		public void Draw(ref Frustum Fr)
		{
			foreach (var KV in Chunks.Items)
			{
				Vector3 ChunkPos = KV.Key * new Vector3(Chunk.ChunkSize);
				KV.Value.Draw(ChunkPos, ref Fr);
			}

			Utils.DrawRaycastRecord();
		}

		public void DrawTransparent(ref Frustum Fr, Vector3 cameraPos)
		{
			// Collect all transparent faces from all visible chunks
			TransparentFaceBuffer.Clear();

			foreach (var KV in Chunks.Items)
			{
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
