using System;
using System.IO;
using System.Numerics;

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

		public PlayerDataStore(string directory = "data/players")
		{
			_directory = directory;
		}

		/// <summary>
		/// Saves player state to disk.
		/// </summary>
		public void Save(string playerName, Vector3 position, float health, Vector3 velocity, ServerInventory inventory = null)
		{
			try
			{
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
			}
			catch (Exception)
			{
				// Silently ignore save failures — logged by caller if needed
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

				return true;
			}
			catch (Exception)
			{
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
