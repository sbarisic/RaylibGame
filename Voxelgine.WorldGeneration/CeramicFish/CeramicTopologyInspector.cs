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
