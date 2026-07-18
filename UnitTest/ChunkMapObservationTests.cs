using System;
using System.Collections.Generic;
using System.IO;
using Voxelgine.Engine;
using Voxelgine.Engine.DI;
using Voxelgine.Graphics;
using Voxelgine.States;

namespace UnitTest;

public sealed class ChunkMapObservationTests
{
	[Fact]
	public void SetBlock_EmitsOneChangeForPlacementReplacementAndRemoval()
	{
		ChunkMap map = CreateMap();
		List<BlockChange> changes = new();
		map.BlockChanged += changes.Add;

		map.SetBlock(-1, -16, -17, BlockType.Water);
		map.SetBlock(-1, -16, -17, BlockType.Water);
		map.SetBlock(-1, -16, -17, BlockType.Glass);
		map.SetBlock(-1, -16, -17, BlockType.None);

		Assert.Collection(
			changes,
			change => AssertChange(change, -1, -16, -17, BlockType.None, BlockType.Water),
			change => AssertChange(change, -1, -16, -17, BlockType.Water, BlockType.Glass),
			change => AssertChange(change, -1, -16, -17, BlockType.Glass, BlockType.None));
		Assert.Equal(changes, map.GetPendingChanges());
		Assert.Equal(BlockType.None, map.GetBlock(-1, -16, -17));
	}

	[Fact]
	public void CaptureChunks_CoversNegativeCoordinatesBoundariesAndNewChunksCellForCell()
	{
		ChunkMap map = CreateMap();
		map.SetBlock(-17, -1, -16, BlockType.Water);
		map.SetBlock(-16, 0, -1, BlockType.Glass);
		map.SetBlock(15, 15, 15, BlockType.Plank);
		map.SetBlock(16, 16, 16, BlockType.Grass);

		IReadOnlyList<ChunkSnapshot> snapshots = map.CaptureChunks();

		Assert.Equal(4, snapshots.Count);
		Assert.Collection(
			snapshots,
			snapshot => AssertChunk(snapshot, -2, -1, -1),
			snapshot => AssertChunk(snapshot, -1, 0, -1),
			snapshot => AssertChunk(snapshot, 0, 0, 0),
			snapshot => AssertChunk(snapshot, 1, 1, 1));

		foreach (ChunkSnapshot snapshot in snapshots)
		{
			Assert.Equal(ChunkSnapshot.BlockCount, snapshot.Blocks.Count);
			for (int z = 0; z < ChunkSnapshot.Size; z++)
			{
				for (int y = 0; y < ChunkSnapshot.Size; y++)
				{
					for (int x = 0; x < ChunkSnapshot.Size; x++)
					{
						int worldX = snapshot.ChunkX * ChunkSnapshot.Size + x;
						int worldY = snapshot.ChunkY * ChunkSnapshot.Size + y;
						int worldZ = snapshot.ChunkZ * ChunkSnapshot.Size + z;
						Assert.Equal(map.GetBlock(worldX, worldY, worldZ), snapshot.GetBlock(x, y, z));
					}
				}
			}
		}

		ChunkSnapshot originalSnapshot = snapshots[2];
		map.SetBlock(15, 15, 15, BlockType.Water);
		Assert.Equal(BlockType.Plank, originalSnapshot.GetBlock(15, 15, 15));
	}

	[Fact]
	public void Read_RaisesOneResetNoChangesAndPreservesSaveBytesAndCells()
	{
		ChunkMap source = CreateMap();
		source.SetBlock(-17, 2, 31, BlockType.Water);
		source.SetBlock(0, 0, 0, BlockType.Stone);
		source.SetBlock(16, 15, -1, BlockType.Glowstone);
		byte[] originalBytes = Write(source);

		ChunkMap loaded = CreateMap();
		int resetCount = 0;
		int changeCount = 0;
		loaded.WorldReset += () => resetCount++;
		loaded.BlockChanged += _ => changeCount++;

		using (MemoryStream input = new(originalBytes, writable: false))
			loaded.Read(input);

		Assert.Equal(1, resetCount);
		Assert.Equal(0, changeCount);
		Assert.Empty(loaded.GetPendingChanges());
		Assert.Equal(originalBytes, Write(loaded));
		Assert.Equal(BlockType.Water, loaded.GetBlock(-17, 2, 31));
		Assert.Equal(BlockType.Stone, loaded.GetBlock(0, 0, 0));
		Assert.Equal(BlockType.Glowstone, loaded.GetBlock(16, 15, -1));
		AssertSnapshotsMatchMap(loaded);
	}

	[Fact]
	public void GenerateFloatingIsland_RaisesOneResetAndSnapshotsEveryGeneratedCell()
	{
		ChunkMap map = CreateMap();
		int resetCount = 0;
		int changeCount = 0;
		map.WorldReset += () => resetCount++;
		map.BlockChanged += _ => changeCount++;

		map.GenerateFloatingIsland(8, 8, 12345);

		Assert.Equal(1, resetCount);
		Assert.Equal(0, changeCount);
		Assert.Empty(map.GetPendingChanges());
		Assert.NotEmpty(map.CaptureChunks());
		Assert.Contains(map.CaptureChunks(), snapshot => ContainsNonAirBlock(snapshot));
		AssertSnapshotsMatchMap(map);
	}

	private static void AssertSnapshotsMatchMap(ChunkMap map)
	{
		foreach (ChunkSnapshot snapshot in map.CaptureChunks())
		{
			Assert.Equal(ChunkSnapshot.BlockCount, snapshot.Blocks.Count);
			for (int z = 0; z < ChunkSnapshot.Size; z++)
			{
				for (int y = 0; y < ChunkSnapshot.Size; y++)
				{
					for (int x = 0; x < ChunkSnapshot.Size; x++)
					{
						int worldX = snapshot.ChunkX * ChunkSnapshot.Size + x;
						int worldY = snapshot.ChunkY * ChunkSnapshot.Size + y;
						int worldZ = snapshot.ChunkZ * ChunkSnapshot.Size + z;
						Assert.Equal(map.GetBlock(worldX, worldY, worldZ), snapshot.GetBlock(x, y, z));
					}
				}
			}
		}
	}

	private static bool ContainsNonAirBlock(ChunkSnapshot snapshot)
	{
		for (int index = 0; index < snapshot.Blocks.Count; index++)
		{
			if (snapshot.Blocks[index] != BlockType.None)
				return true;
		}

		return false;
	}

	private static void AssertChunk(ChunkSnapshot snapshot, int x, int y, int z)
	{
		Assert.Equal(x, snapshot.ChunkX);
		Assert.Equal(y, snapshot.ChunkY);
		Assert.Equal(z, snapshot.ChunkZ);
		Assert.Equal(ChunkSnapshot.BlockCount, snapshot.Blocks.Count);
	}

	private static void AssertChange(
		BlockChange change,
		int x,
		int y,
		int z,
		BlockType oldType,
		BlockType newType)
	{
		Assert.Equal(x, change.X);
		Assert.Equal(y, change.Y);
		Assert.Equal(z, change.Z);
		Assert.Equal(oldType, change.OldType);
		Assert.Equal(newType, change.NewType);
	}

	private static byte[] Write(ChunkMap map)
	{
		using MemoryStream output = new();
		map.Write(output);
		return output.ToArray();
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
