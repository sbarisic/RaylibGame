using Voxelgine.Engine;
using Voxelgine.Engine.DI;

namespace VoxelgineEngine.Tests;

public sealed class RuntimePathResolverTests
{
	[Fact]
	public void ExplicitRootIsNormalizedAndOwnsAllMutablePaths()
	{
		string workingDirectory = Path.Combine(Path.GetTempPath(), $"aurora-working-{Guid.NewGuid():N}");
		RuntimePaths paths = RuntimePathResolver.ResolveRuntimePaths(
			ApplicationKind.Client,
			"relative-runtime",
			workingDirectory);

		string expectedRoot = Path.GetFullPath("relative-runtime", workingDirectory);
		Assert.Equal(expectedRoot, paths.Root);
		Assert.Equal(Path.Combine(expectedRoot, "config.json"), paths.ConfigurationFile);
		Assert.Equal(Path.Combine(expectedRoot, "worlds"), paths.WorldDirectory);
		Assert.Equal(Path.Combine(expectedRoot, "players"), paths.PlayerDirectory);
		Assert.Equal(Path.Combine(expectedRoot, "logs"), paths.LogDirectory);
	}

	[Fact]
	public void TestApplicationGetsAnIsolatedTemporaryRoot()
	{
		RuntimePaths first = RuntimePathResolver.ResolveRuntimePaths(
			ApplicationKind.Test,
			null,
			Environment.CurrentDirectory);
		RuntimePaths second = RuntimePathResolver.ResolveRuntimePaths(
			ApplicationKind.Test,
			null,
			Environment.CurrentDirectory);

		Assert.NotEqual(first.Root, second.Root);
		Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), first.Root, StringComparison.OrdinalIgnoreCase);
		Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()), second.Root, StringComparison.OrdinalIgnoreCase);
	}
}

public sealed class WorldLightingOwnershipTests
{
	[Fact]
	public void SimulationsOwnIndependentLightingSnapshots()
	{
		TestEngineRunner runner = new();
		GameSimulation first = new(runner);
		GameSimulation second = new(runner);
		WorldLightingState secondBefore = second.Lighting;

		first.PublishLighting(new WorldLightingState(0.25f, 4, true, 99));

		Assert.Equal(new WorldLightingState(0.25f, 4, true, 99), first.Lighting);
		Assert.Equal(secondBefore, second.Lighting);
	}

	[Fact]
	public void LightingServiceProducesANewSnapshotWithoutOwningSimulation()
	{
		WorldLightingService service = new();
		WorldLightingState current = new(1f, 2, true, 41);

		WorldLightingState next = service.Calculate(
			current,
			new DayNightLightingState(0.4f, 3));

		Assert.Equal(new WorldLightingState(0.4f, 3, true, 42), next);
		Assert.Equal(new WorldLightingState(1f, 2, true, 41), current);
	}

	private sealed class TestEngineRunner : IFishEngineRunner
	{
		public IFishLogging Logging { get; } = new NullLogging();
		public ILerpManager LerpManager { get; } = new LerpManager();
		public int ChunkDrawCalls { get; set; }
		public bool DebugMode { get; set; }
		public float TotalTime { get; set; }
	}

	private sealed class NullLogging : IFishLogging
	{
		public void Init(bool IsServer = false) { }
		public void WriteLine(string message) { }
		public void ServerWriteLine(string message) { }
		public void ClientWriteLine(string message) { }
		public void ServerNetworkWriteLine(string message) { }
		public void ClientNetworkWriteLine(string message) { }
	}
}
