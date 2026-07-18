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
		FishDI services = new();
		services.AddSingleton<IFishLogging, NullLogging>();
		services.AddSingleton<ILerpManager, LerpManager>();
		services.Build();
		services.CreateScope();
		return new TestEngineRunner { DI = services };
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
		public FishDI DI { get; set; } = null!;
		public int ChunkDrawCalls { get; set; }
		public bool DebugMode { get; set; }
		public float TotalTime { get; set; }
		public MainMenuStateFishUI MainMenuState { get; set; } = null!;
		public NPCPreviewState NPCPreviewState { get; set; } = null!;
		public EffectsPreviewState EffectsPreviewState { get; set; } = null!;
		public MPClientGameState MultiplayerGameState { get; set; } = null!;

		public void Init() { }
	}
}
