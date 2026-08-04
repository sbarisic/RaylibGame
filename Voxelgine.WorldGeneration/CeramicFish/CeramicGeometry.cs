namespace Voxelgine.WorldGeneration;

/// <summary>Deterministic coordinate and socket transformations shared by CeramicFish implementations.</summary>
public static class CeramicGeometry
{
	public static CeramicDirection Opposite(CeramicDirection direction) => direction switch
	{
		CeramicDirection.North => CeramicDirection.South,
		CeramicDirection.East => CeramicDirection.West,
		CeramicDirection.South => CeramicDirection.North,
		CeramicDirection.West => CeramicDirection.East,
		_ => throw new ArgumentOutOfRangeException(nameof(direction)),
	};

	public static CeramicCell Offset(CeramicCell cell, CeramicDirection direction) => direction switch
	{
		CeramicDirection.North => cell with { Z = checked(cell.Z - 1) },
		CeramicDirection.East => cell with { X = checked(cell.X + 1) },
		CeramicDirection.South => cell with { Z = checked(cell.Z + 1) },
		CeramicDirection.West => cell with { X = checked(cell.X - 1) },
		_ => throw new ArgumentOutOfRangeException(nameof(direction)),
	};

	public static CeramicDirection Rotate(CeramicDirection direction, CeramicRotation rotation)
	{
		ValidateRotation(rotation);
		return (CeramicDirection)(((int)direction + (int)rotation / 90) & 3);
	}

	public static CeramicSocket GetSocket(
		ICeramicPrefab prefab,
		CeramicDirection worldDirection,
		CeramicRotation rotation)
	{
		ArgumentNullException.ThrowIfNull(prefab);
		ValidateRotation(rotation);
		CeramicDirection sourceDirection =
			(CeramicDirection)(((int)worldDirection - (int)rotation / 90) & 3);
		CeramicSocket source = prefab.Sockets.FirstOrDefault(socket => socket.Direction == sourceDirection)
			?? throw new InvalidDataException(
				$"Prefab '{prefab.Id}' does not define its {sourceDirection} socket.");
		return source with { Direction = worldDirection };
	}

	public static CeramicEntity RotateEntity(
		CeramicEntity entity,
		int sizeX,
		int sizeZ,
		CeramicRotation rotation)
	{
		if (sizeX <= 0 || sizeZ <= 0)
			throw new ArgumentOutOfRangeException(nameof(sizeX), "Prefab dimensions must be positive.");
		ValidateRotation(rotation);
		(int x, int z) = rotation switch
		{
			CeramicRotation.Rot0 => (entity.X, entity.Z),
			CeramicRotation.Rot90CW => (sizeZ - 1 - entity.Z, entity.X),
			CeramicRotation.Rot180CW => (sizeX - 1 - entity.X, sizeZ - 1 - entity.Z),
			CeramicRotation.Rot270CW => (entity.Z, sizeX - 1 - entity.X),
			_ => throw new ArgumentOutOfRangeException(nameof(rotation)),
		};
		return entity with
		{
			X = x,
			Z = z,
			Rotation = Combine(entity.Rotation, rotation),
		};
	}

	public static (int SizeX, int SizeZ) RotateSize(int sizeX, int sizeZ, CeramicRotation rotation)
	{
		if (sizeX <= 0 || sizeZ <= 0)
			throw new ArgumentOutOfRangeException(nameof(sizeX), "Prefab dimensions must be positive.");
		ValidateRotation(rotation);
		return rotation is CeramicRotation.Rot90CW or CeramicRotation.Rot270CW
			? (sizeZ, sizeX)
			: (sizeX, sizeZ);
	}

	private static CeramicRotation Combine(CeramicRotation first, CeramicRotation second)
	{
		ValidateRotation(first);
		int degrees = ((int)first + (int)second) % 360;
		return (CeramicRotation)degrees;
	}

	private static void ValidateRotation(CeramicRotation rotation)
	{
		if (rotation is not (CeramicRotation.Rot0 or CeramicRotation.Rot90CW
			or CeramicRotation.Rot180CW or CeramicRotation.Rot270CW))
			throw new ArgumentOutOfRangeException(nameof(rotation));
	}
}
