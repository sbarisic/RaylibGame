namespace Voxelgine.WorldGeneration;

internal static class CeramicFishValidation
{
	internal static CeramicValidationResult ValidateDefinition(CeramicFishDefinition? definition)
	{
		List<CeramicValidationError> errors = [];
		if (definition is null)
			return new([new("definition-null", "The CeramicFish definition is null.", "$")]);
		if (definition.FormatVersion != CeramicFishDefinition.CurrentFormatVersion)
			Add("definition-format-version", $"Format version {definition.FormatVersion} is unsupported.",
				"$.formatVersion");
		if (string.IsNullOrWhiteSpace(definition.Id) || definition.Id.Length > 128)
			Add("definition-id", "The definition ID must contain 1-128 characters.", "$.id");
		if (definition.Prefabs is null || definition.Prefabs.Count == 0)
			Add("definition-prefabs", "At least one prefab is required.", "$.prefabs");
		if (definition.ConnectionPolicies is null)
			Add("definition-policies", "Connection policies are required.", "$.connectionPolicies");
		if (definition.ComponentAdjacencyPolicies is null)
			Add("definition-adjacency-policies", "Component adjacency policies are required.",
				"$.componentAdjacencyPolicies");
		if (definition.ComponentTagPolicies is null)
			Add("definition-component-tag-policies", "Component tag policies are required.",
				"$.componentTagPolicies");
		if (definition.ComponentEntryPolicies is null)
			Add("definition-component-entry-policies", "Component entry policies are required.",
				"$.componentEntryPolicies");
		if (definition.WallFeaturePolicies is null)
			Add("definition-wall-feature-policies", "Wall feature policies are required.",
				"$.wallFeaturePolicies");
		if (definition.InteriorFeaturePolicies is null)
			Add("definition-interior-feature-policies", "Interior feature policies are required.",
				"$.interiorFeaturePolicies");
		if (errors.Count != 0 && (definition.Prefabs is null || definition.ConnectionPolicies is null
			|| definition.ComponentAdjacencyPolicies is null
			|| definition.ComponentTagPolicies is null
			|| definition.ComponentEntryPolicies is null
			|| definition.WallFeaturePolicies is null
			|| definition.InteriorFeaturePolicies is null)) return new(errors);

		IReadOnlyList<CeramicPrefabDefinition> prefabs = definition.Prefabs!;
		IReadOnlyList<CeramicConnectionPolicy> connectionPolicies = definition.ConnectionPolicies!;
		IReadOnlyList<CeramicComponentAdjacencyPolicy> adjacencyPolicies =
			definition.ComponentAdjacencyPolicies!;
		IReadOnlyList<CeramicComponentTagPolicy> componentTagPolicies =
			definition.ComponentTagPolicies!;
		IReadOnlyList<CeramicComponentEntryPolicy> componentEntryPolicies =
			definition.ComponentEntryPolicies!;
		IReadOnlyList<CeramicWallFeaturePolicy> wallFeaturePolicies =
			definition.WallFeaturePolicies!;
		IReadOnlyList<CeramicInteriorFeaturePolicy> interiorFeaturePolicies =
			definition.InteriorFeaturePolicies!;
		HashSet<string> prefabIds = new(StringComparer.Ordinal);
		int? sizeX = null;
		int? sizeZ = null;
		HashSet<string> socketTypes = new(StringComparer.Ordinal) { CeramicSocket.NoConnection };
		for (int index = 0; index < prefabs.Count; index++)
		{
			CeramicPrefabDefinition? prefab = prefabs[index];
			string path = $"$.prefabs[{index}]";
			if (prefab is null)
			{
				Add("prefab-null", "Prefab entries cannot be null.", path);
				continue;
			}
			if (string.IsNullOrWhiteSpace(prefab.Id) || prefab.Id.Length > 128)
				Add("prefab-id", "Prefab IDs must contain 1-128 characters.", path + ".id");
			else if (!prefabIds.Add(prefab.Id))
				Add("prefab-id-duplicate", $"Prefab ID '{prefab.Id}' is duplicated.", path + ".id");
			if (prefab.Tags is null || prefab.Tags.Any(string.IsNullOrWhiteSpace)
				|| prefab.Tags.Distinct(StringComparer.Ordinal).Count() != prefab.Tags.Count)
				Add("prefab-tags", "Prefab tags must be non-empty and unique.", path + ".tags");
			if (prefab.SizeX <= 0 || prefab.SizeY <= 0 || prefab.SizeZ <= 0)
				Add("prefab-size", "Prefab dimensions must be positive.", path);
			if (prefab.SizeX != prefab.SizeZ)
				Add("prefab-square", "Prefab X/Z footprints must be square.", path);
			sizeX ??= prefab.SizeX;
			sizeZ ??= prefab.SizeZ;
			if (sizeX != prefab.SizeX || sizeZ != prefab.SizeZ)
				Add("prefab-footprint", "All prefabs must use the same X/Z footprint.", path);
			if (prefab.Weight <= 0)
				Add("prefab-weight", "Prefab weight must be positive.", path + ".weight");
			if (prefab.AllowedRotations == CeramicRotationOptions.None
				|| (prefab.AllowedRotations & ~CeramicRotationOptions.All) != 0)
				Add("prefab-rotations", "Prefab rotations are invalid.", path + ".allowedRotations");
			if (prefab.Entities is null)
				Add("prefab-entities", "Prefab entities are required.", path + ".entities");
			else
			{
				HashSet<(int, int, int)> occupied = [];
				foreach (CeramicEntity? entity in prefab.Entities)
				{
					if (entity is null || entity.X < 0 || entity.X >= prefab.SizeX
						|| entity.Y < 0 || entity.Y >= prefab.SizeY
						|| entity.Z < 0 || entity.Z >= prefab.SizeZ)
						Add("prefab-entity-bounds", "A prefab entity is outside the prefab.", path + ".entities");
					else if (!occupied.Add((entity.X, entity.Y, entity.Z)))
						Add("prefab-entity-overlap", "Prefab entities overlap.", path + ".entities");
				}
			}
			if (prefab.Sockets is null || prefab.Sockets.Count != 4
				|| prefab.Sockets.Any(socket => socket is null || string.IsNullOrWhiteSpace(socket.Type))
				|| prefab.Sockets.GroupBy(socket => socket.Direction).Any(group => group.Count() != 1)
				|| prefab.Sockets.Any(socket => !Enum.IsDefined(socket.Direction)))
				Add("prefab-sockets", "Each prefab requires one valid socket per direction.", path + ".sockets");
			else foreach (CeramicSocket socket in prefab.Sockets) socketTypes.Add(socket.Type);
		}

		HashSet<string> policyTypes = new(StringComparer.Ordinal);
		for (int index = 0; index < connectionPolicies.Count; index++)
		{
			CeramicConnectionPolicy? policy = connectionPolicies[index];
			string path = $"$.connectionPolicies[{index}]";
			if (policy is null)
			{
				Add("policy-null", "Connection policies cannot be null.", path);
				continue;
			}
			if (string.IsNullOrWhiteSpace(policy.SocketType)
				|| policy.SocketType == CeramicSocket.NoConnection)
				Add("policy-socket", "A policy requires a connection-bearing socket type.", path + ".socketType");
			else
			{
				if (!policyTypes.Add(policy.SocketType))
					Add("policy-duplicate", $"Policy '{policy.SocketType}' is duplicated.", path);
				if (!socketTypes.Contains(policy.SocketType))
					Add("policy-socket-unknown", $"Socket type '{policy.SocketType}' is not authored.", path);
			}
			ValidateRange(policy.Degree, path + ".degree", maximumAllowed: 4);
			ValidateRange(policy.ComponentCount, path + ".componentCount");
			ValidateRange(policy.ExternalConnectionCount, path + ".externalConnectionCount");
			if (policy.RequireEntranceReachability && policy.ExternalConnectionCount.Maximum == 0)
				Add("policy-entrance-impossible", "Entrance reachability requires external connections.", path);
		}

		HashSet<(string, string)> adjacencyKeys = [];
		for (int index = 0; index < adjacencyPolicies.Count; index++)
		{
			CeramicComponentAdjacencyPolicy? policy = adjacencyPolicies[index];
			string path = $"$.componentAdjacencyPolicies[{index}]";
			if (policy is null || string.IsNullOrWhiteSpace(policy.ComponentSocketType)
				|| string.IsNullOrWhiteSpace(policy.RequiredAdjacentTag)
				|| policy.MinimumAdjacentEdgesPerComponent <= 0)
			{
				Add("adjacency-policy", "The component adjacency policy is invalid.", path);
				continue;
			}
			if (!policyTypes.Contains(policy.ComponentSocketType))
				Add("adjacency-policy-socket", "The adjacency policy references an unknown network.", path);
			if (!adjacencyKeys.Add((policy.ComponentSocketType, policy.RequiredAdjacentTag)))
				Add("adjacency-policy-duplicate", "The adjacency policy is duplicated.", path);
			if (!prefabs.Any(prefab => prefab.Tags.Contains(policy.RequiredAdjacentTag,
				StringComparer.Ordinal)))
				Add("adjacency-policy-tag", "The required adjacent tag is not authored.", path);
		}
		HashSet<(string, string)> componentTagKeys = [];
		for (int index = 0; index < componentTagPolicies.Count; index++)
		{
			CeramicComponentTagPolicy? policy = componentTagPolicies[index];
			string path = $"$.componentTagPolicies[{index}]";
			if (policy is null || string.IsNullOrWhiteSpace(policy.ComponentSocketType)
				|| string.IsNullOrWhiteSpace(policy.RequiredTag))
			{
				Add("component-tag-policy", "The component tag policy is invalid.", path);
				continue;
			}
			if (!policyTypes.Contains(policy.ComponentSocketType))
				Add("component-tag-policy-socket",
					"The component tag policy references an unknown network.", path);
			if (!componentTagKeys.Add((policy.ComponentSocketType, policy.RequiredTag)))
				Add("component-tag-policy-duplicate", "The component tag policy is duplicated.", path);
			if (!prefabs.Any(prefab => prefab.Tags.Contains(policy.RequiredTag,
				StringComparer.Ordinal)))
				Add("component-tag-policy-tag", "The required component tag is not authored.", path);
			ValidateRange(policy.TagCountPerComponent, path + ".tagCountPerComponent");
		}
		HashSet<string> componentEntryNetworks = new(StringComparer.Ordinal);
		for (int index = 0; index < componentEntryPolicies.Count; index++)
		{
			CeramicComponentEntryPolicy? policy = componentEntryPolicies[index];
			string path = $"$.componentEntryPolicies[{index}]";
			if (policy is null || string.IsNullOrWhiteSpace(policy.ComponentSocketType)
				|| string.IsNullOrWhiteSpace(policy.RootEntryTag)
				|| string.IsNullOrWhiteSpace(policy.RootAdjacentTag)
				|| string.IsNullOrWhiteSpace(policy.ParentDoorTag)
				|| string.IsNullOrWhiteSpace(policy.ChildEntryTag))
			{
				Add("component-entry-policy", "The component entry policy is invalid.", path);
				continue;
			}
			if (!policyTypes.Contains(policy.ComponentSocketType))
				Add("component-entry-policy-socket",
					"The component entry policy references an unknown network.", path);
			if (!componentEntryNetworks.Add(policy.ComponentSocketType))
				Add("component-entry-policy-duplicate",
					"Only one component entry policy is allowed per socket network.", path);
			string[] entryTags = [policy.RootEntryTag, policy.ParentDoorTag, policy.ChildEntryTag];
			if (entryTags.Distinct(StringComparer.Ordinal).Count() != entryTags.Length)
				Add("component-entry-policy-tags",
					"Root, parent-door, and child-entry tags must be distinct.", path);
			foreach (string tag in entryTags.Append(policy.RootAdjacentTag).Distinct(StringComparer.Ordinal))
				if (!prefabs.Any(prefab => prefab.Tags is not null
					&& prefab.Tags.Contains(tag, StringComparer.Ordinal)))
					Add("component-entry-policy-tag",
						$"The component entry tag '{tag}' is not authored.", path);
			if (!prefabs.Any(prefab => prefab.Tags is not null
				&& prefab.Tags.Contains(policy.ParentDoorTag, StringComparer.Ordinal)
				&& prefab.Tags.Contains(policy.ChildEntryTag, StringComparer.Ordinal)))
				Add("component-entry-policy-shared-door",
					"A prefab carrying both room-door tags is required for a shared partition.", path);
			ValidateRange(policy.AdditionalRoomsPerRoot, path + ".additionalRoomsPerRoot");
		}
		HashSet<(string, string)> wallFeatureKeys = [];
		for (int index = 0; index < wallFeaturePolicies.Count; index++)
		{
			CeramicWallFeaturePolicy? policy = wallFeaturePolicies[index];
			string path = $"$.wallFeaturePolicies[{index}]";
			if (policy is null || string.IsNullOrWhiteSpace(policy.ComponentSocketType)
				|| string.IsNullOrWhiteSpace(policy.FeatureTag))
			{
				Add("wall-feature-policy", "The wall feature policy is invalid.", path);
				continue;
			}
			if (!policyTypes.Contains(policy.ComponentSocketType))
				Add("wall-feature-policy-socket",
					"The wall feature policy references an unknown network.", path);
			if (!wallFeatureKeys.Add((policy.ComponentSocketType, policy.FeatureTag)))
				Add("wall-feature-policy-duplicate", "The wall feature policy is duplicated.", path);
			if (!prefabs.Any(prefab => prefab.Tags is not null
				&& prefab.Tags.Contains(policy.FeatureTag, StringComparer.Ordinal)))
				Add("wall-feature-policy-tag", "The wall feature tag is not authored.", path);
			if (policy.OuterWallsOnly && !componentEntryPolicies.Any(entry =>
				entry.ComponentSocketType == policy.ComponentSocketType))
				Add("wall-feature-policy-entry",
					"Outer-wall-only features require a component entry policy for that network.", path);
			if (policy.CellsPerFeature is <= 0)
				Add("wall-feature-policy-density", "Cells per feature must be positive.",
					path + ".cellsPerFeature");
			ValidateRange(policy.CountPerComponent, path + ".countPerComponent");
		}
		HashSet<(string, string)> interiorFeatureKeys = [];
		for (int index = 0; index < interiorFeaturePolicies.Count; index++)
		{
			CeramicInteriorFeaturePolicy? policy = interiorFeaturePolicies[index];
			string path = $"$.interiorFeaturePolicies[{index}]";
			if (policy is null || string.IsNullOrWhiteSpace(policy.ComponentSocketType)
				|| string.IsNullOrWhiteSpace(policy.FeatureTag))
			{
				Add("interior-feature-policy", "The interior feature policy is invalid.", path);
				continue;
			}
			if (!policyTypes.Contains(policy.ComponentSocketType))
				Add("interior-feature-policy-socket",
					"The interior feature policy references an unknown network.", path);
			if (!interiorFeatureKeys.Add((policy.ComponentSocketType, policy.FeatureTag)))
				Add("interior-feature-policy-duplicate",
					"The interior feature policy is duplicated.", path);
			CeramicPrefabDefinition[] featurePrefabs = prefabs.Where(prefab => prefab is not null
				&& prefab.Tags is not null
				&& prefab.Tags.Contains(policy.FeatureTag, StringComparer.Ordinal)).ToArray();
			if (featurePrefabs.Length == 0)
				Add("interior-feature-policy-tag", "The interior feature tag is not authored.", path);
			foreach (CeramicPrefabDefinition prefab in featurePrefabs)
			{
				if (prefab.Tags.Contains(policy.ComponentSocketType, StringComparer.Ordinal)
					|| prefab.Sockets is null || prefab.Sockets.Count != 4
					|| prefab.Sockets.Any(socket => socket is null
						|| socket.Type != CeramicSocket.NoConnection))
					Add("interior-feature-prefab-connections",
						$"Interior feature prefab '{prefab.Id}' must use no-connection on all faces.",
						path);
			}
			ValidateRange(policy.CountPerComponent, path + ".countPerComponent");
		}
		return new(errors);

		void Add(string code, string message, string path) => errors.Add(new(code, message, path));
		void ValidateRange(CeramicCountRange range, string path, int? maximumAllowed = null)
		{
			if (range.Minimum < 0 || range.Maximum < range.Minimum
				|| (maximumAllowed.HasValue && (range.Minimum > maximumAllowed
					|| range.Maximum > maximumAllowed)))
				Add("count-range", "Count ranges must be non-negative and non-inverted.", path);
		}
	}

	internal static CeramicValidationResult ValidateRequest(
		CeramicGenerationRequest? request,
		CeramicFishDefinition? definition)
	{
		List<CeramicValidationError> errors = [];
		if (request is null)
			return new([new("request-null", "The generation request is null.", "$")]);
		if (definition is null || !ValidateDefinition(definition).IsValid)
			return new([new("request-definition-invalid", "The definition must be valid first.", "$")]);
		if (request.Region is null || request.Region.Count == 0)
			return new([new("request-region", "The request region cannot be empty.", "$.region")]);
		HashSet<CeramicCell> region = [];
		foreach (CeramicCell cell in request.Region)
			if (!region.Add(cell)) Add("request-region-duplicate", "The region contains a duplicate cell.",
				"$.region", cell);
		if (region.Count > 0)
		{
			HashSet<CeramicCell> reached = [];
			Queue<CeramicCell> queue = new();
			CeramicCell first = region.OrderBy(cell => cell.Z).ThenBy(cell => cell.X).First();
			reached.Add(first);
			queue.Enqueue(first);
			while (queue.TryDequeue(out CeramicCell cell))
			foreach (CeramicDirection direction in Enum.GetValues<CeramicDirection>())
			{
				CeramicCell neighbor;
				try { neighbor = CeramicGeometry.Offset(cell, direction); }
				catch (OverflowException) { continue; }
				if (region.Contains(neighbor) && reached.Add(neighbor)) queue.Enqueue(neighbor);
			}
			if (reached.Count != region.Count)
				Add("request-region-disconnected", "The region must be four-directionally connected.", "$.region");
		}
		if (string.IsNullOrWhiteSpace(request.BoundarySocket))
			Add("request-boundary-socket", "The boundary socket is required.", "$.boundarySocket");
		if (request.MaxAttempts <= 0 || request.MaxTopologyChecks <= 0
			|| request.MaxPropagationChecks <= 0)
			Add("request-budget", "Attempt and check budgets must be positive.", "$");
		if (request.TopologyRoot.HasValue && !region.Contains(request.TopologyRoot.Value))
			Add("request-root", "The topology root is outside the region.", "$.topologyRoot",
				request.TopologyRoot.Value);

		ValidateCellLists(request, definition, region, errors);
		ValidateEntrances(request, definition, region, errors);
		ValidateQuotas(request, definition, region.Count, errors);
		if (errors.Count == 0)
		{
			CeramicCompiledCatalog catalog = CeramicCompiledCatalog.Create(definition, request);
			CeramicConstraintIndex constraintIndex = new(request, region);
			foreach (CeramicCell cell in region)
				if (!catalog.Variants.Any(variant =>
					constraintIndex.AllowsCell(cell, variant)))
					Add("request-cell-domain-empty", "No rotated prefab can satisfy this cell's local constraints.",
						"$.region", cell);
		}
		return new(errors);

		void Add(string code, string message, string path, CeramicCell? cell = null) =>
			errors.Add(new(code, message, path, cell));
	}

	private static void ValidateCellLists(
		CeramicGenerationRequest request,
		CeramicFishDefinition definition,
		HashSet<CeramicCell> region,
		List<CeramicValidationError> errors)
	{
		foreach (CeramicAnchor? anchor in request.Anchors ?? [])
		{
			if (anchor is null || !region.Contains(anchor.Cell))
				errors.Add(new("request-anchor", "An anchor is invalid or outside the region.", "$.anchors",
					anchor?.Cell));
			else if (anchor.RequiredTags is null || anchor.RequiredTags.Any(string.IsNullOrWhiteSpace)
				|| anchor.RequiredTags.Distinct(StringComparer.Ordinal).Count() != anchor.RequiredTags.Count)
				errors.Add(new("request-anchor-tags", "Anchor tags must be valid and unique.", "$.anchors", anchor.Cell));
		}
		HashSet<(CeramicCell, CeramicDirection)> sockets = [];
		foreach (CeramicSocketConstraint? constraint in request.SocketConstraints ?? [])
		{
			if (constraint is null || !region.Contains(constraint.Cell)
				|| string.IsNullOrWhiteSpace(constraint.SocketType) || !Enum.IsDefined(constraint.Direction))
				errors.Add(new("request-socket-constraint", "A socket constraint is invalid.",
					"$.socketConstraints", constraint?.Cell));
			else if (!sockets.Add((constraint.Cell, constraint.Direction)))
				errors.Add(new("request-socket-duplicate", "A cell direction has multiple socket constraints.",
					"$.socketConstraints", constraint.Cell));
		}
		HashSet<string> prefabIds = definition.Prefabs.Select(prefab => prefab.Id)
			.ToHashSet(StringComparer.Ordinal);
		foreach (CeramicCellConstraint? constraint in request.CellConstraints ?? [])
		{
			if (constraint is null || !region.Contains(constraint.Cell))
			{
				errors.Add(new("request-cell-constraint", "A cell constraint is invalid or outside the region.",
					"$.cellConstraints", constraint?.Cell));
				continue;
			}
			if (constraint.RequiredTags is null || constraint.ForbiddenTags is null
				|| constraint.RequiredTags.Any(string.IsNullOrWhiteSpace)
				|| constraint.ForbiddenTags.Any(string.IsNullOrWhiteSpace)
				|| constraint.RequiredTags.Intersect(constraint.ForbiddenTags, StringComparer.Ordinal).Any())
				errors.Add(new("request-cell-tags", "Cell tag constraints conflict or are invalid.",
					"$.cellConstraints", constraint.Cell));
			if (constraint.RequiredPrefabId is not null && !prefabIds.Contains(constraint.RequiredPrefabId))
				errors.Add(new("request-prefab", "A required prefab ID is unknown.",
					"$.cellConstraints", constraint.Cell));
			if (constraint.RequiredRotation.HasValue && !Enum.IsDefined(constraint.RequiredRotation.Value))
				errors.Add(new("request-rotation", "A required rotation is invalid.",
					"$.cellConstraints", constraint.Cell));
		}
	}

	private static void ValidateEntrances(
		CeramicGenerationRequest request,
		CeramicFishDefinition definition,
		HashSet<CeramicCell> region,
		List<CeramicValidationError> errors)
	{
		HashSet<(CeramicCell, CeramicDirection)> keys = [];
		foreach (CeramicEntrance? entrance in request.Entrances ?? [])
		{
			if (entrance is null || !region.Contains(entrance.Cell)
				|| !Enum.IsDefined(entrance.Direction) || string.IsNullOrWhiteSpace(entrance.SocketType)
				|| entrance.SocketType == CeramicSocket.NoConnection)
			{
				errors.Add(new("request-entrance", "An entrance is invalid.", "$.entrances", entrance?.Cell));
				continue;
			}
			if (!keys.Add((entrance.Cell, entrance.Direction)))
				errors.Add(new("request-entrance-duplicate", "An entrance edge is duplicated.",
					"$.entrances", entrance.Cell));
			CeramicCell outside;
			try { outside = CeramicGeometry.Offset(entrance.Cell, entrance.Direction); }
			catch (OverflowException)
			{
				errors.Add(new("request-entrance-overflow", "The entrance coordinate overflows.",
					"$.entrances", entrance.Cell));
				continue;
			}
			if (region.Contains(outside))
				errors.Add(new("request-entrance-boundary", "An entrance must face outside the region.",
					"$.entrances", entrance.Cell));
			if (!definition.Prefabs.Any(prefab => prefab.Sockets.Any(socket =>
				string.Equals(socket.Type, entrance.SocketType, StringComparison.Ordinal))))
				errors.Add(new("request-entrance-socket", "The entrance socket type is not authored.",
					"$.entrances", entrance.Cell));
		}
		foreach (CeramicConnectionPolicy policy in definition.ConnectionPolicies)
		{
			int count = (request.Entrances ?? []).Count(entrance => entrance is not null
				&& string.Equals(entrance.SocketType, policy.SocketType, StringComparison.Ordinal));
			if (!policy.ExternalConnectionCount.Contains(count))
				errors.Add(new("request-external-count", $"Entrance count for '{policy.SocketType}' is outside its range.",
					"$.entrances"));
			if (policy.RequireEntranceReachability && count == 0 && policy.ComponentCount.Minimum > 0)
				errors.Add(new("request-entrance-reachability", $"Network '{policy.SocketType}' requires an entrance.",
					"$.entrances"));
		}
	}

	private static void ValidateQuotas(
		CeramicGenerationRequest request,
		CeramicFishDefinition definition,
		int regionCount,
		List<CeramicValidationError> errors)
	{
		HashSet<string> tags = new(StringComparer.Ordinal);
		foreach (CeramicTagQuota? quota in request.TagQuotas ?? [])
		{
			if (quota is null || string.IsNullOrWhiteSpace(quota.Tag) || quota.MinimumCells < 0
				|| quota.MaximumCells < quota.MinimumCells || quota.MinimumCells > regionCount
				|| quota.MaximumCells > regionCount)
			{
				errors.Add(new("request-quota", "A tag quota is invalid.", "$.tagQuotas"));
				continue;
			}
			if (!tags.Add(quota.Tag))
				errors.Add(new("request-quota-duplicate", $"Tag quota '{quota.Tag}' is duplicated.", "$.tagQuotas"));
			if (!definition.Prefabs.Any(prefab => prefab.Tags.Contains(quota.Tag, StringComparer.Ordinal)))
				errors.Add(new("request-quota-tag", $"Tag quota '{quota.Tag}' is not authored.", "$.tagQuotas"));
		}
	}
}
