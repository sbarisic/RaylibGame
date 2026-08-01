using System.Diagnostics;
using System.Numerics;
using Voxelgine.Engine.DI;

namespace Voxelgine.Engine.Server;

public sealed class PlayerDataStore
{
	private readonly string _directory;
	private readonly IFishLogging _logging;

	public PlayerDataStore(string directory, IFishLogging logging = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directory);
		_directory = Path.GetFullPath(directory);
		_logging = logging;
	}

	public void Save(
		string playerName,
		Vector3 position,
		float health,
		Vector3 velocity,
		PlayerInventory inventory = null,
		byte selectedHotbarSlot = 0)
	{
		try
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			Directory.CreateDirectory(_directory);
			string filePath = GetFilePath(playerName);
			string temporaryPath = filePath + ".tmp";
			PlayerInventory source = inventory ?? new PlayerInventory();
			if (selectedHotbarSlot >= PlayerInventory.HotbarSlotCount)
				throw new ArgumentOutOfRangeException(nameof(selectedHotbarSlot));

			using (FileStream stream = File.Create(temporaryPath))
			using (var writer = new BinaryWriter(stream))
			{
				writer.Write(position.X);
				writer.Write(position.Y);
				writer.Write(position.Z);
				writer.Write(health);
				writer.Write(velocity.X);
				writer.Write(velocity.Y);
				writer.Write(velocity.Z);
				writer.Write(selectedHotbarSlot);
				WriteStack(writer, source.Cursor);
				writer.Write(source.CursorOriginSlot);
				foreach (ItemStack stack in source.GetSlots())
					WriteStack(writer, stack);
				writer.Flush();
				stream.Flush(flushToDisk: true);
			}

			File.Move(temporaryPath, filePath, overwrite: true);
			_logging?.Log(
				GameLogLevel.Debug,
				"Persistence",
				$"Saved player name={playerName} path={filePath} bytes={new FileInfo(filePath).Length} durationMs={stopwatch.Elapsed.TotalMilliseconds:F1}");
		}
		catch (Exception exception)
		{
			_logging?.Log(GameLogLevel.Error, "Persistence", $"Failed to save player name={playerName} path={GetFilePath(playerName)}", exception);
		}
	}

	public bool TryLoad(
		string playerName,
		out Vector3 position,
		out float health,
		out Vector3 velocity) =>
		TryLoad(playerName, out position, out health, out velocity, null, out _);

	public bool TryLoad(
		string playerName,
		out Vector3 position,
		out float health,
		out Vector3 velocity,
		PlayerInventory inventory,
		out byte selectedHotbarSlot)
	{
		position = Vector3.Zero;
		health = 100f;
		velocity = Vector3.Zero;
		selectedHotbarSlot = 0;
		string filePath = GetFilePath(playerName);
		if (!File.Exists(filePath))
			return false;

		try
		{
			Stopwatch stopwatch = Stopwatch.StartNew();
			using FileStream stream = File.OpenRead(filePath);
			using var reader = new BinaryReader(stream);
			position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			health = reader.ReadSingle();
			velocity = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
			selectedHotbarSlot = reader.ReadByte();
			if (selectedHotbarSlot >= PlayerInventory.HotbarSlotCount)
				throw new InvalidDataException("Saved hotbar selection is invalid.");

			ItemStack cursor = ReadStack(reader);
			int cursorOrigin = reader.ReadInt32();
			var slots = new ItemStack[PlayerInventory.SlotCount];
			for (int i = 0; i < slots.Length; i++)
				slots[i] = ReadStack(reader);
			if (stream.Position != stream.Length)
				throw new InvalidDataException("Player data contains trailing bytes.");
			inventory?.Restore(slots, cursor, cursorOrigin);

			_logging?.Log(
				GameLogLevel.Debug,
				"Persistence",
				$"Loaded player name={playerName} path={filePath} bytes={stream.Length} durationMs={stopwatch.Elapsed.TotalMilliseconds:F1}");
			return true;
		}
		catch (Exception exception)
		{
			_logging?.Log(GameLogLevel.Error, "Persistence", $"Failed to load player name={playerName} path={filePath}; deleting incompatible data", exception);
			try
			{
				File.Delete(filePath);
			}
			catch (Exception deleteException)
			{
				_logging?.Log(GameLogLevel.Error, "Persistence", $"Failed to delete invalid player data path={filePath}", deleteException);
			}
			return false;
		}
	}

	private static void WriteStack(BinaryWriter writer, ItemStack stack)
	{
		if (!ItemCatalog.IsCanonical(stack))
			throw new InvalidDataException("Cannot persist a non-canonical item stack.");
		writer.Write(stack.Item.Value);
		writer.Write(stack.Count);
	}

	private static ItemStack ReadStack(BinaryReader reader)
	{
		var stack = new ItemStack(new ItemId(reader.ReadUInt16()), reader.ReadUInt16());
		if (!ItemCatalog.IsCanonical(stack))
			throw new InvalidDataException($"Invalid saved item stack item={stack.Item.Value} count={stack.Count}.");
		return stack;
	}

	private string GetFilePath(string playerName)
	{
		string safeName = SanitizeFileName(playerName);
		return Path.Combine(_directory, safeName + ".bin");
	}

	private static string SanitizeFileName(string name)
	{
		char[] invalid = Path.GetInvalidFileNameChars();
		var result = new char[name.Length];
		for (int i = 0; i < name.Length; i++)
			result[i] = Array.IndexOf(invalid, name[i]) >= 0 ? '_' : name[i];
		return new string(result);
	}
}
