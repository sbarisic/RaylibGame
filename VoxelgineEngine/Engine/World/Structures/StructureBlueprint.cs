using System.Collections.ObjectModel;
using System.Text.Json;
using System.Text.RegularExpressions;
using Voxelgine.Graphics;

namespace Voxelgine.Engine.World.Structures;

public readonly record struct BlockCoordinate(int X, int Y, int Z) : IComparable<BlockCoordinate>
{
	public int CompareTo(BlockCoordinate other)
	{
		int comparison = X.CompareTo(other.X);
		if (comparison != 0)
			return comparison;
		comparison = Y.CompareTo(other.Y);
		return comparison != 0 ? comparison : Z.CompareTo(other.Z);
	}

	public static BlockCoordinate operator +(BlockCoordinate left, BlockCoordinate right) =>
		new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);

	public static BlockCoordinate operator -(BlockCoordinate left, BlockCoordinate right) =>
		new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
}

public enum StructureRole : byte
{
	Shelter,
	Relay,
	GravityAnchor,
	Shaft,
	Support,
}

public enum StructureMarkerKind : byte
{
	PlayerSpawn,
	NpcSpawn,
	Door,
	Loot,
	MachineFunction,
	RoadConnector,
	ConduitConnector,
	ShaftConnector,
	Effect,
}

public enum StructureConnectorKind : byte
{
	Road,
	Conduit,
	Shaft,
}

public readonly record struct StructureMarker(
	string Id,
	StructureMarkerKind Kind,
	BlockCoordinate Position,
	BlockType? ExpectedBlock,
	string Data);

public readonly record struct StructureConnector(
	string Id,
	StructureConnectorKind Kind,
	BlockCoordinate Position,
	BlockCoordinate Direction);

public readonly record struct StructureFogVolume(
	string Id,
	BlockCoordinate Minimum,
	BlockCoordinate Size,
	FogVoxel Fog);

public sealed class StructureBlueprint
{
	internal StructureBlueprint(
		string id,
		StructureRole role,
		bool critical,
		BlockCoordinate size,
		BlockCoordinate anchor,
		int[] rotations,
		char[] cells,
		IReadOnlyDictionary<char, BlockType> palette,
		StructureMarker[] markers,
		StructureConnector[] connectors,
		StructureFogVolume[] fogVolumes)
	{
		Id = id;
		Role = role;
		Critical = critical;
		Size = size;
		Anchor = anchor;
		AllowedRotations = rotations;
		Cells = cells;
		Palette = palette;
		Markers = markers;
		Connectors = connectors;
		FogVolumes = fogVolumes;
	}

	public string Id { get; }
	public StructureRole Role { get; }
	public bool Critical { get; }
	public BlockCoordinate Size { get; }
	public BlockCoordinate Anchor { get; }
	public IReadOnlyList<int> AllowedRotations { get; }
	public IReadOnlyList<StructureMarker> Markers { get; }
	public IReadOnlyList<StructureConnector> Connectors { get; }
	public IReadOnlyList<StructureFogVolume> FogVolumes { get; }
	internal char[] Cells { get; }
	internal IReadOnlyDictionary<char, BlockType> Palette { get; }

	public char GetCell(int x, int y, int z) => Cells[(y * Size.Z + z) * Size.X + x];
}

public sealed class StructureBlueprintCatalog
{
	private readonly StructureBlueprint[] blueprints;
	private readonly IReadOnlyDictionary<string, StructureBlueprint> byId;

	private StructureBlueprintCatalog(StructureBlueprint[] blueprints)
	{
		this.blueprints = blueprints;
		byId = new ReadOnlyDictionary<string, StructureBlueprint>(
			blueprints.ToDictionary(static blueprint => blueprint.Id, StringComparer.Ordinal));
	}

	public IReadOnlyList<StructureBlueprint> Blueprints => blueprints;

	public StructureBlueprint Get(string id) => byId.TryGetValue(id, out StructureBlueprint blueprint)
		? blueprint
		: throw new KeyNotFoundException($"Unknown structure blueprint '{id}'.");

	public IReadOnlyList<StructureBlueprint> ForRole(StructureRole role) =>
		blueprints.Where(blueprint => blueprint.Role == role).ToArray();

	public static StructureBlueprintCatalog LoadDirectory(string directory)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directory);
		if (!Directory.Exists(directory))
			throw new DirectoryNotFoundException($"Structure blueprint directory was not found: {directory}");

		StructureBlueprint[] loaded = Directory.GetFiles(directory, "*.json", SearchOption.TopDirectoryOnly)
			.OrderBy(static path => path, StringComparer.Ordinal)
			.Select(StructureBlueprintLoader.Load)
			.ToArray();
		if (loaded.Length == 0)
			throw new InvalidDataException($"No structure blueprints were found in {directory}.");
		if (loaded.Select(static blueprint => blueprint.Id).Distinct(StringComparer.Ordinal).Count() != loaded.Length)
			throw new InvalidDataException("Structure blueprint IDs must be unique.");
		return new StructureBlueprintCatalog(loaded);
	}
}

internal static class StructureBlueprintLoader
{
	private const int FormatVersion = 1;
	private const int MarkerDataVersion = 1;
	private const int MaximumVolume = 131_072;
	private static readonly Regex ValidId = new("^[a-z][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);

	public static StructureBlueprint Load(string path)
	{
		using FileStream stream = File.OpenRead(path);
		using JsonDocument document = JsonDocument.Parse(stream);
		JsonElement root = document.RootElement;
		RequireVersion(root, "formatVersion", FormatVersion);
		RequireVersion(root, "markerDataVersion", MarkerDataVersion);
		string id = ReadId(root, "id");
		StructureRole role = ReadEnum<StructureRole>(root, "role");
		bool critical = root.TryGetProperty("critical", out JsonElement criticalElement) && criticalElement.GetBoolean();
		BlockCoordinate size = ReadCoordinate(root.GetProperty("size"), "size");
		ValidateSize(size);
		BlockCoordinate anchor = ReadCoordinate(root.GetProperty("anchor"), "anchor");
		if (!Inside(anchor, size))
			throw Invalid(path, "anchor must be inside blueprint bounds");

		int paletteCount = root.GetProperty("palette").EnumerateObject().Count();
		int markerCount = root.TryGetProperty("markers", out JsonElement markerElements) ? markerElements.GetArrayLength() : 0;
		int connectorCount = root.TryGetProperty("connectors", out JsonElement connectorElements) ? connectorElements.GetArrayLength() : 0;
		int fogCount = root.TryGetProperty("fogVolumes", out JsonElement fogElements) ? fogElements.GetArrayLength() : 0;
		if (paletteCount > 128 || markerCount > 512 || connectorCount > 128 || fogCount > 64)
			throw Invalid(path, "blueprint collection limit exceeded");

		Dictionary<char, BlockType> palette = ReadPalette(root.GetProperty("palette"), path);
		char[] cells = ReadLayers(root.GetProperty("layers"), size, palette, path);
		int[] rotations = ReadRotations(root, path);
		StructureMarker[] markers = ReadMarkers(markerElements, size, cells, palette, path);
		StructureConnector[] connectors = ReadConnectors(connectorElements, size, cells, palette, path);
		StructureFogVolume[] fogVolumes = ReadFogVolumes(fogElements, size, path);
		return new StructureBlueprint(id, role, critical, size, anchor, rotations, cells,
			new ReadOnlyDictionary<char, BlockType>(palette), markers, connectors, fogVolumes);
	}

	private static Dictionary<char, BlockType> ReadPalette(JsonElement element, string path)
	{
		Dictionary<char, BlockType> palette = new();
		foreach (JsonProperty property in element.EnumerateObject())
		{
			if (property.Name.Length != 1 || property.Name[0] is '.' or '_')
				throw Invalid(path, $"palette key '{property.Name}' must be one character other than '.' or '_'");
			if (!Enum.TryParse(property.Value.GetString(), ignoreCase: false, out BlockType block) || block == BlockType.None)
				throw Invalid(path, $"palette entry '{property.Name}' has an unknown or empty block");
			palette.Add(property.Name[0], block);
		}
		return palette;
	}

	private static char[] ReadLayers(JsonElement element, BlockCoordinate size, Dictionary<char, BlockType> palette, string path)
	{
		if (element.GetArrayLength() != size.Y)
			throw Invalid(path, "layer count does not match size Y");
		char[] cells = new char[checked(size.X * size.Y * size.Z)];
		int index = 0;
		foreach (JsonElement layer in element.EnumerateArray())
		{
			if (layer.GetArrayLength() != size.Z)
				throw Invalid(path, "layer row count does not match size Z");
			foreach (JsonElement rowElement in layer.EnumerateArray())
			{
				string row = rowElement.GetString();
				if (row == null || row.Length != size.X)
					throw Invalid(path, "layer row width does not match size X");
				foreach (char cell in row)
				{
					if (cell is not ('.' or '_') && !palette.ContainsKey(cell))
						throw Invalid(path, $"layer uses undefined palette key '{cell}'");
					cells[index++] = cell;
				}
			}
		}
		return cells;
	}

	private static int[] ReadRotations(JsonElement root, string path)
	{
		int[] rotations = root.GetProperty("rotations").EnumerateArray().Select(static value => value.GetInt32()).ToArray();
		if (rotations.Length == 0 || rotations.Any(static rotation => rotation is not (0 or 90 or 180 or 270)))
			throw Invalid(path, "rotations must contain values from 0, 90, 180, 270");
		return rotations.Distinct().OrderBy(static rotation => rotation).ToArray();
	}

	private static StructureMarker[] ReadMarkers(JsonElement element, BlockCoordinate size, char[] cells, Dictionary<char, BlockType> palette, string path)
	{
		if (element.ValueKind == JsonValueKind.Undefined)
			return Array.Empty<StructureMarker>();
		List<StructureMarker> result = new();
		HashSet<string> ids = new(StringComparer.Ordinal);
		foreach (JsonElement item in element.EnumerateArray())
		{
			string id = ReadId(item, "id");
			if (!ids.Add(id))
				throw Invalid(path, $"duplicate marker ID '{id}'");
			StructureMarkerKind kind = ReadEnum<StructureMarkerKind>(item, "kind");
			BlockCoordinate position = ReadCoordinate(item.GetProperty("position"), "marker position");
			if (!Inside(position, size))
				throw Invalid(path, $"marker '{id}' is outside blueprint bounds");
			BlockType? expected = null;
			if (item.TryGetProperty("expectedBlock", out JsonElement expectedElement))
			{
				if (!Enum.TryParse(expectedElement.GetString(), ignoreCase: false, out BlockType value))
					throw Invalid(path, $"marker '{id}' has an unknown expected block");
				expected = value;
			}
			if (expected != null)
			{
				char cell = cells[(position.Y * size.Z + position.Z) * size.X + position.X];
				if (!palette.TryGetValue(cell, out BlockType actual) || actual != expected)
					throw Invalid(path, $"marker '{id}' expected block does not match its palette cell");
			}
			if (kind == StructureMarkerKind.MachineFunction)
			{
				if (expected == null || !InfrastructureBlockCatalog.TryGet(expected.Value, out InfrastructureBlockDefinition definition) || definition.Function == null)
					throw Invalid(path, $"machine marker '{id}' must expect an infrastructure function block");
			}
			string data = item.TryGetProperty("data", out JsonElement dataElement) ? dataElement.GetRawText() : string.Empty;
			result.Add(new StructureMarker(id, kind, position, expected, data));
		}
		return result.OrderBy(static marker => marker.Id, StringComparer.Ordinal).ToArray();
	}

	private static StructureConnector[] ReadConnectors(
		JsonElement element,
		BlockCoordinate size,
		char[] cells,
		Dictionary<char, BlockType> palette,
		string path)
	{
		if (element.ValueKind == JsonValueKind.Undefined)
			return Array.Empty<StructureConnector>();
		List<StructureConnector> result = new();
		HashSet<string> ids = new(StringComparer.Ordinal);
		foreach (JsonElement item in element.EnumerateArray())
		{
			string id = ReadId(item, "id");
			if (!ids.Add(id))
				throw Invalid(path, $"duplicate connector ID '{id}'");
			BlockCoordinate position = ReadCoordinate(item.GetProperty("position"), "connector position");
			BlockCoordinate direction = ReadCoordinate(item.GetProperty("direction"), "connector direction");
			if (!Inside(position, size) || Math.Abs(direction.X) + Math.Abs(direction.Y) + Math.Abs(direction.Z) != 1)
				throw Invalid(path, $"connector '{id}' has invalid position or direction");
			StructureConnectorKind kind = ReadEnum<StructureConnectorKind>(item, "kind");
			ValidateConnectorExit(id, kind, position, direction, size, cells, palette, path);
			result.Add(new StructureConnector(id, kind, position, direction));
		}
		return result.OrderBy(static connector => connector.Id, StringComparer.Ordinal).ToArray();
	}

	private static void ValidateConnectorExit(
		string id,
		StructureConnectorKind kind,
		BlockCoordinate position,
		BlockCoordinate direction,
		BlockCoordinate size,
		char[] cells,
		Dictionary<char, BlockType> palette,
		string path)
	{
		BlockCoordinate exit = position + direction;
		if (Inside(exit, size) && IsAuthoredSolid(exit, size, cells, palette, kind))
			throw Invalid(path, $"connector '{id}' exits directly into an unrelated authored solid");
		if (kind == StructureConnectorKind.Road)
		{
			BlockCoordinate head = exit + new BlockCoordinate(0, 1, 0);
			if (Inside(head, size) && IsAuthoredSolid(head, size, cells, palette, kind))
				throw Invalid(path, $"road connector '{id}' lacks two-block-high authored clearance");
		}
	}

	private static bool IsAuthoredSolid(
		BlockCoordinate position,
		BlockCoordinate size,
		char[] cells,
		Dictionary<char, BlockType> palette,
		StructureConnectorKind kind)
	{
		char cell = cells[(position.Y * size.Z + position.Z) * size.X + position.X];
		if (cell is '.' or '_')
			return false;
		BlockType block = palette[cell];
		return kind != StructureConnectorKind.Conduit || block != BlockType.PowerConduit;
	}

	private static StructureFogVolume[] ReadFogVolumes(JsonElement element, BlockCoordinate size, string path)
	{
		if (element.ValueKind == JsonValueKind.Undefined)
			return Array.Empty<StructureFogVolume>();
		List<StructureFogVolume> result = new();
		HashSet<string> ids = new(StringComparer.Ordinal);
		foreach (JsonElement item in element.EnumerateArray())
		{
			string id = ReadId(item, "id");
			if (!ids.Add(id))
				throw Invalid(path, $"duplicate fog volume ID '{id}'");
			BlockCoordinate minimum = ReadCoordinate(item.GetProperty("minimum"), "fog minimum");
			BlockCoordinate volumeSize = ReadCoordinate(item.GetProperty("size"), "fog size");
			if (volumeSize.X <= 0 || volumeSize.Y <= 0 || volumeSize.Z <= 0 || !Inside(minimum, size) ||
				minimum.X + volumeSize.X > size.X || minimum.Y + volumeSize.Y > size.Y || minimum.Z + volumeSize.Z > size.Z)
				throw Invalid(path, $"fog volume '{id}' is outside blueprint bounds");
			byte density = checked((byte)item.GetProperty("density").GetInt32());
			JsonElement color = item.GetProperty("color");
			byte red = checked((byte)color[0].GetInt32());
			byte green = checked((byte)color[1].GetInt32());
			byte blue = checked((byte)color[2].GetInt32());
			result.Add(new StructureFogVolume(id, minimum, volumeSize,
				FogVoxel.FromStraight(new Rgba32(red, green, blue), density)));
		}
		return result.OrderBy(static fog => fog.Minimum).ThenBy(static fog => fog.Id, StringComparer.Ordinal).ToArray();
	}

	private static T ReadEnum<T>(JsonElement root, string name) where T : struct, Enum =>
		Enum.TryParse(root.GetProperty(name).GetString(), ignoreCase: true, out T value)
			? value
			: throw new InvalidDataException($"Unknown {name} value.");

	private static string ReadId(JsonElement root, string name)
	{
		string value = root.GetProperty(name).GetString();
		if (value == null || !ValidId.IsMatch(value))
			throw new InvalidDataException($"{name} must match {ValidId}.");
		return value;
	}

	private static BlockCoordinate ReadCoordinate(JsonElement element, string name)
	{
		if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != 3)
			throw new InvalidDataException($"{name} must be an integer array of length three.");
		return new BlockCoordinate(element[0].GetInt32(), element[1].GetInt32(), element[2].GetInt32());
	}

	private static void ValidateSize(BlockCoordinate size)
	{
		if (size.X is < 1 or > 64 || size.Y is < 1 or > 64 || size.Z is < 1 or > 64 || (long)size.X * size.Y * size.Z > MaximumVolume)
			throw new InvalidDataException("Blueprint dimensions exceed the configured limits.");
	}

	private static bool Inside(BlockCoordinate position, BlockCoordinate size) =>
		position.X >= 0 && position.Y >= 0 && position.Z >= 0 &&
		position.X < size.X && position.Y < size.Y && position.Z < size.Z;

	private static void RequireVersion(JsonElement root, string property, int expected)
	{
		int actual = root.GetProperty(property).GetInt32();
		if (actual != expected)
			throw new InvalidDataException($"Unsupported {property} {actual}; expected {expected}.");
	}

	private static InvalidDataException Invalid(string path, string message) =>
		new($"Invalid structure blueprint '{path}': {message}.");
}
