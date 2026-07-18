using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.FishGfxClient.Effects;
using Voxelgine.FishGfxClient.Voxels;
using Voxelgine.Graphics;
using Voxelgine.States;

namespace UnitTest;

public sealed class FishGfxVoxelSupportTests
{
	[Fact]
	public void CampfireIndex_ResetCapturesNegativeCoordinatesInDeterministicOrder()
	{
		ChunkMap map = CreateMap();
		map.SetBlock(16, 3, 1, BlockType.Campfire);
		map.SetBlock(-17, -1, -16, BlockType.Campfire);
		map.SetBlock(0, 0, 0, BlockType.Campfire);
		map.SetBlock(4, 5, 6, BlockType.Stone);
		CampfireEmitterIndex index = new();

		index.Reset(map.CaptureChunks());

		Assert.Equal(3, index.Count);
		Assert.Equal(
			new[]
			{
				new Vector3(-16.5f, -0.5f, -15.5f),
				new Vector3(0.5f, 0.5f, 0.5f),
				new Vector3(16.5f, 3.5f, 1.5f),
			},
			index.Positions);
	}

	[Fact]
	public void CampfireIndex_ApplyUpdatesOnlyCampfireTransitionsAndKeepsOldSnapshotsStable()
	{
		CampfireEmitterIndex index = new();
		ChunkMap map = CreateMap();
		map.SetBlock(1, 2, 3, BlockType.Campfire);
		index.Reset(map.CaptureChunks());
		IReadOnlyList<Vector3> original = index.Positions;

		index.Apply(new BlockChange(9, 9, 9, BlockType.Stone, BlockType.Dirt));
		Assert.Same(original, index.Positions);

		index.Apply(new BlockChange(1, 2, 3, BlockType.Campfire, BlockType.Stone));
		index.Apply(new BlockChange(-1, -2, -3, BlockType.None, BlockType.Campfire));

		Assert.Equal(new[] { new Vector3(-0.5f, -1.5f, -2.5f) }, index.Positions);
		Assert.Equal(new[] { new Vector3(1.5f, 2.5f, 3.5f) }, original);
	}

	[Fact]
	public void CampfireIndex_TracksCampfiresAndTorchesForParticleEmission()
	{
		ChunkMap map = CreateMap();
		map.SetBlock(4, 5, 6, BlockType.Torch);
		map.SetBlock(-2, 3, 1, BlockType.Campfire);
		CampfireEmitterIndex index = new();

		index.Reset(map.CaptureChunks());

		Assert.Equal(1, index.Count);
		Assert.Equal(1, index.TorchCount);
		Assert.Equal(
			new[]
			{
				new VoxelFireEmitter(BlockType.Campfire, new Vector3(-1.5f, 3.5f, 1.5f)),
				new VoxelFireEmitter(BlockType.Torch, new Vector3(4.5f, 5.5f, 6.5f)),
			},
			index.ParticleEmitters
		);

		index.Apply(new BlockChange(4, 5, 6, BlockType.Torch, BlockType.Stone));
		index.Apply(new BlockChange(8, 9, 10, BlockType.None, BlockType.Torch));

		Assert.Equal(1, index.TorchCount);
		Assert.Contains(
			new VoxelFireEmitter(BlockType.Torch, new Vector3(8.5f, 9.5f, 10.5f)),
			index.ParticleEmitters
		);
	}

	[Fact]
	public void FireEmissionScheduler_EmitsBothVoxelTypesAtStableRates()
	{
		VoxelFireEmissionScheduler scheduler = new();
		List<VoxelFireEmission> output = new();
		VoxelFireEmitter campfire = new(BlockType.Campfire, new Vector3(1.5f, 2.5f, 3.5f));
		VoxelFireEmitter torch = new(BlockType.Torch, new Vector3(2.5f, 2.5f, 3.5f));

		scheduler.Advance(0, Vector3.Zero, [campfire, torch], output);

		Assert.Equal(3, output.Count);
		Assert.Contains(new VoxelFireEmission(campfire, VoxelFireEmissionKind.Flame), output);
		Assert.Contains(new VoxelFireEmission(campfire, VoxelFireEmissionKind.Smoke), output);
		Assert.Contains(new VoxelFireEmission(torch, VoxelFireEmissionKind.Flame), output);

		scheduler.Advance(0.1f, Vector3.Zero, [campfire, torch], output);
		Assert.Empty(output);

		scheduler.Advance(0.02f, Vector3.Zero, [campfire, torch], output);
		Assert.Equal(
			new VoxelFireEmission(campfire, VoxelFireEmissionKind.Flame),
			Assert.Single(output)
		);
	}

	[Fact]
	public void FireEmissionScheduler_IgnoresEmittersOutsideTheEffectRange()
	{
		VoxelFireEmissionScheduler scheduler = new();
		List<VoxelFireEmission> output = new();
		VoxelFireEmitter distant = new(
			BlockType.Torch,
			new Vector3(VoxelFireEmissionScheduler.MaximumDistance + 1, 0, 0)
		);

		scheduler.Advance(1, Vector3.Zero, [distant], output);

		Assert.Empty(output);
	}

	[Theory]
	[InlineData(-1, 0)]
	[InlineData(0, 0)]
	[InlineData(7, 7f / 15f)]
	[InlineData(15, 1)]
	[InlineData(16, 1)]
	public void NormalizeSkyLight_ClampsToUnitRange(int value, float expected)
	{
		Assert.Equal(expected, VoxelEnvironmentSampling.NormalizeSkyLight(value), 0.0001f);
	}

	[Fact]
	public void OutdoorExposure_AveragesNormalizedProbesAndHandlesEmptyInput()
	{
		byte[] probes = { 0, 15, 30 };

		Assert.Equal(
			2f / 3f,
			VoxelEnvironmentSampling.CalculateOutdoorExposure(probes),
			0.0001f);
		Assert.Equal(
			0,
			VoxelEnvironmentSampling.CalculateOutdoorExposure(ReadOnlySpan<byte>.Empty));
	}

	[Theory]
	[InlineData(15, 0, 1, 0, 15)]
	[InlineData(15, 0, 0.15f, 1, 2)]
	[InlineData(15, 10, 0.15f, 1, 10)]
	[InlineData(0, 0, 0, 2, 2)]
	[InlineData(30, 30, 2, 30, 15)]
	public void CombineLightLevel_AppliesDayNightWithoutDimmingBlockLight(
		int sky,
		int block,
		float multiplier,
		int ambient,
		int expected)
	{
		Assert.Equal(
			expected,
			VoxelEnvironmentSampling.CombineLightLevel(sky, block, multiplier, ambient)
		);
	}

	private static ChunkMap CreateMap()
	{
		return new ChunkMap();
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
