#if WINDOWS
using System.Numerics;

namespace Voxelgine.States;

internal enum NormalPaintMode
{
	Vector,
	Height,
}

internal readonly record struct AtlasHeightRegionKey(
	string DocumentKey, int X, int Y, int Width, int Height);

internal sealed class AtlasHeightField
{
	private readonly byte[] pixels;

	internal AtlasHeightField(AtlasHeightRegionKey key, byte[] pixels, float strength, long generation)
	{
		Key = key;
		this.pixels = pixels;
		Strength = strength;
		Generation = generation;
	}

	internal AtlasHeightRegionKey Key { get; }
	internal float Strength { get; set; }
	internal long Generation { get; set; }
	internal ReadOnlySpan<byte> Pixels => pixels;
	internal byte Get(int x, int y) => pixels[y * Key.Width + x];
	internal void Set(int x, int y, byte value) => pixels[y * Key.Width + x] = value;
	internal AtlasHeightCacheSnapshot Snapshot() => new(Key, (byte[])pixels.Clone(), Strength, Generation);
}

internal sealed record AtlasHeightCacheSnapshot(
	AtlasHeightRegionKey Key,
	byte[] Pixels,
	float Strength,
	long Generation);

internal readonly record struct AtlasHeightCacheChange(
	AtlasHeightRegionKey Key,
	AtlasHeightCacheSnapshot Before,
	AtlasHeightCacheSnapshot After);

internal sealed class AtlasHeightStore
{
	internal const float DefaultStrength = 4f;
	internal const float MinimumStrength = 0.25f;
	internal const float MaximumStrength = 16f;
	private const double MinimumNormalZ = 1.0 / 64.0;
	private const double MaximumGradient = 8.0;
	private readonly Dictionary<AtlasHeightRegionKey, AtlasHeightField> fields = new();
	private long generation;

	internal AtlasHeightField GetOrCreate(AtlasPaintTarget target)
	{
		AtlasHeightRegionKey key = Key(target);
		if (fields.TryGetValue(key, out AtlasHeightField existing))
			return existing;
		AtlasHeightField field = Reconstruct(target, key, DefaultStrength, checked(++generation));
		fields.Add(key, field);
		return field;
	}

	internal bool TryGet(AtlasPaintTarget target, out AtlasHeightField field) =>
		fields.TryGetValue(Key(target), out field);

	internal AtlasHeightCacheSnapshot Capture(AtlasPaintTarget target) =>
		fields.TryGetValue(Key(target), out AtlasHeightField field) ? field.Snapshot() : null;

	internal AtlasHeightCacheSnapshot Remove(AtlasPaintTarget target)
	{
		AtlasHeightRegionKey key = Key(target);
		if (!fields.Remove(key, out AtlasHeightField field))
			return null;
		return field.Snapshot();
	}

	internal void Restore(AtlasHeightRegionKey key, AtlasHeightCacheSnapshot snapshot)
	{
		if (snapshot == null)
		{
			fields.Remove(key);
			return;
		}
		fields[key] = new AtlasHeightField(
			key, (byte[])snapshot.Pixels.Clone(), snapshot.Strength, snapshot.Generation);
		generation = Math.Max(generation, snapshot.Generation);
	}

	internal void Clear() => fields.Clear();

	internal IReadOnlyList<AtlasPixelDelta> RegenerateNormals(
		AtlasPaintTarget target,
		AtlasHeightField field,
		IEnumerable<(int X, int Y)> pixels)
	{
		Dictionary<(int X, int Y), AtlasPixelDelta> deltas = new();
		foreach ((int x, int y) in pixels.Distinct())
		{
			if ((uint)x >= target.Width || (uint)y >= target.Height)
				continue;
			AtlasPixel next = EncodeNormal(field, x, y);
			int documentX = target.X + x;
			int documentY = target.Y + y;
			AtlasPixel previous = target.Document.GetPixel(documentX, documentY);
			if (!target.Document.SetPixel(documentX, documentY, next))
				continue;
			deltas[(documentX, documentY)] = new AtlasPixelDelta(
				target.Document.Key, documentX, documentY, previous, next);
		}
		field.Generation = checked(++generation);
		return deltas.Values.ToArray();
	}

	internal IReadOnlyList<AtlasPixelDelta> RegenerateAllNormals(
		AtlasPaintTarget target, AtlasHeightField field) =>
		RegenerateNormals(target, field,
			from y in Enumerable.Range(0, target.Height)
			from x in Enumerable.Range(0, target.Width)
			select (x, y));

	internal static AtlasHeightRegionKey Key(AtlasPaintTarget target) => new(
		target.Document.Key, target.X, target.Y, target.Width, target.Height);

	private static AtlasPixel EncodeNormal(AtlasHeightField field, int x, int y)
	{
		int left = Math.Max(0, x - 1);
		int right = Math.Min(field.Key.Width - 1, x + 1);
		int up = Math.Max(0, y - 1);
		int down = Math.Min(field.Key.Height - 1, y + 1);
		float du = (field.Get(right, y) / 255f - field.Get(left, y) / 255f) * 0.5f;
		float dv = (field.Get(x, up) / 255f - field.Get(x, down) / 255f) * 0.5f;
		Vector3 normal = Vector3.Normalize(new Vector3(
			-field.Strength * du,
			-field.Strength * dv,
			1));
		return AtlasPixel.Normal(normal.X, normal.Y);
	}

	private static AtlasHeightField Reconstruct(
		AtlasPaintTarget target, AtlasHeightRegionKey key, float strength, long generation)
	{
		int count = key.Width * key.Height;
		double[] gradientX = new double[count];
		double[] gradientY = new double[count];
		bool anyGradient = false;
		for (int y = 0; y < key.Height; y++)
		for (int x = 0; x < key.Width; x++)
		{
			AtlasPixel pixel = target.Get(x, y);
			double nx = pixel.R / 255.0 * 2.0 - 1.0;
			double ny = pixel.G / 255.0 * 2.0 - 1.0;
			if (Math.Abs(nx) <= 1.1 / 255.0) nx = 0;
			if (Math.Abs(ny) <= 1.1 / 255.0) ny = 0;
			double lengthSquared = nx * nx + ny * ny;
			if (!double.IsFinite(lengthSquared))
				nx = ny = lengthSquared = 0;
			if (lengthSquared > 1)
			{
				double inverse = 1 / Math.Sqrt(lengthSquared);
				nx *= inverse;
				ny *= inverse;
				lengthSquared = 1;
			}
			double nz = Math.Sqrt(Math.Max(0, 1 - lengthSquared));
			double du = Math.Clamp(-nx / (strength * Math.Max(nz, MinimumNormalZ)),
				-MaximumGradient, MaximumGradient);
			double dv = Math.Clamp(-ny / (strength * Math.Max(nz, MinimumNormalZ)),
				-MaximumGradient, MaximumGradient);
			int index = y * key.Width + x;
			gradientX[index] = du;
			gradientY[index] = -dv;
			anyGradient |= Math.Abs(du) > 1e-12 || Math.Abs(dv) > 1e-12;
		}

		byte[] result = new byte[count];
		if (!anyGradient)
		{
			Array.Fill(result, (byte)128);
			return new AtlasHeightField(key, result, strength, generation);
		}

		double[] height = Enumerable.Repeat(0.5, count).ToArray();
		for (int iteration = 0; iteration < 256; iteration++)
		{
			for (int y = 0; y < key.Height; y++)
			for (int x = 0; x < key.Width; x++)
			{
				double sum = 0;
				int neighbors = 0;
				int index = y * key.Width + x;
				if (x > 0)
				{
					int left = index - 1;
					sum += height[left] + (gradientX[left] + gradientX[index]) * 0.5;
					neighbors++;
				}
				if (x + 1 < key.Width)
				{
					int right = index + 1;
					sum += height[right] - (gradientX[index] + gradientX[right]) * 0.5;
					neighbors++;
				}
				if (y > 0)
				{
					int up = index - key.Width;
					sum += height[up] + (gradientY[up] + gradientY[index]) * 0.5;
					neighbors++;
				}
				if (y + 1 < key.Height)
				{
					int down = index + key.Width;
					sum += height[down] - (gradientY[index] + gradientY[down]) * 0.5;
					neighbors++;
				}
				if (neighbors > 0)
					height[index] = sum / neighbors;
			}
			double shift = 0.5 - height.Average();
			for (int index = 0; index < height.Length; index++)
				height[index] += shift;
		}

		for (int index = 0; index < result.Length; index++)
			result[index] = (byte)Math.Clamp(
				(int)Math.Round(Math.Clamp(height[index], 0, 1) * 255,
					MidpointRounding.AwayFromZero), 0, 255);
		return new AtlasHeightField(key, result, strength, generation);
	}
}
#endif
