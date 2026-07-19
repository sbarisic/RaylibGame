using Voxelgine.Engine;

namespace Voxelgine.Graphics;

public readonly record struct FogVoxel
{
	public FogVoxel(byte r, byte g, byte b, byte density)
	{
		if (r > density || g > density || b > density)
		{
			throw new ArgumentException(
				"Fog color channels must be premultiplied and cannot exceed density."
			);
		}

		R = r;
		G = g;
		B = b;
		Density = density;
	}

	public byte R { get; }

	public byte G { get; }

	public byte B { get; }

	public byte Density { get; }

	public bool IsEmpty => Density == 0;

	public uint Packed => (uint)(R | G << 8 | B << 16 | Density << 24);

	public static FogVoxel Empty => default;

	public static FogVoxel FromStraight(Rgba32 color, byte density)
	{
		return new FogVoxel(
			Premultiply(color.R, density),
			Premultiply(color.G, density),
			Premultiply(color.B, density),
			density
		);
	}

	public static FogVoxel FromPacked(uint value)
	{
		return new FogVoxel(
			(byte)value,
			(byte)(value >> 8),
			(byte)(value >> 16),
			(byte)(value >> 24)
		);
	}

	private static byte Premultiply(byte channel, byte density)
	{
		return (byte)((channel * density + 127) / byte.MaxValue);
	}
}

public readonly record struct FogChange(
	int X,
	int Y,
	int Z,
	FogVoxel OldValue,
	FogVoxel NewValue,
	long ColumnRevision);

public enum WorldMutationKind : byte
{
	Block,
	Fog,
}

public readonly record struct WorldMutation
{
	private WorldMutation(
		WorldMutationKind kind,
		BlockChange block,
		FogChange fog
	)
	{
		Kind = kind;
		Block = block;
		Fog = fog;
	}

	public WorldMutationKind Kind { get; }

	public BlockChange Block { get; }

	public FogChange Fog { get; }

	public static WorldMutation FromBlock(BlockChange change) => new(
		WorldMutationKind.Block,
		change,
		default
	);

	public static WorldMutation FromFog(FogChange change) => new(
		WorldMutationKind.Fog,
		default,
		change
	);
}
