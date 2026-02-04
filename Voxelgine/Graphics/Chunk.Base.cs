using Raylib_cs;
using System.Numerics;

using Voxelgine.Engine;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Graphics
{
	/// <summary>
	/// Represents a transparent block face for depth-sorted rendering.
	/// Contains 6 vertices (2 triangles) and the center position for sorting.
	/// </summary>
	public struct TransparentFace
	{
		/// <summary>Center of face for distance calculation during depth sorting.</summary>
		public Vector3 Center;
		/// <summary>6 vertices forming 2 triangles for this face.</summary>
		public Vertex3[] Vertices;
		/// <summary>Cached squared distance from camera for sorting.</summary>
		public float DistanceSquared;

		public TransparentFace(Vector3 center, Vertex3[] vertices)
		{
			Center = center;
			Vertices = vertices;
			DistanceSquared = 0;
		}

		public void CalcDistance(Vector3 cameraPos)
		{
			DistanceSquared = Vector3.DistanceSquared(cameraPos, Center);
		}
	}

	/// <summary>
	/// A 16³ block container that manages block storage, mesh generation, and rendering.
	/// Uses separate meshes for opaque and transparent blocks for correct rendering order.
	/// </summary>
	/// <remarks>
	/// Chunks cache their meshes and only rebuild when marked dirty. Transparent faces
	/// are stored separately for per-frame depth sorting against the camera position.
	/// </remarks>
	public unsafe partial class Chunk
	{
		/// <summary>Number of blocks per chunk dimension (16³ = 4096 blocks per chunk).</summary>
		public const int ChunkSize = 16;
		/// <summary>Size of each block in world units.</summary>
		public const float BlockSize = 1;
		/// <summary>Number of textures per row in the texture atlas.</summary>
		public const int AtlasSize = 16;

		/// <summary>Block data array indexed as [X + ChunkSize * (Y + ChunkSize * Z)].</summary>
		public PlacedBlock[] Blocks;
		bool Dirty;

		bool ModelValidOpaque;
		bool ModelValidTransp;

		public AABB ModelAABB;

		Model CachedModelOpaque;
		Mesh CachedMeshOpaque;

		Model CachedModelTransp;
		Mesh CachedMeshTransp;

		// Cached transparent faces for depth-sorted rendering
		List<TransparentFace> CachedTransparentFaces;
		bool TransparentFacesValid;

		public Color ChunkColor = Color.White;

		public Vector3 GlobalChunkIndex;
		ChunkMap WorldMap;

		//List<Vector3> SunRayOrigins = new List<Vector3>();
		//Vector3 SunDir = -Vector3.UnitY;

		//public Vector3 Position;

		public Chunk(Vector3 GlobalChunkIndex, ChunkMap WorldMap)
		{
			this.GlobalChunkIndex = GlobalChunkIndex;
			this.WorldMap = WorldMap;

			Blocks = new PlacedBlock[ChunkSize * ChunkSize * ChunkSize];
			for (int i = 0; i < Blocks.Length; i++)
				Blocks[i] = new PlacedBlock(BlockType.None);

			Dirty = true;
			ModelValidTransp = ModelValidOpaque = false;
			TransparentFacesValid = false;
			CachedTransparentFaces = new List<TransparentFace>();

			//int TileTexSize = AtlasTex.width / 16;
		}

		/*public void SetPosition(Vector3 Pos) {
			Position = Pos;
		}*/

		public void Write(BinaryWriter Writer)
		{
			for (int i = 0; i < Blocks.Length;)
			{
				PlacedBlock Cur = Blocks[i];
				ushort Count = 1;

				for (int j = i + 1; j < Blocks.Length; j++)
				{
					if (Blocks[j].Type == Cur.Type)
						Count++;
					else
						break;
				}

				Writer.Write(Count);
				Cur.Write(Writer);

				i += Count;
			}
		}

		public void Read(BinaryReader Reader)
		{
			for (int i = 0; i < Blocks.Length;)
			{
				ushort Count = Reader.ReadUInt16();

				PlacedBlock Block = new PlacedBlock(BlockType.None);
				Block.Read(Reader);

				for (int j = 0; j < Count; j++)
					Blocks[i + j] = new PlacedBlock(Block);

				i += Count;
			}

			Dirty = true;
		}

		public PlacedBlock GetBlock(int X, int Y, int Z)
		{
			/*if (X < 0 || X >= ChunkSize)
				return PlacedBlock.None;

			if (Y < 0 || Y >= ChunkSize)
				return PlacedBlock.None;

			if (Z < 0 || Z >= ChunkSize)
				return PlacedBlock.None;*/

			if (X < 0 || X >= ChunkSize || Y < 0 || Y >= ChunkSize || Z < 0 || Z >= ChunkSize)
			{
				WorldMap.GetWorldPos(0, 0, 0, GlobalChunkIndex, out Vector3 GlobalBlockPos);
				return WorldMap.GetPlacedBlock((int)GlobalBlockPos.X + X, (int)GlobalBlockPos.Y + Y, (int)GlobalBlockPos.Z + Z, out Chunk Chk);
			}

			return Blocks[X + ChunkSize * (Y + ChunkSize * Z)];
		}

		public PlacedBlock GetBlock(Vector3 Orig)
		{
			return GetBlock((int)Orig.X, (int)Orig.Y, (int)Orig.Z);
		}

		public void SetBlock(int X, int Y, int Z, PlacedBlock Block)
		{
			Blocks[X + ChunkSize * (Y + ChunkSize * Z)] = Block;
			Dirty = true;
			// Invalidate sky exposure cache when blocks change
			SkyExposureCacheValid = false;
		}

		public void Fill(PlacedBlock Block)
		{
			for (int i = 0; i < Blocks.Length; i++)
				Blocks[i] = Block;
			Dirty = true;
			SkyExposureCacheValid = false;
		}

		public void Fill(BlockType T)
		{
			Fill(new PlacedBlock(T));
		}

		public void MarkDirty()
		{
			Dirty = true;
		}

		void PrintVert(Vector3 V)
		{
			Console.WriteLine("new Vector3({0}, {1}, {2}) * Size + Pos,", V.X, V.Y, V.Z);
		}


		public void To3D(int Idx, out int X, out int Y, out int Z)
		{
			Z = Idx / (ChunkSize * ChunkSize);
			Idx -= (Z * ChunkSize * ChunkSize);
			Y = Idx / ChunkSize;
			X = Idx % ChunkSize;
		}

		bool IsCovered(int X, int Y, int Z)
		{
			for (int i = 0; i < Utils.MainDirs.Length; i++)
			{
				int XX = (int)(X + Utils.MainDirs[i].X);
				int YY = (int)(Y + Utils.MainDirs[i].Y);
				int ZZ = (int)(Z + Utils.MainDirs[i].Z);

				if (GetBlock(XX, YY, ZZ).Type == BlockType.None)
					return false;
			}

			return true;
		}

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
		/// </summary>
		void ComputeBlockLights()
		{
			Queue<BlockPos> blockLightQueue = new Queue<BlockPos>(256);

			// Find all light-emitting blocks
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
							blockLightQueue.Enqueue(new BlockPos(x, y, z));
						}
					}
				}
			}

			// Propagate block light
			PropagateBlockLight(blockLightQueue);
		}

		/// <summary>
		/// Propagates block light from light sources to neighbors.
		/// </summary>
		void PropagateBlockLight(Queue<BlockPos> queue)
		{
			while (queue.Count > 0)
			{
				BlockPos pos = queue.Dequeue();

				if (pos.X < 0 || pos.X >= ChunkSize || pos.Y < 0 || pos.Y >= ChunkSize || pos.Z < 0 || pos.Z >= ChunkSize)
					continue;

				int idx = pos.X + ChunkSize * (pos.Y + ChunkSize * pos.Z);
				byte currentBlockLight = Blocks[idx].GetMaxBlockLight();

				if (currentBlockLight <= 1)
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

						byte neighborBlockLight = neighborBlock.GetMaxBlockLight();
						byte newBlockLight = (byte)(currentBlockLight - 1);
						if (newBlockLight > neighborBlockLight)
						{
							neighborBlock.SetBlockLightLevel(newBlockLight);
							WorldMap.SetPlacedBlockNoLighting(worldNx, worldNy, worldNz, neighborBlock);
						}
						continue;
					}

					int nidx = nx + ChunkSize * (ny + ChunkSize * nz);
					PlacedBlock neighborBlock2 = Blocks[nidx];

					if (BlockInfo.IsOpaque(neighborBlock2.Type))
						continue;

					byte neighborBlockLight2 = neighborBlock2.GetMaxBlockLight();
					byte newBlockLight2 = (byte)(currentBlockLight - 1);

					if (newBlockLight2 > neighborBlockLight2)
					{
						Blocks[nidx].SetBlockLightLevel(newBlockLight2);
						queue.Enqueue(new BlockPos(nx, ny, nz));
					}
				}
			}
		}

		// Returns true if this chunk is within a certain distance from the camera/player
		private bool IsDistantChunk(Vector3 chunkIndex, Vector3 cameraChunkIndex, float maxDistance)
		{
			return Vector3.Distance(chunkIndex, cameraChunkIndex) > maxDistance;
		}

		// Optimized AO calculation: use a simple approximation for distant chunks
		Color CalcAOColor(Vector3 GlobalBlockPos, Vector3 A, Vector3 B, Vector3 C, bool useApproximation = false)
		{
			if (useApproximation)
			{
				// Simple AO: always return a fixed value (e.g., 0.8f brightness)
				return Utils.Color(0.8f);
			}

			int Hits = 0;

			if (BlockInfo.IsOpaque(WorldMap.GetBlock(GlobalBlockPos + A)))
				Hits++;

			if (BlockInfo.IsOpaque(WorldMap.GetBlock(GlobalBlockPos + B)))
				Hits++;

			if (BlockInfo.IsOpaque(WorldMap.GetBlock(GlobalBlockPos + C)))
				Hits++;

			if (Hits != 0)
				return Utils.Color(1.0f - (Hits * 0.2f));

			return Utils.Color(1.0f);
		}

		/// <summary>
		/// Gets the light color for an opaque block's face by reading from the adjacent block.
		/// For opaque blocks, the light should come from the neighboring air/transparent block
		/// that the face is exposed to, not from the opaque block itself.
		/// </summary>
		Color GetFaceLightColor(int x, int y, int z, Vector3 faceDir)
		{
			// Get the neighbor block in the direction of the face
			int nx = x + (int)faceDir.X;
			int ny = y + (int)faceDir.Y;
			int nz = z + (int)faceDir.Z;

			PlacedBlock neighbor = GetBlock(nx, ny, nz);

			// Get the light from the neighbor (the air block facing this surface)
			// Use the opposite direction since we want the light hitting this face
			return neighbor.GetBlockLight(-faceDir).ToColor();
		}

		void SetBlockTextureUV(BlockType BlockType, Vector3 FaceNormal, MeshBuilder Verts)
		{
			int BlockID = BlockInfo.GetBlockID(BlockType, FaceNormal);
			int BlockX = BlockID % AtlasSize;
			int BlockY = BlockID / AtlasSize;

			BlockInfo.GetBlockTexCoords(BlockType, FaceNormal, out Vector2 UVSize, out Vector2 UVPos);
			Verts.SetUVOffsetSize(UVPos + new Vector2(0, UVSize.Y), UVSize * new Vector2(1, -1));
		}

		void RecalcModel()
		{
			if (!Dirty)
				return;

			Dirty = false;

			if (ModelValidOpaque)
			{
				// Set texture ID to 1 to disable texture unloading? Does that even do anything?
				CachedModelOpaque.Materials[0].Maps[0].Texture.Id = 0;
				Raylib.UnloadModel(CachedModelOpaque);
			}

			CachedMeshOpaque = GenMesh();
			CachedModelOpaque = Raylib.LoadModelFromMesh(CachedMeshOpaque);
			CachedModelOpaque.Materials[0].Maps[0].Texture = ResMgr.AtlasTexture;
			CachedModelOpaque.Materials[0].Shader = ResMgr.GetShader("default");
			ModelValidOpaque = CachedMeshOpaque.VertexCount > 0;

			if (ModelValidTransp)
			{
				// Set texture ID to 1 to disable texture unloading? Does that even do anything?
				CachedModelTransp.Materials[0].Maps[0].Texture.Id = 0;
				Raylib.UnloadModel(CachedModelTransp);
			}

			CachedMeshTransp = GenMeshTransparent();
			CachedModelTransp = Raylib.LoadModelFromMesh(CachedMeshTransp);
			CachedModelTransp.Materials[0].Maps[0].Texture = ResMgr.AtlasTexture;
			CachedModelTransp.Materials[0].Shader = ResMgr.GetShader("default");
			ModelValidTransp = CachedMeshTransp.VertexCount > 0;

			// Debug: log transparent mesh vertex count
			if (ModelValidTransp && Program.DebugMode)
			{
				Console.WriteLine($"[Chunk {GlobalChunkIndex}] Transparent mesh: {CachedMeshTransp.VertexCount} vertices");
			}

			// Generate transparent faces for depth-sorted rendering
			Vector3 chunkWorldPos = GlobalChunkIndex * ChunkSize;
			CachedTransparentFaces = GenTransparentFaces(chunkWorldPos);
			TransparentFacesValid = CachedTransparentFaces.Count > 0;

			if (!ModelValidOpaque && !ModelValidTransp)
			{
				ModelAABB = AABB.Empty;
			}
			else
			{
				if (!ModelValidOpaque)
				{
					ModelAABB = new AABB(Raylib.GetMeshBoundingBox(CachedMeshTransp));
				}
				else if (!ModelValidTransp)
				{
					ModelAABB = new AABB(Raylib.GetMeshBoundingBox(CachedMeshOpaque));
				}
				else
				{
					AABB BBOpaque = new AABB(Raylib.GetMeshBoundingBox(CachedMeshOpaque));
					AABB BBTransp = new AABB(Raylib.GetMeshBoundingBox(CachedMeshTransp));
					ModelAABB = AABB.Union(BBOpaque, BBTransp);
				}
			}
		}

		public RayCollision Collide(Vector3 ChunkPosition, Ray R)
		{
			Matrix4x4 Transform = Matrix4x4.Transpose(Matrix4x4.CreateTranslation(ChunkPosition));
			return Raylib.GetRayCollisionMesh(R, CachedMeshOpaque, Transform);
		}

		bool IsInsideFrustum(Vector3 ChunkPosition, ref Frustum Fr)
		{
			if (Fr.IsInside(ModelAABB.Offset(ChunkPosition)))
			{
				return true;
			}

			return false;
		}

		public void Draw(Vector3 ChunkPosition, ref Frustum Fr)
		{
			RecalcModel();

			if (!IsInsideFrustum(ChunkPosition, ref Fr))
				return;

			if (Program.DebugMode)
				Raylib.DrawBoundingBox(ModelAABB.Offset(ChunkPosition).ToBoundingBox(), Color.Yellow);

			if (ModelValidOpaque)
			{
				Raylib.DrawModel(CachedModelOpaque, ChunkPosition, BlockSize, ChunkColor);
				Program.ChunkDrawCalls++;
			}
		}

		public void DrawTransparent(Vector3 ChunkPosition, ref Frustum Fr)
		{
			if (!IsInsideFrustum(ChunkPosition, ref Fr))
				return;

			if (ModelValidTransp)
			{
				// Backfaces are now explicitly generated in the mesh for glass-like blocks
				Raylib.DrawModel(CachedModelTransp, ChunkPosition, BlockSize, ChunkColor);
			}
		}

		/// <summary>
		/// Returns the cached transparent faces for this chunk for depth-sorted rendering.
		/// Call RecalcModel first if dirty.
		/// </summary>
		public List<TransparentFace> GetTransparentFaces(ref Frustum Fr)
		{
			RecalcModel();
			Vector3 chunkWorldPos = GlobalChunkIndex * ChunkSize;
			if (!IsInsideFrustum(chunkWorldPos, ref Fr))
				return new List<TransparentFace>();
			return CachedTransparentFaces;
		}

		public bool HasTransparentFaces()
		{
			RecalcModel();
			return TransparentFacesValid;
		}
	}
}
