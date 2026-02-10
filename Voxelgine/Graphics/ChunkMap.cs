using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Raylib_cs;
using Voxelgine.Engine;
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
	public unsafe partial class ChunkMap
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
			Chunks = new SpatialHashGrid<Chunk>();
		}

		/// <summary>
		/// Returns all block changes recorded since the last call to <see cref="ClearPendingChanges"/>.
		/// </summary>
		public IReadOnlyList<BlockChange> GetPendingChanges() => _blockChangeLog;

		/// <summary>
		/// Clears the block change log. Called by the server after broadcasting deltas each tick.
		/// </summary>
		public void ClearPendingChanges() => _blockChangeLog.Clear();

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

		public void Tick()
		{
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
	}
}
