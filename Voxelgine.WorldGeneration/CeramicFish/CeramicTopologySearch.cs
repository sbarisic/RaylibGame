namespace Voxelgine.WorldGeneration;

internal sealed class CeramicTopologySearch
{
	private readonly CeramicGenerationRequest request;
	private readonly CeramicFishDefinition definition;
	private readonly CeramicCompiledCatalog catalog;
	private readonly CeramicDeterministicRandom random;
	private readonly CancellationToken cancellationToken;
	private readonly CeramicCell[] cells;
	private readonly Dictionary<CeramicCell, int> indices;
	private readonly HashSet<CeramicCell> region;
	private readonly CeramicConstraintIndex constraintIndex;
	private readonly SortedSet<int>[] domains;
	private long checks;
	private bool budgetExceeded;

	internal CeramicTopologySearch(
		CeramicGenerationRequest request,
		CeramicFishDefinition definition,
		CeramicCompiledCatalog catalog,
		CeramicDeterministicRandom random,
		CancellationToken cancellationToken)
	{
		this.request = request;
		this.definition = definition;
		this.catalog = catalog;
		this.random = random;
		this.cancellationToken = cancellationToken;
		cells = request.Region.OrderBy(cell => cell.Z).ThenBy(cell => cell.X).ToArray();
		indices = cells.Select((cell, index) => (cell, index)).ToDictionary(item => item.cell,
			item => item.index);
		region = cells.ToHashSet();
		constraintIndex = new(request, region);
		domains = new SortedSet<int>[cells.Length];
		for (int cellIndex = 0; cellIndex < cells.Length; cellIndex++)
		{
			domains[cellIndex] = [];
			for (int optionIndex = 0; optionIndex < catalog.Options.Count; optionIndex++)
				if (catalog.Options[optionIndex].Variants.Any(variant =>
					constraintIndex.AllowsCell(cells[cellIndex], variant)))
					domains[cellIndex].Add(optionIndex);
		}
	}

	internal CeramicTopologyAttemptResult Run()
	{
		List<(int Cell, int Option)> initialTrail = [];
		if (!Propagate(Enumerable.Range(0, cells.Length), initialTrail))
			return Failure(budgetExceeded ? CeramicTopologyAttemptStatus.BudgetExceeded
				: CeramicTopologyAttemptStatus.Unsatisfiable,
				budgetExceeded ? "topology-budget-exceeded" : "topology-domain-empty",
				budgetExceeded ? "The topology check budget was exhausted."
					: "Local socket constraints prove that no topology exists.");

		if (TryConstruct(out IReadOnlyList<CeramicTopologyCell>? constructed,
			out CeramicGenerationFailure? constructionFailure))
			return new(CeramicTopologyAttemptStatus.Success, constructed, checks);
		if (budgetExceeded)
			return Failure(CeramicTopologyAttemptStatus.BudgetExceeded,
				"topology-budget-exceeded", "The topology check budget was exhausted.");
		List<(int Cell, int Option)> trail = [];
		if (Search(trail, out IReadOnlyList<CeramicTopologyCell>? searched))
			return new(CeramicTopologyAttemptStatus.Success, searched, checks);
		if (budgetExceeded)
			return Failure(CeramicTopologyAttemptStatus.BudgetExceeded,
				"topology-budget-exceeded", "The topology check budget was exhausted.");
		return new(CeramicTopologyAttemptStatus.Unsatisfiable, [], checks,
			constructionFailure ?? new("topology-unsatisfiable",
				"The complete bounded topology domain was exhausted without a solution.",
				Stage: CeramicGenerationStage.Topology));
	}

	private bool TryConstruct(
		out IReadOnlyList<CeramicTopologyCell> topology,
		out CeramicGenerationFailure? failure)
	{
		string[][] sockets = new string[cells.Length][];
		HashSet<string>[] tags = new HashSet<string>[cells.Length];
		for (int index = 0; index < cells.Length; index++)
		{
			sockets[index] = Enumerable.Repeat(CeramicSocket.NoConnection, 4).ToArray();
			tags[index] = new(StringComparer.Ordinal);
			foreach (CeramicAnchor anchor in request.Anchors.Where(anchor => anchor.Cell == cells[index]))
				tags[index].UnionWith(anchor.RequiredTags);
			foreach (CeramicCellConstraint constraint in request.CellConstraints
				.Where(constraint => constraint.Cell == cells[index]))
				tags[index].UnionWith(constraint.RequiredTags);
			foreach (CeramicDirection direction in Enum.GetValues<CeramicDirection>())
			{
				bool outside = false;
				CeramicCell neighbor = default;
				try { neighbor = CeramicGeometry.Offset(cells[index], direction); }
				catch (OverflowException) { outside = true; }
				if (!outside && region.Contains(neighbor)) continue;
				CeramicEntrance? entrance = request.Entrances.FirstOrDefault(item =>
					item.Cell == cells[index] && item.Direction == direction);
				sockets[index][(int)direction] = entrance?.SocketType ?? request.BoundarySocket;
			}
		}

		bool SetEdge(int first, CeramicDirection direction, string type)
		{
			CeramicCell neighborCell;
			try { neighborCell = CeramicGeometry.Offset(cells[first], direction); }
			catch (OverflowException) { return false; }
			if (!indices.TryGetValue(neighborCell, out int second)) return false;
			CeramicDirection opposite = CeramicGeometry.Opposite(direction);
			string left = sockets[first][(int)direction];
			string right = sockets[second][(int)opposite];
			if (left != CeramicSocket.NoConnection && !string.Equals(left, type, StringComparison.Ordinal))
				return false;
			if (right != CeramicSocket.NoConnection && !string.Equals(right, type, StringComparison.Ordinal))
				return false;
			sockets[first][(int)direction] = type;
			sockets[second][(int)opposite] = type;
			return true;
		}

		foreach (CeramicSocketConstraint constraint in request.SocketConstraints)
		{
			int index = indices[constraint.Cell];
			CeramicCell neighbor = CeramicGeometry.Offset(constraint.Cell, constraint.Direction);
			if (region.Contains(neighbor))
			{
				if (!SetEdge(index, constraint.Direction, constraint.SocketType))
					return ConstructionFailure(out topology, out failure, "topology-socket-conflict",
						"A hard socket constraint conflicts with another required edge.", constraint.Cell,
						constraint.SocketType, constraint.Direction);
			}
			else if (!string.Equals(sockets[index][(int)constraint.Direction],
				constraint.SocketType, StringComparison.Ordinal))
				return ConstructionFailure(out topology, out failure, "topology-boundary-conflict",
					"A hard socket constraint conflicts with the boundary.", constraint.Cell,
					constraint.SocketType, constraint.Direction);
		}

		foreach (CeramicConnectionPolicy policy in definition.ConnectionPolicies
			.OrderBy(item => item.SocketType, StringComparer.Ordinal))
		{
			if (policy.Degree.Minimum != 2 || policy.Degree.Maximum != 2
				|| policy.ExternalConnectionCount.Minimum != 0
				|| policy.ExternalConnectionCount.Maximum != 0) continue;
			List<int> forced = Enumerable.Range(0, cells.Length)
				.Where(index => tags[index].Contains(policy.SocketType)).ToList();
			if (forced.Count == 0) continue;
			HashSet<int> forcedSet = forced.ToHashSet();
			bool simpleCycle = true;
			foreach (int index in forced)
			{
				List<CeramicDirection> neighbors = Enum.GetValues<CeramicDirection>()
					.Where(direction =>
					{
						try { return indices.TryGetValue(CeramicGeometry.Offset(cells[index], direction),
							out int neighbor) && forcedSet.Contains(neighbor); }
						catch (OverflowException) { return false; }
					}).ToList();
				if (neighbors.Count != 2) { simpleCycle = false; break; }
				foreach (CeramicDirection direction in neighbors)
					if (!SetEdge(index, direction, policy.SocketType)) simpleCycle = false;
			}
			if (!simpleCycle)
				return ConstructionFailure(out topology, out failure, "topology-forced-cycle",
					$"Forced '{policy.SocketType}' cells do not form a simple cycle.");
		}

		foreach (CeramicConnectionPolicy policy in definition.ConnectionPolicies
			.Where(policy => policy.RequireEntranceReachability)
			.OrderBy(item => item.SocketType, StringComparer.Ordinal))
		{
			CeramicTagQuota? quota = request.TagQuotas.FirstOrDefault(item =>
				string.Equals(item.Tag, policy.SocketType, StringComparison.Ordinal));
			if (quota is null) continue;
			List<int> network = Enumerable.Range(0, cells.Length).Where(index =>
				sockets[index].Any(type => string.Equals(type, policy.SocketType,
					StringComparison.Ordinal))).ToList();
			foreach (int index in network)
				if (domains[index].Any(option => catalog.Options[option].TagSet.Contains(quota.Tag)))
					tags[index].Add(quota.Tag);
			int taggedCount = tags.Count(set => set.Contains(quota.Tag));
			if (taggedCount > 0)
				GrowOrganicNetwork(policy, quota, ref taggedCount);
			while (taggedCount < quota.MinimumCells)
			{
				cancellationToken.ThrowIfCancellationRequested();
				List<(int Parent, int Child, CeramicDirection Direction)> frontier = [];
				HashSet<int> networkSet = Enumerable.Range(0, cells.Length).Where(index =>
					sockets[index].Any(type => string.Equals(type, policy.SocketType,
					StringComparison.Ordinal))).ToHashSet();
				foreach (int networkParent in networkSet.Order())
				{
					int degree = sockets[networkParent].Count(type => string.Equals(type, policy.SocketType,
						StringComparison.Ordinal));
					if (degree >= (policy.Degree.Maximum ?? 4)) continue;
					foreach (CeramicDirection candidateDirection in Enum.GetValues<CeramicDirection>())
					{
						CeramicCell childCell;
						try { childCell = CeramicGeometry.Offset(cells[networkParent], candidateDirection); }
						catch (OverflowException) { continue; }
						if (!indices.TryGetValue(childCell, out int candidateChild)
							|| networkSet.Contains(candidateChild)) continue;
						if (!string.Equals(sockets[networkParent][(int)candidateDirection], CeramicSocket.NoConnection,
							StringComparison.Ordinal) || tags[candidateChild].Contains("defense-wall")) continue;
						if (!domains[candidateChild].Any(option => catalog.Options[option].TagSet.Contains(quota.Tag)))
							continue;
						int adjacentNetwork = Enum.GetValues<CeramicDirection>().Count(candidateDirection =>
						{
							try { return indices.TryGetValue(CeramicGeometry.Offset(childCell, candidateDirection),
								out int adjacent) && networkSet.Contains(adjacent); }
							catch (OverflowException) { return false; }
						});
						if (adjacentNetwork == 1)
							frontier.Add((networkParent, candidateChild, candidateDirection));
					}
				}
				if (frontier.Count == 0)
					return ConstructionFailure(out topology, out failure, "topology-network-growth",
						$"The '{policy.SocketType}' network could not reach its tag quota.");
				(int selectedParent, int selectedChild, CeramicDirection selectedDirection) =
					frontier[random.NextInt(frontier.Count)];
				if (!SetEdge(selectedParent, selectedDirection, policy.SocketType))
					return ConstructionFailure(out topology, out failure, "topology-network-edge",
						"Network growth produced a conflicting edge.");
				tags[selectedChild].Add(quota.Tag);
				taggedCount++;
				if (!CheckBudget()) return ConstructionFailure(out topology, out failure,
					"topology-budget-exceeded",
					"The topology check budget was exhausted.");
			}
		}

		foreach (CeramicConnectionPolicy policy in definition.ConnectionPolicies
			.OrderBy(item => item.SocketType, StringComparer.Ordinal))
		{
			CeramicTagQuota? quota = request.TagQuotas.FirstOrDefault(item =>
				string.Equals(item.Tag, policy.SocketType, StringComparison.Ordinal));
			CeramicComponentEntryPolicy? entryPolicy = definition.ComponentEntryPolicies
				.SingleOrDefault(item => item.ComponentSocketType == policy.SocketType);
			if (quota is null || policy.Degree.Minimum != 2
				|| (entryPolicy is null ? policy.Degree.Maximum != 2 : policy.Degree.Maximum is < 3)
				|| policy.ExternalConnectionCount.Maximum != 0) continue;
			int count = tags.Count(set => set.Contains(quota.Tag));
			if (count >= quota.MinimumCells) continue;
			List<CeramicRectangle> rectangles = CreateRectangles();
			random.Shuffle(rectangles);
			HashSet<int> claimedBuildingArea = [];
			CeramicComponentTagPolicy[] componentTagPolicies = definition.ComponentTagPolicies
				.Where(item => item.ComponentSocketType == policy.SocketType)
				.OrderBy(item => item.RequiredTag, StringComparer.Ordinal).ToArray();
			CeramicWallFeaturePolicy[] wallFeaturePolicies = definition.WallFeaturePolicies
				.Where(item => item.ComponentSocketType == policy.SocketType)
				.OrderBy(item => item.FeatureTag, StringComparer.Ordinal).ToArray();
			foreach (CeramicRectangle rectangle in rectangles)
			{
				if (count >= quota.MinimumCells) break;
				List<int> perimeter = RectanglePerimeter(rectangle);
				List<int> area = RectangleArea(rectangle);
				if (perimeter.Count == 0 || area.Count == 0
					|| count + perimeter.Count > (quota.MaximumCells ?? int.MaxValue)
					|| !AreaIsAvailable(area, claimedBuildingArea, []))
					continue;
				HashSet<int> perimeterSet = perimeter.ToHashSet();
				if (!CanRealizeLoop(rectangle, perimeter, policy.SocketType, quota.Tag))
					continue;
				bool adjacentToRequired = definition.ComponentAdjacencyPolicies
					.Where(item => item.ComponentSocketType == policy.SocketType)
					.All(adjacency => perimeter.Sum(index => Enum.GetValues<CeramicDirection>().Count(direction =>
					{
						try { return indices.TryGetValue(CeramicGeometry.Offset(cells[index], direction),
							out int neighbor) && !perimeterSet.Contains(neighbor)
							&& tags[neighbor].Contains(adjacency.RequiredAdjacentTag); }
						catch (OverflowException) { return false; }
					})) >= adjacency.MinimumAdjacentEdgesPerComponent);
				if (!adjacentToRequired) continue;
				List<(CeramicRectangle Rectangle, List<int> Perimeter)> plannedComponents =
					[(rectangle, perimeter)];
				HashSet<int> plannedArea = area.ToHashSet();
				List<(int Index, string Tag)> selectedComponentTags = [];
				if (!TrySelectComponentTags(rectangle, perimeter, componentTagPolicies,
					selectedComponentTags)) continue;

				if (entryPolicy is not null)
				{
					List<int> rootEntries = TaggedLoopCandidates(rectangle, perimeter,
						entryPolicy.RootEntryTag).Where(index => HasAdjacentTag(index,
							perimeterSet, entryPolicy.RootAdjacentTag)).ToList();
					random.Shuffle(rootEntries);
					if (rootEntries.Count == 0) continue;
					selectedComponentTags.Add((rootEntries[0], entryPolicy.RootEntryTag));

					int minimumRooms = entryPolicy.AdditionalRoomsPerRoot.Minimum;
					int maximumRooms = entryPolicy.AdditionalRoomsPerRoot.Maximum
						?? Math.Max(minimumRooms, 2);
					maximumRooms = Math.Min(maximumRooms, Math.Max(minimumRooms, 3));
					int requestedRooms = minimumRooms + random.NextInt(maximumRooms - minimumRooms + 1);
					int roomsAdded = 0;
					for (int roomOrdinal = 0; roomOrdinal < requestedRooms; roomOrdinal++)
					{
						List<(CeramicRectangle Rectangle, List<int> Perimeter, List<int> Area,
							int SharedDoor)> roomCandidates = [];
						List<CeramicRectangle> wallSharingRectangles = plannedComponents
							.SelectMany(item => CreateWallSharingRectangles(item.Rectangle))
							.Distinct().ToList();
						random.Shuffle(wallSharingRectangles);
						foreach (CeramicRectangle childRectangle in wallSharingRectangles)
						{
							List<int> childArea = RectangleArea(childRectangle);
							if (childArea.Count == 0
								|| !AreaIsAvailable(childArea, claimedBuildingArea, plannedArea,
									allowReservedOverlap: true)) continue;
							List<int> childPerimeter = RectanglePerimeter(childRectangle);
							if (childPerimeter.Count == 0) continue;
							HashSet<int> childSet = childPerimeter.ToHashSet();
							var sharedParents = plannedComponents.Select(item => new
							{
								Component = item,
								SharedCells = item.Perimeter.Where(childSet.Contains).ToArray(),
							}).Where(item => item.SharedCells.Length != 0).ToList();
							if (sharedParents.Count != 1 || sharedParents[0].SharedCells.Length < 3)
								continue;
							HashSet<int> sharedWall = sharedParents[0].SharedCells.ToHashSet();
							HashSet<int> areaOverlap = childArea.Where(plannedArea.Contains).ToHashSet();
							if (!areaOverlap.SetEquals(sharedWall)) continue;
							HashSet<int> prospectivePerimeter = plannedComponents
								.SelectMany(item => item.Perimeter).Concat(childPerimeter).ToHashSet();
							if (count + prospectivePerimeter.Count > (quota.MaximumCells ?? int.MaxValue))
								continue;
							List<int> sharedDoorCandidates = sharedWall.Where(index =>
								!selectedComponentTags.Any(item => item.Index == index)).ToList();
							random.Shuffle(sharedDoorCandidates);
							foreach (int sharedDoor in sharedDoorCandidates)
							{
								List<(CeramicRectangle Rectangle, List<int> Perimeter)> prospectiveComponents =
									[.. plannedComponents, (childRectangle, childPerimeter)];
								List<(int Index, string Tag)> prospectiveTags =
									[.. selectedComponentTags,
										(sharedDoor, entryPolicy.ParentDoorTag),
										(sharedDoor, entryPolicy.ChildEntryTag)];
								if (!CanRealizePlannedBuilding(prospectiveComponents, prospectiveTags))
									continue;
								roomCandidates.Add((childRectangle, childPerimeter, childArea,
									sharedDoor));
								break;
							}
						}
						if (roomCandidates.Count == 0) break;
						random.Shuffle(roomCandidates);
						var selectedRoom = roomCandidates[0];
						plannedComponents.Add((selectedRoom.Rectangle, selectedRoom.Perimeter));
						plannedArea.UnionWith(selectedRoom.Area);
						selectedComponentTags.Add((selectedRoom.SharedDoor,
							entryPolicy.ParentDoorTag));
						selectedComponentTags.Add((selectedRoom.SharedDoor,
							entryPolicy.ChildEntryTag));
						roomsAdded++;
					}
					if (roomsAdded < minimumRooms) continue;
				}
				HashSet<int> buildingPerimeter = plannedComponents
					.SelectMany(item => item.Perimeter).ToHashSet();
				Dictionary<int, int> wallMembership = plannedComponents.SelectMany(item => item.Perimeter)
					.GroupBy(index => index).ToDictionary(group => group.Key, group => group.Count());
				bool featuresAvailable = true;
				foreach (CeramicWallFeaturePolicy featurePolicy in wallFeaturePolicies)
				{
					int eligibleSurfaceCells = featurePolicy.OuterWallsOnly
						? wallMembership.Count(item => item.Value == 1)
						: buildingPerimeter.Count;
					List<int> candidates = buildingPerimeter.Where(index =>
						(!featurePolicy.OuterWallsOnly || wallMembership[index] == 1)
						&& !selectedComponentTags.Any(item => item.Index == index)).Where(index =>
					{
						List<(int Index, string Tag)> prospectiveTags =
							[.. selectedComponentTags, (index, featurePolicy.FeatureTag)];
						return CanRealizePlannedBuilding(plannedComponents, prospectiveTags);
					}).ToList();
					random.Shuffle(candidates);
					int minimumFeatures = featurePolicy.CountPerComponent.Minimum;
					int maximumFeatures = Math.Min(candidates.Count,
						featurePolicy.CountPerComponent.Maximum ?? candidates.Count);
					int selectedCount;
					if (featurePolicy.CellsPerFeature.HasValue)
					{
						int scaledCount = (eligibleSurfaceCells
							+ featurePolicy.CellsPerFeature.Value - 1)
							/ featurePolicy.CellsPerFeature.Value;
						selectedCount = Math.Max(minimumFeatures, scaledCount);
						if (featurePolicy.CountPerComponent.Maximum.HasValue)
							selectedCount = Math.Min(selectedCount,
								featurePolicy.CountPerComponent.Maximum.Value);
					}
					else
					{
						selectedCount = minimumFeatures;
						if (maximumFeatures >= minimumFeatures)
							selectedCount += random.NextInt(maximumFeatures - minimumFeatures + 1);
					}
					if (candidates.Count < selectedCount || maximumFeatures < minimumFeatures)
					{
						featuresAvailable = false;
						break;
					}
					foreach (int index in candidates.Take(selectedCount))
						selectedComponentTags.Add((index, featurePolicy.FeatureTag));
				}
				if (!featuresAvailable
					|| !CanRealizePlannedBuilding(plannedComponents, selectedComponentTags)) continue;

				foreach ((CeramicRectangle plannedRectangle, List<int> plannedPerimeter)
					in plannedComponents)
				{
					for (int x = plannedRectangle.X;
						x < plannedRectangle.X + plannedRectangle.Width - 1; x++)
					{
						int top = indices[new(x, plannedRectangle.Z)];
						int bottom = indices[new(x,
							plannedRectangle.Z + plannedRectangle.Height - 1)];
						if (!SetEdge(top, CeramicDirection.East, policy.SocketType)
							|| !SetEdge(bottom, CeramicDirection.East, policy.SocketType))
							return ConstructionFailure(out topology, out failure, "topology-loop-edge",
								"A loop edge conflicts with existing topology.");
					}
					for (int z = plannedRectangle.Z;
						z < plannedRectangle.Z + plannedRectangle.Height - 1; z++)
					{
						int left = indices[new(plannedRectangle.X, z)];
						int right = indices[new(plannedRectangle.X
							+ plannedRectangle.Width - 1, z)];
						if (!SetEdge(left, CeramicDirection.South, policy.SocketType)
							|| !SetEdge(right, CeramicDirection.South, policy.SocketType))
							return ConstructionFailure(out topology, out failure, "topology-loop-edge",
								"A loop edge conflicts with existing topology.");
					}
				}
				foreach (int index in buildingPerimeter) tags[index].Add(quota.Tag);
				count += buildingPerimeter.Count;
				foreach ((int index, string tag) in selectedComponentTags) tags[index].Add(tag);
				claimedBuildingArea.UnionWith(plannedArea);
				if (!CheckBudget()) return ConstructionFailure(out topology, out failure,
					"topology-budget-exceeded",
					"The topology check budget was exhausted.");
			}
			if (count < quota.MinimumCells)
				return ConstructionFailure(out topology, out failure, "topology-loop-quota",
					$"Closed '{policy.SocketType}' loops reached {count} cells but require {quota.MinimumCells}.");

			bool TrySelectComponentTags(
				CeramicRectangle componentRectangle,
				List<int> componentPerimeter,
				IReadOnlyList<CeramicComponentTagPolicy> policies,
				List<(int Index, string Tag)> selections)
			{
				HashSet<int> componentSet = componentPerimeter.ToHashSet();
				foreach (CeramicComponentTagPolicy componentTagPolicy in policies)
				{
					List<int> candidates = TaggedLoopCandidates(componentRectangle,
						componentPerimeter, componentTagPolicy.RequiredTag).Where(index =>
						definition.ComponentAdjacencyPolicies
							.Where(item => item.ComponentSocketType == policy.SocketType)
							.All(adjacency => HasAdjacentTag(index, componentSet,
								adjacency.RequiredAdjacentTag))).ToList();
					random.Shuffle(candidates);
					if (candidates.Count < componentTagPolicy.TagCountPerComponent.Minimum) return false;
					foreach (int index in candidates.Take(componentTagPolicy.TagCountPerComponent.Minimum))
						selections.Add((index, componentTagPolicy.RequiredTag));
				}
				return true;
			}

			IEnumerable<int> TaggedLoopCandidates(
				CeramicRectangle componentRectangle,
				IEnumerable<int> componentPerimeter,
				string requiredTag) => componentPerimeter.Where(index =>
			{
				string[] expectedSockets = ExpectedLoopSockets(index, componentRectangle,
					policy.SocketType);
				return domains[index].Any(optionIndex =>
				{
					CeramicTopologyOption option = catalog.Options[optionIndex];
					return option.TagSet.Contains(requiredTag)
						&& option.Sockets.SequenceEqual(expectedSockets, StringComparer.Ordinal);
				});
			});

			bool CanRealizeLoop(
				CeramicRectangle componentRectangle,
				IEnumerable<int> componentPerimeter,
				string socketType,
				string requiredTag) => componentPerimeter.All(index =>
				domains[index].Any(option => catalog.Options[option].TagSet.Contains(requiredTag)
					&& catalog.Options[option].Sockets.SequenceEqual(ExpectedLoopSockets(index,
						componentRectangle, socketType), StringComparer.Ordinal)));

			bool CanRealizePlannedBuilding(
				IReadOnlyList<(CeramicRectangle Rectangle, List<int> Perimeter)> components,
				IReadOnlyList<(int Index, string Tag)> selections)
			{
				Dictionary<int, string[]> expectedByCell = [];
				foreach ((CeramicRectangle componentRectangle, _) in components)
				{
					for (int x = componentRectangle.X;
						x < componentRectangle.X + componentRectangle.Width - 1; x++)
					{
						AddExpectedEdge(indices[new(x, componentRectangle.Z)], CeramicDirection.East);
						AddExpectedEdge(indices[new(x,
							componentRectangle.Z + componentRectangle.Height - 1)],
							CeramicDirection.East);
					}
					for (int z = componentRectangle.Z;
						z < componentRectangle.Z + componentRectangle.Height - 1; z++)
					{
						AddExpectedEdge(indices[new(componentRectangle.X, z)], CeramicDirection.South);
						AddExpectedEdge(indices[new(componentRectangle.X
							+ componentRectangle.Width - 1, z)], CeramicDirection.South);
					}
				}
				foreach ((int index, string[] expected) in expectedByCell)
				{
					string[] requiredTags = selections.Where(item => item.Index == index)
						.Select(item => item.Tag).Distinct(StringComparer.Ordinal).ToArray();
					if (!domains[index].Any(optionIndex =>
					{
						CeramicTopologyOption option = catalog.Options[optionIndex];
						return option.TagSet.Contains(quota.Tag)
							&& requiredTags.All(option.TagSet.Contains)
							&& option.Sockets.SequenceEqual(expected, StringComparer.Ordinal);
					})) return false;
				}
				return true;

				void AddExpectedEdge(int first, CeramicDirection direction)
				{
					int second = indices[CeramicGeometry.Offset(cells[first], direction)];
					string[] left = GetExpected(first);
					string[] right = GetExpected(second);
					left[(int)direction] = policy.SocketType;
					right[(int)CeramicGeometry.Opposite(direction)] = policy.SocketType;
				}

				string[] GetExpected(int index)
				{
					if (!expectedByCell.TryGetValue(index, out string[]? expected))
					{
						expected = Enumerable.Repeat(CeramicSocket.NoConnection, 4).ToArray();
						expectedByCell[index] = expected;
					}
					return expected;
				}
			}

			bool AreaIsAvailable(
				IReadOnlyCollection<int> candidateArea,
				HashSet<int> occupied,
				HashSet<int> reserved,
				bool allowReservedOverlap = false)
			{
				if (candidateArea.Any(index => occupied.Contains(index)
					|| (!allowReservedOverlap && reserved.Contains(index))
					|| tags[index].Contains("defense-wall") || tags[index].Contains("road")
					|| tags[index].Contains(quota.Tag)
					|| sockets[index].Any(type => type != CeramicSocket.NoConnection))) return false;
				foreach (int index in candidateArea)
				foreach (CeramicDirection direction in Enum.GetValues<CeramicDirection>())
				{
					CeramicCell neighbor;
					try { neighbor = CeramicGeometry.Offset(cells[index], direction); }
					catch (OverflowException) { continue; }
					if (indices.TryGetValue(neighbor, out int neighborIndex)
						&& occupied.Contains(neighborIndex)) return false;
				}
				return true;
			}

			bool HasAdjacentTag(int index, HashSet<int> ownComponent, string adjacentTag) =>
				Enum.GetValues<CeramicDirection>().Any(direction =>
						{
							try { return indices.TryGetValue(CeramicGeometry.Offset(cells[index], direction),
								out int neighbor) && !ownComponent.Contains(neighbor)
								&& tags[neighbor].Contains(adjacentTag); }
							catch (OverflowException) { return false; }
						});
		}

		List<CeramicTopologyCell> result = new(cells.Length);
		for (int index = 0; index < cells.Length; index++)
		{
			List<int> matches = domains[index].Where(optionIndex =>
			{
				CeramicTopologyOption option = catalog.Options[optionIndex];
				return option.Sockets.SequenceEqual(sockets[index], StringComparer.Ordinal)
					&& tags[index].All(option.TagSet.Contains);
			}).OrderBy(optionIndex => catalog.Options[optionIndex].Tags.Length)
				.ThenBy(optionIndex => catalog.Options[optionIndex].Key, StringComparer.Ordinal).ToList();
			if (matches.Count == 0)
				return ConstructionFailure(out topology, out failure,
					"topology-signature-unavailable",
					"The constructed graph has no realizable prefab signature.", cells[index]);
			int minimumTags = catalog.Options[matches[0]].Tags.Length;
			matches.RemoveAll(option => catalog.Options[option].Tags.Length != minimumTags);
			CeramicTopologyOption selected = catalog.Options[matches[random.NextInt(matches.Count)]];
			result.Add(CreateTopologyCell(index, selected));
		}

		if (!CeramicTopologyInspector.TryValidate(request, definition, result,
			out failure, out long validationChecks))
		{
			checks = SaturatingAdd(checks, validationChecks);
			topology = [];
			return false;
		}
		checks = SaturatingAdd(checks, validationChecks);
		if (checks > request.MaxTopologyChecks)
		{
			budgetExceeded = true;
			return ConstructionFailure(out topology, out failure, "topology-budget-exceeded",
				"The topology check budget was exhausted during final validation.");
		}
		topology = result;
		failure = null;
		return true;

		List<CeramicRectangle> CreateRectangles()
		{
			int minX = cells.Min(cell => cell.X);
			int maxX = cells.Max(cell => cell.X);
			int minZ = cells.Min(cell => cell.Z);
			int maxZ = cells.Max(cell => cell.Z);
			List<CeramicRectangle> values = [];
			for (int height = 4; height <= 8; height++)
			for (int width = 4; width <= 8; width++)
			for (int z = minZ + 1; z + height - 1 < maxZ; z++)
			for (int x = minX + 1; x + width - 1 < maxX; x++)
				values.Add(new(x, z, width, height));
			return values;
		}

		static IEnumerable<CeramicRectangle> CreateWallSharingRectangles(
			CeramicRectangle parent)
		{
			int parentRight = parent.X + parent.Width - 1;
			int parentBottom = parent.Z + parent.Height - 1;
			for (int height = 4; height <= 8; height++)
			for (int width = 4; width <= 8; width++)
			{
				for (int x = parent.X - width + 3; x <= parentRight - 2; x++)
				{
					yield return new(x, parent.Z - height + 1, width, height);
					yield return new(x, parentBottom, width, height);
				}
				for (int z = parent.Z - height + 3; z <= parentBottom - 2; z++)
				{
					yield return new(parent.X - width + 1, z, width, height);
					yield return new(parentRight, z, width, height);
				}
			}
		}

		string[] ExpectedLoopSockets(int index, CeramicRectangle rectangle, string socketType)
		{
			string[] expected = Enumerable.Repeat(CeramicSocket.NoConnection, 4).ToArray();
			CeramicCell cell = cells[index];
			int right = rectangle.X + rectangle.Width - 1;
			int bottom = rectangle.Z + rectangle.Height - 1;
			if (cell.Z == rectangle.Z || cell.Z == bottom)
			{
				if (cell.X > rectangle.X) expected[(int)CeramicDirection.West] = socketType;
				if (cell.X < right) expected[(int)CeramicDirection.East] = socketType;
			}
			if (cell.X == rectangle.X || cell.X == right)
			{
				if (cell.Z > rectangle.Z) expected[(int)CeramicDirection.North] = socketType;
				if (cell.Z < bottom) expected[(int)CeramicDirection.South] = socketType;
			}
			return expected;
		}

		void GrowOrganicNetwork(
			CeramicConnectionPolicy policy,
			CeramicTagQuota quota,
			ref int taggedCount)
		{
			int minX = cells.Min(cell => cell.X) + 2;
			int maxX = cells.Max(cell => cell.X) - 2;
			int minZ = cells.Min(cell => cell.Z) + 2;
			int maxZ = cells.Max(cell => cell.Z) - 2;
			if (minX > maxX || minZ > maxZ) return;
			int spacing = Math.Clamp((int)Math.Sqrt(cells.Length / 45.0), 8, 14);
			int jitter = Math.Max(2, spacing / 3);
			HashSet<int> uniqueTargets = [];
			List<int> targets = [];
			for (int blockZ = minZ; blockZ <= maxZ; blockZ += spacing)
			for (int blockX = minX; blockX <= maxX; blockX += spacing)
			{
				int x = Math.Clamp(blockX + random.NextInt(jitter * 2 + 1) - jitter, minX, maxX);
				int z = Math.Clamp(blockZ + random.NextInt(jitter * 2 + 1) - jitter, minZ, maxZ);
				if (!indices.TryGetValue(new(x, z), out int target)
					|| tags[target].Contains("defense-wall")
					|| !domains[target].Any(option => catalog.Options[option].TagSet.Contains(quota.Tag))
					|| !uniqueTargets.Add(target)) continue;
				targets.Add(target);
			}
			random.Shuffle(targets);
			foreach (int target in targets)
			{
				if (taggedCount >= quota.MinimumCells || budgetExceeded) break;
				HashSet<int> networkSet = Enumerable.Range(0, cells.Length).Where(index =>
					sockets[index].Any(type => string.Equals(type, policy.SocketType,
						StringComparison.Ordinal))).ToHashSet();
				if (networkSet.Contains(target)) continue;
				List<int> starts = networkSet.Where(index =>
					sockets[index].Count(type => string.Equals(type, policy.SocketType,
						StringComparison.Ordinal)) < (policy.Degree.Maximum ?? 4)).ToList();
				random.Shuffle(starts);
				starts = starts.OrderBy(index => Manhattan(cells[index], cells[target])).Take(16).ToList();
				foreach (int start in starts)
				{
					if (!TryFindOrganicPath(start, target, networkSet, quota.Tag,
						out List<int> path)) continue;
					int current = start;
					foreach (int next in path)
					{
						if (taggedCount >= quota.MinimumCells) break;
						CeramicDirection direction = DirectionBetween(cells[current], cells[next]);
						if (!SetEdge(current, direction, policy.SocketType)) break;
						tags[next].Add(quota.Tag);
						taggedCount++;
						current = next;
						if (!CheckBudget()) break;
					}
					break;
				}
			}

			bool TryFindOrganicPath(
				int start,
				int target,
				HashSet<int> networkSet,
				string tag,
				out List<int> path)
			{
				ulong salt = random.NextUInt64();
				int[] costs = Enumerable.Repeat(int.MaxValue, cells.Length).ToArray();
				int[] previous = Enumerable.Repeat(-1, cells.Length).ToArray();
				PriorityQueue<int, long> frontier = new();
				costs[start] = 0;
				frontier.Enqueue(start, Manhattan(cells[start], cells[target]) * 70L);
				while (frontier.TryDequeue(out int current, out long priority))
				{
					cancellationToken.ThrowIfCancellationRequested();
					long expected = (long)costs[current]
						+ Manhattan(cells[current], cells[target]) * 70L;
					if (priority != expected) continue;
					if (current == target) break;
					foreach (CeramicDirection direction in Enum.GetValues<CeramicDirection>())
					{
						CeramicCell nextCell;
						try { nextCell = CeramicGeometry.Offset(cells[current], direction); }
						catch (OverflowException) { continue; }
						if (!indices.TryGetValue(nextCell, out int next)
							|| (current == start && !string.Equals(sockets[start][(int)direction],
								CeramicSocket.NoConnection, StringComparison.Ordinal))
							|| (networkSet.Contains(next) && next != start)
							|| tags[next].Contains("defense-wall")
							|| !domains[next].Any(option => catalog.Options[option].TagSet.Contains(tag)))
							continue;
						int turnPenalty = previous[current] >= 0
							&& DirectionBetween(cells[previous[current]], cells[current]) != direction ? 14 : 0;
						int candidateCost = costs[current] + 100 + turnPenalty
							+ (int)(CoordinateNoise(nextCell, salt) % 61UL);
						if (candidateCost >= costs[next]) continue;
						costs[next] = candidateCost;
						previous[next] = current;
						long estimate = (long)candidateCost + Manhattan(nextCell, cells[target]) * 70L;
						frontier.Enqueue(next, estimate);
						if (!CheckBudget()) { path = []; return false; }
					}
				}
				if (previous[target] < 0) { path = []; return false; }
				path = [];
				for (int current = target; current != start; current = previous[current])
					path.Add(current);
				path.Reverse();
				return true;
			}

			static int Manhattan(CeramicCell first, CeramicCell second) =>
				Math.Abs(first.X - second.X) + Math.Abs(first.Z - second.Z);

			static CeramicDirection DirectionBetween(CeramicCell first, CeramicCell second) =>
				second.X > first.X ? CeramicDirection.East
				: second.X < first.X ? CeramicDirection.West
				: second.Z > first.Z ? CeramicDirection.South
				: CeramicDirection.North;

			static ulong CoordinateNoise(CeramicCell cell, ulong salt)
			{
				unchecked
				{
					ulong value = (((ulong)(uint)cell.X << 32) | (uint)cell.Z) ^ salt;
					value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
					value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
					return value ^ (value >> 31);
				}
			}
		}

		List<int> RectanglePerimeter(CeramicRectangle rectangle)
		{
			List<int> values = [];
			for (int x = rectangle.X; x < rectangle.X + rectangle.Width; x++)
			{
				if (!indices.TryGetValue(new(x, rectangle.Z), out int top)
					|| !indices.TryGetValue(new(x, rectangle.Z + rectangle.Height - 1), out int bottom))
					return [];
				values.Add(top);
				if (bottom != top) values.Add(bottom);
			}
			for (int z = rectangle.Z + 1; z < rectangle.Z + rectangle.Height - 1; z++)
			{
				if (!indices.TryGetValue(new(rectangle.X, z), out int left)
					|| !indices.TryGetValue(new(rectangle.X + rectangle.Width - 1, z), out int right))
					return [];
				values.Add(left);
				if (right != left) values.Add(right);
			}
			return values;
		}

		List<int> RectangleArea(CeramicRectangle rectangle)
		{
			List<int> values = new(rectangle.Width * rectangle.Height);
			for (int z = rectangle.Z; z < rectangle.Z + rectangle.Height; z++)
			for (int x = rectangle.X; x < rectangle.X + rectangle.Width; x++)
			{
				if (!indices.TryGetValue(new(x, z), out int index)) return [];
				values.Add(index);
			}
			return values;
		}

	}

	private static bool ConstructionFailure(
		out IReadOnlyList<CeramicTopologyCell> topology,
		out CeramicGenerationFailure? failure,
		string code,
		string message,
		CeramicCell? cell = null,
		string? socketType = null,
		CeramicDirection? direction = null)
	{
		topology = [];
		failure = new(code, message, cell, CeramicGenerationStage.Topology, socketType, direction);
		return false;
	}

	private bool Search(
		List<(int Cell, int Option)> trail,
		out IReadOnlyList<CeramicTopologyCell> topology)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (budgetExceeded) { topology = []; return false; }
		int selectedCell = -1;
		int smallest = int.MaxValue;
		for (int index = 0; index < domains.Length; index++)
			if (domains[index].Count > 1 && domains[index].Count < smallest)
			{
				selectedCell = index;
				smallest = domains[index].Count;
			}
		if (selectedCell < 0)
		{
			List<CeramicTopologyCell> candidate = new(cells.Length);
			for (int index = 0; index < cells.Length; index++)
				candidate.Add(CreateTopologyCell(index, catalog.Options[domains[index].Single()]));
			if (CeramicTopologyInspector.TryValidate(request, definition, candidate,
				out _, out long validationChecks))
			{
				checks = SaturatingAdd(checks, validationChecks);
				topology = candidate;
				return true;
			}
			checks = SaturatingAdd(checks, validationChecks);
			topology = [];
			return false;
		}

		List<int> choices = domains[selectedCell].OrderBy(option => catalog.Options[option].Key,
			StringComparer.Ordinal).ToList();
		random.Shuffle(choices);
		foreach (int choice in choices)
		{
			int marker = trail.Count;
			foreach (int option in domains[selectedCell].Where(option => option != choice).ToArray())
				Remove(selectedCell, option, trail);
			if (Propagate([selectedCell], trail) && Search(trail, out topology)) return true;
			Rollback(trail, marker);
			if (budgetExceeded) break;
		}
		topology = [];
		return false;
	}

	private bool Propagate(IEnumerable<int> seeds, List<(int Cell, int Option)> trail)
	{
		Queue<int> queue = new(seeds.Distinct());
		while (queue.TryDequeue(out int source))
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (domains[source].Count == 0) return false;
			foreach (CeramicDirection direction in Enum.GetValues<CeramicDirection>())
			{
				CeramicCell neighborCell;
				try { neighborCell = CeramicGeometry.Offset(cells[source], direction); }
				catch (OverflowException) { continue; }
				if (!indices.TryGetValue(neighborCell, out int neighbor)) continue;
				HashSet<string> allowed = domains[source].Select(option =>
					catalog.Options[option].Sockets[(int)direction]).ToHashSet(StringComparer.Ordinal);
				CeramicDirection opposite = CeramicGeometry.Opposite(direction);
				bool changed = false;
				foreach (int option in domains[neighbor].ToArray())
				{
					if (!CheckBudget()) return false;
					if (allowed.Contains(catalog.Options[option].Sockets[(int)opposite])) continue;
					Remove(neighbor, option, trail);
					changed = true;
				}
				if (domains[neighbor].Count == 0) return false;
				if (changed) queue.Enqueue(neighbor);
			}
		}

		foreach (CeramicTagQuota quota in request.TagQuotas)
		{
			int required = 0;
			int possible = 0;
			for (int index = 0; index < domains.Length; index++)
			{
				if (!CheckBudget()) return false;
				bool any = domains[index].Any(option => catalog.Options[option].TagSet.Contains(quota.Tag));
				bool all = domains[index].All(option => catalog.Options[option].TagSet.Contains(quota.Tag));
				if (any) possible++;
				if (all) required++;
			}
			if (possible < quota.MinimumCells || required > (quota.MaximumCells ?? int.MaxValue)) return false;
		}
		return true;
	}

	private CeramicTopologyCell CreateTopologyCell(int cellIndex, CeramicTopologyOption option)
	{
		List<CeramicTopologySocket> sockets = new(4);
		foreach (CeramicDirection direction in Enum.GetValues<CeramicDirection>())
		{
			string type = option.Sockets[(int)direction];
			bool external = request.Entrances.Any(entrance => entrance.Cell == cells[cellIndex]
				&& entrance.Direction == direction
				&& string.Equals(entrance.SocketType, type, StringComparison.Ordinal));
			sockets.Add(new(direction, type, external));
		}
		return new(cells[cellIndex], option.Tags, sockets);
	}

	private void Remove(int cell, int option, List<(int Cell, int Option)> trail)
	{
		if (domains[cell].Remove(option)) trail.Add((cell, option));
	}

	private void Rollback(List<(int Cell, int Option)> trail, int marker)
	{
		for (int index = trail.Count - 1; index >= marker; index--)
			domains[trail[index].Cell].Add(trail[index].Option);
		trail.RemoveRange(marker, trail.Count - marker);
	}

	private bool CheckBudget()
	{
		if (checks >= request.MaxTopologyChecks)
		{
			budgetExceeded = true;
			return false;
		}
		checks++;
		return true;
	}

	private CeramicTopologyAttemptResult Failure(
		CeramicTopologyAttemptStatus status,
		string code,
		string message) => new(status, [], checks,
		new(code, message, Stage: CeramicGenerationStage.Topology));

	private static long SaturatingAdd(long left, long right) =>
		left > long.MaxValue - right ? long.MaxValue : left + right;

	private readonly record struct CeramicRectangle(int X, int Z, int Width, int Height);
}
