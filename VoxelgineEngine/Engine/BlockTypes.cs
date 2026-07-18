using System.Numerics;

namespace Voxelgine.Engine;

/// <summary>
/// Persisted and networked voxel IDs. Existing numeric values are part of the
/// save and packet compatibility contract and must never be reordered.
/// </summary>
public enum BlockType : ushort
{
	None = 0,
	Stone = 1,
	Dirt = 2,
	StoneBrick = 3,
	Sand = 4,
	Bricks = 5,
	Plank = 6,
	EndStoneBrick = 7,
	Ice = 8,
	Test = 9,
	Leaf = 10,
	Water = 11,
	Glass = 12,
	Glowstone = 13,
	Test2 = 14,
	Grass = 15,
	Wood = 16,
	CraftingTable = 17,
	Barrel = 18,
	Campfire = 19,
	Torch = 20,
	Foliage = 21,
	Gravel = 22,
}

/// <summary>Stable inventory icon identifiers.</summary>
public enum IconType : ushort
{
	None = 0,
	Particle1 = 1,
	Gun = 2,
	Apple = 3,
	Hammer = 4,
	HeartEmpty = 5,
	HeartFull = 6,
	HeartHalf = 7,
	Lava = 8,
	Pickaxe = 9,
	Torch = 10,
}

/// <summary>Renderer-independent block behavior and atlas identity rules.</summary>
public static class BlockInfo
{
	private static readonly bool[] Rendered;
	private static readonly bool[] Opaque;
	private static readonly bool[] Solid;
	private static readonly bool[] Emits;
	private static readonly byte[] Emission;

	static BlockInfo()
	{
		int count = Enum.GetValues<BlockType>().Max(static value => (int)value) + 1;
		Rendered = new bool[count];
		Opaque = new bool[count];
		Solid = new bool[count];
		Emits = new bool[count];
		Emission = new byte[count];

		foreach (BlockType value in Enum.GetValues<BlockType>())
		{
			int index = (int)value;
			Rendered[index] = value != BlockType.None;
			Opaque[index] = value is not (
				BlockType.None or
				BlockType.Campfire or
				BlockType.Torch or
				BlockType.Foliage or
				BlockType.Water or
				BlockType.Glass or
				BlockType.Ice or
				BlockType.Leaf);
			Solid[index] = value is not (
				BlockType.None or
				BlockType.Water or
				BlockType.Foliage or
				BlockType.Torch);
			Emission[index] = value switch
			{
				BlockType.Glowstone => 15,
				BlockType.Campfire => 14,
				BlockType.Torch => 10,
				_ => 0,
			};
			Emits[index] = Emission[index] != 0;
		}
	}

	public static bool IsRendered(BlockType type) => Rendered[Validate(type)];

	public static bool IsOpaque(BlockType type) => Opaque[Validate(type)];

	public static bool IsSolid(BlockType type) => Solid[Validate(type)];

	public static bool IsWater(BlockType type) => type == BlockType.Water;

	public static bool EmitsLight(BlockType type) => Emits[Validate(type)];

	public static byte GetLightEmission(BlockType type) => Emission[Validate(type)];

	public static bool NeedsBackfaceRendering(BlockType type) =>
		type is BlockType.Glass or BlockType.Ice;

	public static bool CustomModel(BlockType type) => type is
		BlockType.Barrel or
		BlockType.Campfire or
		BlockType.Torch or
		BlockType.Foliage;

	/// <summary>Returns the legacy atlas tile ID for a block face.</summary>
	public static int GetBlockID(BlockType type, Vector3 face)
	{
		int blockId = (int)type - 1;
		if (type == BlockType.Grass)
		{
			if (face.Y == 1)
				return 240;
			if (face.Y == 0)
				return 241;
			return 1;
		}

		if (type == BlockType.Wood)
			return face.Y == 0 ? 242 : 243;

		if (type == BlockType.CraftingTable)
		{
			if (face.Y == 1)
				return 244;
			if (face.Y == -1)
				return 247;
			return face.X is 1 or -1 ? 245 : 246;
		}

		return type == BlockType.Barrel ? 8 : blockId;
	}

	private static int Validate(BlockType type)
	{
		int index = (int)type;
		if ((uint)index >= (uint)Rendered.Length)
			throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown block type.");
		return index;
	}
}
