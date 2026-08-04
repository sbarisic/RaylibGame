using System.Security.Cryptography;
using Voxelgine.Engine;
using Voxelgine.Graphics;
using Voxelgine.WorldGeneration;

namespace Voxelgine.Engine.World.Structures;

/// <summary>
/// Game-specific view of one CeramicFish definition. The core definition remains
/// renderer-independent; this wrapper verifies that entity values are voxel block IDs.
/// </summary>
public sealed class CeramicVillageCatalog
{
	public const int PrefabWidth = 3;
	public const int PrefabHeight = 5;
	public const int PrefabLength = 3;

	private readonly Dictionary<string, CeramicPrefabDefinition> byId;

	private CeramicVillageCatalog(string path, CeramicFishDefinition definition, string hash)
	{
		Path = path;
		Definition = definition;
		Hash = hash;
		byId = definition.Prefabs.ToDictionary(static prefab => prefab.Id, StringComparer.Ordinal);
	}

	public string Path { get; }
	public string Hash { get; }
	public CeramicFishDefinition Definition { get; }
	public IReadOnlyList<CeramicPrefabDefinition> Prefabs => Definition.Prefabs;

	public CeramicPrefabDefinition Get(string id) => byId.TryGetValue(id, out CeramicPrefabDefinition prefab)
		? prefab
		: throw new KeyNotFoundException($"Unknown CeramicFish village prefab '{id}'.");

	public static CeramicVillageCatalog Load(string path)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		string fullPath = System.IO.Path.GetFullPath(path);
		byte[] bytes = File.ReadAllBytes(fullPath);
		CeramicFishDefinition definition = new CeramicFishJsonStorage().LoadAsync(fullPath)
			.AsTask().GetAwaiter().GetResult();
		ValidateVoxelDefinition(definition);
		string hash = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
		return new(fullPath, definition, hash);
	}

	public static IReadOnlyList<CeramicVillageCatalog> SaveSynchronized(
		IEnumerable<string> paths,
		CeramicFishDefinition definition)
	{
		ArgumentNullException.ThrowIfNull(paths);
		ArgumentNullException.ThrowIfNull(definition);
		ValidateVoxelDefinition(definition);
		string[] targets = paths.Where(static path => !string.IsNullOrWhiteSpace(path))
			.Select(System.IO.Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
		if (targets.Length == 0)
			throw new InvalidDataException("No CeramicFish village definition save target was resolved.");

		Dictionary<string, byte[]> originals = targets.ToDictionary(static path => path,
			static path => File.Exists(path) ? File.ReadAllBytes(path) : null,
			StringComparer.OrdinalIgnoreCase);
		List<string> replaced = [];
		try
		{
			CeramicFishJsonStorage storage = new();
			foreach (string target in targets)
			{
				storage.SaveAsync(target, definition).AsTask().GetAwaiter().GetResult();
				replaced.Add(target);
			}
			CeramicVillageCatalog[] catalogs = targets.Select(Load).ToArray();
			if (catalogs.Select(static catalog => catalog.Hash)
				.Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
				throw new InvalidDataException("Saved CeramicFish village definitions do not match.");
			return catalogs;
		}
		catch
		{
			foreach (string target in replaced.AsEnumerable().Reverse())
			{
				byte[] original = originals[target];
				if (original is null)
					File.Delete(target);
				else
					File.WriteAllBytes(target, original);
			}
			throw;
		}
	}

	public static BlockValue ToBlockValue(CeramicEntity entity)
	{
		BlockType type = (BlockType)entity.Value;
		if (!Enum.IsDefined(type) || type == BlockType.None)
			throw new InvalidDataException($"CeramicFish entity value {entity.Value} is not a non-empty voxel block.");
		byte state = BlockShapeCatalog.IsStair(type)
			? (byte)((int)entity.Rotation / 90)
			: (byte)0;
		return new(type, state);
	}

	public static void ValidateVoxelDefinition(CeramicFishDefinition definition)
	{
		ArgumentNullException.ThrowIfNull(definition);
		CeramicValidationResult core = new CeramicFish().ValidateDefinition(definition);
		if (!core.IsValid)
			throw new CeramicDefinitionException("The CeramicFish village definition is invalid.", core.Errors);
		List<CeramicValidationError> errors = [];
		for (int prefabIndex = 0; prefabIndex < definition.Prefabs.Count; prefabIndex++)
		{
			CeramicPrefabDefinition prefab = definition.Prefabs[prefabIndex];
			string path = $"$.prefabs[{prefabIndex}]";
			if (prefab.SizeX != PrefabWidth || prefab.SizeY != PrefabHeight
				|| prefab.SizeZ != PrefabLength)
				errors.Add(new("voxel-prefab-size",
					$"Village prefab '{prefab.Id}' must be exactly {PrefabWidth}x{PrefabHeight}x{PrefabLength}.",
					path));
			for (int entityIndex = 0; entityIndex < prefab.Entities.Count; entityIndex++)
			{
				CeramicEntity entity = prefab.Entities[entityIndex];
				if (!Enum.IsDefined(entity.Rotation))
					errors.Add(new("voxel-entity-rotation", "A voxel entity has an invalid rotation.",
						$"{path}.entities[{entityIndex}].rotation"));
				if (!Enum.IsDefined((BlockType)entity.Value) || entity.Value == (int)BlockType.None)
					errors.Add(new("voxel-entity-value",
						$"Entity value {entity.Value} is not a non-empty BlockType.",
						$"{path}.entities[{entityIndex}].value"));
			}
		}
		if (errors.Count != 0)
			throw new CeramicDefinitionException("The CeramicFish village voxel definition is invalid.", errors);
	}
}
