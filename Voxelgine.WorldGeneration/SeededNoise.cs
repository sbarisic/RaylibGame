namespace Voxelgine.WorldGeneration;

internal sealed class SeededNoise
{
	private readonly ulong seed;
	internal SeededNoise(int seed) => this.seed = unchecked((ulong)(uint)seed) | 1UL;

	internal double Sample2D(double x, double z)
	{
		int x0 = (int)Math.Floor(x), z0 = (int)Math.Floor(z);
		double tx = Smooth(x - x0), tz = Smooth(z - z0);
		double a = Lerp(Value(x0, z0, 0), Value(x0 + 1, z0, 0), tx);
		double b = Lerp(Value(x0, z0 + 1, 0), Value(x0 + 1, z0 + 1, 0), tx);
		return Lerp(a, b, tz) * 2 - 1;
	}

	internal double Fractal2D(double x, double z, int octaves)
	{
		double value = 0, amplitude = 1, total = 0;
		for (int octave = 0; octave < octaves; octave++)
		{
			value += Sample2D(x, z) * amplitude;
			total += amplitude;
			x *= 2; z *= 2; amplitude *= 0.5;
		}
		return value / total;
	}

	internal double Sample3D(double x, double y, double z)
	{
		int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y), z0 = (int)Math.Floor(z);
		double tx = Smooth(x - x0), ty = Smooth(y - y0), tz = Smooth(z - z0);
		double[] v = new double[8];
		int i = 0;
		for (int dz = 0; dz <= 1; dz++) for (int dy = 0; dy <= 1; dy++) for (int dx = 0; dx <= 1; dx++)
			v[i++] = Value(x0 + dx, z0 + dz, y0 + dy);
		double z00 = Lerp(Lerp(v[0], v[1], tx), Lerp(v[2], v[3], tx), ty);
		double z01 = Lerp(Lerp(v[4], v[5], tx), Lerp(v[6], v[7], tx), ty);
		return Lerp(z00, z01, tz) * 2 - 1;
	}

	internal uint Hash(int x, int z, int salt = 0)
	{
		ulong value = seed ^ unchecked((ulong)(uint)x * 0x9E3779B185EBCA87UL)
			^ unchecked((ulong)(uint)z * 0xC2B2AE3D27D4EB4FUL) ^ unchecked((ulong)(uint)salt);
		value ^= value >> 30; value *= 0xBF58476D1CE4E5B9UL;
		value ^= value >> 27; value *= 0x94D049BB133111EBUL;
		value ^= value >> 31;
		return (uint)value;
	}

	private double Value(int x, int z, int y) => Hash(x ^ y * 92821, z ^ y * 68917, y) / (double)uint.MaxValue;
	private static double Smooth(double value) => value * value * (3 - 2 * value);
	private static double Lerp(double a, double b, double t) => a + (b - a) * t;
}
