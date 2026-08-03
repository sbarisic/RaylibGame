namespace Voxelgine.WorldGeneration;

public enum VillageModuleKind : byte
{
	Outside,
	Road,
	Plaza,
	Yard,
	DefenseWall,
	DefenseCorner,
	Gate,
	Room,
	Hallway,
	Stairs,
	Utility,
	Roof,
}

[Flags]
public enum VillageModuleLevel : byte
{
	Ground = 1,
	Upper = 2,
	Roof = 4,
}

public enum VillageSocketDirection : byte
{
	NegativeZ,
	PositiveX,
	PositiveZ,
	NegativeX,
	PositiveY,
	NegativeY,
}

public sealed record VillageSocketDescriptor(VillageSocketDirection Direction, string[] Types, byte[] Openings)
{
	public const int OpeningMaskLength = 25;

	public bool IsWildcard => Types.Contains("any", StringComparer.Ordinal);
	public bool IsClosed => Types.Contains("closed", StringComparer.Ordinal);
}

public sealed record VillageMarkerDescriptor(string Id, string Kind, int X, int Y, int Z);

public sealed record VillagePrefabDescriptor(
	string Id,
	VillageModuleKind Kind,
	int Weight,
	VillageModuleLevel Levels,
	int[] AllowedRotations,
	VillageSocketDescriptor[] Sockets,
	byte[] SupportMask,
	byte[] LoadMask,
	byte[] WalkableMask,
	VillageMarkerDescriptor[] Markers)
{
	public string DisplayName { get; init; } = Id;
	public const int Width = 5;
	public const int Length = 5;
	public const int Height = 5;
	public const int MaskLength = Width * Length;

	public void Validate()
	{
		if (string.IsNullOrWhiteSpace(Id) || Id.Length > 64)
			throw new InvalidDataException("Village prefab IDs must contain 1-64 characters.");
		if (string.IsNullOrWhiteSpace(DisplayName) || DisplayName.Length > 96)
			throw new InvalidDataException($"Village prefab '{Id}' must have a display name containing 1-96 characters.");
		if (!Enum.IsDefined(Kind) || Levels == 0 || (Levels & ~(VillageModuleLevel.Ground | VillageModuleLevel.Upper | VillageModuleLevel.Roof)) != 0)
			throw new InvalidDataException($"Village prefab '{Id}' has invalid kind or level flags.");
		if (Weight is < 1 or > 1_000_000)
			throw new InvalidDataException($"Village prefab '{Id}' has an invalid weight.");
		if (AllowedRotations.Length == 0 || AllowedRotations.Any(static value => value is not (0 or 90 or 180 or 270))
			|| AllowedRotations.Distinct().Count() != AllowedRotations.Length)
			throw new InvalidDataException($"Village prefab '{Id}' has invalid rotations.");
		if (SupportMask.Length != MaskLength || LoadMask.Length != MaskLength || WalkableMask.Length != MaskLength
			|| SupportMask.Concat(LoadMask).Concat(WalkableMask).Any(static value => value is not (0 or 1)))
			throw new InvalidDataException($"Village prefab '{Id}' masks must be canonical 5x5 bit rasters.");
		if (Sockets.Length != Enum.GetValues<VillageSocketDirection>().Length
			|| Sockets.GroupBy(static socket => socket.Direction).Any(static group => group.Count() != 1)
			|| Sockets.Any(static socket => socket.Types is null
				|| socket.Types.Distinct(StringComparer.Ordinal).Count() != socket.Types.Length
				|| socket.Types.Any(static value => string.IsNullOrWhiteSpace(value) || value.Length > 32)
				|| socket.Openings is null || socket.Openings.Length != VillageSocketDescriptor.OpeningMaskLength
				|| socket.Openings.Any(static value => value is not (0 or 1))
				|| socket.Types.Contains("any", StringComparer.Ordinal) && socket.Types.Length != 1
				|| socket.Types.Contains("closed", StringComparer.Ordinal) && socket.Types.Length != 1))
			throw new InvalidDataException($"Village prefab '{Id}' sockets are invalid.");
		if (Markers.Select(static marker => marker.Id).Distinct(StringComparer.Ordinal).Count() != Markers.Length
			|| Markers.Any(static marker => string.IsNullOrWhiteSpace(marker.Id) || string.IsNullOrWhiteSpace(marker.Kind)
				|| (uint)marker.X >= Width || (uint)marker.Y >= Height || (uint)marker.Z >= Length))
			throw new InvalidDataException($"Village prefab '{Id}' markers are invalid.");
	}

	public VillageSocketDescriptor Socket(VillageSocketDirection direction) =>
		Sockets.First(socket => socket.Direction == direction);
}

public sealed class VillagePrefabCatalogDescriptor
{
	private readonly VillagePrefabDescriptor[] prefabs;
	private readonly string[] socketSemantics;

	public VillagePrefabCatalogDescriptor(
		IEnumerable<VillagePrefabDescriptor> prefabs,
		string hash = "",
		IEnumerable<string>? socketSemantics = null)
	{
		ArgumentNullException.ThrowIfNull(prefabs);
		this.prefabs = prefabs.OrderBy(static prefab => prefab.Id, StringComparer.Ordinal).ToArray();
		if (this.prefabs.Length == 0 || this.prefabs.Select(static prefab => prefab.Id).Distinct(StringComparer.Ordinal).Count() != this.prefabs.Length)
			throw new InvalidDataException("Village prefab catalogs require unique modules.");
		foreach (VillagePrefabDescriptor prefab in this.prefabs) prefab.Validate();
		this.socketSemantics = (socketSemantics ?? this.prefabs
			.SelectMany(static prefab => prefab.Sockets)
			.SelectMany(static socket => socket.Types)
			.Append("closed")
			.Append("any"))
			.Distinct(StringComparer.Ordinal)
			.ToArray();
		if (this.socketSemantics.Length < 2
			|| this.socketSemantics.Distinct(StringComparer.Ordinal).Count() != this.socketSemantics.Length
			|| !this.socketSemantics.Contains("closed", StringComparer.Ordinal)
			|| !this.socketSemantics.Contains("any", StringComparer.Ordinal)
			|| this.socketSemantics.Any(static value => string.IsNullOrWhiteSpace(value) || value.Length > 32)
			|| this.prefabs.SelectMany(static prefab => prefab.Sockets).SelectMany(static socket => socket.Types)
				.Any(value => !this.socketSemantics.Contains(value, StringComparer.Ordinal)))
			throw new InvalidDataException("Village prefab socket semantics must be unique, include 'closed' and 'any', and define every socket value in use.");
		Hash = hash ?? string.Empty;
		if (Hash.Length != 0 && (Hash.Length != 64 || !Hash.All(Uri.IsHexDigit)))
			throw new InvalidDataException("Village prefab catalog hash is malformed.");
	}

	public IReadOnlyList<VillagePrefabDescriptor> Prefabs => prefabs;
	public IReadOnlyList<string> SocketSemantics => socketSemantics;
	public string Hash { get; }
}

public sealed record PlannedVillageModule(
	string PrefabId,
	int Rotation,
	int Floor,
	int ComponentId,
	PlanPoint3 Origin,
	VillageModuleKind Kind,
	long AttemptSeed);

public sealed record PlannedVillageLayout(
	string VillageId,
	PlannedVillageModule[] Modules,
	PlanPoint3[] InternalRoadCells,
	int GroundAttempts);

internal readonly record struct VillagePrefabVariant(VillagePrefabDescriptor Prefab, int Rotation)
{
	public string Id => $"{Prefab.Id}@{Rotation}";
	public VillageModuleKind Kind => Prefab.Kind;
	public int Weight => Prefab.Weight;

	public VillageSocketDescriptor Socket(VillageSocketDirection worldDirection)
	{
		if (worldDirection is VillageSocketDirection.PositiveY or VillageSocketDirection.NegativeY)
			return Prefab.Socket(worldDirection);
		int direction = ((int)worldDirection - Rotation / 90) & 3;
		VillageSocketDescriptor source = Prefab.Socket((VillageSocketDirection)direction);
		return source with { Direction = worldDirection, Openings = RotateOpeningMask(source.Openings, worldDirection, Rotation) };
	}

	private static byte[] RotateOpeningMask(byte[] source, VillageSocketDirection direction, int rotation)
	{
		if (rotation == 0 || direction is not (VillageSocketDirection.PositiveY or VillageSocketDirection.NegativeY)) return source;
		byte[] result = new byte[source.Length];
		for (int z = 0; z < 5; z++) for (int x = 0; x < 5; x++)
		{
			(int worldX, int worldZ) = rotation switch
			{
				90 => (4 - z, x), 180 => (4 - x, 4 - z), 270 => (z, 4 - x),
				_ => throw new ArgumentOutOfRangeException(nameof(rotation)),
			};
			result[worldZ * 5 + worldX] = source[z * 5 + x];
		}
		return result;
	}
}

public static class VillageSocketCompatibility
{
	public static VillageSocketDirection Opposite(VillageSocketDirection direction) => direction switch
	{
		VillageSocketDirection.PositiveX => VillageSocketDirection.NegativeX,
		VillageSocketDirection.NegativeX => VillageSocketDirection.PositiveX,
		VillageSocketDirection.PositiveY => VillageSocketDirection.NegativeY,
		VillageSocketDirection.NegativeY => VillageSocketDirection.PositiveY,
		VillageSocketDirection.PositiveZ => VillageSocketDirection.NegativeZ,
		VillageSocketDirection.NegativeZ => VillageSocketDirection.PositiveZ,
		_ => throw new ArgumentOutOfRangeException(nameof(direction)),
	};

	public static string Label(VillageSocketDirection direction) => direction switch
	{
		VillageSocketDirection.PositiveX => "+X",
		VillageSocketDirection.NegativeX => "-X",
		VillageSocketDirection.PositiveY => "+Y",
		VillageSocketDirection.NegativeY => "-Y",
		VillageSocketDirection.PositiveZ => "+Z",
		VillageSocketDirection.NegativeZ => "-Z",
		_ => throw new ArgumentOutOfRangeException(nameof(direction)),
	};

	public static bool Matches(VillageSocketDescriptor left, VillageSocketDescriptor right)
	{
		if (left.Types.Length == 0 || right.Types.Length == 0) return false;
		if (left.IsClosed || right.IsClosed) return left.IsClosed && right.IsClosed;
		return left.IsWildcard || right.IsWildcard
			|| left.Types.Intersect(right.Types, StringComparer.Ordinal).Any();
	}
}
