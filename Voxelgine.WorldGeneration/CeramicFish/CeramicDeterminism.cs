namespace Voxelgine.WorldGeneration;

/// <summary>Stable seed derivation shared by all CeramicFish implementations.</summary>
public static class CeramicDeterminism
{
	/// <summary>
	/// Derives one retry seed by packing the request seed and attempt ordinal, mixing
	/// in the generator version, and applying the SplitMix64 finalizer. This calculation
	/// is part of the reproducibility contract and must not use runtime Random behavior.
	/// </summary>
	public static ulong DeriveAttemptSeed(int seed, int attemptOrdinal, int generatorVersion)
	{
		if (attemptOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(attemptOrdinal));
		if (generatorVersion <= 0) throw new ArgumentOutOfRangeException(nameof(generatorVersion));
		unchecked
		{
			ulong value = ((ulong)(uint)seed << 32) | (uint)attemptOrdinal;
			value ^= (ulong)(uint)generatorVersion * 0x9E3779B97F4A7C15UL;
			value += 0x9E3779B97F4A7C15UL;
			value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
			value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
			return value ^ (value >> 31);
		}
	}
}
