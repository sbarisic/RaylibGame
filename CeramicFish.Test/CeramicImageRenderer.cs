using Voxelgine.WorldGeneration;

namespace CeramicFish.TestHarness;

internal readonly record struct RgbaColor(byte R, byte G, byte B, byte A);

internal static class CeramicImageRenderer
{
	internal static async ValueTask SaveAsync(
		string path,
		CeramicFishDefinition definition,
		CeramicGenerationRequest request,
		CeramicGenerationResult result,
		CancellationToken cancellationToken = default)
	{
		byte[] pixels = Render(definition, request, result);
		byte[] png = PngRgbaCodec.Encode(CeramicTestCatalog.ImageSize,
			CeramicTestCatalog.ImageSize, pixels);
		await File.WriteAllBytesAsync(path, png, cancellationToken).ConfigureAwait(false);
	}

	private static byte[] Render(
		CeramicFishDefinition definition,
		CeramicGenerationRequest request,
		CeramicGenerationResult result)
	{
		if (!result.Success)
			throw new InvalidOperationException(
				$"CeramicFish generation failed: {result.Failure?.Message ?? result.Status.ToString()}.");
		HashSet<CeramicCell> region = request.Region.ToHashSet();
		if (region.Count != request.Region.Count)
			throw new InvalidDataException("The CeramicFish request region contains duplicate cells.");
		if (result.Placements.Count != region.Count)
			throw new InvalidDataException("CeramicFish must return exactly one placement per active region cell.");

		Dictionary<string, CeramicPrefabDefinition> prefabs = definition.Prefabs
			.ToDictionary(prefab => prefab.Id, StringComparer.Ordinal);
		Dictionary<CeramicCell, CeramicPlacement> placements = [];
		foreach (CeramicPlacement placement in result.Placements)
		{
			if (!region.Contains(placement.Cell))
				throw new InvalidDataException($"Placement {placement.Cell} is outside the requested region.");
			if (!placements.TryAdd(placement.Cell, placement))
				throw new InvalidDataException($"Cell {placement.Cell} contains multiple placements.");
		}

		byte[] rgba = new byte[CeramicTestCatalog.ImageSize * CeramicTestCatalog.ImageSize * 4];
		bool[] occupied = new bool[CeramicTestCatalog.ImageSize * CeramicTestCatalog.ImageSize];
		foreach (CeramicPlacement placement in result.Placements)
		{
			if (!prefabs.TryGetValue(placement.PrefabId, out CeramicPrefabDefinition? prefab))
				throw new InvalidDataException($"Unknown CeramicFish prefab '{placement.PrefabId}'.");
			(int rotatedWidth, int rotatedLength) = CeramicGeometry.RotateSize(
				prefab.SizeX, prefab.SizeZ, placement.Rotation);
			if (rotatedWidth != CeramicTestCatalog.PrefabSize
				|| rotatedLength != CeramicTestCatalog.PrefabSize)
				throw new InvalidDataException($"Prefab '{prefab.Id}' does not remain 3x3 after rotation.");
			foreach (CeramicEntity source in prefab.Entities)
			{
				if (source.Y != 0)
					throw new InvalidDataException($"Prefab '{prefab.Id}' contains a non-raster entity.");
				CeramicEntity entity = CeramicGeometry.RotateEntity(
					source, prefab.SizeX, prefab.SizeZ, placement.Rotation);
				int x = checked(placement.Cell.X * CeramicTestCatalog.PrefabSize + entity.X);
				int y = checked(placement.Cell.Z * CeramicTestCatalog.PrefabSize + entity.Z);
				if ((uint)x >= CeramicTestCatalog.ImageSize || (uint)y >= CeramicTestCatalog.ImageSize)
					throw new InvalidDataException($"Prefab '{prefab.Id}' writes outside the 256x256 image.");
				int pixel = y * CeramicTestCatalog.ImageSize + x;
				if (occupied[pixel])
					throw new InvalidDataException($"Multiple prefab entities overlap pixel ({x},{y}).");
				occupied[pixel] = true;
				if (!CeramicTestCatalog.Palette.TryGetValue(entity.Value, out RgbaColor color))
					throw new InvalidDataException($"No test color is mapped for entity value {entity.Value}.");
				int destination = pixel * 4;
				rgba[destination] = color.R;
				rgba[destination + 1] = color.G;
				rgba[destination + 2] = color.B;
				rgba[destination + 3] = color.A;
			}
		}
		return rgba;
	}
}
