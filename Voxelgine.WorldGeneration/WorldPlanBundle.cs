using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Voxelgine.WorldGeneration;

public static class WorldPlanBundle
{
	public const string ManifestFileName = "manifest.json";
	private static readonly string[] LayerNames = ["height.png", "biome.png", "hill-mask.png", "tree-density.png", "features.png", "features-floor-2.png", "features-floor-3.png", "features-roof.png", "combined.png"];
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
	{
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() },
	};

	public static async Task SaveAsync(string directory, WorldPlan plan, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directory); ArgumentNullException.ThrowIfNull(plan); plan.Validate();
		string target = Path.GetFullPath(directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
		if (Directory.Exists(target) || File.Exists(target)) throw new IOException($"World-plan destination already exists: {target}");
		string parent = Path.GetDirectoryName(target) ?? throw new IOException("World-plan destination requires a parent directory.");
		Directory.CreateDirectory(parent);
		string temporary = Path.Combine(parent, $".{Path.GetFileName(target)}.tmp-{Guid.NewGuid():N}");
		try
		{
			Directory.CreateDirectory(temporary);
			Dictionary<string, byte[]> layers = RenderLayers(plan, cancellationToken);
			Dictionary<string, string> checksums = new(StringComparer.Ordinal);
			foreach ((string name, byte[] pixels) in layers)
			{
				cancellationToken.ThrowIfCancellationRequested();
				byte[] png = PngRgbaCodec.Encode(plan.Width, plan.Length, pixels);
				await File.WriteAllBytesAsync(Path.Combine(temporary, name), png, cancellationToken).ConfigureAwait(false);
				checksums[name] = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant();
			}
			BundleManifest manifest = BundleManifest.From(plan, checksums);
			await File.WriteAllTextAsync(Path.Combine(temporary, ManifestFileName), JsonSerializer.Serialize(manifest, JsonOptions), cancellationToken).ConfigureAwait(false);
			_ = await LoadAsync(temporary, plan.StructureCatalogHash, cancellationToken, plan.CeramicFishDefinitionHash).ConfigureAwait(false);
			Directory.Move(temporary, target);
		}
		catch
		{
			if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
			throw;
		}
	}

	public static async Task<WorldPlan> LoadAsync(string directory, string? expectedStructureCatalogHash = null, CancellationToken cancellationToken = default, string? expectedCeramicFishDefinitionHash = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(directory);
		string root = Path.GetFullPath(directory);
		string manifestPath = Path.Combine(root, ManifestFileName);
		if (!File.Exists(manifestPath)) throw new FileNotFoundException("World-plan manifest is missing.", manifestPath);
		BundleManifest manifest = JsonSerializer.Deserialize<BundleManifest>(await File.ReadAllTextAsync(manifestPath, cancellationToken).ConfigureAwait(false), JsonOptions)
			?? throw new InvalidDataException("World-plan manifest is empty.");
		ValidateManifest(manifest, expectedStructureCatalogHash, expectedCeramicFishDefinitionHash);
		Dictionary<string, byte[]> decoded = new(StringComparer.Ordinal);
		foreach (string name in LayerNames)
		{
			string path = Path.Combine(root, name);
			if (!File.Exists(path)) throw new FileNotFoundException($"World-plan layer '{name}' is missing.", path);
			byte[] png = await File.ReadAllBytesAsync(path, cancellationToken).ConfigureAwait(false);
			string actualHash = Convert.ToHexString(SHA256.HashData(png)).ToLowerInvariant();
			if (!manifest.LayerChecksums.TryGetValue(name, out string? expectedHash) || !CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actualHash), Convert.FromHexString(expectedHash)))
				throw new InvalidDataException($"World-plan layer '{name}' checksum mismatch.");
			(int width, int height, byte[] pixels) = PngRgbaCodec.Decode(png);
			if (width != manifest.Settings.Width || height != manifest.Settings.Length) throw new InvalidDataException($"World-plan layer '{name}' dimensions do not match the manifest.");
			decoded[name] = pixels;
		}

		int count = checked(manifest.Settings.Width * manifest.Settings.Length);
		byte[] heights = new byte[count], mask = new byte[count], hillMask = new byte[count], biomes = new byte[count], density = new byte[count];
		Dictionary<uint, WorldBiome> reversePalette = manifest.BiomePalette.ToDictionary(pair => pair.Value, pair => pair.Key);
		for (int x = 0; x < manifest.Settings.Width; x++) for (int z = 0; z < manifest.Settings.Length; z++)
		{
			int index = x * manifest.Settings.Length + z, pixel = (z * manifest.Settings.Width + x) * 4;
			byte[] height = decoded["height.png"]; heights[index] = height[pixel]; mask[index] = height[pixel + 3] == 0 ? (byte)0 : (byte)255;
			if (height[pixel] != height[pixel + 1] || height[pixel] != height[pixel + 2] || (height[pixel + 3] != 0 && height[pixel + 3] != 255)) throw new InvalidDataException("height.png is not canonical grayscale RGBA.");
			byte[] biome = decoded["biome.png"]; uint color = Pack(biome, pixel);
			if (!reversePalette.TryGetValue(color, out WorldBiome value)) throw new InvalidDataException($"biome.png contains unknown palette value 0x{color:X8}.");
			biomes[index] = (byte)value;
			byte[] tree = decoded["tree-density.png"]; if (tree[pixel] != tree[pixel + 1] || tree[pixel] != tree[pixel + 2] || tree[pixel + 3] != 255) throw new InvalidDataException("tree-density.png is not canonical grayscale RGBA.");
			density[index] = tree[pixel];
			byte[] hills = decoded["hill-mask.png"];
			if (hills[pixel] != hills[pixel + 1] || hills[pixel] != hills[pixel + 2]
				|| hills[pixel + 3] != mask[index] || hills[pixel] % WorldPlanRendering.HillEncodingScale != 0)
				throw new InvalidDataException("hill-mask.png is not canonical scaled grayscale RGBA.");
			hillMask[index] = (byte)(hills[pixel] / WorldPlanRendering.HillEncodingScale);
		}
		WorldPlan plan = new(manifest.Settings, heights, biomes, density, mask, hillMask, manifest.Ponds, manifest.Sites, manifest.Routes, manifest.Villages,
			manifest.StructureCatalogHash, manifest.VillageLayouts, manifest.CeramicFishDefinitionHash, manifest.VillageFailures);
		if (!decoded["features.png"].AsSpan().SequenceEqual(WorldPlanRendering.RenderFeatures(plan))) throw new InvalidDataException("features.png does not match manifest feature records.");
		if (!decoded["features-floor-2.png"].AsSpan().SequenceEqual(WorldPlanRendering.RenderVillageFloor(plan, 1))) throw new InvalidDataException("features-floor-2.png does not match manifest feature records.");
		if (!decoded["features-floor-3.png"].AsSpan().SequenceEqual(WorldPlanRendering.RenderVillageFloor(plan, 2))) throw new InvalidDataException("features-floor-3.png does not match manifest feature records.");
		if (!decoded["features-roof.png"].AsSpan().SequenceEqual(WorldPlanRendering.RenderVillageFloor(plan, 3))) throw new InvalidDataException("features-roof.png does not match manifest feature records.");
		if (!decoded["combined.png"].AsSpan().SequenceEqual(WorldPlanRendering.RenderCombined(plan, cancellationToken))) throw new InvalidDataException("combined.png does not match the semantic plan.");
		return plan;
	}

	private static Dictionary<string, byte[]> RenderLayers(WorldPlan plan, CancellationToken token) => new(StringComparer.Ordinal)
	{
		["height.png"] = WorldPlanRendering.RenderHeight(plan), ["biome.png"] = WorldPlanRendering.RenderBiome(plan),
		["hill-mask.png"] = WorldPlanRendering.RenderHills(plan),
		["tree-density.png"] = WorldPlanRendering.RenderTreeDensity(plan), ["features.png"] = WorldPlanRendering.RenderFeatures(plan),
		["features-floor-2.png"] = WorldPlanRendering.RenderVillageFloor(plan, 1),
		["features-floor-3.png"] = WorldPlanRendering.RenderVillageFloor(plan, 2),
		["features-roof.png"] = WorldPlanRendering.RenderVillageFloor(plan, 3),
		["combined.png"] = WorldPlanRendering.RenderCombined(plan, token),
	};

	private static void ValidateManifest(BundleManifest manifest, string? expectedHash, string? expectedVillageHash)
	{
		if (manifest.FormatVersion != WorldPlan.CurrentFormatVersion) throw new NotSupportedException($"World-plan format {manifest.FormatVersion} is unsupported.");
		if (manifest.GeneratorVersion != WorldPlan.CurrentGeneratorVersion) throw new NotSupportedException($"World-plan generator {manifest.GeneratorVersion} is unsupported.");
		if (manifest.MaterializerVersion != WorldPlan.CurrentMaterializerVersion) throw new NotSupportedException($"World-plan materializer {manifest.MaterializerVersion} is unsupported.");
		manifest.Settings.Validate();
		if (manifest.LayerChecksums.Count != LayerNames.Length || LayerNames.Any(name => !manifest.LayerChecksums.ContainsKey(name))) throw new InvalidDataException("World-plan layer checksum directory is incomplete.");
		foreach (string hash in manifest.LayerChecksums.Values) if (hash.Length != 64 || !hash.All(Uri.IsHexDigit)) throw new InvalidDataException("World-plan layer checksum is malformed.");
		if (expectedHash is not null && !string.Equals(expectedHash, manifest.StructureCatalogHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("World-plan structure catalog hash does not match the active catalog.");
		if (expectedVillageHash is not null && !string.Equals(expectedVillageHash, manifest.CeramicFishDefinitionHash, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("World-plan CeramicFish definition hash does not match the active definition.");
		if (manifest.BiomePalette.Count != Enum.GetValues<WorldBiome>().Length || WorldPlanRendering.BiomePalette.Any(pair => !manifest.BiomePalette.TryGetValue(pair.Key, out uint color) || color != pair.Value)) throw new InvalidDataException("World-plan biome palette is invalid.");
		HashSet<string> siteIds = manifest.Sites.Select(site => site.Id).ToHashSet(StringComparer.Ordinal);
		if (siteIds.Count != manifest.Sites.Length || manifest.Routes.Any(route => !siteIds.Contains(route.SourceSite) || !siteIds.Contains(route.DestinationSite))) throw new InvalidDataException("World-plan features contain duplicate or unresolved site references.");
	}

	private static uint Pack(byte[] bytes, int index) => ((uint)bytes[index] << 24) | ((uint)bytes[index + 1] << 16) | ((uint)bytes[index + 2] << 8) | bytes[index + 3];

	private sealed record BundleManifest(
		int FormatVersion, int GeneratorVersion, int MaterializerVersion, WorldGenerationSettings Settings,
		Dictionary<WorldBiome, uint> BiomePalette, string StructureCatalogHash, string CeramicFishDefinitionHash, PlannedPond[] Ponds,
		PlannedWorldSite[] Sites, PlannedWorldRoute[] Routes, PlannedVillageArea[] Villages, PlannedVillageLayout[] VillageLayouts,
		PlannedVillageFailure[] VillageFailures, Dictionary<string, string> LayerChecksums)
	{
		internal static BundleManifest From(WorldPlan plan, Dictionary<string, string> checksums) => new(
			WorldPlan.CurrentFormatVersion, WorldPlan.CurrentGeneratorVersion, WorldPlan.CurrentMaterializerVersion, plan.Settings,
			WorldPlanRendering.BiomePalette.ToDictionary(), plan.StructureCatalogHash, plan.CeramicFishDefinitionHash, plan.Ponds.ToArray(),
			plan.Sites.ToArray(), plan.Routes.ToArray(), plan.Villages.ToArray(), plan.VillageLayouts.ToArray(), plan.VillageFailures.ToArray(), checksums);
	}
}
