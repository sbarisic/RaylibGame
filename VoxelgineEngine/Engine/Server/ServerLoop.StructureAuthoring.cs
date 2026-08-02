#if DEBUG
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Voxelgine.Engine.World.Structures;

namespace Voxelgine.Engine.Server;

public partial class ServerLoop
{
	private readonly Dictionary<int, StructureAuthoringSelection> structureSelections = new();
	private static readonly Regex ValidStructureId = new("^[a-z][a-z0-9._-]{0,63}$", RegexOptions.CultureInvariant);

	private void CmdStructure(NetConnection connection, string arguments)
	{
		if (!_sessions.TryGetValue(connection.PlayerId, out ServerClientSession session))
			return;
		string[] parts = arguments.Split(' ', 4, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length == 0)
		{
			SendServerMessageTo(connection.PlayerId, "Usage: /structure pos1|pos2|anchor|marker|export ...");
			return;
		}
		if (!structureSelections.TryGetValue(connection.PlayerId, out StructureAuthoringSelection selection))
		{
			selection = new StructureAuthoringSelection();
			structureSelections.Add(connection.PlayerId, selection);
		}
		BlockCoordinate position = new((int)MathF.Floor(session.Player.FeetPosition.X), (int)MathF.Floor(session.Player.FeetPosition.Y), (int)MathF.Floor(session.Player.FeetPosition.Z));
		switch (parts[0].ToLowerInvariant())
		{
			case "pos1": selection.Position1 = position; break;
			case "pos2": selection.Position2 = position; break;
			case "anchor": selection.Anchor = position; break;
			case "marker": AddAuthoringMarker(selection, parts, position); break;
			case "export":
				if (parts.Length != 2 || !ValidStructureId.IsMatch(parts[1]))
					throw new InvalidDataException("Export ID must match ^[a-z][a-z0-9._-]{0,63}$.");
				string exported = ExportStructureSelection(selection, parts[1]);
				SendServerMessageTo(connection.PlayerId, $"Exported structure blueprint to {exported}");
				return;
			default: throw new InvalidDataException("Unknown structure authoring operation.");
		}
		SendServerMessageTo(connection.PlayerId, $"Structure {parts[0]} set at {position}.");
	}

	private void AddAuthoringMarker(StructureAuthoringSelection selection, string[] parts, BlockCoordinate position)
	{
		if (parts.Length < 3 || !ValidStructureId.IsMatch(parts[1]) || !Enum.TryParse(parts[2], true, out StructureMarkerKind kind))
			throw new InvalidDataException("Usage: /structure marker <id> <kind> [json-data]");
		selection.Markers.RemoveAll(marker => string.Equals(marker.Id, parts[1], StringComparison.Ordinal));
		selection.Markers.Add(new AuthoredMarker(parts[1], kind, position, parts.Length == 4 ? parts[3] : string.Empty));
	}

	private string ExportStructureSelection(StructureAuthoringSelection selection, string id)
	{
		if (selection.Position1 == null || selection.Position2 == null || selection.Anchor == null)
			throw new InvalidOperationException("Set pos1, pos2, and anchor before exporting.");
		BlockCoordinate minimum = Minimum(selection.Position1.Value, selection.Position2.Value);
		BlockCoordinate maximum = Maximum(selection.Position1.Value, selection.Position2.Value);
		BlockCoordinate size = new(maximum.X - minimum.X + 1, maximum.Y - minimum.Y + 1, maximum.Z - minimum.Z + 1);
		if (size.X > 64 || size.Y > 64 || size.Z > 64 || (long)size.X * size.Y * size.Z > 131_072)
			throw new InvalidOperationException("Selection exceeds blueprint limits.");

		const string symbols = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!#$%&*+,-/:;<=>?@[]^`{|}~";
		Dictionary<BlockType, char> symbolsByBlock = new();
		for (int y = minimum.Y; y <= maximum.Y; y++)
			for (int z = minimum.Z; z <= maximum.Z; z++)
				for (int x = minimum.X; x <= maximum.X; x++)
				{
					BlockType block = _simulation.Map.GetBlock(x, y, z);
					if (block != BlockType.None && !symbolsByBlock.ContainsKey(block))
						symbolsByBlock.Add(block, symbols[symbolsByBlock.Count]);
				}
		if (symbolsByBlock.Count > 128 || symbolsByBlock.Count > symbols.Length)
			throw new InvalidOperationException("Selection uses too many block types for the authoring symbol set.");

		JsonObject root = new()
		{
			["formatVersion"] = 1,
			["markerDataVersion"] = 1,
			["id"] = id,
			["role"] = "Support",
			["critical"] = false,
			["size"] = CoordinateArray(size),
			["anchor"] = CoordinateArray(selection.Anchor.Value - minimum),
			["rotations"] = new JsonArray(0, 90, 180, 270),
		};
		JsonObject palette = new();
		foreach ((BlockType block, char symbol) in symbolsByBlock.OrderBy(static pair => pair.Value))
			palette[symbol.ToString()] = block.ToString();
		root["palette"] = palette;
		JsonArray layers = new();
		for (int y = minimum.Y; y <= maximum.Y; y++)
		{
			JsonArray rows = new();
			for (int z = minimum.Z; z <= maximum.Z; z++)
			{
				char[] row = new char[size.X];
				for (int x = minimum.X; x <= maximum.X; x++)
				{
					BlockType block = _simulation.Map.GetBlock(x, y, z);
					row[x - minimum.X] = block == BlockType.None ? '_' : symbolsByBlock[block];
				}
				rows.Add(new string(row));
			}
			layers.Add(rows);
		}
		root["layers"] = layers;
		JsonArray markers = new();
		foreach (AuthoredMarker marker in selection.Markers.OrderBy(static marker => marker.Id, StringComparer.Ordinal))
		{
			JsonObject value = new()
			{
				["id"] = marker.Id,
				["kind"] = marker.Kind.ToString(),
				["position"] = CoordinateArray(marker.Position - minimum),
			};
			if (marker.Kind == StructureMarkerKind.MachineFunction)
				value["expectedBlock"] = _simulation.Map.GetBlock(marker.Position.X, marker.Position.Y, marker.Position.Z).ToString();
			if (marker.Data.Length != 0)
				value["data"] = JsonNode.Parse(marker.Data);
			markers.Add(value);
		}
		root["markers"] = markers;
		root["connectors"] = new JsonArray();
		root["fogVolumes"] = new JsonArray();

		string directory = Path.Combine(_runtimePaths.Root, "structure-exports");
		Directory.CreateDirectory(directory);
		string path = Path.Combine(directory, id + ".json");
		File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
		return path;
	}

	private static JsonArray CoordinateArray(BlockCoordinate value) => new(value.X, value.Y, value.Z);
	private static BlockCoordinate Minimum(BlockCoordinate left, BlockCoordinate right) => new(Math.Min(left.X, right.X), Math.Min(left.Y, right.Y), Math.Min(left.Z, right.Z));
	private static BlockCoordinate Maximum(BlockCoordinate left, BlockCoordinate right) => new(Math.Max(left.X, right.X), Math.Max(left.Y, right.Y), Math.Max(left.Z, right.Z));

	private sealed class StructureAuthoringSelection
	{
		public BlockCoordinate? Position1 { get; set; }
		public BlockCoordinate? Position2 { get; set; }
		public BlockCoordinate? Anchor { get; set; }
		public List<AuthoredMarker> Markers { get; } = new();
	}

	private readonly record struct AuthoredMarker(string Id, StructureMarkerKind Kind, BlockCoordinate Position, string Data);
}
#endif
