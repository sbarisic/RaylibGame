namespace Voxelgine.WorldGeneration;

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
}

public sealed record VillageMarkerDescriptor(string Id, string Kind, int X, int Y, int Z);

public enum VillageAdjacencyRelation : byte { Any, Connected, Disconnected }

public sealed record VillageAdjacencyRuleDescriptor(string Id, string FirstPattern, string SecondPattern, int WeightPercent,
	VillageAdjacencyRelation Relation = VillageAdjacencyRelation.Any)
{
	public void Validate()
	{
		if (string.IsNullOrWhiteSpace(Id) || Id.Length > 64 || !ValidPattern(FirstPattern) || !ValidPattern(SecondPattern)
			|| WeightPercent is < 0 or > 100 || !Enum.IsDefined(Relation))
			throw new InvalidDataException("Village adjacency rules require an ID, exact or trailing-* prefab patterns, and a weight from 0-100 percent.");
	}

	public bool Matches(string firstId, string secondId) =>
		MatchesPattern(FirstPattern, firstId) && MatchesPattern(SecondPattern, secondId)
		|| MatchesPattern(FirstPattern, secondId) && MatchesPattern(SecondPattern, firstId);
	public bool AppliesTo(bool connected) => Relation == VillageAdjacencyRelation.Any
		|| connected && Relation == VillageAdjacencyRelation.Connected
		|| !connected && Relation == VillageAdjacencyRelation.Disconnected;

	private static bool ValidPattern(string value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 64
		&& (value == "*" || !value.Contains('*') || value.EndsWith('*') && value.Count(static character => character == '*') == 1);
	private static bool MatchesPattern(string pattern, string value) => pattern == "*"
		|| (pattern.EndsWith('*') ? value.StartsWith(pattern[..^1], StringComparison.Ordinal) : value == pattern);
}

public sealed record VillagePrefabDescriptor(
	string Id,
	int Weight,
	int[] AllowedRotations,
	VillageSocketDescriptor[] Sockets,
	byte[] SupportMask,
	byte[] LoadMask,
	byte[] WalkableMask,
	VillageMarkerDescriptor[] Markers)
{
	public string DisplayName { get; init; } = Id;
	public string[] RotationSignatures { get; init; } = [];
	public bool HasVoxels { get; init; } = true;
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
				|| socket.Openings.Any(static value => value is not (0 or 1))))
			throw new InvalidDataException($"Village prefab '{Id}' sockets are invalid.");
		if (RotationSignatures.Length != 0 && (RotationSignatures.Length != 4
			|| RotationSignatures.Any(static value => value.Length != 64 || !value.All(Uri.IsHexDigit))))
			throw new InvalidDataException($"Village prefab '{Id}' has invalid rotation signatures.");
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
	private readonly VillageAdjacencyRuleDescriptor[] adjacencyRules;

	public VillagePrefabCatalogDescriptor(
		IEnumerable<VillagePrefabDescriptor> prefabs,
		string hash = "",
		IEnumerable<string>? socketSemantics = null,
		string externalEntrySemantic = "road",
		IEnumerable<VillageAdjacencyRuleDescriptor>? adjacencyRules = null)
	{
		ArgumentNullException.ThrowIfNull(prefabs);
		this.prefabs = prefabs.OrderBy(static prefab => prefab.Id, StringComparer.Ordinal).ToArray();
		if (this.prefabs.Length == 0 || this.prefabs.Select(static prefab => prefab.Id).Distinct(StringComparer.Ordinal).Count() != this.prefabs.Length)
			throw new InvalidDataException("Village prefab catalogs require unique modules.");
		foreach (VillagePrefabDescriptor prefab in this.prefabs) prefab.Validate();
		this.socketSemantics = (socketSemantics ?? this.prefabs
			.SelectMany(static prefab => prefab.Sockets)
			.SelectMany(static socket => socket.Types))
			.Distinct(StringComparer.Ordinal)
			.ToArray();
		if (this.socketSemantics.Length == 0
			|| this.socketSemantics.Distinct(StringComparer.Ordinal).Count() != this.socketSemantics.Length
			|| this.socketSemantics.Contains("any", StringComparer.Ordinal)
			|| this.socketSemantics.Contains("closed", StringComparer.Ordinal)
			|| this.socketSemantics.Any(static value => string.IsNullOrWhiteSpace(value) || value.Length > 32)
			|| this.prefabs.SelectMany(static prefab => prefab.Sockets).SelectMany(static socket => socket.Types)
				.Any(value => !this.socketSemantics.Contains(value, StringComparer.Ordinal)))
			throw new InvalidDataException("Village prefab socket semantics must be unique and define every socket value in use.");
		ExternalEntrySemantic = externalEntrySemantic?.Trim() ?? string.Empty;
		if (ExternalEntrySemantic.Length == 0 || !this.socketSemantics.Contains(ExternalEntrySemantic, StringComparer.Ordinal))
			throw new InvalidDataException("The external-entry semantic must be defined by the catalog vocabulary.");
		Hash = hash ?? string.Empty;
		if (Hash.Length != 0 && (Hash.Length != 64 || !Hash.All(Uri.IsHexDigit)))
			throw new InvalidDataException("Village prefab catalog hash is malformed.");
		this.adjacencyRules = adjacencyRules?.ToArray() ?? [];
		if (this.adjacencyRules.Select(static rule => rule.Id).Distinct(StringComparer.Ordinal).Count() != this.adjacencyRules.Length)
			throw new InvalidDataException("Village adjacency rule IDs must be unique.");
		foreach (VillageAdjacencyRuleDescriptor rule in this.adjacencyRules) rule.Validate();
	}

	public IReadOnlyList<VillagePrefabDescriptor> Prefabs => prefabs;
	public IReadOnlyList<string> SocketSemantics => socketSemantics;
	public string ExternalEntrySemantic { get; }
	public string Hash { get; }
	public IReadOnlyList<VillageAdjacencyRuleDescriptor> AdjacencyRules => adjacencyRules;

	public bool HasUsefulConnectedChain()
	{
		VillageSocketDirection[] horizontal =
			[VillageSocketDirection.NegativeZ, VillageSocketDirection.PositiveX, VillageSocketDirection.PositiveZ, VillageSocketDirection.NegativeX];
		Dictionary<string, HashSet<string>> graph = socketSemantics.ToDictionary(static value => value, static _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
		HashSet<string> terminalSemantics = new(StringComparer.Ordinal);
		foreach (VillagePrefabDescriptor prefab in prefabs)
		{
			VillageSocketDescriptor[] faces = horizontal.Select(prefab.Socket).Where(static socket => socket.Types.Length != 0).ToArray();
			if (faces.Length == 1) terminalSemantics.UnionWith(faces[0].Types);
			for (int left = 0; left < faces.Length; left++) for (int right = left + 1; right < faces.Length; right++)
				foreach (string leftType in faces[left].Types) foreach (string rightType in faces[right].Types)
				{
					graph[leftType].Add(rightType);
					graph[rightType].Add(leftType);
				}
		}
		HashSet<string> reached = new(StringComparer.Ordinal) { ExternalEntrySemantic };
		Queue<string> pending = new(); pending.Enqueue(ExternalEntrySemantic);
		while (pending.TryDequeue(out string? semantic))
			foreach (string adjacent in graph[semantic]) if (reached.Add(adjacent)) pending.Enqueue(adjacent);
		// A useful grammar may terminate on the same semantic it entered with. This is the
		// common road case: transit modules expose `road` on two or more faces and a complete
		// roadside building exposes one `road` face. Requiring a semantic rename between those
		// modules incorrectly rejects that simple, unambiguous chain.
		return reached.Any(terminalSemantics.Contains)
			&& graph[ExternalEntrySemantic].Count != 0;
	}
}

public sealed record PlannedVillageModule(
	string PrefabId,
	int Rotation,
	int Floor,
	PlanPoint3 Origin,
	long AttemptSeed);

public sealed record PlannedVillageLayout(
	string VillageId,
	PlannedVillageModule[] Modules,
	int GroundAttempts);

internal readonly record struct VillagePrefabVariant(VillagePrefabDescriptor Prefab, int Rotation, double Weight)
{
	public string Id => $"{Prefab.Id}@{Rotation}";

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
		if (left.Types.Length == 0 || right.Types.Length == 0)
			return left.Types.Length == 0 && right.Types.Length == 0;
		return left.Types.Intersect(right.Types, StringComparer.Ordinal).Any();
	}

	public static bool CreatesConnection(VillageSocketDescriptor left, VillageSocketDescriptor right) =>
		left.Types.Length != 0 && right.Types.Length != 0
		&& left.Types.Intersect(right.Types, StringComparer.Ordinal).Any();
}
