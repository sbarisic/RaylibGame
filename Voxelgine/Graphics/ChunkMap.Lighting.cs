using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Raylib_cs;
using Voxelgine.Engine;

namespace Voxelgine.Graphics
{
	public unsafe partial class ChunkMap
	{
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
	}
}
