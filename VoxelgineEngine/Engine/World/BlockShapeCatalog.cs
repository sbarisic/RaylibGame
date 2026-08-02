using System.Numerics;
using Voxelgine.Engine;

namespace Voxelgine.Graphics;

public enum StairFacing : byte
{
	North = 0,
	East = 1,
	South = 2,
	West = 3,
}

public readonly record struct LocalAgentFootprint(float MinimumX, float MinimumZ, float MaximumX, float MaximumZ)
{
	public static LocalAgentFootprint Centered(float width) => new(
		0.5f - width * 0.5f,
		0.5f - width * 0.5f,
		0.5f + width * 0.5f,
		0.5f + width * 0.5f);
}

/// <summary>Canonical local-space geometry for block rendering, raycasts, physics, and navigation.</summary>
public static class BlockShapeCatalog
{
	private static readonly AABB[] Empty = Array.Empty<AABB>();
	private static readonly AABB[] FullCube = { new(Vector3.Zero, Vector3.One) };

	public static bool IsStair(BlockType type) => type is
		BlockType.StoneStairs or BlockType.WoodStairs or BlockType.ConcreteStairs;

	public static byte GetNormalStairState(Vector3 direction)
	{
		if (!float.IsFinite(direction.X) || !float.IsFinite(direction.Z) ||
			MathF.Abs(direction.X) + MathF.Abs(direction.Z) < 1e-6f)
			return (byte)StairFacing.North;
		if (MathF.Abs(direction.X) > MathF.Abs(direction.Z))
			return (byte)(direction.X >= 0 ? StairFacing.East : StairFacing.West);
		return (byte)(direction.Z >= 0 ? StairFacing.South : StairFacing.North);
	}

	public static StairFacing GetStairFacing(BlockValue value)
	{
		if (!IsStair(value.Type))
			throw new ArgumentException($"Block {value.Type} is not a stair.", nameof(value));
		return (StairFacing)(value.State & 0b11);
	}

	public static bool IsUpsideDown(BlockValue value)
	{
		if (!IsStair(value.Type))
			throw new ArgumentException($"Block {value.Type} is not a stair.", nameof(value));
		return (value.State & 0b100) != 0;
	}

	public static IReadOnlyList<AABB> GetCollisionBoxes(BlockValue value)
	{
		if (!BlockInfo.IsSolid(value.Type))
			return Empty;
		if (!IsStair(value.Type))
			return FullCube;

		bool upsideDown = IsUpsideDown(value);
		AABB baseHalf = new(
			new Vector3(0, upsideDown ? 0.5f : 0, 0),
			new Vector3(1, 0.5f, 1));
		(float minX, float minZ, float sizeX, float sizeZ) = GetFacingHalf(GetStairFacing(value));
		AABB facingHalf = new(
			new Vector3(minX, upsideDown ? 0 : 0.5f, minZ),
			new Vector3(sizeX, 0.5f, sizeZ));
		return new[] { baseHalf, facingHalf };
	}

	public static bool TryGetHighestWalkSurface(
		BlockValue value,
		LocalAgentFootprint localAgentFootprint,
		float maximumStepHeight,
		out float localSurfaceHeight)
	{
		localSurfaceHeight = 0;
		if (!float.IsFinite(maximumStepHeight) || maximumStepHeight < 0)
			return false;
		bool found = false;
		foreach (AABB box in GetCollisionBoxes(value))
		{
			if (box.Max.Y > maximumStepHeight + 1e-5f ||
				box.Max.X <= localAgentFootprint.MinimumX || box.Min.X >= localAgentFootprint.MaximumX ||
				box.Max.Z <= localAgentFootprint.MinimumZ || box.Min.Z >= localAgentFootprint.MaximumZ)
				continue;
			localSurfaceHeight = MathF.Max(localSurfaceHeight, box.Max.Y);
			found = true;
		}
		return found;
	}

	private static (float MinX, float MinZ, float SizeX, float SizeZ) GetFacingHalf(StairFacing facing) => facing switch
	{
		StairFacing.North => (0, 0, 1, 0.5f),
		StairFacing.East => (0.5f, 0, 0.5f, 1),
		StairFacing.South => (0, 0.5f, 1, 0.5f),
		StairFacing.West => (0, 0, 0.5f, 1),
		_ => throw new ArgumentOutOfRangeException(nameof(facing)),
	};
}
