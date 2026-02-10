using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;
using Raylib_cs;
using Voxelgine.Engine;

namespace Voxelgine.Graphics
{
	public unsafe partial class ChunkMap
	{
		/// <summary>
		/// Emits particles for blocks that produce them (e.g. campfire fire particles).
		/// Called each frame; internally throttled to emit every ~0.25 seconds.
		/// Uses <see cref="_cameraPosition"/> from the previous Draw call for distance filtering.
		/// </summary>
		public void EmitBlockParticles(ParticleSystem particle, float dt)
		{
			const float EmitInterval = 0.25f;

			_blockParticleTimer -= dt;
			if (_blockParticleTimer > 0f)
				return;
			_blockParticleTimer = EmitInterval;

			float halfChunk = Chunk.ChunkSize * 0.5f;
			float renderDistSq = RenderDistanceBlocks * RenderDistanceBlocks;

			foreach (var KV in Chunks.Items)
			{
				Chunk chunk = KV.Value;
				if (!chunk.HasCustomModelBlocks)
					continue;

				Vector3 chunkPos = KV.Key * new Vector3(Chunk.ChunkSize);
				Vector3 chunkCenter = chunkPos + new Vector3(halfChunk);
				if (Vector3.DistanceSquared(_cameraPosition, chunkCenter) > renderDistSq)
					continue;

				for (int i = 0; i < chunk.CachedCustomModelBlocks.Count; i++)
				{
					var cmb = chunk.CachedCustomModelBlocks[i];
					if (cmb.Type == BlockType.Campfire)
					{
						Vector3 worldPos = chunkPos + new Vector3(cmb.X + 0.5f, cmb.Y + 0.6f, cmb.Z + 0.5f);

						Vector3 rndDir = Vector3.Normalize(Vector3.UnitY + Utils.GetRandomUnitVector() * 0.6f);

						particle.SpawnFire(worldPos, rndDir * 1.8f, Color.White, (float)(Random.Shared.NextDouble() + 0.5), noCollide: true, lifetime: 0.9f);
					}
				}
			}
		}

		public void Draw(ref Frustum Fr)
		{
			_cameraPosition = Fr.CamPos;
			float halfChunk = Chunk.ChunkSize * 0.5f;
			float renderDistSq = RenderDistanceBlocks * RenderDistanceBlocks;

			// Collect chunks that entered render distance and need deferred relighting
			List<Chunk> relightChunks = null;
			foreach (var KV in Chunks.Items)
			{
				if (!KV.Value.NeedsRelighting)
					continue;

				Vector3 ChunkCenter = KV.Key * new Vector3(Chunk.ChunkSize) + new Vector3(halfChunk);
				if (Vector3.DistanceSquared(Fr.CamPos, ChunkCenter) <= renderDistSq)
				{
					relightChunks ??= new List<Chunk>();
					relightChunks.Add(KV.Value);
					KV.Value.NeedsRelighting = false;
				}
			}

			if (relightChunks != null)
			{
				foreach (var chunk in relightChunks)
					chunk.ResetLighting();
				ComputeLightingParallel(relightChunks.ToArray());
				foreach (var chunk in relightChunks)
					chunk.MarkDirty();
			}

			foreach (var KV in Chunks.Items)
			{
				Vector3 ChunkPos = KV.Key * new Vector3(Chunk.ChunkSize);
				Vector3 ChunkCenter = ChunkPos + new Vector3(halfChunk);
				if (Vector3.DistanceSquared(Fr.CamPos, ChunkCenter) > renderDistSq)
					continue;

				KV.Value.Draw(ChunkPos, ref Fr);
			}

			Utils.DrawRaycastRecord();
		}

		public void DrawTransparent(ref Frustum Fr, Vector3 cameraPos)
		{
			// Collect all transparent faces from visible chunks within render distance
			TransparentFaceBuffer.Clear();

			float halfChunk = Chunk.ChunkSize * 0.5f;
			float renderDistSq = RenderDistanceBlocks * RenderDistanceBlocks;

			foreach (var KV in Chunks.Items)
			{
				Vector3 ChunkPos = KV.Key * new Vector3(Chunk.ChunkSize);
				Vector3 ChunkCenter = ChunkPos + new Vector3(halfChunk);
				if (Vector3.DistanceSquared(cameraPos, ChunkCenter) > renderDistSq)
					continue;

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
	}
}
