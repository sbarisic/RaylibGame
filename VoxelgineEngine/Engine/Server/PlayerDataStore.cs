using System;
using System.IO;
using System.Numerics;
using System.Diagnostics;
using Voxelgine.Engine.DI;

namespace Voxelgine.Engine.Server
{
	/// <summary>
	/// Persists player state (position, health, velocity, inventory) to binary files in the players data directory.
	/// Each player's data is stored in a separate file keyed by sanitized player name.
	/// </summary>
	public class PlayerDataStore
	{
		/// <summary>
		/// Data format version. Increment when adding fields to maintain backward compatibility.
		/// Version 1: position, health, velocity.
		/// Version 2: + inventory slot counts.
		/// </summary>
		private const int DataVersion = 2;

		private readonly string _directory;
		private readonly IFishLogging _logging;

		public PlayerDataStore(string directory = "data/players", IFishLogging logging = null)
		{
			_directory = directory;
			_logging = logging;
		}

		/// <summary>
		/// Saves player state to disk.
		/// </summary>
		public void Save(string playerName, Vector3 position, float health, Vector3 velocity, ServerInventory inventory = null)
		{
			try
			{
				Stopwatch stopwatch = Stopwatch.StartNew();
				Directory.CreateDirectory(_directory);
				string filePath = GetFilePath(playerName);

				using var fs = File.Create(filePath);
				using var writer = new BinaryWriter(fs);

				writer.Write(DataVersion);
				writer.Write(position.X);
				writer.Write(position.Y);
				writer.Write(position.Z);
				writer.Write(health);
				writer.Write(velocity.X);
				writer.Write(velocity.Y);
				writer.Write(velocity.Z);

				// Version 2: inventory
				if (inventory != null)
					inventory.Write(writer);
				else
					new ServerInventory().Write(writer);
				writer.Flush();
				_logging?.Log(GameLogLevel.Debug, "Persistence", $"Saved player name={playerName} path={Path.GetFullPath(filePath)} bytes={fs.Length} version={DataVersion} durationMs={stopwatch.Elapsed.TotalMilliseconds:F1}");
			}
			catch (Exception exception)
			{
				_logging?.Log(GameLogLevel.Error, "Persistence", $"Failed to save player name={playerName} path={Path.GetFullPath(GetFilePath(playerName))}", exception);
			}
		}

		/// <summary>
		/// Attempts to load saved player state from disk.
		/// Returns true if data was found and loaded, false otherwise.
		/// </summary>
		public bool TryLoad(string playerName, out Vector3 position, out float health, out Vector3 velocity, ServerInventory inventory = null)
		{
			position = Vector3.Zero;
			health = 100f;
			velocity = Vector3.Zero;

			string filePath = GetFilePath(playerName);
			if (!File.Exists(filePath))
				return false;

			try
			{
				Stopwatch stopwatch = Stopwatch.StartNew();
				using var fs = File.OpenRead(filePath);
				using var reader = new BinaryReader(fs);

				int version = reader.ReadInt32();
				if (version >= 1)
				{
					position = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
					health = reader.ReadSingle();
					velocity = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
				}

				if (version >= 2 && inventory != null)
				{
					inventory.Read(reader);
				}

				_logging?.Log(GameLogLevel.Debug, "Persistence", $"Loaded player name={playerName} path={Path.GetFullPath(filePath)} bytes={fs.Length} version={version} durationMs={stopwatch.Elapsed.TotalMilliseconds:F1}");
				return true;
			}
			catch (Exception exception)
			{
				_logging?.Log(GameLogLevel.Error, "Persistence", $"Failed to load player name={playerName} path={Path.GetFullPath(filePath)}", exception);
				return false;
			}
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
			{
				result[i] = Array.IndexOf(invalid, name[i]) >= 0 ? '_' : name[i];
			}
			return new string(result);
		}
	}
}
