using Raylib_cs;
using System.Numerics;

using Voxelgine.Engine;

using System;
using System.Collections.Generic;
using Voxelgine.Engine.DI;

namespace Voxelgine.Graphics
{
	/// <summary>
	/// Tracks a custom model block position within a chunk for separate rendering.
	/// </summary>
	public struct CustomModelBlock
	{
		public int X, Y, Z;
		public BlockType Type;
	}

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
	/// 
	/// This class is split into multiple partial class files:
	/// - Chunk.Base.cs: Core fields, constructor, basic block operations
	/// - Chunk.Lighting.cs: Lighting computation and propagation
	/// - Chunk.Rendering.cs: Drawing, model management, frustum culling
	/// - Chunk.GenMesh.cs: Mesh generation for opaque and transparent blocks
	/// - Chunk.Serialization.cs: Save/load functionality
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

		/// <summary>
		/// True when a block change affected this chunk's lighting but it was outside
		/// render distance at the time. Lighting will be recomputed when the chunk
		/// enters render distance during <see cref="ChunkMap.Draw"/>.
		/// </summary>
		public bool NeedsRelighting;

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

		List<CustomModelBlock> CachedCustomModelBlocks;
		bool HasCustomModelBlocks;

		public Color ChunkColor = Color.White;

		public Vector3 GlobalChunkIndex;
		ChunkMap WorldMap;

		IFishEngineRunner Eng;
		IFishLogging Logging;

		public Chunk(IFishEngineRunner Eng, Vector3 GlobalChunkIndex, ChunkMap WorldMap)
		{
			this.GlobalChunkIndex = GlobalChunkIndex;
			this.WorldMap = WorldMap;

			this.Eng = Eng;
			this.Logging = Eng.DI.GetRequiredService<IFishLogging>();

			Blocks = new PlacedBlock[ChunkSize * ChunkSize * ChunkSize];
			for (int i = 0; i < Blocks.Length; i++)
				Blocks[i] = new PlacedBlock(BlockType.None);

			Dirty = true;
			ModelValidTransp = ModelValidOpaque = false;
			TransparentFacesValid = false;
			CachedTransparentFaces = new List<TransparentFace>();
			CachedCustomModelBlocks = new List<CustomModelBlock>();
			HasCustomModelBlocks = false;
		}

		public PlacedBlock GetBlock(int X, int Y, int Z)
		{
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
	}
}
