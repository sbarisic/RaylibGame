namespace Voxelgine.Graphics;

internal readonly record struct TreeGenerationCandidate(int X, int Z, int SurfaceY);

internal readonly record struct NaturalPondCell(int X, int Z, int SurfaceY);

internal sealed record NaturalPondPlan(int WaterLevel, NaturalPondCell[] Cells);

internal static class WorldGenerationPlanning
{
	internal const int MinimumNaturalPondCells = 24;
	internal const int MaximumNaturalPondDepth = 4;

	internal static float CalculateHeightFalloff(
		float normalizedHeight,
		float falloffStart = 0.8f,
		float falloffEnd = 1.0f)
	{
		if (falloffEnd <= falloffStart)
			throw new ArgumentOutOfRangeException(nameof(falloffEnd));

		return Math.Clamp(
			(falloffEnd - normalizedHeight) / (falloffEnd - falloffStart),
			0f,
			1f);
	}

	internal static TreeGenerationCandidate[] SelectTreePositions(
		IReadOnlyList<TreeGenerationCandidate> candidates,
		int minimumSpacing,
		int selectionSeed)
	{
		ArgumentNullException.ThrowIfNull(candidates);
		if (minimumSpacing <= 0)
			throw new ArgumentOutOfRangeException(nameof(minimumSpacing));

		List<TreeGenerationCandidate> shuffled = new(candidates);
		Random random = new(selectionSeed);
		for (int index = shuffled.Count - 1; index > 0; index--)
		{
			int other = random.Next(index + 1);
			(shuffled[index], shuffled[other]) = (shuffled[other], shuffled[index]);
		}

		int minimumSpacingSquared = checked(minimumSpacing * minimumSpacing);
		Dictionary<(int X, int Z), List<TreeGenerationCandidate>> buckets = new();
		List<TreeGenerationCandidate> accepted = new();

		foreach (TreeGenerationCandidate candidate in shuffled)
		{
			(int bucketX, int bucketZ) = (candidate.X / minimumSpacing, candidate.Z / minimumSpacing);
			bool tooClose = false;
			for (int offsetX = -1; offsetX <= 1 && !tooClose; offsetX++)
			{
				for (int offsetZ = -1; offsetZ <= 1 && !tooClose; offsetZ++)
				{
					if (!buckets.TryGetValue((bucketX + offsetX, bucketZ + offsetZ), out List<TreeGenerationCandidate> nearby))
						continue;

					foreach (TreeGenerationCandidate selected in nearby)
					{
						long deltaX = candidate.X - selected.X;
						long deltaZ = candidate.Z - selected.Z;
						if (deltaX * deltaX + deltaZ * deltaZ < minimumSpacingSquared)
						{
							tooClose = true;
							break;
						}
					}
				}
			}

			if (tooClose)
				continue;

			accepted.Add(candidate);
			if (!buckets.TryGetValue((bucketX, bucketZ), out List<TreeGenerationCandidate> bucket))
			{
				bucket = new List<TreeGenerationCandidate>();
				buckets.Add((bucketX, bucketZ), bucket);
			}
			bucket.Add(candidate);
		}

		accepted.Sort(static (left, right) =>
		{
			int comparison = left.X.CompareTo(right.X);
			return comparison != 0 ? comparison : left.Z.CompareTo(right.Z);
		});
		return accepted.ToArray();
	}

	internal static bool TryPlanNaturalPond(
		int[] surfaceHeight,
		int width,
		int length,
		int centerX,
		int centerZ,
		int searchRadius,
		out NaturalPondPlan plan)
	{
		ArgumentNullException.ThrowIfNull(surfaceHeight);
		if (width <= 0)
			throw new ArgumentOutOfRangeException(nameof(width));
		if (length <= 0)
			throw new ArgumentOutOfRangeException(nameof(length));
		if (surfaceHeight.Length != checked(width * length))
			throw new ArgumentException("Surface height data does not match its dimensions.", nameof(surfaceHeight));
		if (searchRadius <= 1)
			throw new ArgumentOutOfRangeException(nameof(searchRadius));

		plan = null!;
		if ((uint)centerX >= (uint)width || (uint)centerZ >= (uint)length)
			return false;

		int centerSurface = surfaceHeight[centerX * length + centerZ];
		if (centerSurface < 0)
			return false;

		int waterLevel = centerSurface + 1;
		int radiusSquared = checked(searchRadius * searchRadius);
		Queue<(int X, int Z)> pending = new();
		HashSet<(int X, int Z)> visited = new();
		List<NaturalPondCell> cells = new();
		pending.Enqueue((centerX, centerZ));

		ReadOnlySpan<(int X, int Z)> neighbors = [(1, 0), (-1, 0), (0, 1), (0, -1)];
		while (pending.Count > 0)
		{
			(int x, int z) = pending.Dequeue();
			if (!visited.Add((x, z)))
				continue;

			if ((uint)x >= (uint)width || (uint)z >= (uint)length)
				return false;

			long deltaX = x - centerX;
			long deltaZ = z - centerZ;
			if (deltaX * deltaX + deltaZ * deltaZ >= radiusSquared)
				return false;

			int height = surfaceHeight[x * length + z];
			if (height < 0)
				return false;
			if (height >= waterLevel)
				continue;
			if (waterLevel - height > MaximumNaturalPondDepth)
				return false;

			cells.Add(new NaturalPondCell(x, z, height));
			foreach ((int offsetX, int offsetZ) in neighbors)
			{
				int neighborX = x + offsetX;
				int neighborZ = z + offsetZ;
				if ((uint)neighborX >= (uint)width || (uint)neighborZ >= (uint)length)
					return false;

				int neighborHeight = surfaceHeight[neighborX * length + neighborZ];
				if (neighborHeight < 0)
					return false;
				if (neighborHeight < waterLevel && !visited.Contains((neighborX, neighborZ)))
					pending.Enqueue((neighborX, neighborZ));
			}
		}

		if (cells.Count < MinimumNaturalPondCells)
			return false;

		cells.Sort(static (left, right) =>
		{
			int comparison = left.X.CompareTo(right.X);
			return comparison != 0 ? comparison : left.Z.CompareTo(right.Z);
		});
		plan = new NaturalPondPlan(waterLevel, cells.ToArray());
		return true;
	}
}
