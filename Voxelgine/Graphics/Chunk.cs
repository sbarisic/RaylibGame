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

namespace Voxelgine.Graphics {
	/// <summary>
	/// Represents a transparent block face for depth-sorted rendering.
	/// Contains 6 vertices (2 triangles) and the center position for sorting.
	/// </summary>
	public struct TransparentFace {
		public Vector3 Center;           // Center of face for distance calculation
		public Vertex3[] Vertices;       // 6 vertices (2 triangles)
		public float DistanceSquared;    // Cached distance for sorting

		public TransparentFace(Vector3 center, Vertex3[] vertices) {
			Center = center;
			Vertices = vertices;
			DistanceSquared = 0;
		}

		public void CalcDistance(Vector3 cameraPos) {
			DistanceSquared = Vector3.DistanceSquared(cameraPos, Center);
		}
	}

	public unsafe class Chunk {
		public const int ChunkSize = 16;
		public const float BlockSize = 1;
		public const int AtlasSize = 16;

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

		public Chunk(Vector3 GlobalChunkIndex, ChunkMap WorldMap) {
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

		public void Write(BinaryWriter Writer) {
			for (int i = 0; i < Blocks.Length;) {
				PlacedBlock Cur = Blocks[i];
				ushort Count = 1;

				for (int j = i + 1; j < Blocks.Length; j++) {
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

		public void Read(BinaryReader Reader) {
			for (int i = 0; i < Blocks.Length;) {
				ushort Count = Reader.ReadUInt16();

				PlacedBlock Block = new PlacedBlock(BlockType.None);
				Block.Read(Reader);

				for (int j = 0; j < Count; j++)
					Blocks[i + j] = new PlacedBlock(Block);

				i += Count;
			}

			Dirty = true;
		}

		public PlacedBlock GetBlock(int X, int Y, int Z) {
			/*if (X < 0 || X >= ChunkSize)
				return PlacedBlock.None;

			if (Y < 0 || Y >= ChunkSize)
				return PlacedBlock.None;

			if (Z < 0 || Z >= ChunkSize)
				return PlacedBlock.None;*/

			if (X < 0 || X >= ChunkSize || Y < 0 || Y >= ChunkSize || Z < 0 || Z >= ChunkSize) {
				WorldMap.GetWorldPos(0, 0, 0, GlobalChunkIndex, out Vector3 GlobalBlockPos);
				return WorldMap.GetPlacedBlock((int)GlobalBlockPos.X + X, (int)GlobalBlockPos.Y + Y, (int)GlobalBlockPos.Z + Z, out Chunk Chk);
			}

			return Blocks[X + ChunkSize * (Y + ChunkSize * Z)];
		}

		public PlacedBlock GetBlock(Vector3 Orig) {
			return GetBlock((int)Orig.X, (int)Orig.Y, (int)Orig.Z);
		}

		public void SetBlock(int X, int Y, int Z, PlacedBlock Block) {
			Blocks[X + ChunkSize * (Y + ChunkSize * Z)] = Block;
			Dirty = true;
		}

		public void Fill(PlacedBlock Block) {
			for (int i = 0; i < Blocks.Length; i++)
				Blocks[i] = Block;
			Dirty = true;
		}

		public void Fill(BlockType T) {
			Fill(new PlacedBlock(T));
		}

		public void MarkDirty() {
			Dirty = true;
		}

		void PrintVert(Vector3 V) {
			Console.WriteLine("new Vector3({0}, {1}, {2}) * Size + Pos,", V.X, V.Y, V.Z);
		}


		public void To3D(int Idx, out int X, out int Y, out int Z) {
			Z = Idx / (ChunkSize * ChunkSize);
			Idx -= (Z * ChunkSize * ChunkSize);
			Y = Idx / ChunkSize;
			X = Idx % ChunkSize;
		}

		bool IsCovered(int X, int Y, int Z) {
			for (int i = 0; i < Utils.MainDirs.Length; i++) {
				int XX = (int)(X + Utils.MainDirs[i].X);
				int YY = (int)(Y + Utils.MainDirs[i].Y);
				int ZZ = (int)(Z + Utils.MainDirs[i].Z);

				if (GetBlock(XX, YY, ZZ).Type == BlockType.None)
					return false;
			}

			return true;
		}

		void SetLightLevel(int x, int y, int z, byte lightLevel) {
			if (x < 0 || x >= ChunkSize || y < 0 || y >= ChunkSize || z < 0 || z >= ChunkSize)
				return;

			PlacedBlock block = GetBlock(x, y, z);

			// Set light for all faces - both air blocks and solid blocks need lighting  
			BlockLight newLight = new BlockLight(lightLevel);
			for (int i = 0; i < 6; i++) {
				block.Lights[i] = newLight;
			}

			SetBlock(x, y, z, block);
		}

		// Helper struct for queue (faster than Vector3 for ints)
		// BlockPos is used to represent integer block positions in the light propagation queue.
		struct BlockPos {
			public int X, Y, Z;
			public BlockPos(int x, int y, int z) {
				X = x;
				Y = y;
				Z = z;
			}
		}

		public void ComputeLighting() {
			// Set all blocks to light level 0 first
			for (int i = 0; i < Blocks.Length; i++) {
				Blocks[i].SetBlockLight(new BlockLight(0));
			}

			Queue<BlockPos> lightQueue = new Queue<BlockPos>(ChunkSize * ChunkSize * ChunkSize);

			// Sunlight: propagate from the top of each column down through air/transparent blocks
			for (int x = 0; x < ChunkSize; x++) {
				for (int z = 0; z < ChunkSize; z++) {
					for (int y = ChunkSize - 1; y >= 0; y--) {
						int idx = x + ChunkSize * (y + ChunkSize * z);
						PlacedBlock block = Blocks[idx];

						// Only air and transparent blocks receive full sunlight
						if (block.Type == BlockType.None || !BlockInfo.IsOpaque(block.Type)) {
							Blocks[idx].SetBlockLight(new BlockLight(15));
							lightQueue.Enqueue(new BlockPos(x, y, z));
						} else {
							// Hit an opaque block - stop sunlight in this column
							break;
						}
					}
				}
			}

			// Artificial light sources: propagate in all directions
			for (int x = 0; x < ChunkSize; x++) {
				for (int y = 0; y < ChunkSize; y++) {
					for (int z = 0; z < ChunkSize; z++) {
						int idx = x + ChunkSize * (y + ChunkSize * z);
						PlacedBlock block = Blocks[idx];
						if (block.Type == BlockType.Glowstone || block.Type == BlockType.Campfire) {
							Blocks[idx].SetBlockLight(new BlockLight(15));
							lightQueue.Enqueue(new BlockPos(x, y, z));
						}
					}
				}
			}

			// Propagate light globally
			PropagateLight(lightQueue);
			Dirty = true; // Mark dirty only once
		}

		void PropagateLight(Queue<BlockPos> lightQueue) {
			int[] dx = { 1, -1, 0, 0, 0, 0 };
			int[] dy = { 0, 0, 1, -1, 0, 0 };
			int[] dz = { 0, 0, 0, 0, 1, -1 };

			while (lightQueue.Count > 0) {
				BlockPos pos = lightQueue.Dequeue();

				// Skip invalid positions
				if (pos.X < 0 || pos.X >= ChunkSize || pos.Y < 0 || pos.Y >= ChunkSize || pos.Z < 0 || pos.Z >= ChunkSize)
					continue;

				int idx = pos.X + ChunkSize * (pos.Y + ChunkSize * pos.Z);
				PlacedBlock currentBlock = Blocks[idx];

				// Use the maximum light value from any face for propagation
				byte currentLight = 0;
				for (int i = 0; i < 6; i++)
					if (currentBlock.Lights[i].R > currentLight)
						currentLight = currentBlock.Lights[i].R;
				if (currentLight <= 1)
					continue; // No light to propagate (need at least 2 to spread as 1)

				for (int d = 0; d < 6; d++) {
					int nx = pos.X + dx[d];
					int ny = pos.Y + dy[d];
					int nz = pos.Z + dz[d];

					// Check if neighbor is outside this chunk
					if (nx < 0 || nx >= ChunkSize || ny < 0 || ny >= ChunkSize || nz < 0 || nz >= ChunkSize) {
						// Cross-chunk light propagation - get world position
						WorldMap.GetWorldPos(pos.X, pos.Y, pos.Z, GlobalChunkIndex, out Vector3 worldPos);
						int worldNx = (int)worldPos.X + dx[d];
						int worldNy = (int)worldPos.Y + dy[d];
						int worldNz = (int)worldPos.Z + dz[d];

						PlacedBlock neighborBlock = WorldMap.GetPlacedBlock(worldNx, worldNy, worldNz, out Chunk neighborChunk);
						if (neighborChunk == null || BlockInfo.IsOpaque(neighborBlock.Type))
							continue;

						byte neighborLight = 0;
						for (int i = 0; i < 6; i++)
							if (neighborBlock.Lights[i].R > neighborLight)
								neighborLight = neighborBlock.Lights[i].R;

						byte newLight = (byte)(currentLight - 1);
						if (newLight > neighborLight) {
							for (int i = 0; i < 6; i++)
								neighborBlock.Lights[i] = new BlockLight(newLight);
							WorldMap.SetPlacedBlock(worldNx, worldNy, worldNz, neighborBlock);
							// Note: Cross-chunk propagation handled by neighbor chunk's own lighting pass
						}
						continue;
					}

					int nidx = nx + ChunkSize * (ny + ChunkSize * nz);
					PlacedBlock neighborBlockInChunk = Blocks[nidx];

					// Skip opaque blocks
					if (BlockInfo.IsOpaque(neighborBlockInChunk.Type))
						continue;

					byte neighborLightInChunk = 0;
					for (int i = 0; i < 6; i++)
						if (neighborBlockInChunk.Lights[i].R > neighborLightInChunk)
							neighborLightInChunk = neighborBlockInChunk.Lights[i].R;

					byte newLightInChunk = (byte)(currentLight - 1);
					if (newLightInChunk > neighborLightInChunk) {
						for (int i = 0; i < 6; i++)
							neighborBlockInChunk.Lights[i] = new BlockLight(newLightInChunk);
						Blocks[nidx] = neighborBlockInChunk;
						lightQueue.Enqueue(new BlockPos(nx, ny, nz));
					}
				}
			}
		}

		// Returns true if this chunk is within a certain distance from the camera/player
		private bool IsDistantChunk(Vector3 chunkIndex, Vector3 cameraChunkIndex, float maxDistance) {
			return Vector3.Distance(chunkIndex, cameraChunkIndex) > maxDistance;
		}

		// Optimized AO calculation: use a simple approximation for distant chunks
		Color CalcAOColor(Vector3 GlobalBlockPos, Vector3 A, Vector3 B, Vector3 C, bool useApproximation = false) {
			if (useApproximation) {
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

		void SetBlockTextureUV(BlockType BlockType, Vector3 FaceNormal, MeshBuilder Verts) {
			int BlockID = BlockInfo.GetBlockID(BlockType, FaceNormal);
			int BlockX = BlockID % AtlasSize;
			int BlockY = BlockID / AtlasSize;

			BlockInfo.GetBlockTexCoords(BlockType, FaceNormal, out Vector2 UVSize, out Vector2 UVPos);
			Verts.SetUVOffsetSize(UVPos + new Vector2(0, UVSize.Y), UVSize * new Vector2(1, -1));
		}

		// For transparent blocks, like glass. Method does not calculate AO
		// Generates both front and back faces for glass-like blocks
		Mesh GenMeshTransparent() {
			MeshBuilder TranspVerts = new MeshBuilder();

			for (int x = 0; x < ChunkSize; x++) {
				for (int y = 0; y < ChunkSize; y++) {
					for (int z = 0; z < ChunkSize; z++) {
						WorldMap.GetWorldPos(x, y, z, GlobalChunkIndex, out Vector3 GlobalBlockPos);

						PlacedBlock CurBlock = GetBlock(x, y, z);
						if (CurBlock.Type == BlockType.None || BlockInfo.IsOpaque(CurBlock.Type))
							continue;

						TranspVerts.SetPositionOffset(new Vector3(x, y, z) * BlockSize);

						// Check if this block needs backface rendering
						bool needsBackface = BlockInfo.NeedsBackfaceRendering(CurBlock.Type);

						BlockType XPosType = GetBlock(x + 1, y, z).Type;
						BlockType XNegType = GetBlock(x - 1, y, z).Type;
						BlockType YPosType = GetBlock(x, y + 1, z).Type;
						BlockType YNegType = GetBlock(x, y - 1, z).Type;
						BlockType ZPosType = GetBlock(x, y, z + 1).Type;
						BlockType ZNegType = GetBlock(x, y, z - 1).Type;

						// For transparent blocks, skip faces only if the neighbor is the same type (to avoid z-fighting and allow for proper blending)
						bool XPosSkipFace = (XPosType == CurBlock.Type);
						bool XNegSkipFace = (XNegType == CurBlock.Type);
						bool YPosSkipFace = (YPosType == CurBlock.Type);
						bool YNegSkipFace = (YNegType == CurBlock.Type);
						bool ZPosSkipFace = (ZPosType == CurBlock.Type);
						bool ZNegSkipFace = (ZNegType == CurBlock.Type);

						// X++
						if (!XPosSkipFace) {
							Vector3 CurDir = new Vector3(1, 0, 0);
							Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
							SetBlockTextureUV(CurBlock.Type, CurDir, TranspVerts);
							// Front face
							TranspVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(1, 1, 1), new Vector2(0, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(1, 0, 0), new Vector2(1, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), CurDir, FaceClr);
							// Back face (reversed winding order)
							if (needsBackface) {
								TranspVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 1, 1), new Vector2(0, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 0, 0), new Vector2(1, 0), -CurDir, FaceClr);
							}
						}
						// X--
						if (!XNegSkipFace) {
							Vector3 CurDir = new Vector3(-1, 0, 0);
							Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
							SetBlockTextureUV(CurBlock.Type, CurDir, TranspVerts);
							// Front face
							TranspVerts.Add(new Vector3(0, 1, 1), new Vector2(1, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 1, 0), new Vector2(0, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 0, 0), new Vector2(0, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 0, 1), new Vector2(1, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 1, 1), new Vector2(1, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 0, 0), new Vector2(0, 0), CurDir, FaceClr);
							// Back face
							if (needsBackface) {
								TranspVerts.Add(new Vector3(0, 0, 0), new Vector2(0, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(0, 1, 0), new Vector2(0, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(0, 1, 1), new Vector2(1, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(0, 0, 0), new Vector2(0, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(0, 1, 1), new Vector2(1, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(0, 0, 1), new Vector2(1, 0), -CurDir, FaceClr);
							}
						}
						// Y++
						if (!YPosSkipFace) {
							Vector3 CurDir = new Vector3(0, 1, 0);
							Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
							SetBlockTextureUV(CurBlock.Type, CurDir, TranspVerts);
							// Front face
							TranspVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 1, 0), new Vector2(0, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(1, 1, 1), new Vector2(1, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 0), CurDir, FaceClr);
							// Back face
							if (needsBackface) {
								TranspVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(0, 1, 0), new Vector2(0, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 1, 1), new Vector2(1, 0), -CurDir, FaceClr);
							}
						}
						// Y--
						if (!YNegSkipFace) {
							Vector3 CurDir = new Vector3(0, -1, 0);
							Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
							SetBlockTextureUV(CurBlock.Type, CurDir, TranspVerts);
							// Front face
							TranspVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 0, 1), new Vector2(1, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(1, 0, 0), new Vector2(0, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 1), CurDir, FaceClr);
							// Back face
							if (needsBackface) {
								TranspVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(0, 0, 1), new Vector2(1, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 0, 0), new Vector2(0, 1), -CurDir, FaceClr);
							}
						}
						// Z++
						if (!ZPosSkipFace) {
							Vector3 CurDir = new Vector3(0, 0, 1);
							Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
							SetBlockTextureUV(CurBlock.Type, CurDir, TranspVerts);
							// Front face
							TranspVerts.Add(new Vector3(1, 0, 1), new Vector2(1, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(1, 1, 1), new Vector2(1, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 0, 1), new Vector2(0, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(1, 0, 1), new Vector2(1, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 1), CurDir, FaceClr);
							// Back face
							if (needsBackface) {
								TranspVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 1, 1), new Vector2(1, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 0, 1), new Vector2(1, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 0, 1), new Vector2(1, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(0, 0, 1), new Vector2(0, 0), -CurDir, FaceClr);
							}
						}
						// Z--
						if (!ZNegSkipFace) {
							Vector3 CurDir = new Vector3(0, 0, -1);
							Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
							SetBlockTextureUV(CurBlock.Type, CurDir, TranspVerts);
							// Front face
							TranspVerts.Add(new Vector3(1, 1, 0), new Vector2(0, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(1, 0, 0), new Vector2(0, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 0), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 1, 0), new Vector2(1, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(1, 1, 0), new Vector2(0, 1), CurDir, FaceClr);
							TranspVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 0), CurDir, FaceClr);
							// Back face
							if (needsBackface) {
								TranspVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 0, 0), new Vector2(0, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 1, 0), new Vector2(0, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 0), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(1, 1, 0), new Vector2(0, 1), -CurDir, FaceClr);
								TranspVerts.Add(new Vector3(0, 1, 0), new Vector2(1, 1), -CurDir, FaceClr);
							}
						}
					}
				}
			}

			return TranspVerts.ToMesh();
		}

		/// <summary>
		/// Generates a list of transparent faces with world positions for depth-sorted rendering.
		/// </summary>
		List<TransparentFace> GenTransparentFaces(Vector3 chunkWorldPos) {
			List<TransparentFace> faces = new List<TransparentFace>();

			for (int x = 0; x < ChunkSize; x++) {
				for (int y = 0; y < ChunkSize; y++) {
					for (int z = 0; z < ChunkSize; z++) {
						PlacedBlock CurBlock = GetBlock(x, y, z);
						if (CurBlock.Type == BlockType.None || BlockInfo.IsOpaque(CurBlock.Type))
							continue;

						Vector3 blockWorldPos = chunkWorldPos + new Vector3(x, y, z) * BlockSize;

						BlockType XPosType = GetBlock(x + 1, y, z).Type;
						BlockType XNegType = GetBlock(x - 1, y, z).Type;
						BlockType YPosType = GetBlock(x, y + 1, z).Type;
						BlockType YNegType = GetBlock(x, y - 1, z).Type;
						BlockType ZPosType = GetBlock(x, y, z + 1).Type;
						BlockType ZNegType = GetBlock(x, y, z - 1).Type;

						bool XPosSkipFace = (XPosType == CurBlock.Type);
						bool XNegSkipFace = (XNegType == CurBlock.Type);
						bool YPosSkipFace = (YPosType == CurBlock.Type);
						bool YNegSkipFace = (YNegType == CurBlock.Type);
						bool ZPosSkipFace = (ZPosType == CurBlock.Type);
						bool ZNegSkipFace = (ZNegType == CurBlock.Type);

						BlockInfo.GetBlockTexCoords(CurBlock.Type, Vector3.UnitX, out Vector2 uvSize, out Vector2 uvPos);

						// X++
						if (!XPosSkipFace) {
							Vector3 faceCenter = blockWorldPos + new Vector3(1, 0.5f, 0.5f);
							Color clr = CurBlock.GetBlockLight(new Vector3(1, 0, 0)).ToColor();
							BlockInfo.GetBlockTexCoords(CurBlock.Type, new Vector3(1, 0, 0), out uvSize, out uvPos);
							faces.Add(CreateFace(faceCenter, blockWorldPos, new Vector3(1, 0, 0), clr, uvPos, uvSize));
						}
						// X--
						if (!XNegSkipFace) {
							Vector3 faceCenter = blockWorldPos + new Vector3(0, 0.5f, 0.5f);
							Color clr = CurBlock.GetBlockLight(new Vector3(-1, 0, 0)).ToColor();
							BlockInfo.GetBlockTexCoords(CurBlock.Type, new Vector3(-1, 0, 0), out uvSize, out uvPos);
							faces.Add(CreateFace(faceCenter, blockWorldPos, new Vector3(-1, 0, 0), clr, uvPos, uvSize));
						}
						// Y++
						if (!YPosSkipFace) {
							Vector3 faceCenter = blockWorldPos + new Vector3(0.5f, 1, 0.5f);
							Color clr = CurBlock.GetBlockLight(new Vector3(0, 1, 0)).ToColor();
							BlockInfo.GetBlockTexCoords(CurBlock.Type, new Vector3(0, 1, 0), out uvSize, out uvPos);
							faces.Add(CreateFace(faceCenter, blockWorldPos, new Vector3(0, 1, 0), clr, uvPos, uvSize));
						}
						// Y--
						if (!YNegSkipFace) {
							Vector3 faceCenter = blockWorldPos + new Vector3(0.5f, 0, 0.5f);
							Color clr = CurBlock.GetBlockLight(new Vector3(0, -1, 0)).ToColor();
							BlockInfo.GetBlockTexCoords(CurBlock.Type, new Vector3(0, -1, 0), out uvSize, out uvPos);
							faces.Add(CreateFace(faceCenter, blockWorldPos, new Vector3(0, -1, 0), clr, uvPos, uvSize));
						}
						// Z++
						if (!ZPosSkipFace) {
							Vector3 faceCenter = blockWorldPos + new Vector3(0.5f, 0.5f, 1);
							Color clr = CurBlock.GetBlockLight(new Vector3(0, 0, 1)).ToColor();
							BlockInfo.GetBlockTexCoords(CurBlock.Type, new Vector3(0, 0, 1), out uvSize, out uvPos);
							faces.Add(CreateFace(faceCenter, blockWorldPos, new Vector3(0, 0, 1), clr, uvPos, uvSize));
						}
						// Z--
						if (!ZNegSkipFace) {
							Vector3 faceCenter = blockWorldPos + new Vector3(0.5f, 0.5f, 0);
							Color clr = CurBlock.GetBlockLight(new Vector3(0, 0, -1)).ToColor();
							BlockInfo.GetBlockTexCoords(CurBlock.Type, new Vector3(0, 0, -1), out uvSize, out uvPos);
							faces.Add(CreateFace(faceCenter, blockWorldPos, new Vector3(0, 0, -1), clr, uvPos, uvSize));
						}
					}
				}
			}

			return faces;
		}

		TransparentFace CreateFace(Vector3 center, Vector3 blockPos, Vector3 normal, Color clr, Vector2 uvPos, Vector2 uvSize) {
			Vertex3[] verts = new Vertex3[6];

			// Apply the same UV transformation as SetBlockTextureUV/MeshBuilder
			// Original: UVPos + new Vector2(0, UVSize.Y), UVSize * new Vector2(1, -1)
			Vector2 transformedUVPos = uvPos + new Vector2(0, uvSize.Y);
			Vector2 transformedUVSize = uvSize * new Vector2(1, -1);

			// UV corners after transformation (matching how MeshBuilder.Add works: UVPos + UV * UVSize)
			Vector2 uv00 = transformedUVPos + new Vector2(0, 0) * transformedUVSize;  // bottom-left
			Vector2 uv10 = transformedUVPos + new Vector2(1, 0) * transformedUVSize;  // bottom-right
			Vector2 uv11 = transformedUVPos + new Vector2(1, 1) * transformedUVSize;  // top-right
			Vector2 uv01 = transformedUVPos + new Vector2(0, 1) * transformedUVSize;  // top-left

			if (normal.X > 0) { // X++
				verts[0] = new Vertex3(blockPos + new Vector3(1, 1, 0), uv11, normal, clr);
				verts[1] = new Vertex3(blockPos + new Vector3(1, 1, 1), uv01, normal, clr);
				verts[2] = new Vertex3(blockPos + new Vector3(1, 0, 1), uv00, normal, clr);
				verts[3] = new Vertex3(blockPos + new Vector3(1, 0, 0), uv10, normal, clr);
				verts[4] = new Vertex3(blockPos + new Vector3(1, 1, 0), uv11, normal, clr);
				verts[5] = new Vertex3(blockPos + new Vector3(1, 0, 1), uv00, normal, clr);
			} else if (normal.X < 0) { // X--
				verts[0] = new Vertex3(blockPos + new Vector3(0, 1, 1), uv11, normal, clr);
				verts[1] = new Vertex3(blockPos + new Vector3(0, 1, 0), uv01, normal, clr);
				verts[2] = new Vertex3(blockPos + new Vector3(0, 0, 0), uv00, normal, clr);
				verts[3] = new Vertex3(blockPos + new Vector3(0, 0, 1), uv10, normal, clr);
				verts[4] = new Vertex3(blockPos + new Vector3(0, 1, 1), uv11, normal, clr);
				verts[5] = new Vertex3(blockPos + new Vector3(0, 0, 0), uv00, normal, clr);
			} else if (normal.Y > 0) { // Y++
				verts[0] = new Vertex3(blockPos + new Vector3(1, 1, 0), uv11, normal, clr);
				verts[1] = new Vertex3(blockPos + new Vector3(0, 1, 0), uv01, normal, clr);
				verts[2] = new Vertex3(blockPos + new Vector3(0, 1, 1), uv00, normal, clr);
				verts[3] = new Vertex3(blockPos + new Vector3(1, 1, 1), uv10, normal, clr);
				verts[4] = new Vertex3(blockPos + new Vector3(1, 1, 0), uv11, normal, clr);
				verts[5] = new Vertex3(blockPos + new Vector3(0, 1, 1), uv00, normal, clr);
			} else if (normal.Y < 0) { // Y--
				verts[0] = new Vertex3(blockPos + new Vector3(1, 0, 1), uv00, normal, clr);
				verts[1] = new Vertex3(blockPos + new Vector3(0, 0, 1), uv10, normal, clr);
				verts[2] = new Vertex3(blockPos + new Vector3(0, 0, 0), uv11, normal, clr);
				verts[3] = new Vertex3(blockPos + new Vector3(1, 0, 0), uv01, normal, clr);
				verts[4] = new Vertex3(blockPos + new Vector3(1, 0, 1), uv00, normal, clr);
				verts[5] = new Vertex3(blockPos + new Vector3(0, 0, 0), uv11, normal, clr);
			} else if (normal.Z > 0) { // Z++
				verts[0] = new Vertex3(blockPos + new Vector3(1, 0, 1), uv10, normal, clr);
				verts[1] = new Vertex3(blockPos + new Vector3(1, 1, 1), uv11, normal, clr);
				verts[2] = new Vertex3(blockPos + new Vector3(0, 1, 1), uv01, normal, clr);
				verts[3] = new Vertex3(blockPos + new Vector3(0, 0, 1), uv00, normal, clr);
				verts[4] = new Vertex3(blockPos + new Vector3(1, 0, 1), uv10, normal, clr);
				verts[5] = new Vertex3(blockPos + new Vector3(0, 1, 1), uv01, normal, clr);
			} else { // Z--
				verts[0] = new Vertex3(blockPos + new Vector3(1, 1, 0), uv01, normal, clr);
				verts[1] = new Vertex3(blockPos + new Vector3(1, 0, 0), uv00, normal, clr);
				verts[2] = new Vertex3(blockPos + new Vector3(0, 0, 0), uv10, normal, clr);
				verts[3] = new Vertex3(blockPos + new Vector3(0, 1, 0), uv11, normal, clr);
				verts[4] = new Vertex3(blockPos + new Vector3(1, 1, 0), uv01, normal, clr);
				verts[5] = new Vertex3(blockPos + new Vector3(0, 0, 0), uv10, normal, clr);
			}

			return new TransparentFace(center, verts);
		}

		Mesh GenMesh(Vector3? cameraChunkIndex = null, float aoApproxDistance = 6f) {
			MeshBuilder OpaqueVerts = new MeshBuilder();

			Vector3 Size = new Vector3(BlockSize);

			Color AOColor = new Color(128, 128, 128);
			AOColor = Utils.ColorMul(AOColor, AOColor);

			Vector3 chunkIdx = GlobalChunkIndex;
			bool useApproxAO = false;

			if (cameraChunkIndex != null)
				useApproxAO = IsDistantChunk(chunkIdx, cameraChunkIndex.Value, aoApproxDistance);

			for (int x = 0; x < ChunkSize; x++) {
				for (int y = 0; y < ChunkSize; y++) {
					for (int z = 0; z < ChunkSize; z++) {
						WorldMap.GetWorldPos(x, y, z, GlobalChunkIndex, out Vector3 GlobalBlockPos);

						PlacedBlock CurBlock = null;
						if ((CurBlock = GetBlock(x, y, z)).Type != BlockType.None) {
							// --- Optimization: skip face culling for fully enclosed opaque blocks ---
							if (BlockInfo.IsOpaque(CurBlock.Type)
								&& BlockInfo.IsOpaque(GetBlock(x + 1, y, z).Type)
								&& BlockInfo.IsOpaque(GetBlock(x - 1, y, z).Type)
								&& BlockInfo.IsOpaque(GetBlock(x, y + 1, z).Type)
								&& BlockInfo.IsOpaque(GetBlock(x, y - 1, z).Type)
								&& BlockInfo.IsOpaque(GetBlock(x, y, z + 1).Type)
								&& BlockInfo.IsOpaque(GetBlock(x, y, z - 1).Type)) {
								// All neighbors are opaque, skip this block
								continue;
							}
							OpaqueVerts.SetPositionOffset(new Vector3(x, y, z) * BlockSize);

							BlockType XPosType = GetBlock(x + 1, y, z).Type;
							BlockType XNegType = GetBlock(x - 1, y, z).Type;
							BlockType YPosType = GetBlock(x, y + 1, z).Type;
							BlockType YNegType = GetBlock(x, y - 1, z).Type;
							BlockType ZPosType = GetBlock(x, y, z + 1).Type;
							BlockType ZNegType = GetBlock(x, y, z - 1).Type;

							if (!BlockInfo.IsOpaque(CurBlock.Type))
								continue;

							bool XPosSkipFace = false;
							bool XNegSkipFace = false;
							bool YPosSkipFace = false;
							bool YNegSkipFace = false;
							bool ZPosSkipFace = false;
							bool ZNegSkipFace = false;

							if (BlockInfo.IsOpaque(CurBlock.Type)) {
								XPosSkipFace = BlockInfo.IsOpaque(XPosType);
								XNegSkipFace = BlockInfo.IsOpaque(XNegType);
								YPosSkipFace = BlockInfo.IsOpaque(YPosType);
								YNegSkipFace = BlockInfo.IsOpaque(YNegType);
								ZPosSkipFace = BlockInfo.IsOpaque(ZPosType);
								ZNegSkipFace = BlockInfo.IsOpaque(ZNegType);
							}

							if (BlockInfo.CustomModel(CurBlock.Type)) {
								if (!XPosSkipFace || !XNegSkipFace || !YPosSkipFace || !YNegSkipFace || !ZPosSkipFace || !ZNegSkipFace) {

									Model Mdl = BlockInfo.GetCustomModel(CurBlock.Type);

									SetBlockTextureUV(CurBlock.Type, Vector3.UnitY, OpaqueVerts);

									for (int j = 0; j < Mdl.MeshCount; j++) {
										for (int i = 0; i < Mdl.Meshes[j].VertexCount; i++) {
											Vector3 Vert = ((Vector3*)Mdl.Meshes[j].Vertices)[i];
											Vector2 UV = new Vector2(0, 1) + ((Vector2*)Mdl.Meshes[j].TexCoords)[i] * new Vector2(1, -1);
											OpaqueVerts.Add(Vert + new Vector3(0.5f, 0, 0.5f), UV, Vector3.Zero, Color.White);
										}
									}



									Console.WriteLine("!");

								}
							} else {

								// X++
								if (!XPosSkipFace) {
									Vector3 CurDir = new Vector3(1, 0, 0);
									Color FaceClr = Utils.ColorMul(CurBlock.GetBlockLight(CurDir).ToColor(), ChunkColor);
									SetBlockTextureUV(CurBlock.Type, CurDir, OpaqueVerts);

									OpaqueVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), new Vector3(1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(1, 0, -1), new Vector3(1, 1, -1), new Vector3(1, 1, 0), useApproxAO)));
									OpaqueVerts.Add(new Vector3(1, 1, 1), new Vector2(0, 1), new Vector3(1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), new Vector3(1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(1, -1, 0), new Vector3(1, -1, 1), new Vector3(1, 0, 1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(1, 0, 0), new Vector2(1, 0), new Vector3(1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(1, 0, -1), new Vector3(1, -1, -1), new Vector3(1, -1, 0), useApproxAO)));
									OpaqueVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), new Vector3(1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(1, 0, -1), new Vector3(1, 1, -1), new Vector3(1, 1, 0), useApproxAO)));
									OpaqueVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), new Vector3(1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(1, -1, 0), new Vector3(1, -1, 1), new Vector3(1, 0, 1), useApproxAO)));
								}

								// X--
								if (!XNegSkipFace) {
									Vector3 CurDir = new Vector3(-1, 0, 0);
									//Color FaceClr = Color.White; 
									Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
									SetBlockTextureUV(CurBlock.Type, CurDir, OpaqueVerts);

									OpaqueVerts.Add(new Vector3(0, 1, 1), new Vector2(1, 1), new Vector3(-1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 1, 0), new Vector3(-1, 1, 1), new Vector3(-1, 0, 1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 1, 0), new Vector2(0, 1), new Vector3(-1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 0, -1), new Vector3(-1, 1, -1), new Vector3(-1, 1, 0), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 0, 0), new Vector2(0, 0), new Vector3(-1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 0, -1), new Vector3(-1, -1, -1), new Vector3(-1, -1, 0), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 0, 1), new Vector2(1, 0), new Vector3(-1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, -1, 0), new Vector3(-1, -1, 1), new Vector3(-1, 0, 1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 1, 1), new Vector2(1, 1), new Vector3(-1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 1, 0), new Vector3(-1, 1, 1), new Vector3(-1, 0, 1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 0, 0), new Vector2(0, 0), new Vector3(-1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 0, -1), new Vector3(-1, -1, -1), new Vector3(-1, -1, 0), useApproxAO)));
								}

								// Y++
								if (!YPosSkipFace) {
									Vector3 CurDir = new Vector3(0, 1, 0);
									//Color FaceClr = Color.White; 
									Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
									SetBlockTextureUV(CurBlock.Type, CurDir, OpaqueVerts);

									OpaqueVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), new Vector3(0, 1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, 1, -1), new Vector3(1, 1, -1), new Vector3(1, 1, 0), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 1, 0), new Vector2(0, 1), new Vector3(0, 1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, 1, -1), new Vector3(-1, 1, -1), new Vector3(-1, 1, 0), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 0), new Vector3(0, 1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 1, 0), new Vector3(-1, 1, 1), new Vector3(0, 1, 1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(1, 1, 1), new Vector2(1, 0), new Vector3(0, 1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(0, 1, 1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(1, 1, 0), new Vector2(1, 1), new Vector3(0, 1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, 1, -1), new Vector3(1, 1, -1), new Vector3(1, 1, 0), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 0), new Vector3(0, 1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 1, 0), new Vector3(-1, 1, 1), new Vector3(0, 1, 1), useApproxAO)));
								}

								// Y--
								if (!YNegSkipFace) {
									Vector3 CurDir = new Vector3(0, -1, 0);
									//Color FaceClr = Color.White; 
									Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
									SetBlockTextureUV(CurBlock.Type, CurDir, OpaqueVerts);

									OpaqueVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), new Vector3(0, -1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, -1, 1), new Vector3(1, -1, 1), new Vector3(1, -1, 0), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 0, 1), new Vector2(1, 0), new Vector3(0, -1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, -1, 1), new Vector3(-1, -1, 1), new Vector3(-1, -1, 0), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 1), new Vector3(0, -1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, -1, 0), new Vector3(-1, -1, -1), new Vector3(0, -1, -1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(1, 0, 0), new Vector2(0, 1), new Vector3(0, -1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(1, -1, 0), new Vector3(1, -1, -1), new Vector3(0, -1, -1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(1, 0, 1), new Vector2(0, 0), new Vector3(0, -1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, -1, 1), new Vector3(1, -1, 1), new Vector3(1, -1, 0), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 1), new Vector3(0, -1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, -1, 0), new Vector3(-1, -1, -1), new Vector3(0, -1, -1), useApproxAO)));
								}

								// Z++
								if (!ZPosSkipFace) {
									Vector3 CurDir = new Vector3(0, 0, 1);
									//Color FaceClr = Color.White; 
									Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
									SetBlockTextureUV(CurBlock.Type, CurDir, OpaqueVerts);

									OpaqueVerts.Add(new Vector3(1, 0, 1), new Vector2(1, 0), new Vector3(0, 0, 1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, -1, 1), new Vector3(1, -1, 1), new Vector3(1, 0, 1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(1, 1, 1), new Vector2(1, 1), new Vector3(0, 0, 1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 0, 1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 1), new Vector3(0, 0, 1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, 1, 1), new Vector3(-1, 1, 1), new Vector3(-1, 0, 1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 0, 1), new Vector2(0, 0), new Vector3(0, 0, 1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 0, 1), new Vector3(-1, -1, 1), new Vector3(0, -1, 1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(1, 0, 1), new Vector2(1, 0), new Vector3(0, 0, 1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, -1, 1), new Vector3(1, -1, 1), new Vector3(1, 0, 1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 1, 1), new Vector2(0, 1), new Vector3(0, 0, 1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, 1, 1), new Vector3(-1, 1, 1), new Vector3(-1, 0, 1), useApproxAO)));
								}

								// Z--
								if (!ZNegSkipFace) {
									Vector3 CurDir = new Vector3(0, 0, -1);
									//Color FaceClr = Color.White; 
									Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
									SetBlockTextureUV(CurBlock.Type, CurDir, OpaqueVerts);

									OpaqueVerts.Add(new Vector3(1, 1, 0), new Vector2(0, 1), new Vector3(0, 0, -1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, 1, -1), new Vector3(1, 1, -1), new Vector3(1, 0, -1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(1, 0, 0), new Vector2(0, 0), new Vector3(0, 0, -1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, -1, -1), new Vector3(1, -1, -1), new Vector3(1, 0, -1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 0), new Vector3(0, 0, -1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, -1, -1), new Vector3(-1, -1, -1), new Vector3(-1, 0, -1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 1, 0), new Vector2(1, 1), new Vector3(0, 0, -1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 0, -1), new Vector3(-1, 1, -1), new Vector3(0, 1, -1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(1, 1, 0), new Vector2(0, 1), new Vector3(0, 0, -1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, 1, -1), new Vector3(1, 1, -1), new Vector3(1, 0, -1), useApproxAO)));
									OpaqueVerts.Add(new Vector3(0, 0, 0), new Vector2(1, 0), new Vector3(0, 0, -1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, -1, -1), new Vector3(-1, -1, -1), new Vector3(-1, 0, -1), useApproxAO)));
								}
							}
						}

					}
				}
			}

			return OpaqueVerts.ToMesh();
		}

		void RecalcModel() {
			if (!Dirty)
				return;

			Dirty = false;

			if (ModelValidOpaque) {
				// Set texture ID to 1 to disable texture unloading? Does that even do anything?
				CachedModelOpaque.Materials[0].Maps[0].Texture.Id = 0;
				Raylib.UnloadModel(CachedModelOpaque);
			}

			CachedMeshOpaque = GenMesh();
			CachedModelOpaque = Raylib.LoadModelFromMesh(CachedMeshOpaque);
			CachedModelOpaque.Materials[0].Maps[0].Texture = ResMgr.AtlasTexture;
			CachedModelOpaque.Materials[0].Shader = ResMgr.GetShader("default");
			ModelValidOpaque = CachedMeshOpaque.VertexCount > 0;

			if (ModelValidTransp) {
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
			if (ModelValidTransp && Program.DebugMode) {
				Console.WriteLine($"[Chunk {GlobalChunkIndex}] Transparent mesh: {CachedMeshTransp.VertexCount} vertices");
			}

			// Generate transparent faces for depth-sorted rendering
			Vector3 chunkWorldPos = GlobalChunkIndex * ChunkSize;
			CachedTransparentFaces = GenTransparentFaces(chunkWorldPos);
			TransparentFacesValid = CachedTransparentFaces.Count > 0;

			if (!ModelValidOpaque && !ModelValidTransp) {
				ModelAABB = AABB.Empty;
			} else {
				if (!ModelValidOpaque) {
					ModelAABB = new AABB(Raylib.GetMeshBoundingBox(CachedMeshTransp));
				} else if (!ModelValidTransp) {
					ModelAABB = new AABB(Raylib.GetMeshBoundingBox(CachedMeshOpaque));
				} else {
					AABB BBOpaque = new AABB(Raylib.GetMeshBoundingBox(CachedMeshOpaque));
					AABB BBTransp = new AABB(Raylib.GetMeshBoundingBox(CachedMeshTransp));
					ModelAABB = AABB.Union(BBOpaque, BBTransp);
				}
			}
		}

		public RayCollision Collide(Vector3 ChunkPosition, Ray R) {
			Matrix4x4 Transform = Matrix4x4.Transpose(Matrix4x4.CreateTranslation(ChunkPosition));
			return Raylib.GetRayCollisionMesh(R, CachedMeshOpaque, Transform);
		}

		bool IsInsideFrustum(Vector3 ChunkPosition, ref Frustum Fr) {
			if (Fr.IsInside(ModelAABB.Offset(ChunkPosition))) {
				return true;
			}

			return false;
		}

		public void Draw(Vector3 ChunkPosition, ref Frustum Fr) {
			RecalcModel();

			if (!IsInsideFrustum(ChunkPosition, ref Fr))
				return;

			if (Program.DebugMode)
				Raylib.DrawBoundingBox(ModelAABB.Offset(ChunkPosition).ToBoundingBox(), Color.Yellow);

			if (ModelValidOpaque) {
				Raylib.DrawModel(CachedModelOpaque, ChunkPosition, BlockSize, ChunkColor);
				Program.ChunkDrawCalls++;
			}
		}

		public void DrawTransparent(Vector3 ChunkPosition, ref Frustum Fr) {
			if (!IsInsideFrustum(ChunkPosition, ref Fr))
				return;

			if (ModelValidTransp) {
				// Backfaces are now explicitly generated in the mesh for glass-like blocks
				Raylib.DrawModel(CachedModelTransp, ChunkPosition, BlockSize, ChunkColor);
			}
		}

		/// <summary>
		/// Returns the cached transparent faces for this chunk for depth-sorted rendering.
		/// Call RecalcModel first if dirty.
		/// </summary>
		public List<TransparentFace> GetTransparentFaces(ref Frustum Fr) {
			RecalcModel();
			Vector3 chunkWorldPos = GlobalChunkIndex * ChunkSize;
			if (!IsInsideFrustum(chunkWorldPos, ref Fr))
				return new List<TransparentFace>();
			return CachedTransparentFaces;
		}

		public bool HasTransparentFaces() {
			RecalcModel();
			return TransparentFacesValid;
		}
	}
}
