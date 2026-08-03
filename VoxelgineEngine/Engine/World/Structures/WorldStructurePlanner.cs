using System.Text.Json;
using System.Text.Json.Nodes;

namespace Voxelgine.Engine.World.Structures;

public static class WorldStructurePlanner
{
	private const int SiteMargin = 24;
	private const int MinimumSpacing = 32;

	public static WorldFeatureGenerationResult Plan(
		StructureBlueprintCatalog catalog,
		int[] surfaceHeight,
		int width,
		int length,
		int seed)
	{
		ArgumentNullException.ThrowIfNull(catalog);
		ArgumentNullException.ThrowIfNull(surfaceHeight);
		if (surfaceHeight.Length != checked(width * length))
			throw new ArgumentException("Surface height dimensions do not match the world.", nameof(surfaceHeight));

		List<SiteRequest> requests = BuildRequests(catalog, seed);
		System.Diagnostics.Stopwatch siteTimer = System.Diagnostics.Stopwatch.StartNew();
		List<PlannedSite> sites = new(requests.Count);
		List<StructurePlanningDiagnostic> diagnostics = new(requests.Count);
		List<StructureBounds> criticalReservations = new();
		for (int ordinal = 0; ordinal < requests.Count; ordinal++)
		{
			SiteRequest request = requests[ordinal];
			PlannedSite site = PlaceSite(request, ordinal, surfaceHeight, width, length, seed, sites, criticalReservations, out StructurePlanningDiagnostic diagnostic);
			diagnostics.Add(diagnostic);
			if (site == null)
				continue;
			sites.Add(site);
			if (request.Critical)
				criticalReservations.Add(site.Reservation);
		}

		PlannedSite[] orderedSites = sites
			.OrderBy(static site => CriticalPriority(site.Role))
			.ThenBy(static site => site.Role)
			.ThenBy(static site => site.Id)
			.ToArray();
		TimeSpan siteDuration = siteTimer.Elapsed;
		System.Diagnostics.Stopwatch routeTimer = System.Diagnostics.Stopwatch.StartNew();
		PlannedRoute[] routes = BuildRoutes(orderedSites, surfaceHeight, width, length);
		return new WorldFeatureGenerationResult(new WorldFeaturePlan(orderedSites, routes), diagnostics.ToArray(), siteDuration, routeTimer.Elapsed);
	}

	private static List<SiteRequest> BuildRequests(StructureBlueprintCatalog catalog, int seed)
	{
		List<SiteRequest> result = new();
		Add(result, catalog, StructureRole.Shelter, 1, critical: true);
		Add(result, catalog, StructureRole.Relay, 3, critical: true);
		Add(result, catalog, StructureRole.GravityAnchor, 1, critical: true);
		Add(result, catalog, StructureRole.Shaft, 3, critical: true);
		int supportCount = 16 + Math.Abs(DeriveSeed(seed, "support-count", 0, "support")) % 13;
		Add(result, catalog, StructureRole.Support, supportCount, critical: false);
		return result;
	}

	private static void Add(List<SiteRequest> destination, StructureBlueprintCatalog catalog, StructureRole role, int count, bool critical)
	{
		StructureBlueprint[] choices = catalog.ForRole(role).OrderBy(static value => value.Id, StringComparer.Ordinal).ToArray();
		if (choices.Length == 0)
			throw new InvalidDataException($"No blueprint exists for required role {role}.");
		for (int index = 0; index < count; index++)
			destination.Add(new SiteRequest(role, choices[index % choices.Length], index, critical));
	}

	private static PlannedSite PlaceSite(
		SiteRequest request,
		int ordinal,
		int[] heights,
		int width,
		int length,
		int seed,
		List<PlannedSite> accepted,
		List<StructureBounds> criticalReservations,
		out StructurePlanningDiagnostic diagnostic)
	{
		StructureBlueprint blueprint = request.Blueprint;
		string stableId = $"{request.Role.ToString().ToLowerInvariant()}-{request.RoleOrdinal + 1:00}";
		GeneratedSiteId siteId = new(stableId);
		int siteSeed = DeriveSeed(seed, "site", ordinal, blueprint.Id);
		Random random = new(siteSeed);
		List<string> rejected = new();
		for (int pass = 0; pass < 2; pass++)
		{
			int attempts = pass == 0 ? 48 : 64;
			for (int attempt = 0; attempt < attempts; attempt++)
			{
				int x = random.Next(SiteMargin, Math.Max(SiteMargin + 1, width - SiteMargin));
				int z = random.Next(SiteMargin, Math.Max(SiteMargin + 1, length - SiteMargin));
				int y = SampleFoundationHeight(heights, width, length, x, z, blueprint.Size, pass == 0 ? 3 : 7, out string rejection);
				if (y < 0)
				{
					rejected.Add(rejection);
					continue;
				}
				int rotation = blueprint.AllowedRotations[Math.Abs(DeriveSeed(siteSeed, "rotation", attempt, blueprint.Id)) % blueprint.AllowedRotations.Count];
				BlockCoordinate origin = new(x - blueprint.Anchor.X, y + 1 - blueprint.Anchor.Y, z - blueprint.Anchor.Z);
				StructureBounds reservation = BoundsFor(origin, blueprint.Size, rotation, 3, 4, 3);
				if (accepted.Any(site => HorizontalDistanceSquared(site.Reservation, reservation) < MinimumSpacing * MinimumSpacing))
				{
					rejected.Add("reservation spacing");
					continue;
				}
				PlannedSite planned = BuildSite(siteId, request.Role, blueprint, origin, rotation, reservation, false, reservation);
				diagnostic = new(siteId, blueprint.Id, false, rejected.Distinct().OrderBy(static value => value, StringComparer.Ordinal).ToArray(), reservation);
				return planned;
			}
		}

		if (!request.Critical)
		{
			StructureBounds none = new(default, default);
			diagnostic = new(siteId, blueprint.Id, false,
				rejected.Distinct().Append("optional site skipped").OrderBy(static value => value, StringComparer.Ordinal).ToArray(), none);
			return null;
		}

		BlockCoordinate fallbackOrigin = EmergencyOrigin(request.RoleOrdinal + ordinal, width, length, heights, blueprint);
		StructureBounds emergency = BoundsFor(fallbackOrigin, blueprint.Size, 0, 8, 12, 8);
		if (criticalReservations.Any(bounds => bounds.Intersects(emergency)))
		{
			fallbackOrigin = new BlockCoordinate(
				Math.Clamp(SiteMargin + ordinal * 20, SiteMargin, width - SiteMargin),
				fallbackOrigin.Y,
				Math.Clamp(length - SiteMargin - ordinal * 17, SiteMargin, length - SiteMargin));
			emergency = BoundsFor(fallbackOrigin, blueprint.Size, 0, 8, 12, 8);
		}
		if (criticalReservations.Any(bounds => bounds.Intersects(emergency)))
			throw new InvalidOperationException($"Critical emergency prism for {siteId} overlaps an accepted critical site.");
		PlannedSite fallback = BuildSite(siteId, request.Role, blueprint, fallbackOrigin, 0, emergency, true, emergency);
		diagnostic = new(siteId, blueprint.Id, true, rejected.Distinct().OrderBy(static value => value, StringComparer.Ordinal).ToArray(), emergency);
		return fallback;
	}

	internal static PlannedSite BuildSite(
		GeneratedSiteId id,
		StructureRole role,
		StructureBlueprint blueprint,
		BlockCoordinate origin,
		int rotation,
		StructureBounds reservation,
		bool emergency,
		StructureBounds modification)
	{
		PlannedMarker[] markers = blueprint.Markers
			.Select(marker => BuildMarker(id, marker, blueprint.Size, origin, rotation))
			.ToArray();
		PlannedConnector[] connectors = blueprint.Connectors.Select(connector => new PlannedConnector(
			id, connector.Id, connector.Kind,
			origin + Rotate(connector.Position, blueprint.Size, rotation),
			RotateDirection(connector.Direction, rotation))).ToArray();
		return new PlannedSite(id, role, blueprint.Id, origin, rotation, reservation, emergency, modification, markers, connectors);
	}

	private static PlannedMarker BuildMarker(
		GeneratedSiteId siteId,
		StructureMarker marker,
		BlockCoordinate blueprintSize,
		BlockCoordinate origin,
		int rotation)
	{
		BlockCoordinate localPosition = marker.Position;
		string data = marker.Data;
		if (marker.Kind == StructureMarkerKind.Effect && TryReadDynamicFogSize(marker, out BlockCoordinate fogSize, out JsonObject fogData))
		{
			if (marker.Position.X + fogSize.X > blueprintSize.X ||
				marker.Position.Y + fogSize.Y > blueprintSize.Y ||
				marker.Position.Z + fogSize.Z > blueprintSize.Z)
			{
				throw new InvalidDataException($"Dynamic fog marker '{marker.Id}' extends outside blueprint bounds.");
			}
			localPosition = RotateBoundsMinimum(marker.Position, fogSize, blueprintSize, rotation);
			BlockCoordinate rotatedSize = RotateBoundsSize(fogSize, rotation);
			fogData["size"] = new JsonArray(rotatedSize.X, rotatedSize.Y, rotatedSize.Z);
			data = fogData.ToJsonString();
		}
		else
		{
			localPosition = Rotate(localPosition, blueprintSize, rotation);
			data = PhaseOneMarkerSchemas.RotateData(marker.Kind,data,blueprintSize,rotation,origin);
		}

		return new PlannedMarker(
			new GeneratedMarkerId(siteId, marker.Id),
			marker.Kind,
			origin + localPosition,
			marker.ExpectedBlock,
			data);
	}

	public static BlockCoordinate RotateLocalPosition(BlockCoordinate position,BlockCoordinate size,int rotation)=>Rotate(position,size,rotation);

	private static bool TryReadDynamicFogSize(
		StructureMarker marker,
		out BlockCoordinate size,
		out JsonObject data)
	{
		size = default;
		data = null;
		if (string.IsNullOrWhiteSpace(marker.Data))
			return false;
		try
		{
			data = JsonNode.Parse(marker.Data)?.AsObject();
			bool isDynamicFog = data?["dynamicFog"]?.GetValue<bool>() == true
				|| string.Equals(data?["effect"]?.GetValue<string>(), "dynamicFog", StringComparison.Ordinal);
			if (data == null || !isDynamicFog)
				return false;
			JsonArray values = data["size"]?.AsArray()
				?? throw new InvalidDataException($"Dynamic fog marker '{marker.Id}' has no size.");
			if (values.Count != 3)
				throw new InvalidDataException($"Dynamic fog marker '{marker.Id}' size must contain three values.");
			size = new BlockCoordinate(
				values[0]?.GetValue<int>() ?? 0,
				values[1]?.GetValue<int>() ?? 0,
				values[2]?.GetValue<int>() ?? 0);
			if (size.X <= 0 || size.Y <= 0 || size.Z <= 0)
				throw new InvalidDataException($"Dynamic fog marker '{marker.Id}' size must be positive.");
			return true;
		}
		catch (Exception exception) when (exception is JsonException or InvalidOperationException or FormatException)
		{
			throw new InvalidDataException($"Dynamic fog marker '{marker.Id}' contains invalid data.", exception);
		}
	}

	private static PlannedRoute[] BuildRoutes(PlannedSite[] sites, int[] heights, int width, int length)
	{
		List<PlannedRoute> routes = new();
		BuildRouteNetwork(routes, sites, StructureConnectorKind.Road, heights, width, length, heightOffset: 0, loopCount: 2);
		BuildRouteNetwork(routes, sites, StructureConnectorKind.Conduit, heights, width, length, heightOffset: 1, loopCount: 1);
		return routes.OrderBy(static route => route.Kind).ThenBy(static route => route.SourceSite)
			.ThenBy(static route => route.DestinationSite).ThenBy(static route => route.Id, StringComparer.Ordinal).ToArray();
	}

	private static void BuildRouteNetwork(
		List<PlannedRoute> routes,
		PlannedSite[] sites,
		StructureConnectorKind kind,
		int[] heights,
		int width,
		int length,
		int heightOffset,
		int loopCount)
	{
		RouteNode[] nodes = sites
			.Select(site => (Site: site, Connector: site.Connectors.FirstOrDefault(connector => connector.Kind == kind)))
			.Where(static value => value.Connector.Id != null)
			.Select(static value => new RouteNode(value.Site, value.Connector))
			.OrderBy(static value => value.Site.Role == StructureRole.Shelter ? 0 : 1)
			.ThenBy(static value => value.Site.Id)
			.ToArray();
		if (nodes.Length < 2)
			return;

		HashSet<int> connected = [0];
		HashSet<RouteEdge> selected = new();
		while (connected.Count < nodes.Length)
		{
			RouteEdge best = default;
			long bestCost = long.MaxValue;
			int nextNode = -1;
			foreach (int source in connected.OrderBy(static value => value))
			{
				for (int destination = 0; destination < nodes.Length; destination++)
				{
					if (connected.Contains(destination))
						continue;
					long cost = RouteCost(RouteEndpoint(nodes[source].Connector), RouteEndpoint(nodes[destination].Connector));
					RouteEdge candidate = RouteEdge.Create(source, destination);
					if (cost < bestCost || cost == bestCost && candidate.CompareTo(best) < 0)
					{
						best = candidate;
						bestCost = cost;
						nextNode = destination;
					}
				}
			}
			selected.Add(best);
			connected.Add(nextNode);
		}

		IEnumerable<RouteEdge> loopCandidates = Enumerable.Range(0, nodes.Length)
			.SelectMany(source => Enumerable.Range(source + 1, nodes.Length - source - 1)
				.Select(destination => RouteEdge.Create(source, destination)))
			.Where(edge => !selected.Contains(edge))
			.OrderBy(edge => RouteCost(RouteEndpoint(nodes[edge.First].Connector), RouteEndpoint(nodes[edge.Second].Connector)))
			.ThenBy(static edge => edge);
		foreach (RouteEdge edge in loopCandidates.Take(loopCount))
			selected.Add(edge);

		int ordinal = 1;
		foreach (RouteEdge edge in selected.OrderBy(static value => value))
		{
			RouteNode source = nodes[edge.First];
			RouteNode destination = nodes[edge.Second];
			string id = $"{kind.ToString().ToLowerInvariant()}.{ordinal++:00}.{source.Site.Id}.{destination.Site.Id}";
			routes.Add(new PlannedRoute(
				id,
				kind,
				source.Site.Id,
				source.Connector.Id,
				destination.Site.Id,
				destination.Connector.Id,
				RasterizeRoute(RouteEndpoint(source.Connector), RouteEndpoint(destination.Connector), heights, width, length, heightOffset)));
		}
	}

	private static BlockCoordinate RouteEndpoint(PlannedConnector connector) =>
		connector.Kind == StructureConnectorKind.Road
			? connector.Position + new BlockCoordinate(0, -1, 0)
			: connector.Position;

	private static long RouteCost(BlockCoordinate left, BlockCoordinate right)
	{
		long x = left.X - right.X;
		long y = left.Y - right.Y;
		long z = left.Z - right.Z;
		return x * x + z * z + y * y * 16;
	}

	private static BlockCoordinate[] RasterizeRoute(BlockCoordinate start, BlockCoordinate end, int[] heights, int width, int length, int heightOffset)
	{
		BlockCoordinate[] terrainPath = FindTerrainRoute(start, end, heights, width, length, heightOffset);
		if (terrainPath.Length != 0)
			return ConnectRouteEndpoints(start, end, terrainPath);
		return RasterizeRouteFallback(start, end, heights, width, length, heightOffset);
	}

	private static BlockCoordinate[] FindTerrainRoute(
		BlockCoordinate start,
		BlockCoordinate end,
		int[] heights,
		int width,
		int length,
		int heightOffset)
	{
		const int corridorMargin = 24;
		int minimumX = Math.Max(0, Math.Min(start.X, end.X) - corridorMargin);
		int maximumX = Math.Min(width - 1, Math.Max(start.X, end.X) + corridorMargin);
		int minimumZ = Math.Max(0, Math.Min(start.Z, end.Z) - corridorMargin);
		int maximumZ = Math.Min(length - 1, Math.Max(start.Z, end.Z) + corridorMargin);
		int localWidth = maximumX - minimumX + 1;
		int localLength = maximumZ - minimumZ + 1;
		int count = checked(localWidth * localLength);
		int[] costs = new int[count];
		int[] parents = new int[count];
		bool[] closed = new bool[count];
		Array.Fill(costs, int.MaxValue);
		Array.Fill(parents, -1);
		int startIndex = ToRouteIndex(start.X, start.Z, minimumX, minimumZ, localLength);
		int endIndex = ToRouteIndex(end.X, end.Z, minimumX, minimumZ, localLength);
		costs[startIndex] = 0;
		PriorityQueue<int, RoutePriority> open = new();
		open.Enqueue(startIndex, new RoutePriority(RouteHeuristic(start.X, start.Z, end.X, end.Z), start.X, start.Z));
		ReadOnlySpan<(int X, int Z)> directions = [(-1, 0), (0, -1), (1, 0), (0, 1)];
		while (open.TryDequeue(out int current, out _))
		{
			if (closed[current])
				continue;
			closed[current] = true;
			if (current == endIndex)
				break;
			FromRouteIndex(current, minimumX, minimumZ, localLength, out int x, out int z);
			int currentHeight = heights[x * length + z];
			foreach ((int offsetX, int offsetZ) in directions)
			{
				int nextX = x + offsetX;
				int nextZ = z + offsetZ;
				if (nextX < minimumX || nextX > maximumX || nextZ < minimumZ || nextZ > maximumZ)
					continue;
				int nextHeight = heights[nextX * length + nextZ];
				if (currentHeight < 0 || nextHeight < 0)
					continue;
				int next = ToRouteIndex(nextX, nextZ, minimumX, minimumZ, localLength);
				if (closed[next])
					continue;
				int stepCost = 10 + Math.Abs(nextHeight - currentHeight) * 18;
				int candidate = costs[current] + stepCost;
				if (candidate >= costs[next])
					continue;
				costs[next] = candidate;
				parents[next] = current;
				int priority = candidate + RouteHeuristic(nextX, nextZ, end.X, end.Z);
				open.Enqueue(next, new RoutePriority(priority, nextX, nextZ));
			}
		}

		if (parents[endIndex] < 0 && endIndex != startIndex)
			return Array.Empty<BlockCoordinate>();
		List<BlockCoordinate> reversed = new();
		for (int current = endIndex; current >= 0; current = parents[current])
		{
			FromRouteIndex(current, minimumX, minimumZ, localLength, out int x, out int z);
			reversed.Add(new BlockCoordinate(x, Math.Max(0, heights[x * length + z] + heightOffset), z));
			if (current == startIndex)
				break;
		}
		reversed.Reverse();
		return reversed.ToArray();
	}

	private static BlockCoordinate[] ConnectRouteEndpoints(
		BlockCoordinate start,
		BlockCoordinate end,
		BlockCoordinate[] terrainPath)
	{
		List<BlockCoordinate> connected = new(terrainPath.Length * 2 + 8) { start };
		AppendVertical(connected, start, terrainPath[0]);
		BlockCoordinate previous = terrainPath[0];
		connected.Add(previous);
		for (int index = 1; index < terrainPath.Length; index++)
		{
			BlockCoordinate next = terrainPath[index];
			AppendVerticalAt(connected, next.X, next.Z, previous.Y, next.Y);
			connected.Add(next);
			previous = next;
		}
		AppendVertical(connected, terrainPath[^1], end);
		connected.Add(end);
		return connected.Distinct().ToArray();
	}

	private static BlockCoordinate[] RasterizeRouteFallback(BlockCoordinate start, BlockCoordinate end, int[] heights, int width, int length, int heightOffset)
	{
		List<BlockCoordinate> result = new();
		int x = start.X;
		int z = start.Z;
		int dx = Math.Abs(end.X - start.X);
		int dz = Math.Abs(end.Z - start.Z);
		int sx = start.X < end.X ? 1 : -1;
		int sz = start.Z < end.Z ? 1 : -1;
		int error = dx - dz;
		while (true)
		{
			if ((uint)x < (uint)width && (uint)z < (uint)length)
				result.Add(new BlockCoordinate(x, Math.Max(0, heights[x * length + z] + heightOffset), z));
			if (x == end.X && z == end.Z)
				break;
			int twice = error * 2;
			if (twice > -dz) { error -= dz; x += sx; }
			if (twice < dx) { error += dx; z += sz; }
		}
		BlockCoordinate[] middle = result.ToArray();
		List<BlockCoordinate> connected = new(middle.Length + 8) { start };
		if (middle.Length > 0)
		{
			AppendVertical(connected, start, middle[0]);
			connected.AddRange(middle);
			AppendVertical(connected, middle[^1], end);
		}
		connected.Add(end);
		return connected.Distinct().ToArray();
	}

	private static int ToRouteIndex(int x, int z, int minimumX, int minimumZ, int localLength) =>
		(x - minimumX) * localLength + z - minimumZ;

	private static void FromRouteIndex(int index, int minimumX, int minimumZ, int localLength, out int x, out int z)
	{
		x = index / localLength + minimumX;
		z = index % localLength + minimumZ;
	}

	private static int RouteHeuristic(int x, int z, int destinationX, int destinationZ) =>
		checked((Math.Abs(destinationX - x) + Math.Abs(destinationZ - z)) * 10);

	private static void AppendVerticalAt(List<BlockCoordinate> destination, int x, int z, int fromY, int toY)
	{
		if (fromY == toY)
			return;
		int step = fromY <= toY ? 1 : -1;
		for (int y = fromY + step; y != toY; y += step)
			destination.Add(new BlockCoordinate(x, y, z));
	}

	private static void AppendVertical(List<BlockCoordinate> destination, BlockCoordinate from, BlockCoordinate to)
	{
		int step = from.Y <= to.Y ? 1 : -1;
		for (int y = from.Y; y != to.Y; y += step)
			destination.Add(new BlockCoordinate(from.X, y, from.Z));
	}

	private static int SampleFoundationHeight(int[] heights, int width, int length, int centerX, int centerZ, BlockCoordinate size, int maximumDelta, out string rejection)
	{
		int minimum = int.MaxValue;
		int maximum = int.MinValue;
		for (int x = centerX - size.X / 2; x <= centerX + size.X / 2; x++)
		{
			for (int z = centerZ - size.Z / 2; z <= centerZ + size.Z / 2; z++)
			{
				if ((uint)x >= (uint)width || (uint)z >= (uint)length || heights[x * length + z] < 0)
				{
					rejection = "missing terrain";
					return -1;
				}
				minimum = Math.Min(minimum, heights[x * length + z]);
				maximum = Math.Max(maximum, heights[x * length + z]);
			}
		}
		if (maximum - minimum > maximumDelta)
		{
			rejection = "slope";
			return -1;
		}
		rejection = string.Empty;
		return maximum;
	}

	private static BlockCoordinate EmergencyOrigin(int ordinal, int width, int length, int[] heights, StructureBlueprint blueprint)
	{
		int x = Math.Clamp(width / 2 + (ordinal % 4 - 2) * 48, SiteMargin, width - SiteMargin);
		int z = Math.Clamp(length / 2 + (ordinal / 4 - 2) * 48, SiteMargin, length - SiteMargin);
		int y = Math.Max(1, heights[x * length + z] + 1);
		return new BlockCoordinate(x - blueprint.Anchor.X, y - blueprint.Anchor.Y, z - blueprint.Anchor.Z);
	}

	internal static BlockCoordinate Rotate(BlockCoordinate local, BlockCoordinate size, int rotation) => rotation switch
	{
		0 => local,
		90 => new BlockCoordinate(size.Z - 1 - local.Z, local.Y, local.X),
		180 => new BlockCoordinate(size.X - 1 - local.X, local.Y, size.Z - 1 - local.Z),
		270 => new BlockCoordinate(local.Z, local.Y, size.X - 1 - local.X),
		_ => throw new ArgumentOutOfRangeException(nameof(rotation)),
	};

	internal static BlockCoordinate RotateDirection(BlockCoordinate direction, int rotation) => rotation switch
	{
		0 => direction,
		90 => new BlockCoordinate(-direction.Z, direction.Y, direction.X),
		180 => new BlockCoordinate(-direction.X, direction.Y, -direction.Z),
		270 => new BlockCoordinate(direction.Z, direction.Y, -direction.X),
		_ => throw new ArgumentOutOfRangeException(nameof(rotation)),
	};

	internal static BlockCoordinate RotateBoundsMinimum(
		BlockCoordinate minimum,
		BlockCoordinate boundsSize,
		BlockCoordinate containerSize,
		int rotation)
	{
		BlockCoordinate maximum = minimum + boundsSize - new BlockCoordinate(1, 1, 1);
		BlockCoordinate[] corners =
		[
			new(minimum.X, minimum.Y, minimum.Z),
			new(maximum.X, minimum.Y, minimum.Z),
			new(minimum.X, minimum.Y, maximum.Z),
			new(maximum.X, minimum.Y, maximum.Z),
		];
		BlockCoordinate[] rotated = corners.Select(corner => Rotate(corner, containerSize, rotation)).ToArray();
		return new BlockCoordinate(
			rotated.Min(static corner => corner.X),
			minimum.Y,
			rotated.Min(static corner => corner.Z));
	}

	internal static BlockCoordinate RotateBoundsSize(BlockCoordinate size, int rotation) => rotation switch
	{
		0 or 180 => size,
		90 or 270 => new BlockCoordinate(size.Z, size.Y, size.X),
		_ => throw new ArgumentOutOfRangeException(nameof(rotation)),
	};

	private static StructureBounds BoundsFor(BlockCoordinate origin, BlockCoordinate size, int rotation, int horizontal, int below, int above)
	{
		int width = rotation is 90 or 270 ? size.Z : size.X;
		int depth = rotation is 90 or 270 ? size.X : size.Z;
		return new StructureBounds(
			new BlockCoordinate(origin.X - horizontal, origin.Y - below, origin.Z - horizontal),
			new BlockCoordinate(origin.X + width - 1 + horizontal, origin.Y + size.Y - 1 + above, origin.Z + depth - 1 + horizontal));
	}

	private static long HorizontalDistanceSquared(StructureBounds left, StructureBounds right)
	{
		long leftX = (left.Minimum.X + left.Maximum.X) / 2;
		long leftZ = (left.Minimum.Z + left.Maximum.Z) / 2;
		long rightX = (right.Minimum.X + right.Maximum.X) / 2;
		long rightZ = (right.Minimum.Z + right.Maximum.Z) / 2;
		long dx = leftX - rightX;
		long dz = leftZ - rightZ;
		return dx * dx + dz * dz;
	}

	private static int CriticalPriority(StructureRole role) => role == StructureRole.Support ? 1 : 0;

	private static int DeriveSeed(int seed, string subsystem, int ordinal, string id)
	{
		unchecked
		{
			uint hash = 2166136261;
			foreach (char value in $"{seed}:{subsystem}:{ordinal}:{id}")
			{
				hash ^= value;
				hash *= 16777619;
			}
			return (int)hash;
		}
	}

	private readonly record struct SiteRequest(StructureRole Role, StructureBlueprint Blueprint, int RoleOrdinal, bool Critical);
	private readonly record struct RouteNode(PlannedSite Site, PlannedConnector Connector);
	private readonly record struct RouteEdge(int First, int Second) : IComparable<RouteEdge>
	{
		public static RouteEdge Create(int left, int right) => left < right ? new(left, right) : new(right, left);

		public int CompareTo(RouteEdge other)
		{
			int first = First.CompareTo(other.First);
			return first != 0 ? first : Second.CompareTo(other.Second);
		}
	}
	private readonly record struct RoutePriority(int Cost, int X, int Z) : IComparable<RoutePriority>
	{
		public int CompareTo(RoutePriority other)
		{
			int cost = Cost.CompareTo(other.Cost);
			if (cost != 0) return cost;
			int x = X.CompareTo(other.X);
			return x != 0 ? x : Z.CompareTo(other.Z);
		}
	}
}
