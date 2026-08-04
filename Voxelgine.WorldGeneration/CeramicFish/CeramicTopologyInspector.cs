namespace Voxelgine.WorldGeneration;

internal static class CeramicTopologyInspector
{
	internal static bool TryValidate(
		CeramicGenerationRequest request,
		CeramicFishDefinition definition,
		IReadOnlyList<CeramicTopologyCell> topology,
		out CeramicGenerationFailure? failure,
		out long checks)
	{
		checks = 0;
		HashSet<CeramicCell> region = request.Region.ToHashSet();
		Dictionary<CeramicCell, CeramicTopologyCell> cells = [];
		if (topology.Count != region.Count)
			return CreateFailure(out failure, "topology-cell-count",
				"Topology does not contain every active cell exactly once.");
		foreach (CeramicTopologyCell? cell in topology)
		{
			checks++;
			if (cell is null || !region.Contains(cell.Cell) || !cells.TryAdd(cell.Cell, cell))
				return CreateFailure(out failure, "topology-cell-duplicate",
					"Topology contains an invalid or duplicate cell.", cell?.Cell);
			if (cell.Tags is null || cell.Tags.Any(string.IsNullOrWhiteSpace)
				|| cell.Tags.Distinct(StringComparer.Ordinal).Count() != cell.Tags.Count)
				return CreateFailure(out failure, "topology-tags",
					"Topology tags must be valid and unique.", cell.Cell);
			if (cell.Sockets is null || cell.Sockets.Count != 4
				|| cell.Sockets.Any(socket => socket is null || string.IsNullOrWhiteSpace(socket.SocketType))
				|| cell.Sockets.GroupBy(socket => socket.Direction).Any(group => group.Count() != 1))
				return CreateFailure(out failure, "topology-sockets",
					"Topology requires exactly one valid socket per direction.", cell.Cell);
		}

		foreach (CeramicTopologyCell cell in topology)
		foreach (CeramicDirection direction in Enum.GetValues<CeramicDirection>())
		{
			checks++;
			CeramicTopologySocket socket = cell.Sockets.Single(item => item.Direction == direction);
			bool outside = false;
			CeramicCell neighborCell = default;
			try { neighborCell = CeramicGeometry.Offset(cell.Cell, direction); }
			catch (OverflowException) { outside = true; }
			if (!outside && cells.TryGetValue(neighborCell, out CeramicTopologyCell? neighbor))
			{
				CeramicTopologySocket opposite = neighbor.Sockets.Single(item =>
					item.Direction == CeramicGeometry.Opposite(direction));
				if (socket.IsExternal || !string.Equals(socket.SocketType, opposite.SocketType,
					StringComparison.Ordinal))
					return CreateFailure(out failure, "topology-socket-mismatch",
						"Facing topology sockets do not match.",
						cell.Cell, socket.SocketType, direction);
			}
			else
			{
				CeramicEntrance? entrance = request.Entrances.FirstOrDefault(item =>
					item.Cell == cell.Cell && item.Direction == direction);
				string expected = entrance?.SocketType ?? request.BoundarySocket;
				if (!string.Equals(socket.SocketType, expected, StringComparison.Ordinal)
					|| socket.IsExternal != (entrance is not null))
					return CreateFailure(out failure, "topology-boundary",
						"A topology boundary socket is invalid.",
						cell.Cell, socket.SocketType, direction);
			}
		}

		foreach (CeramicAnchor anchor in request.Anchors)
			if (!anchor.RequiredTags.All(cells[anchor.Cell].Tags.Contains))
				return CreateFailure(out failure, "topology-anchor",
					"A topology anchor is not satisfied.", anchor.Cell);
		foreach (CeramicCellConstraint constraint in request.CellConstraints)
		{
			CeramicTopologyCell cell = cells[constraint.Cell];
			if (!constraint.RequiredTags.All(cell.Tags.Contains)
				|| constraint.ForbiddenTags.Any(cell.Tags.Contains))
				return CreateFailure(out failure, "topology-cell-tags",
					"A topology cell tag constraint is not satisfied.",
					constraint.Cell);
		}
		foreach (CeramicTagQuota quota in request.TagQuotas)
		{
			checks += topology.Count;
			int count = topology.Count(cell => cell.Tags.Contains(quota.Tag, StringComparer.Ordinal));
			if (count < quota.MinimumCells || count > (quota.MaximumCells ?? int.MaxValue))
				return CreateFailure(out failure, "topology-tag-quota",
					$"Topology tag '{quota.Tag}' is outside its quota.");
		}

		Dictionary<string, List<HashSet<CeramicCell>>> componentsByType =
			new(StringComparer.Ordinal);
		foreach (CeramicConnectionPolicy policy in definition.ConnectionPolicies)
		{
			HashSet<CeramicCell> nodes = topology.Where(cell => cell.Sockets.Any(socket =>
				string.Equals(socket.SocketType, policy.SocketType, StringComparison.Ordinal)))
				.Select(cell => cell.Cell).ToHashSet();
			int externalCount = 0;
			foreach (CeramicCell node in nodes)
			{
				checks += 4;
				CeramicTopologyCell cell = cells[node];
				int degree = cell.Sockets.Count(socket => string.Equals(socket.SocketType,
					policy.SocketType, StringComparison.Ordinal));
				externalCount += cell.Sockets.Count(socket => socket.IsExternal
					&& string.Equals(socket.SocketType, policy.SocketType, StringComparison.Ordinal));
				if (!policy.Degree.Contains(degree))
					return CreateFailure(out failure, "topology-degree",
						$"Network '{policy.SocketType}' has an invalid degree.",
						node, policy.SocketType);
			}
			if (!policy.ExternalConnectionCount.Contains(externalCount))
				return CreateFailure(out failure, "topology-external-count",
					$"Network '{policy.SocketType}' has an invalid external connection count.",
					SocketType: policy.SocketType);

			List<HashSet<CeramicCell>> components = [];
			HashSet<CeramicCell> unseen = nodes.ToHashSet();
			while (unseen.Count > 0)
			{
				CeramicCell root = unseen.OrderBy(cell => cell.Z).ThenBy(cell => cell.X).First();
				HashSet<CeramicCell> component = [];
				Queue<CeramicCell> queue = new();
				unseen.Remove(root);
				queue.Enqueue(root);
				while (queue.TryDequeue(out CeramicCell node))
				{
					component.Add(node);
					CeramicTopologyCell cell = cells[node];
					foreach (CeramicTopologySocket socket in cell.Sockets.Where(socket =>
						!socket.IsExternal && string.Equals(socket.SocketType, policy.SocketType,
							StringComparison.Ordinal)))
					{
						CeramicCell neighbor = CeramicGeometry.Offset(node, socket.Direction);
						if (unseen.Remove(neighbor)) queue.Enqueue(neighbor);
					}
				}
				components.Add(component);
			}
			if (!policy.ComponentCount.Contains(components.Count))
				return CreateFailure(out failure, "topology-component-count",
					$"Network '{policy.SocketType}' has an invalid component count.",
					SocketType: policy.SocketType);
			if (policy.RequireEntranceReachability)
			foreach (HashSet<CeramicCell> component in components)
				if (!component.Any(node => cells[node].Sockets.Any(socket => socket.IsExternal
					&& string.Equals(socket.SocketType, policy.SocketType, StringComparison.Ordinal))))
					return CreateFailure(out failure, "topology-entrance-reachability",
						$"A '{policy.SocketType}' component cannot reach an entrance.",
						SocketType: policy.SocketType);
			componentsByType[policy.SocketType] = components;
		}

		foreach (CeramicComponentTagPolicy policy in definition.ComponentTagPolicies)
		foreach (HashSet<CeramicCell> component in componentsByType[policy.ComponentSocketType])
		{
			checks += component.Count;
			int count = component.Count(node => cells[node].Tags.Contains(policy.RequiredTag,
				StringComparer.Ordinal));
			if (!policy.TagCountPerComponent.Contains(count))
				return CreateFailure(out failure, "topology-component-tag-count",
					$"A '{policy.ComponentSocketType}' component has {count} '{policy.RequiredTag}' tags.",
					SocketType: policy.ComponentSocketType);
		}

		foreach (CeramicComponentEntryPolicy policy in definition.ComponentEntryPolicies)
		{
			List<HashSet<CeramicCell>> components = componentsByType[policy.ComponentSocketType];
			foreach (HashSet<CeramicCell> component in components)
			{
				CeramicCell[] rootEntries = component.Where(node => cells[node].Tags.Contains(
					policy.RootEntryTag, StringComparer.Ordinal)).ToArray();
				CeramicCell[] parentDoors = component.Where(node => cells[node].Tags.Contains(
					policy.ParentDoorTag, StringComparer.Ordinal)).ToArray();
				CeramicCell[] childEntries = component.Where(node => cells[node].Tags.Contains(
					policy.ChildEntryTag, StringComparer.Ordinal)).ToArray();
				checks += component.Count * 3L;
				if (rootEntries.Length != 1)
					return CreateFailure(out failure, "topology-component-entry-count",
						$"A '{policy.ComponentSocketType}' building must have exactly one exterior entry.",
						SocketType: policy.ComponentSocketType);
				if (!AdjacentCells(rootEntries[0]).Any(neighbor => cells.TryGetValue(neighbor,
					out CeramicTopologyCell? adjacent) && adjacent.Tags.Contains(
						policy.RootAdjacentTag, StringComparer.Ordinal)))
					return CreateFailure(out failure, "topology-root-entry-adjacency",
						$"A '{policy.RootEntryTag}' entry does not border '{policy.RootAdjacentTag}'.",
						rootEntries[0], policy.ComponentSocketType);
				HashSet<CeramicCell> sharedDoors = parentDoors.Intersect(childEntries).ToHashSet();
				if (sharedDoors.Count != parentDoors.Length || sharedDoors.Count != childEntries.Length
					|| !policy.AdditionalRoomsPerRoot.Contains(sharedDoors.Count))
					return CreateFailure(out failure, "topology-room-count",
						$"A building has {sharedDoors.Count} shared room doors, outside the configured range.",
						SocketType: policy.ComponentSocketType);
				foreach (CeramicCell sharedDoor in sharedDoors)
					if (TraceSharedPartition(component, sharedDoor, policy.ComponentSocketType) is null)
						return CreateFailure(out failure, "topology-shared-partition",
							"A shared room door must lie between two three-way partition endpoints.",
							sharedDoor, policy.ComponentSocketType);

				long directedInternalEdges = component.Sum(node => cells[node].Sockets.Count(socket =>
					!socket.IsExternal && string.Equals(socket.SocketType,
						policy.ComponentSocketType, StringComparison.Ordinal)));
				long independentCycles = directedInternalEdges / 2 - component.Count + 1;
				long requiredCycles = 1L + sharedDoors.Count;
				if (independentCycles < requiredCycles)
					return CreateFailure(out failure, "topology-room-loop-missing",
						$"Paired room doors require at least {requiredCycles} independent wall cycles,"
							+ $" but the building has {independentCycles}.",
						SocketType: policy.ComponentSocketType);
			}
		}

		foreach (CeramicWallFeaturePolicy policy in definition.WallFeaturePolicies)
		foreach (HashSet<CeramicCell> component in componentsByType[policy.ComponentSocketType])
		{
			CeramicCell[] features = component.Where(node => cells[node].Tags.Contains(
				policy.FeatureTag, StringComparer.Ordinal)).ToArray();
			checks += component.Count;
			if (!policy.CountPerComponent.Contains(features.Length))
				return CreateFailure(out failure, "topology-wall-feature-count",
					$"A '{policy.ComponentSocketType}' component has {features.Length}"
						+ $" '{policy.FeatureTag}' features, outside the configured range.",
					SocketType: policy.ComponentSocketType);
			HashSet<CeramicCell> sharedPartition = [];
			if (policy.OuterWallsOnly)
			{
				CeramicComponentEntryPolicy entryPolicy = definition.ComponentEntryPolicies.Single(entry =>
					entry.ComponentSocketType == policy.ComponentSocketType);
				foreach (CeramicCell sharedDoor in component.Where(node =>
					cells[node].Tags.Contains(entryPolicy.ParentDoorTag, StringComparer.Ordinal)
					&& cells[node].Tags.Contains(entryPolicy.ChildEntryTag, StringComparer.Ordinal)))
				{
					HashSet<CeramicCell>? traced = TraceSharedPartition(component, sharedDoor,
						policy.ComponentSocketType);
					if (traced is not null) sharedPartition.UnionWith(traced);
				}
			}
			if (policy.CellsPerFeature.HasValue)
			{
				int eligibleCells = component.Count - sharedPartition.Count;
				int expectedCount = (eligibleCells + policy.CellsPerFeature.Value - 1)
					/ policy.CellsPerFeature.Value;
				expectedCount = Math.Max(policy.CountPerComponent.Minimum, expectedCount);
				if (policy.CountPerComponent.Maximum.HasValue)
					expectedCount = Math.Min(expectedCount, policy.CountPerComponent.Maximum.Value);
				if (features.Length != expectedCount)
					return CreateFailure(out failure, "topology-wall-feature-density",
						$"The feature '{policy.FeatureTag}' requires {expectedCount} placements"
							+ $" for {eligibleCells} eligible wall cells, but has {features.Length}.",
						SocketType: policy.ComponentSocketType);
			}
			if (!policy.OuterWallsOnly) continue;
			CeramicCell? invalid = features.Where(sharedPartition.Contains)
				.Cast<CeramicCell?>().FirstOrDefault();
			if (invalid.HasValue)
				return CreateFailure(out failure, "topology-wall-feature-shared",
					$"The outer-wall feature '{policy.FeatureTag}' is on a shared room partition.",
					invalid.Value, policy.ComponentSocketType);
		}

		foreach (CeramicInteriorFeaturePolicy policy in definition.InteriorFeaturePolicies)
		{
			CeramicCell[] allFeatures = topology.Where(cell => cell.Tags.Contains(
				policy.FeatureTag, StringComparer.Ordinal)).Select(cell => cell.Cell).ToArray();
			Dictionary<CeramicCell, int> ownership = allFeatures.ToDictionary(
				static cell => cell, static _ => 0);
			foreach (HashSet<CeramicCell> component in componentsByType[policy.ComponentSocketType])
			{
				HashSet<CeramicCell> enclosed = FindEnclosedCells(component);
				CeramicCell[] features = allFeatures.Where(enclosed.Contains).ToArray();
				checks += enclosed.Count + allFeatures.Length;
				if (!policy.CountPerComponent.Contains(features.Length))
					return CreateFailure(out failure, "topology-interior-feature-count",
						$"A '{policy.ComponentSocketType}' component has {features.Length}"
							+ $" enclosed '{policy.FeatureTag}' features, outside the configured range.",
						SocketType: policy.ComponentSocketType);
				foreach (CeramicCell feature in features) ownership[feature]++;
			}
			foreach (CeramicCell feature in allFeatures)
			{
				if (ownership[feature] != 1)
					return CreateFailure(out failure, "topology-interior-feature-enclosure",
						$"Interior feature '{policy.FeatureTag}' must be enclosed by exactly one"
							+ $" '{policy.ComponentSocketType}' component.",
						feature, policy.ComponentSocketType);
				if (cells[feature].Sockets.Any(socket => socket.IsExternal
					|| socket.SocketType != CeramicSocket.NoConnection))
					return CreateFailure(out failure, "topology-interior-feature-connections",
						$"Interior feature '{policy.FeatureTag}' must not create connections.",
						feature, policy.ComponentSocketType);
			}
		}

		foreach (CeramicComponentAdjacencyPolicy policy in definition.ComponentAdjacencyPolicies)
		foreach (HashSet<CeramicCell> component in componentsByType[policy.ComponentSocketType])
		{
			int edges = 0;
			foreach (CeramicCell node in component)
			foreach (CeramicDirection direction in Enum.GetValues<CeramicDirection>())
			{
				checks++;
				CeramicCell neighbor = CeramicGeometry.Offset(node, direction);
				if (!component.Contains(neighbor) && cells.TryGetValue(neighbor,
					out CeramicTopologyCell? adjacent)
					&& adjacent.Tags.Contains(policy.RequiredAdjacentTag, StringComparer.Ordinal)) edges++;
			}
			if (edges < policy.MinimumAdjacentEdgesPerComponent)
				return CreateFailure(out failure, "topology-component-adjacency",
					$"A '{policy.ComponentSocketType}' component lacks required '{policy.RequiredAdjacentTag}' adjacency.",
					SocketType: policy.ComponentSocketType);
		}

		failure = null;
		return true;

		static IEnumerable<CeramicCell> AdjacentCells(CeramicCell cell)
		{
			foreach (CeramicDirection direction in Enum.GetValues<CeramicDirection>())
			{
				CeramicCell neighbor;
				try { neighbor = CeramicGeometry.Offset(cell, direction); }
				catch (OverflowException) { continue; }
				yield return neighbor;
			}
		}

		HashSet<CeramicCell>? TraceSharedPartition(
			HashSet<CeramicCell> component,
			CeramicCell door,
			string socketType)
		{
			CeramicDirection[] doorDirections = cells[door].Sockets.Where(socket =>
				!socket.IsExternal && socket.SocketType == socketType)
				.Select(socket => socket.Direction).ToArray();
			if (doorDirections.Length != 2
				|| CeramicGeometry.Opposite(doorDirections[0]) != doorDirections[1]) return null;
			HashSet<CeramicCell> partition = [door];
			foreach (CeramicDirection initialDirection in doorDirections)
			{
				CeramicCell current = door;
				CeramicDirection direction = initialDirection;
				HashSet<CeramicCell> visited = [door];
				while (true)
				{
					CeramicCell next;
					try { next = CeramicGeometry.Offset(current, direction); }
					catch (OverflowException) { return null; }
					if (!component.Contains(next) || !visited.Add(next)) return null;
					partition.Add(next);
					CeramicDirection[] directions = cells[next].Sockets.Where(socket =>
						!socket.IsExternal && socket.SocketType == socketType)
						.Select(socket => socket.Direction).ToArray();
					if (directions.Length == 3) break;
					if (directions.Length != 2) return null;
					CeramicDirection incoming = CeramicGeometry.Opposite(direction);
					CeramicDirection[] onward = directions.Where(candidate => candidate != incoming).ToArray();
					if (onward.Length != 1) return null;
					current = next;
					direction = onward[0];
				}
			}
			return partition;
		}

		HashSet<CeramicCell> FindEnclosedCells(HashSet<CeramicCell> wall)
		{
			int minimumX = wall.Min(static cell => cell.X) - 1;
			int maximumX = wall.Max(static cell => cell.X) + 1;
			int minimumZ = wall.Min(static cell => cell.Z) - 1;
			int maximumZ = wall.Max(static cell => cell.Z) + 1;
			HashSet<CeramicCell> exterior = [];
			Queue<CeramicCell> pending = new();
			for (int x = minimumX; x <= maximumX; x++)
			{
				Enqueue(new(x, minimumZ));
				Enqueue(new(x, maximumZ));
			}
			for (int z = minimumZ + 1; z < maximumZ; z++)
			{
				Enqueue(new(minimumX, z));
				Enqueue(new(maximumX, z));
			}
			while (pending.TryDequeue(out CeramicCell current))
			foreach (CeramicDirection direction in Enum.GetValues<CeramicDirection>())
			{
				CeramicCell neighbor;
				try { neighbor = CeramicGeometry.Offset(current, direction); }
				catch (OverflowException) { continue; }
				Enqueue(neighbor);
			}
			HashSet<CeramicCell> enclosed = [];
			for (int z = minimumZ + 1; z < maximumZ; z++)
			for (int x = minimumX + 1; x < maximumX; x++)
			{
				CeramicCell candidate = new(x, z);
				if (cells.ContainsKey(candidate) && !wall.Contains(candidate)
					&& !exterior.Contains(candidate)) enclosed.Add(candidate);
			}
			return enclosed;

			void Enqueue(CeramicCell candidate)
			{
				if (candidate.X < minimumX || candidate.X > maximumX
					|| candidate.Z < minimumZ || candidate.Z > maximumZ
					|| wall.Contains(candidate) || !exterior.Add(candidate)) return;
				pending.Enqueue(candidate);
			}
		}
	}

	private static bool CreateFailure(
		out CeramicGenerationFailure? failure,
		string code,
		string message,
		CeramicCell? cell = null,
		string? SocketType = null,
		CeramicDirection? direction = null)
	{
		failure = new(code, message, cell, CeramicGenerationStage.Topology, SocketType, direction);
		return false;
	}
}
