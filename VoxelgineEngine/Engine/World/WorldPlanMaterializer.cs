using System.Security.Cryptography;
using System.Text;
using Voxelgine.Engine;
using Voxelgine.Engine.World.Structures;
using Voxelgine.WorldGeneration;

namespace Voxelgine.Graphics;

public static class WorldPlanMaterializer
{
	public static WorldPlan GeneratePlan(
		int width,
		int length,
		int seed,
		StructureBlueprintCatalog catalog,
		CancellationToken cancellationToken = default,
		IProgress<WorldGenerationProgress> progress = null,
		CeramicVillageCatalog ceramicFish = null)
	{
		StructureTemplateDescriptor[] descriptors = catalog is null ? [] : Describe(catalog);
		string catalogHash = catalog is null ? string.Empty : ComputeCatalogHash(catalog);
		return WorldPlanGenerator.Generate(new(seed, width, length, 64), descriptors, catalogHash, progress,
			cancellationToken, ceramicFish?.Definition, ceramicFish?.Hash ?? string.Empty);
	}

	public static void MaterializeAtomically(
		ChunkMap destination,
		WorldPlan plan,
		StructureBlueprintCatalog catalog,
		CancellationToken cancellationToken = default,
		CeramicVillageCatalog ceramicFish = null)
	{
		ArgumentNullException.ThrowIfNull(destination); ArgumentNullException.ThrowIfNull(plan);
		plan.Validate(); cancellationToken.ThrowIfCancellationRequested();
		if (catalog is not null)
		{
			string activeHash = ComputeCatalogHash(catalog);
			if (!string.Equals(plan.StructureCatalogHash, activeHash, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException("World plan structure catalog does not match the active engine catalog.");
		}
		else if (plan.Sites.Count != 0)
			throw new InvalidDataException("A world plan with structures requires a structure blueprint catalog.");
		if (plan.VillageLayouts.Count != 0)
		{
			if (ceramicFish is null) throw new InvalidDataException("A world plan with village layouts requires a CeramicFish definition.");
			if (!string.Equals(plan.CeramicFishDefinitionHash, ceramicFish.Hash, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException("World plan CeramicFish definition does not match the active definition.");
		}

		WorldPlanBuildResult result = WorldPlanVoxelBuilder.Build(plan, catalog, ceramicFish, cancellationToken);
		cancellationToken.ThrowIfCancellationRequested();
		destination.ReplaceAllColumns(result.Columns);
		destination.RestoreGeneratedFeatures(result.Features);
		destination.PublishStructureGenerationTimings(result.Timings);
		destination.ResetLighting();
	}

	internal static StructureTemplateDescriptor[] Describe(StructureBlueprintCatalog catalog) => catalog.Blueprints
		.OrderBy(static blueprint => blueprint.Id, StringComparer.Ordinal)
		.Select(static blueprint => new StructureTemplateDescriptor(
			blueprint.Id,
			(WorldStructureRole)(byte)blueprint.Role,
			blueprint.Size.X,
			blueprint.Size.Z,
			blueprint.Anchor.X,
			blueprint.Anchor.Z,
			blueprint.AllowedRotations.ToArray(),
			blueprint.Connectors.Select(connector => new StructureConnectorDescriptor(
				connector.Id,
				connector.Kind == StructureConnectorKind.Conduit ? WorldFeatureKind.Conduit : WorldFeatureKind.Road,
				connector.Position.X, connector.Position.Z, connector.Direction.X, connector.Direction.Z)).ToArray()))
		.ToArray();

	internal static string ComputeCatalogHash(IReadOnlyList<StructureTemplateDescriptor> descriptors)
	{
		StringBuilder canonical = new();
		foreach (StructureTemplateDescriptor descriptor in descriptors.OrderBy(static value => value.Id, StringComparer.Ordinal))
		{
			canonical.Append(descriptor.Id).Append('|').Append((byte)descriptor.Role).Append('|')
				.Append(descriptor.Width).Append('x').Append(descriptor.Length).Append('|')
				.Append(descriptor.AnchorX).Append(',').Append(descriptor.AnchorZ).Append('|')
				.AppendJoin(',', descriptor.AllowedRotations.Order()).Append(';');
			foreach (StructureConnectorDescriptor connector in descriptor.Connectors.OrderBy(static value => value.Id, StringComparer.Ordinal))
				canonical.Append(connector.Id).Append(',').Append((byte)connector.Kind).Append(',').Append(connector.X).Append(',').Append(connector.Z).Append(',').Append(connector.DirectionX).Append(',').Append(connector.DirectionZ).Append(';');
			canonical.AppendLine();
		}
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
	}

	internal static string ComputeCatalogHash(StructureBlueprintCatalog catalog)
	{
		StringBuilder canonical = new StringBuilder(ComputeCatalogHash(Describe(catalog))).AppendLine();
		foreach (StructureBlueprint blueprint in catalog.Blueprints.OrderBy(static value => value.Id, StringComparer.Ordinal))
		{
			canonical.Append(blueprint.Id).Append('|').Append(blueprint.Size).Append('|').Append(blueprint.Anchor).Append('|');
			foreach (char cell in blueprint.Cells) canonical.Append(cell);
			canonical.Append('|');
			foreach ((char symbol, BlockType block) in blueprint.Palette.OrderBy(static pair => pair.Key)) canonical.Append(symbol).Append('=').Append((ushort)block).Append(';');
			foreach (StructureMarker marker in blueprint.Markers.OrderBy(static value => value.Id, StringComparer.Ordinal)) canonical.Append(marker.Id).Append(',').Append((byte)marker.Kind).Append(',').Append(marker.Position).Append(',').Append(marker.ExpectedBlock).Append(',').Append(marker.Data).Append(';');
			foreach (StructureFogVolume fog in blueprint.FogVolumes.OrderBy(static value => value.Id, StringComparer.Ordinal)) canonical.Append(fog.Id).Append(',').Append(fog.Minimum).Append(',').Append(fog.Size).Append(',').Append(fog.Fog).Append(';');
			canonical.AppendLine();
		}
		return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical.ToString()))).ToLowerInvariant();
	}
}
