using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.Server;
using Voxelgine.Graphics;

namespace VoxelgineEngine.Tests;

[Collection(WorldGenerationCollection.Name)]
public sealed class ChunkMapObservationTests
{
	[Fact]
	public void NewWorldDefaultsUseFourTimesThePreviousHorizontalExtent()
	{
		Assert.Equal(1024, ServerLoop.DefaultWorldWidth);
		Assert.Equal(1024, ServerLoop.DefaultWorldLength);
	}

	[Fact]
	public void GenerateFloatingIslandHonorsCancellationBeforeAllocatingChunks()
	{
		ChunkMap map = new();
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		Assert.Throws<OperationCanceledException>(
			() => map.GenerateFloatingIsland(1024, 1024, 666, cancellation.Token)
		);
		Assert.Empty(map.CaptureChunks());
	}

	[Fact]
	public void WorldArchiveReadHonorsCancellationBeforeDecompression()
	{
		ChunkMap map = new();
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();
		using MemoryStream input = new(Write(map));

		Assert.Throws<OperationCanceledException>(
			() => WorldArchive.Read(input, cancellation.Token)
		);
		Assert.Empty(map.CaptureChunks());
	}

	[Fact]
	public void ComputeLightingHonorsCancellationBeforeStartingWork()
	{
		ChunkMap map = new();
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		Assert.Throws<OperationCanceledException>(
			() => map.ComputeLighting(cancellation.Token)
		);
	}

	[Fact]
	public void SetBlock_EmitsOneChangeForPlacementReplacementAndRemoval()
	{
		ChunkMap map = new();
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
		ChunkMap map = new();
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
			AssertSnapshotMatchesMap(map, snapshot);

		ChunkSnapshot originalSnapshot = snapshots[2];
		map.SetBlock(15, 15, 15, BlockType.Water);
		Assert.Equal(BlockType.Plank, originalSnapshot.GetBlock(15, 15, 15));
	}

	[Fact]
	public void ArchiveRead_RaisesOneResetNoChangesAndPreservesSaveBytesAndCells()
	{
		ChunkMap source = new();
		source.SetBlock(-17, 2, 31, BlockType.Water);
		source.SetBlock(0, 0, 0, BlockType.Stone);
		source.SetBlock(16, 15, -1, BlockType.Glowstone);
		byte[] originalBytes = Write(source);

		ChunkMap loaded = new();
		int resetCount = 0;
		int changeCount = 0;
		loaded.WorldReset += () => resetCount++;
		loaded.BlockChanged += _ => changeCount++;

		using (MemoryStream input = new(originalBytes, writable: false))
			loaded.ReplaceAllColumns(WorldArchive.Read(input).Columns);

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
		ChunkMap map = new();
		int resetCount = 0;
		int changeCount = 0;
		map.WorldReset += () => resetCount++;
		map.BlockChanged += _ => changeCount++;

		map.GenerateFloatingIsland(64, 64, 12345);

		Assert.Equal(1, resetCount);
		Assert.Equal(0, changeCount);
		Assert.Empty(map.GetPendingChanges());
		Assert.NotEmpty(map.CaptureChunks());
		Assert.Contains(map.CaptureChunks(), ContainsNonAirBlock);
		AssertSnapshotsMatchMap(map);
	}

	private static void AssertSnapshotsMatchMap(ChunkMap map)
	{
		foreach (ChunkSnapshot snapshot in map.CaptureChunks())
			AssertSnapshotMatchesMap(map, snapshot);
	}

	private static void AssertSnapshotMatchesMap(ChunkMap map, ChunkSnapshot snapshot)
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

	private static bool ContainsNonAirBlock(ChunkSnapshot snapshot)
	{
		return snapshot.Blocks.Any(block => block != BlockType.None);
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
		Assert.Equal(new Vector3(x, y, z), new Vector3(change.X, change.Y, change.Z));
		Assert.Equal(oldType, change.OldType);
		Assert.Equal(newType, change.NewType);
	}

	private static byte[] Write(ChunkMap map)
	{
		using MemoryStream output = new();
		WorldArchive.Write(output, map, default);
		return output.ToArray();
	}
}
