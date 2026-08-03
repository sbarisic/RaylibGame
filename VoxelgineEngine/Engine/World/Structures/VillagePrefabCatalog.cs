using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Voxelgine.Graphics;
using Voxelgine.WorldGeneration;

namespace Voxelgine.Engine.World.Structures;

public sealed class VillagePrefab
{
	public VillagePrefab(VillagePrefabDescriptor descriptor, IEnumerable<BlockValue> cells)
	{
		ArgumentNullException.ThrowIfNull(descriptor); ArgumentNullException.ThrowIfNull(cells); descriptor.Validate();
		BlockValue[] values = cells.ToArray();
		if (values.Length != VillagePrefabDescriptor.Width * VillagePrefabDescriptor.Height * VillagePrefabDescriptor.Length)
			throw new ArgumentException("Village prefab voxel data must be exactly 5x5x5.", nameof(cells));
		Descriptor = descriptor with
		{
			Sockets = descriptor.Sockets.Select(socket => socket with { Openings = DeriveOpeningMask(socket.Direction, values) }).ToArray()
		};
		Descriptor.Validate();
		Cells = values;
	}

	public VillagePrefabDescriptor Descriptor { get; }
	internal BlockValue[] Cells { get; }
	public BlockValue GetCell(int x, int y, int z) => Cells[(y * VillagePrefabDescriptor.Length + z) * VillagePrefabDescriptor.Width + x];

	public static byte[] DeriveOpeningMask(VillageSocketDirection direction, IReadOnlyList<BlockValue> cells)
	{
		byte[] mask = new byte[VillageSocketDescriptor.OpeningMaskLength];
		for (int v = 0; v < 5; v++) for (int u = 0; u < 5; u++)
		{
			(int x, int y, int z) = direction switch
			{
				VillageSocketDirection.PositiveX => (4, v, u), VillageSocketDirection.NegativeX => (0, v, 4 - u),
				VillageSocketDirection.PositiveZ => (4 - u, v, 4), VillageSocketDirection.NegativeZ => (u, v, 0),
				VillageSocketDirection.PositiveY => (u, 4, v), VillageSocketDirection.NegativeY => (u, 0, 4 - v),
				_ => throw new ArgumentOutOfRangeException(nameof(direction)),
			};
			mask[v * 5 + u] = cells[(y * 5 + z) * 5 + x].Type == BlockType.None ? (byte)1 : (byte)0;
		}
		return mask;
	}
}

public sealed class VillagePrefabCatalog
{
	private const int FormatVersion = 2;
	public static readonly string[] DefaultSocketSemantics = ["closed", "open", "road", "door", "wall", "gate", "stairs", "any"];
	private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() },
	};
	private readonly Dictionary<string, VillagePrefab> byId;

	private VillagePrefabCatalog(string path, VillagePrefab[] prefabs, string[] socketSemantics, string hash)
	{
		Path = path; Prefabs = prefabs; SocketSemantics = socketSemantics; Hash = hash;
		byId = prefabs.ToDictionary(static prefab => prefab.Descriptor.Id, StringComparer.Ordinal);
		Descriptor = new(prefabs.Select(static prefab => prefab.Descriptor), hash, socketSemantics);
	}

	public string Path { get; }
	public string Hash { get; }
	public IReadOnlyList<VillagePrefab> Prefabs { get; }
	public IReadOnlyList<string> SocketSemantics { get; }
	public VillagePrefabCatalogDescriptor Descriptor { get; }
	public VillagePrefab Get(string id) => byId.TryGetValue(id, out VillagePrefab prefab)
		? prefab : throw new KeyNotFoundException($"Unknown village prefab '{id}'.");

	public static VillagePrefabCatalog Load(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		byte[] bytes = File.ReadAllBytes(path);
		CatalogDocument document = JsonSerializer.Deserialize<CatalogDocument>(bytes, Options)
			?? throw new InvalidDataException("Village prefab catalog is empty.");
		if (document.FormatVersion != FormatVersion || document.Width != 5 || document.Height != 5 || document.Length != 5
			|| document.Modules is null || document.Modules.Length == 0)
			throw new InvalidDataException("Village prefab catalog format is unsupported or empty.");
		VillagePrefab[] prefabs = document.Modules.Select(Parse).OrderBy(static prefab => prefab.Descriptor.Id, StringComparer.Ordinal).ToArray();
		if (prefabs.Select(static prefab => prefab.Descriptor.Id).Distinct(StringComparer.Ordinal).Count() != prefabs.Length)
			throw new InvalidDataException("Village prefab IDs must be unique.");
		string[] socketSemantics = NormalizeSocketSemantics(document.SocketSemantics, prefabs);
		_ = new VillagePrefabCatalogDescriptor(prefabs.Select(static prefab => prefab.Descriptor), socketSemantics: socketSemantics);
		string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
		return new(System.IO.Path.GetFullPath(path), prefabs, socketSemantics, hash);
	}

	public static void Save(string path, IEnumerable<VillagePrefab> prefabs, IEnumerable<string> socketSemantics = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path); ArgumentNullException.ThrowIfNull(prefabs);
		VillagePrefab[] values = prefabs.OrderBy(static prefab => prefab.Descriptor.Id, StringComparer.Ordinal).ToArray();
		if (values.Length == 0) throw new InvalidDataException("Village prefab catalogs cannot be empty.");
		string[] semantics = NormalizeSocketSemantics(socketSemantics, values);
		_ = new VillagePrefabCatalogDescriptor(values.Select(static prefab => prefab.Descriptor), socketSemantics: semantics);
		CatalogDocument document = new(FormatVersion, 5, 5, 5, semantics, values.Select(ToDocument).ToArray());
		byte[] json = JsonSerializer.SerializeToUtf8Bytes(document, Options);
		string fullPath = System.IO.Path.GetFullPath(path); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);
		string temporary = fullPath + $".tmp-{Guid.NewGuid():N}";
		try
		{
			File.WriteAllBytes(temporary, json);
			_ = Load(temporary);
			File.Move(temporary, fullPath, overwrite: true);
		}
		finally { if (File.Exists(temporary)) File.Delete(temporary); }
	}

	public static IReadOnlyList<VillagePrefabCatalog> SaveSynchronized(
		IEnumerable<string> paths,
		IEnumerable<VillagePrefab> prefabs,
		IEnumerable<string> socketSemantics)
	{
		ArgumentNullException.ThrowIfNull(paths);
		VillagePrefab[] modules = prefabs?.ToArray() ?? throw new ArgumentNullException(nameof(prefabs));
		string[] semantics = socketSemantics?.ToArray() ?? throw new ArgumentNullException(nameof(socketSemantics));
		string[] targets = paths.Where(static path => !string.IsNullOrWhiteSpace(path)).Select(System.IO.Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		if (targets.Length == 0) throw new InvalidDataException("No village prefab catalog save target was resolved.");
		Dictionary<string, byte[]> originals = targets.ToDictionary(static path => path, static path => File.Exists(path) ? File.ReadAllBytes(path) : null, StringComparer.OrdinalIgnoreCase);
		List<string> replaced = [];
		try
		{
			foreach (string target in targets) { Save(target, modules, semantics); replaced.Add(target); }
			VillagePrefabCatalog[] reloaded = targets.Select(Load).ToArray();
			if (reloaded.Any(catalog => !CatalogEquals(catalog, modules, semantics))
				|| reloaded.Select(static catalog => catalog.Hash).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
				throw new InvalidDataException("Saved catalog verification did not match the requested editor state.");
			return reloaded;
		}
		catch
		{
			foreach (string target in replaced.AsEnumerable().Reverse())
			{
				byte[] original = originals[target];
				if (original is null) File.Delete(target); else File.WriteAllBytes(target, original);
			}
			throw;
		}
	}

	private static bool CatalogEquals(VillagePrefabCatalog catalog, VillagePrefab[] modules, string[] semantics)
	{
		if (!catalog.SocketSemantics.SequenceEqual(semantics) || catalog.Prefabs.Count != modules.Length) return false;
		VillagePrefab[] expected = modules.OrderBy(static module => module.Descriptor.Id, StringComparer.Ordinal).ToArray();
		for (int index = 0; index < expected.Length; index++)
		{
			VillagePrefab actual = catalog.Prefabs[index];
			VillagePrefab wanted = expected[index];
			if (!DescriptorEquals(actual.Descriptor, wanted.Descriptor)
				|| !actual.Cells.SequenceEqual(wanted.Cells)) return false;
		}
		return true;
	}

	private static bool DescriptorEquals(VillagePrefabDescriptor left, VillagePrefabDescriptor right) =>
		left.Id == right.Id && left.DisplayName == right.DisplayName && left.Kind == right.Kind
		&& left.Weight == right.Weight && left.Levels == right.Levels
		&& left.AllowedRotations.SequenceEqual(right.AllowedRotations)
		&& left.SupportMask.SequenceEqual(right.SupportMask) && left.LoadMask.SequenceEqual(right.LoadMask)
		&& left.WalkableMask.SequenceEqual(right.WalkableMask) && left.Markers.SequenceEqual(right.Markers)
		&& left.Sockets.Length == right.Sockets.Length
		&& left.Sockets.All(socket => right.Sockets.Any(other => socket.Direction == other.Direction
			&& socket.Types.SequenceEqual(other.Types) && socket.Openings.SequenceEqual(other.Openings)));

	public static string ValidateSocketSemantic(string value)
	{
		string normalized = value?.Trim().ToLowerInvariant() ?? string.Empty;
		if (normalized.Length is < 1 or > 32 || !char.IsLetter(normalized[0])
			|| normalized.Any(static character => !char.IsLetterOrDigit(character) && character is not ('.' or '_' or '-')))
			throw new InvalidDataException("Socket names must contain 1-32 lowercase letters, digits, '.', '_' or '-', and start with a letter.");
		return normalized;
	}

	private static string[] NormalizeSocketSemantics(IEnumerable<string> requested, VillagePrefab[] prefabs)
	{
		IEnumerable<string> source = requested ?? DefaultSocketSemantics
			.Concat(prefabs.SelectMany(static prefab => prefab.Descriptor.Sockets).SelectMany(static socket => socket.Types));
		string[] normalized = source.Select(ValidateSocketSemantic).ToArray();
		if (requested is not null && normalized.Distinct(StringComparer.Ordinal).Count() != normalized.Length)
			throw new InvalidDataException("Socket semantics must be unique after normalization.");
		string[] values = normalized.Distinct(StringComparer.Ordinal).ToArray();
		if (!values.Contains("closed", StringComparer.Ordinal) || !values.Contains("any", StringComparer.Ordinal))
			throw new InvalidDataException("Socket semantics must include the reserved 'closed' and 'any' values.");
		return values;
	}

	private static ModuleDocument ToDocument(VillagePrefab prefab)
	{
		const string symbols = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
		BlockType[] blocks = prefab.Cells.Select(static value => value.Type).Where(static value => value != BlockType.None).Distinct().Order().ToArray();
		if (blocks.Length > symbols.Length) throw new InvalidDataException($"Village prefab '{prefab.Descriptor.Id}' uses too many block types.");
		Dictionary<BlockType, char> byBlock = blocks.Select((block, index) => (block, symbol: symbols[index])).ToDictionary(static value => value.block, static value => value.symbol);
		Dictionary<string, string> palette = byBlock.ToDictionary(static pair => pair.Value.ToString(), static pair => pair.Key.ToString());
		string[][] layers = new string[VillagePrefabDescriptor.Height][];
		for (int y = 0; y < VillagePrefabDescriptor.Height; y++)
		{
			layers[y] = new string[VillagePrefabDescriptor.Length];
			for (int z = 0; z < VillagePrefabDescriptor.Length; z++)
			{
				char[] row = new char[VillagePrefabDescriptor.Width];
				for (int x = 0; x < VillagePrefabDescriptor.Width; x++)
				{
					BlockType block = prefab.GetCell(x, y, z).Type; row[x] = block == BlockType.None ? '.' : byBlock[block];
				}
				layers[y][z] = new(row);
			}
		}
		VillagePrefabDescriptor descriptor = prefab.Descriptor;
		return new(descriptor.Id, descriptor.DisplayName, descriptor.Kind, descriptor.Weight, descriptor.Levels, descriptor.AllowedRotations,
			descriptor.Sockets.ToDictionary(static socket => socket.Direction.ToString(), static socket => socket.Types), palette, layers,
			MaskRows(descriptor.SupportMask), MaskRows(descriptor.LoadMask), MaskRows(descriptor.WalkableMask), descriptor.Markers);
	}

	private static string[] MaskRows(byte[] mask) => Enumerable.Range(0, VillagePrefabDescriptor.Length)
		.Select(z => new string(Enumerable.Range(0, VillagePrefabDescriptor.Width).Select(x => mask[z * VillagePrefabDescriptor.Width + x] == 0 ? '0' : '1').ToArray())).ToArray();

	private static VillagePrefab Parse(ModuleDocument module)
	{
		if (module.Palette is null || module.Layers is null || module.Layers.Length != VillagePrefabDescriptor.Height)
			throw new InvalidDataException($"Village prefab '{module.Id}' has invalid voxel data.");
		Dictionary<char, BlockValue> palette = [];
		foreach ((string key, string value) in module.Palette)
		{
			if (key.Length != 1 || key[0] is '.' or '_' || !Enum.TryParse(value, out BlockType block) || block == BlockType.None)
				throw new InvalidDataException($"Village prefab '{module.Id}' has an invalid palette entry.");
			palette.Add(key[0], new(block));
		}
		BlockValue[] cells = new BlockValue[VillagePrefabDescriptor.Width * VillagePrefabDescriptor.Height * VillagePrefabDescriptor.Length];
		int index = 0;
		foreach (string[] layer in module.Layers)
		{
			if (layer.Length != VillagePrefabDescriptor.Length) throw new InvalidDataException($"Village prefab '{module.Id}' has an invalid layer height.");
			foreach (string row in layer)
			{
				if (row?.Length != VillagePrefabDescriptor.Width) throw new InvalidDataException($"Village prefab '{module.Id}' has an invalid row width.");
				foreach (char symbol in row)
					cells[index++] = symbol is '.' or '_' ? default : palette.TryGetValue(symbol, out BlockValue value) ? value : throw new InvalidDataException($"Village prefab '{module.Id}' uses unknown symbol '{symbol}'.");
			}
		}
		byte[] support = ParseMask(module.SupportMask, defaultValue: module.Kind is not (VillageModuleKind.Outside or VillageModuleKind.Yard or VillageModuleKind.Road));
		byte[] load = ParseMask(module.LoadMask, defaultValue: module.Kind is VillageModuleKind.Room or VillageModuleKind.Hallway or VillageModuleKind.Stairs or VillageModuleKind.Utility);
		byte[] walkable = ParseMask(module.WalkableMask, defaultValue: module.Kind is VillageModuleKind.Road or VillageModuleKind.Plaza or VillageModuleKind.Yard or VillageModuleKind.Gate or VillageModuleKind.Room or VillageModuleKind.Hallway or VillageModuleKind.Stairs or VillageModuleKind.Utility);
		VillageSocketDescriptor[] sockets = Enum.GetValues<VillageSocketDirection>()
			.Select(direction => new VillageSocketDescriptor(direction,
				module.Sockets is not null && module.Sockets.TryGetValue(direction.ToString(), out string[] values) ? values : ["any"],
				VillagePrefab.DeriveOpeningMask(direction, cells)))
			.ToArray();
		VillagePrefabDescriptor descriptor = new(module.Id, module.Kind, module.Weight, module.Levels,
			module.Rotations ?? [0], sockets, support, load, walkable, module.Markers ?? [])
		{ DisplayName = string.IsNullOrWhiteSpace(module.DisplayName) ? module.Id : module.DisplayName };
		descriptor.Validate();
		ValidateGeometry(descriptor, cells);
		return new(descriptor, cells);
	}

	private static byte[] ParseMask(string[] rows, bool defaultValue)
	{
		if (rows is null) return Enumerable.Repeat(defaultValue ? (byte)1 : (byte)0, VillagePrefabDescriptor.MaskLength).ToArray();
		if (rows.Length != VillagePrefabDescriptor.Length || rows.Any(static row => row?.Length != VillagePrefabDescriptor.Width || row.Any(character => character is not ('0' or '1'))))
			throw new InvalidDataException("Village prefab masks must contain five rows of five binary digits.");
		return rows.SelectMany(static row => row.Select(static character => character == '1' ? (byte)1 : (byte)0)).ToArray();
	}

	private static void ValidateGeometry(VillagePrefabDescriptor descriptor, BlockValue[] cells)
	{
		if (descriptor.Kind is VillageModuleKind.Room or VillageModuleKind.Hallway or VillageModuleKind.Utility)
			for (int z = 1; z < 4; z++) for (int x = 1; x < 4; x++) for (int y = 1; y < 4; y++)
				if (cells[(y * 5 + z) * 5 + x].Type != BlockType.None)
					throw new InvalidDataException($"Village prefab '{descriptor.Id}' does not preserve three-block room clearance.");
		if (descriptor.Markers.Any(static marker => string.Equals(marker.Kind, "Door", StringComparison.OrdinalIgnoreCase)))
			throw new InvalidDataException($"Village prefab '{descriptor.Id}' uses a Door marker. Doors must be authored as voxel openings.");
	}

	private sealed record CatalogDocument(int FormatVersion, int Width, int Height, int Length, string[] SocketSemantics, ModuleDocument[] Modules);
	private sealed record ModuleDocument(
		string Id,
		string DisplayName,
		VillageModuleKind Kind,
		int Weight,
		VillageModuleLevel Levels,
		int[] Rotations,
		Dictionary<string, string[]> Sockets,
		Dictionary<string, string> Palette,
		string[][] Layers,
		string[] SupportMask = null,
		string[] LoadMask = null,
		string[] WalkableMask = null,
		VillageMarkerDescriptor[] Markers = null);
}
