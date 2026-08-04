namespace Voxelgine.WorldGeneration;

internal sealed class CeramicCompiledCatalog
{
	private CeramicCompiledCatalog(
		IReadOnlyList<CeramicCompiledVariant> variants,
		IReadOnlyList<CeramicTopologyOption> options)
	{
		Variants = variants;
		Options = options;
	}

	internal IReadOnlyList<CeramicCompiledVariant> Variants { get; }
	internal IReadOnlyList<CeramicTopologyOption> Options { get; }

	internal static CeramicCompiledCatalog Create(
		CeramicFishDefinition definition,
		CeramicGenerationRequest request)
	{
		HashSet<string> relevantTags = new(StringComparer.Ordinal);
		foreach (CeramicConnectionPolicy policy in definition.ConnectionPolicies)
			relevantTags.Add(policy.SocketType);
		foreach (CeramicComponentAdjacencyPolicy policy in definition.ComponentAdjacencyPolicies)
			relevantTags.Add(policy.RequiredAdjacentTag);
		foreach (CeramicTagQuota quota in request.TagQuotas) relevantTags.Add(quota.Tag);
		foreach (CeramicAnchor anchor in request.Anchors)
			foreach (string tag in anchor.RequiredTags) relevantTags.Add(tag);
		foreach (CeramicCellConstraint constraint in request.CellConstraints)
		{
			foreach (string tag in constraint.RequiredTags) relevantTags.Add(tag);
			foreach (string tag in constraint.ForbiddenTags) relevantTags.Add(tag);
		}

		List<CeramicCompiledVariant> variants = [];
		foreach (CeramicPrefabDefinition prefab in definition.Prefabs.OrderBy(item => item.Id,
			StringComparer.Ordinal))
		foreach (CeramicRotation rotation in CeramicSolverUtilities.Rotations(prefab.AllowedRotations))
		{
			string[] tags = prefab.Tags.OrderBy(tag => tag, StringComparer.Ordinal).ToArray();
			string[] sockets = Enum.GetValues<CeramicDirection>()
				.Select(direction => CeramicGeometry.GetSocket(prefab, direction, rotation).Type)
				.ToArray();
			variants.Add(new(prefab, rotation, tags, sockets));
		}

		Dictionary<string, CeramicTopologyOption> grouped = new(StringComparer.Ordinal);
		foreach (CeramicCompiledVariant variant in variants)
		{
			string[] tags = variant.Tags.Where(relevantTags.Contains).ToArray();
			string key = string.Join('\u001f', tags) + "\u001e" + string.Join('\u001f', variant.Sockets);
			if (!grouped.TryGetValue(key, out CeramicTopologyOption? option))
			{
				option = new(key, tags, variant.Sockets.ToArray(), []);
				grouped.Add(key, option);
			}
			option.Variants.Add(variant);
		}
		return new(variants, grouped.Values.OrderBy(option => option.Key, StringComparer.Ordinal).ToArray());
	}
}

internal sealed class CeramicCompiledVariant
{
	internal CeramicCompiledVariant(
		CeramicPrefabDefinition prefab,
		CeramicRotation rotation,
		string[] tags,
		string[] sockets)
	{
		Prefab = prefab;
		Rotation = rotation;
		Tags = tags;
		TagSet = tags.ToHashSet(StringComparer.Ordinal);
		Sockets = sockets;
	}

	internal CeramicPrefabDefinition Prefab { get; }
	internal CeramicRotation Rotation { get; }
	internal string[] Tags { get; }
	internal HashSet<string> TagSet { get; }
	internal string[] Sockets { get; }
	internal int Weight => Prefab.Weight;
}

internal sealed class CeramicTopologyOption
{
	internal CeramicTopologyOption(
		string key,
		string[] tags,
		string[] sockets,
		List<CeramicCompiledVariant> variants)
	{
		Key = key;
		Tags = tags;
		TagSet = tags.ToHashSet(StringComparer.Ordinal);
		Sockets = sockets;
		Variants = variants;
	}

	internal string Key { get; }
	internal string[] Tags { get; }
	internal HashSet<string> TagSet { get; }
	internal string[] Sockets { get; }
	internal List<CeramicCompiledVariant> Variants { get; }
}

internal static class CeramicSolverUtilities
{
	internal static IEnumerable<CeramicRotation> Rotations(CeramicRotationOptions options)
	{
		if ((options & CeramicRotationOptions.Rot0) != 0) yield return CeramicRotation.Rot0;
		if ((options & CeramicRotationOptions.Rot90CW) != 0) yield return CeramicRotation.Rot90CW;
		if ((options & CeramicRotationOptions.Rot180CW) != 0) yield return CeramicRotation.Rot180CW;
		if ((options & CeramicRotationOptions.Rot270CW) != 0) yield return CeramicRotation.Rot270CW;
	}

	internal static bool Allows(CeramicCellConstraint constraint, CeramicCompiledVariant variant) =>
		(constraint.RequiredPrefabId is null
			|| string.Equals(constraint.RequiredPrefabId, variant.Prefab.Id, StringComparison.Ordinal))
		&& (!constraint.RequiredRotation.HasValue || constraint.RequiredRotation == variant.Rotation)
		&& constraint.RequiredTags.All(variant.TagSet.Contains)
		&& !constraint.ForbiddenTags.Any(variant.TagSet.Contains);

	internal static bool AllowsCell(
		CeramicGenerationRequest request,
		HashSet<CeramicCell> region,
		CeramicCell cell,
		CeramicCompiledVariant variant)
	{
		foreach (CeramicAnchor anchor in request.Anchors)
			if (anchor.Cell == cell && !anchor.RequiredTags.All(variant.TagSet.Contains)) return false;
		foreach (CeramicCellConstraint constraint in request.CellConstraints)
			if (constraint.Cell == cell && !Allows(constraint, variant)) return false;
		foreach (CeramicSocketConstraint constraint in request.SocketConstraints)
			if (constraint.Cell == cell && !string.Equals(variant.Sockets[(int)constraint.Direction],
				constraint.SocketType, StringComparison.Ordinal)) return false;
		foreach (CeramicDirection direction in Enum.GetValues<CeramicDirection>())
		{
			bool outside = false;
			CeramicCell neighbor = default;
			try { neighbor = CeramicGeometry.Offset(cell, direction); }
			catch (OverflowException) { outside = true; }
			if (!outside && region.Contains(neighbor)) continue;
			CeramicEntrance? entrance = request.Entrances.FirstOrDefault(item =>
				item.Cell == cell && item.Direction == direction);
			string expected = entrance?.SocketType ?? request.BoundarySocket;
			if (!string.Equals(variant.Sockets[(int)direction], expected, StringComparison.Ordinal))
				return false;
		}
		return true;
	}

	internal static string[] GetTopologySockets(CeramicTopologyCell cell)
	{
		if (cell.Sockets.Count != 4
			|| cell.Sockets.GroupBy(socket => socket.Direction).Any(group => group.Count() != 1))
			throw new InvalidDataException("A topology cell does not contain exactly four directions.");
		return Enum.GetValues<CeramicDirection>().Select(direction =>
			cell.Sockets.Single(socket => socket.Direction == direction).SocketType).ToArray();
	}
}

internal sealed class CeramicConstraintIndex
{
	private readonly CeramicGenerationRequest request;
	private readonly HashSet<CeramicCell> region;
	private readonly Dictionary<CeramicCell, List<CeramicAnchor>> anchors;
	private readonly Dictionary<CeramicCell, List<CeramicCellConstraint>> cellConstraints;
	private readonly Dictionary<CeramicCell, List<CeramicSocketConstraint>> socketConstraints;
	private readonly Dictionary<(CeramicCell Cell, CeramicDirection Direction), CeramicEntrance> entrances;

	internal CeramicConstraintIndex(CeramicGenerationRequest request, HashSet<CeramicCell> region)
	{
		this.request = request;
		this.region = region;
		anchors = request.Anchors.GroupBy(item => item.Cell)
			.ToDictionary(group => group.Key, group => group.ToList());
		cellConstraints = request.CellConstraints.GroupBy(item => item.Cell)
			.ToDictionary(group => group.Key, group => group.ToList());
		socketConstraints = request.SocketConstraints.GroupBy(item => item.Cell)
			.ToDictionary(group => group.Key, group => group.ToList());
		entrances = request.Entrances.ToDictionary(item => (item.Cell, item.Direction));
	}

	internal bool AllowsCell(CeramicCell cell, CeramicCompiledVariant variant)
	{
		if (anchors.TryGetValue(cell, out List<CeramicAnchor>? localAnchors)
			&& localAnchors.Any(anchor => !anchor.RequiredTags.All(variant.TagSet.Contains))) return false;
		if (cellConstraints.TryGetValue(cell, out List<CeramicCellConstraint>? localConstraints)
			&& localConstraints.Any(constraint => !CeramicSolverUtilities.Allows(constraint, variant))) return false;
		if (socketConstraints.TryGetValue(cell, out List<CeramicSocketConstraint>? localSockets)
			&& localSockets.Any(constraint => !string.Equals(variant.Sockets[(int)constraint.Direction],
				constraint.SocketType, StringComparison.Ordinal))) return false;
		foreach (CeramicDirection direction in Enum.GetValues<CeramicDirection>())
		{
			bool outside = false;
			CeramicCell neighbor = default;
			try { neighbor = CeramicGeometry.Offset(cell, direction); }
			catch (OverflowException) { outside = true; }
			if (!outside && region.Contains(neighbor)) continue;
			string expected = entrances.TryGetValue((cell, direction), out CeramicEntrance? entrance)
				? entrance.SocketType : request.BoundarySocket;
			if (!string.Equals(variant.Sockets[(int)direction], expected, StringComparison.Ordinal))
				return false;
		}
		return true;
	}
}

internal sealed class CeramicDeterministicRandom
{
	private ulong state;

	internal CeramicDeterministicRandom(ulong seed) => state = seed;

	internal ulong NextUInt64()
	{
		unchecked
		{
			state += 0x9E3779B97F4A7C15UL;
			ulong value = state;
			value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
			value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
			return value ^ (value >> 31);
		}
	}

	internal ulong NextBelow(ulong exclusiveMaximum)
	{
		if (exclusiveMaximum == 0) throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
		ulong threshold = unchecked(0UL - exclusiveMaximum) % exclusiveMaximum;
		while (true)
		{
			ulong value = NextUInt64();
			if (value >= threshold) return value % exclusiveMaximum;
		}
	}

	internal int NextInt(int exclusiveMaximum) => checked((int)NextBelow((uint)exclusiveMaximum));

	internal void Shuffle<T>(IList<T> values)
	{
		for (int index = values.Count - 1; index > 0; index--)
		{
			int other = NextInt(index + 1);
			(values[index], values[other]) = (values[other], values[index]);
		}
	}
}

