using System.Text.Json;
using Voxelgine.Engine.DI;
using Voxelgine.Graphics;

namespace Voxelgine.Engine.World.Structures;

public enum HabitatMilestone : byte
{
	None,
	RelaysOnline,
	Stabilized,
}

public sealed class HabitatProgressionService : IDisposable
{
	private readonly ChunkMap world;
	private readonly InfrastructureMachineService infrastructure;
	private readonly IFishLogging logging;
	private readonly MachineKey[] storyRelays;
	private readonly MachineKey? storyAnchor;
	private readonly DynamicFogController[] fogControllers;
	private bool disposed;

	public HabitatProgressionService(
		ChunkMap world,
		WorldFeaturePlan features,
		InfrastructureMachineService infrastructure,
		IFishLogging logging)
	{
		this.world = world ?? throw new ArgumentNullException(nameof(world));
		this.infrastructure = infrastructure ?? throw new ArgumentNullException(nameof(infrastructure));
		this.logging = logging ?? throw new ArgumentNullException(nameof(logging));

		storyRelays = ResolveStoryMachines(features, StructureRole.Relay, InfrastructureFunctionKind.Relay);
		MachineKey[] anchors = ResolveStoryMachines(features, StructureRole.GravityAnchor, InfrastructureFunctionKind.GravityAnchor);
		storyAnchor = anchors.Length == 0 ? null : anchors[0];
		fogControllers = ResolveFogControllers(features);
		infrastructure.StateChanged += OnMachineStateChanged;
	}

	public HabitatMilestone Milestone { get; private set; }
	public bool GravityAnchorUnlocked => Milestone >= HabitatMilestone.RelaysOnline;

	public event Action<HabitatMilestone> MilestoneChanged;

	public void RestoreMilestone(HabitatMilestone milestone)
	{
		Milestone = milestone;
		ReconcileFogControllers();
	}

	public void Update(long serverTick)
	{
		if (Milestone < HabitatMilestone.RelaysOnline && storyRelays.Length == 3 && storyRelays.All(IsActive))
			PublishMilestone(HabitatMilestone.RelaysOnline);
		if (Milestone == HabitatMilestone.RelaysOnline && storyAnchor is MachineKey anchor && IsActive(anchor))
			PublishMilestone(HabitatMilestone.Stabilized);
	}

	private void OnMachineStateChanged(InfrastructureMachineSnapshot snapshot)
	{
		if (snapshot.State == InfrastructureMachineState.UnpoweredDirty)
			logging.Log(GameLogLevel.Info, "Progression", $"machine disabled immediately key={snapshot.Key} reason=dirty-network");
	}

	private bool IsActive(MachineKey key) =>
		infrastructure.TryGet(key, out InfrastructureMachineSnapshot snapshot) && snapshot.State == InfrastructureMachineState.Active;

	private void PublishMilestone(HabitatMilestone milestone)
	{
		if (milestone <= Milestone)
			return;
		Milestone = milestone;
		logging.Log(GameLogLevel.Info, "Progression", $"milestone={milestone}");
		ReconcileFogControllers();
		MilestoneChanged?.Invoke(milestone);
	}

	private void ReconcileFogControllers()
	{
		foreach (DynamicFogController controller in fogControllers.OrderBy(static value => value.MarkerId.Site).ThenBy(static value => value.MarkerId.BlueprintMarkerId, StringComparer.Ordinal))
		{
			FogVoxel target = Milestone >= HabitatMilestone.Stabilized ? FogVoxel.Empty : controller.Fog;
			for (int z = 0; z < controller.Size.Z; z++)
				for (int y = 0; y < controller.Size.Y; y++)
					for (int x = 0; x < controller.Size.X; x++)
					{
						BlockCoordinate cell = controller.Minimum + new BlockCoordinate(x, y, z);
						if (world.GetFog(cell.X, cell.Y, cell.Z) != target)
							world.SetFog(cell.X, cell.Y, cell.Z, target);
					}
		}
	}

	private static MachineKey[] ResolveStoryMachines(WorldFeaturePlan features, StructureRole role, InfrastructureFunctionKind function)
	{
		HashSet<GeneratedSiteId> sites = features.Sites.Where(site => site.Role == role).Select(static site => site.Id).ToHashSet();
		return features.Markers
			.Where(marker => sites.Contains(marker.Id.Site) && marker.Kind == StructureMarkerKind.MachineFunction)
			.Select(marker => new MachineKey(marker.Position, function))
			.Distinct()
			.OrderBy(static key => key)
			.ToArray();
	}

	private static DynamicFogController[] ResolveFogControllers(WorldFeaturePlan features)
	{
		List<DynamicFogController> result = new();
		foreach (PlannedMarker marker in features.Markers.Where(static marker => marker.Kind == StructureMarkerKind.Effect && marker.Data.Contains("dynamicFog", StringComparison.Ordinal)))
		{
			try
			{
				using JsonDocument document = JsonDocument.Parse(marker.Data);
				JsonElement root = document.RootElement;
				JsonElement size = root.GetProperty("size");
				JsonElement color = root.GetProperty("color");
				byte density = checked((byte)root.GetProperty("density").GetInt32());
				result.Add(new DynamicFogController(marker.Id, marker.Position,
					new BlockCoordinate(size[0].GetInt32(), size[1].GetInt32(), size[2].GetInt32()),
					FogVoxel.FromStraight(new Rgba32(checked((byte)color[0].GetInt32()), checked((byte)color[1].GetInt32()), checked((byte)color[2].GetInt32())), density)));
			}
			catch (Exception exception) when (exception is JsonException or InvalidOperationException or OverflowException)
			{
				throw new InvalidDataException($"Dynamic fog marker {marker.Id} contains invalid controller data.", exception);
			}
		}
		return result.ToArray();
	}

	public void Dispose()
	{
		if (disposed)
			return;
		disposed = true;
		infrastructure.StateChanged -= OnMachineStateChanged;
	}

	private readonly record struct DynamicFogController(
		GeneratedMarkerId MarkerId,
		BlockCoordinate Minimum,
		BlockCoordinate Size,
		FogVoxel Fog);
}
