using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Voxelgine.Engine;

namespace Voxelgine.Graphics
{
	public unsafe partial class ChunkMap
	{
		public void ResetLighting(CancellationToken cancellationToken = default)
		{
			cancellationToken.ThrowIfCancellationRequested();
			Chunk[] chunks = GetAllChunks();
			Parallel.ForEach(
				chunks,
				new ParallelOptions { CancellationToken = cancellationToken },
				static chunk => chunk.ResetLighting()
			);
		}

		public void ComputeLighting(CancellationToken cancellationToken = default)
		{
			ResetLighting(cancellationToken);
			Chunk[] chunks = GetAllChunks();
			ComputeLightingParallel(chunks, cancellationToken);
			Parallel.ForEach(
				chunks,
				new ParallelOptions { CancellationToken = cancellationToken },
				static chunk => chunk.MarkDirty()
			);
		}

		private void ComputeLightingParallel(
			Chunk[] chunks,
			CancellationToken cancellationToken = default)
		{
			List<Chunk>[] phases = new List<Chunk>[8];
			for (int index = 0; index < phases.Length; index++)
				phases[index] = new List<Chunk>(chunks.Length / phases.Length + 1);

			foreach (Chunk chunk in chunks)
			{
				cancellationToken.ThrowIfCancellationRequested();
				int x = ((int)chunk.GlobalChunkIndex.X % 2 + 2) % 2;
				int y = ((int)chunk.GlobalChunkIndex.Y % 2 + 2) % 2;
				int z = ((int)chunk.GlobalChunkIndex.Z % 2 + 2) % 2;
				phases[x + y * 2 + z * 4].Add(chunk);
			}

			foreach (List<Chunk> phase in phases)
			{
				if (phase.Count > 0)
				{
					Parallel.ForEach(
						phase,
						new ParallelOptions { CancellationToken = cancellationToken },
						static chunk => chunk.ComputeLightingWithoutReset()
					);
				}
			}
		}

		public void ComputeLightingWithEntities(IEnumerable<PointLight> entityLights)
		{
			Chunk[] chunks = GetAllChunks();
			Parallel.ForEach(chunks, static chunk => chunk.ResetLighting());
			ComputeLightingParallel(chunks);

			if (entityLights != null)
			{
				List<PointLight> lights = entityLights.ToList();
				if (lights.Count > 0)
				{
					foreach (Chunk chunk in chunks)
						chunk.ComputeEntityLights(lights);
				}
			}

			Parallel.ForEach(chunks, static chunk => chunk.MarkDirty());
		}

		public float GetLightLevel(Vector3 position) => GetLightLevel(
			(int)MathF.Floor(position.X),
			(int)MathF.Floor(position.Y),
			(int)MathF.Floor(position.Z));

		public float GetLightLevel(int x, int y, int z)
		{
			PlacedBlock block = GetPlacedBlock(x, y, z, out _);
			float skyContribution = block.GetMaxSkylight() * BlockLight.SkyLightMultiplier;
			float combined = MathF.Max(skyContribution, block.GetMaxBlockLight());
			combined = MathF.Max(combined, BlockLight.AmbientLight);
			return combined / 15f;
		}

		public Rgba32 GetLightColor(Vector3 position)
		{
			byte value = (byte)(GetLightLevel(position) * byte.MaxValue);
			return new Rgba32(value, value, value);
		}
	}
}
