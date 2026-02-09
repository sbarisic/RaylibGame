using Raylib_cs;
using System.Numerics;

using Voxelgine.Engine;

using System;
using System.Collections.Generic;
using System.Threading;
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

		/// <summary>Number of non-air blocks in this chunk. Used for empty chunk early-out.</summary>
		int NonAirBlockCount;

		// Padded block cache (18³) for fast neighbor lookups during mesh generation.
		// Includes a 1-block border from neighboring chunks, indexed as [(x+1) + PaddedSize * ((y+1) + PaddedSize * (z+1))].
		const int PaddedSize = ChunkSize + 2;
		PlacedBlock[] _paddedBlocks;

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

			NonAirBlockCount = 0;
			_paddedBlocks = new PlacedBlock[PaddedSize * PaddedSize * PaddedSize];

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
			int idx = X + ChunkSize * (Y + ChunkSize * Z);
			BlockType oldType = Blocks[idx].Type;
			Blocks[idx] = Block;

			// Update non-air block count (Interlocked for thread safety during parallel world gen)
			if (oldType == BlockType.None && Block.Type != BlockType.None)
				Interlocked.Increment(ref NonAirBlockCount);
			else if (oldType != BlockType.None && Block.Type == BlockType.None)
				Interlocked.Decrement(ref NonAirBlockCount);

			Dirty = true;
			SkyExposureCacheValid = false;
		}

		public void Fill(PlacedBlock Block)
		{
			for (int i = 0; i < Blocks.Length; i++)
				Blocks[i] = Block;
			NonAirBlockCount = Block.Type != BlockType.None ? Blocks.Length : 0;
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

		/// <summary>
		/// Recomputes NonAirBlockCount by scanning the Blocks array.
		/// Call after bulk block writes that bypass SetBlock (e.g., deserialization).
		/// </summary>
		public void RecomputeNonAirBlockCount()
		{
			int count = 0;
			for (int i = 0; i < Blocks.Length; i++)
			{
				if (Blocks[i].Type != BlockType.None)
					count++;
			}
			NonAirBlockCount = count;
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

		/// <summary>
		/// Builds the 18³ padded block cache for fast neighbor lookups during mesh generation.
		/// Fetches a 1-block border from neighboring chunks so all lookups become O(1) array accesses.
		/// </summary>
		void BuildPaddedCache()
		{
			var padded = _paddedBlocks;
			PlacedBlock airBlock = new PlacedBlock(BlockType.None);

			// Fill interior from own blocks (x,y,z in [0..ChunkSize-1] → padded index [1..ChunkSize])
			for (int z = 0; z < ChunkSize; z++)
			{
				for (int y = 0; y < ChunkSize; y++)
				{
					for (int x = 0; x < ChunkSize; x++)
					{
						padded[(x + 1) + PaddedSize * ((y + 1) + PaddedSize * (z + 1))] = Blocks[x + ChunkSize * (y + ChunkSize * z)];
					}
				}
			}

			// Fill border from neighboring chunks (or air if neighbor doesn't exist)
			// Use GetBlock which handles cross-chunk lookups via WorldMap
			for (int pz = 0; pz < PaddedSize; pz++)
			{
				int z = pz - 1;
				for (int py = 0; py < PaddedSize; py++)
				{
					int y = py - 1;
					for (int px = 0; px < PaddedSize; px++)
					{
						int x = px - 1;

						// Skip interior blocks (already filled above)
						if (x >= 0 && x < ChunkSize && y >= 0 && y < ChunkSize && z >= 0 && z < ChunkSize)
							continue;

						padded[px + PaddedSize * (py + PaddedSize * pz)] = GetBlock(x, y, z);
					}
				}
			}
		}

		/// <summary>
		/// Reads from the padded block cache. Coordinates are in chunk-local space [-1..ChunkSize].
		/// </summary>
		PlacedBlock PaddedGet(int x, int y, int z)
		{
			return _paddedBlocks[(x + 1) + PaddedSize * ((y + 1) + PaddedSize * (z + 1))];
		}
	}
}
