using System.Text.Json;
using System.Text.Json.Serialization;

namespace Voxelgine.WorldGeneration;

/// <summary>System.Text.Json storage for one complete CeramicFish definition per file.</summary>
public sealed class CeramicFishJsonStorage : ICeramicFishJsonStorage
{
	private static readonly JsonSerializerOptions Options = CreateOptions();

	public async ValueTask<CeramicFishDefinition> LoadAsync(
		string path,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		string fullPath = Path.GetFullPath(path);
		await using FileStream input = new(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
			bufferSize: 16 * 1024, useAsync: true);
		CeramicFishDefinition definition = await JsonSerializer.DeserializeAsync<CeramicFishDefinition>(
			input, Options, cancellationToken).ConfigureAwait(false)
			?? throw new InvalidDataException("The CeramicFish JSON document is empty.");
		ValidateStructure(definition);
		return definition;
	}

	public async ValueTask SaveAsync(
		string path,
		CeramicFishDefinition definition,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		ArgumentNullException.ThrowIfNull(definition);
		ValidateStructure(definition);
		string fullPath = Path.GetFullPath(path);
		string? directory = Path.GetDirectoryName(fullPath);
		if (string.IsNullOrEmpty(directory))
			throw new InvalidDataException("The CeramicFish JSON path has no parent directory.");
		Directory.CreateDirectory(directory);
		string temporary = Path.Combine(directory,
			$".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
		try
		{
			await using (FileStream output = new(temporary, FileMode.CreateNew, FileAccess.Write,
				FileShare.None, bufferSize: 16 * 1024, useAsync: true))
			{
				await JsonSerializer.SerializeAsync(output, definition, Options, cancellationToken)
					.ConfigureAwait(false);
				await output.FlushAsync(cancellationToken).ConfigureAwait(false);
			}
			_ = await LoadAsync(temporary, cancellationToken).ConfigureAwait(false);
			File.Move(temporary, fullPath, overwrite: true);
		}
		finally
		{
			if (File.Exists(temporary)) File.Delete(temporary);
		}
	}

	private static void ValidateStructure(CeramicFishDefinition definition)
	{
		if (definition.FormatVersion != CeramicFishDefinition.CurrentFormatVersion)
			throw new InvalidDataException(
				$"Unsupported CeramicFish format version {definition.FormatVersion}; expected {CeramicFishDefinition.CurrentFormatVersion}.");
		if (string.IsNullOrWhiteSpace(definition.Id) || definition.Id.Length > 128)
			throw new InvalidDataException("CeramicFish definition IDs must contain 1-128 characters.");
		if (definition.Prefabs is null || definition.Prefabs.Count == 0)
			throw new InvalidDataException("CeramicFish definitions require at least one prefab.");
		if (definition.ConnectionPolicies is null)
			throw new InvalidDataException("CeramicFish connection policies are missing.");
		if (definition.Prefabs.Select(prefab => prefab?.Id).Distinct(StringComparer.Ordinal).Count()
			!= definition.Prefabs.Count)
			throw new InvalidDataException("CeramicFish prefab IDs must be unique.");
		foreach (CeramicPrefabDefinition prefab in definition.Prefabs)
			ValidatePrefab(prefab ?? throw new InvalidDataException("CeramicFish prefabs cannot be null."));
		if (definition.ConnectionPolicies.Select(policy => policy?.SocketType)
			.Distinct(StringComparer.Ordinal).Count() != definition.ConnectionPolicies.Count)
			throw new InvalidDataException("CeramicFish connection-policy socket types must be unique.");
		foreach (CeramicConnectionPolicy policy in definition.ConnectionPolicies)
		{
			if (policy is null || string.IsNullOrWhiteSpace(policy.SocketType)
				|| policy.SocketType == CeramicSocket.Closed || policy.RequiredDegree is < 1 or > 4
				|| policy.RequiredComponentCount is <= 0)
				throw new InvalidDataException("CeramicFish connection policies are invalid.");
		}
	}

	private static void ValidatePrefab(CeramicPrefabDefinition prefab)
	{
		if (string.IsNullOrWhiteSpace(prefab.Id) || prefab.Id.Length > 128)
			throw new InvalidDataException("CeramicFish prefab IDs must contain 1-128 characters.");
		if (prefab.Tags is null || prefab.Tags.Any(string.IsNullOrWhiteSpace)
			|| prefab.Tags.Distinct(StringComparer.Ordinal).Count() != prefab.Tags.Count)
			throw new InvalidDataException($"CeramicFish prefab '{prefab.Id}' has invalid tags.");
		if (prefab.SizeX <= 0 || prefab.SizeY <= 0 || prefab.SizeZ <= 0)
			throw new InvalidDataException($"CeramicFish prefab '{prefab.Id}' dimensions must be positive.");
		if (prefab.Weight <= 0 || prefab.AllowedRotations == CeramicRotationOptions.None
			|| (prefab.AllowedRotations & ~CeramicRotationOptions.All) != 0)
			throw new InvalidDataException($"CeramicFish prefab '{prefab.Id}' selection settings are invalid.");
		if (prefab.Entities is null || prefab.Sockets is null)
			throw new InvalidDataException($"CeramicFish prefab '{prefab.Id}' data is missing.");
		if (prefab.Entities.Any(entity => entity is null || entity.X < 0 || entity.X >= prefab.SizeX
			|| entity.Y < 0 || entity.Y >= prefab.SizeY || entity.Z < 0 || entity.Z >= prefab.SizeZ))
			throw new InvalidDataException($"CeramicFish prefab '{prefab.Id}' contains an out-of-bounds entity.");
		if (prefab.Entities.Select(entity => (entity.X, entity.Y, entity.Z)).Distinct().Count()
			!= prefab.Entities.Count)
			throw new InvalidDataException($"CeramicFish prefab '{prefab.Id}' contains overlapping entities.");
		if (prefab.Sockets.Count != Enum.GetValues<CeramicDirection>().Length
			|| prefab.Sockets.GroupBy(socket => socket.Direction).Any(group => group.Count() != 1)
			|| prefab.Sockets.Any(socket => socket is null || string.IsNullOrWhiteSpace(socket.Type)))
			throw new InvalidDataException($"CeramicFish prefab '{prefab.Id}' must define one valid socket per direction.");
	}

	private static JsonSerializerOptions CreateOptions()
	{
		JsonSerializerOptions options = new(JsonSerializerDefaults.Web)
		{
			WriteIndented = true,
		};
		options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
		return options;
	}
}
