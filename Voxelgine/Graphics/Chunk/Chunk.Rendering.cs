using Raylib_cs;
using System.Numerics;

using Voxelgine.Engine;

using System;
using System.Collections.Generic;

namespace Voxelgine.Graphics
{
	public unsafe partial class Chunk
	{
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
			if (ModelValidTransp && Eng.DebugMode)
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

			if (Eng.DebugMode)
				Raylib.DrawBoundingBox(ModelAABB.Offset(ChunkPosition).ToBoundingBox(), Color.Yellow);

			if (ModelValidOpaque)
			{
				Raylib.DrawModel(CachedModelOpaque, ChunkPosition, BlockSize, ChunkColor);
				Eng.ChunkDrawCalls++;
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
