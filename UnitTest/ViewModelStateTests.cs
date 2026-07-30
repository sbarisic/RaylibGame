using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.States;

namespace UnitTest;

public sealed class ViewModelStateTests
{
	[Fact]
	public void FishGfxStateConstruction_DoesNotRequireLegacyGraphicsInitialization()
	{
		TestEngineRunner engine = CreateEngine();

		using ViewModel viewModel = new(engine, useLegacyRenderer: false);
		viewModel.SetPresentationAsset(ViewModelAssetKind.Gun);

		string debugInfo = viewModel.GetDebugInfo();
		Assert.Contains("Renderer: FishGfx", debugInfo);
		Assert.Contains("Weapon: Gun", debugInfo);
	}

	private static TestEngineRunner CreateEngine()
	{
		return new TestEngineRunner();
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

	private sealed class TestEngineRunner : IFishEngineRunner
	{
		public IFishLogging Logging { get; } = new NullLogging();
		public ILerpManager LerpManager { get; } = new LerpManager();
		public int ChunkDrawCalls { get; set; }
		public bool DebugMode { get; set; }
		public float TotalTime { get; set; }
	}
}
