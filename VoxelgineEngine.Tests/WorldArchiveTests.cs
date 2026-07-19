using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Graphics;

namespace VoxelgineEngine.Tests;

public sealed class WorldArchiveTests
{
	[Fact]
	public void ColumnCodec_RoundTripsNegativeCoordinatesAndBlockIds()
	{
		ChunkColumnSnapshot source = CreateColumn(-3, 7, 11, BlockType.Glowstone);
		byte[] payload = WorldColumnCodec.Encode(source);

		ChunkColumnSnapshot decoded = WorldColumnCodec.Decode(-3, 7, 11, payload);

		Assert.Equal(source.X, decoded.X);
		Assert.Equal(source.Z, decoded.Z);
		Assert.Equal(source.Revision, decoded.Revision);
		Assert.Equal((ushort)BlockType.Glowstone, (ushort)decoded.Chunks[0].Blocks[0]);
		Assert.Equal(source.Chunks[0].Blocks, decoded.Chunks[0].Blocks);
	}

	[Fact]
	public void ColumnCodec_RoundTripsEmptyAndFullChunks()
	{
		BlockType[] empty = new BlockType[ChunkSnapshot.BlockCount];
		BlockType[] full = Enumerable.Repeat(BlockType.Stone, ChunkSnapshot.BlockCount).ToArray();
		ChunkColumnSnapshot source = new(2, -4, 1, new[]
		{
			new ChunkSnapshot(2, -1, -4, empty),
			new ChunkSnapshot(2, 0, -4, full),
		});

		ChunkColumnSnapshot decoded = WorldColumnCodec.Decode(2, -4, 1, WorldColumnCodec.Encode(source));

		Assert.All(decoded.Chunks[0].Blocks, block => Assert.Equal(BlockType.None, block));
		Assert.All(decoded.Chunks[1].Blocks, block => Assert.Equal(BlockType.Stone, block));
	}

	[Fact]
	public void Archive_RoundTripsMetadataAndSupportsRandomColumnRead()
	{
		ChunkMap map = new();
		map.ApplyColumn(CreateColumn(-1, 0, 1, BlockType.Grass));
		map.ApplyColumn(CreateColumn(2, 3, 1, BlockType.Water));
		WorldArchiveMetadata metadata = new(
			1234,
			new Vector3(1, 2, 3),
			new Vector3(4, 5, 6),
			new Vector3(7, 8, 9));
		using MemoryStream archive = new();

		WorldArchive.Write(archive, map, metadata);
		archive.Position = 0;
		WorldArchiveReadResult all = WorldArchive.Read(archive);
		archive.Position = 0;
		ChunkColumnSnapshot random = WorldArchive.ReadColumn(archive, 2, 3);

		Assert.Equal(metadata, all.Metadata);
		Assert.Equal(2, all.Columns.Count);
		Assert.Equal(BlockType.Water, random.Chunks[0].Blocks[0]);
	}

	[Fact]
	public void Archive_RejectsCorruptColumnChecksum()
	{
		ChunkMap map = new();
		map.ApplyColumn(CreateColumn(0, 0, 1, BlockType.Stone));
		using MemoryStream archive = new();
		WorldArchive.Write(archive, map, default);
		byte[] bytes = archive.ToArray();
		bytes[^1] ^= 0x5A;

		Assert.ThrowsAny<Exception>(() => WorldArchive.Read(new MemoryStream(bytes)));
	}

	[Fact]
	public void Archive_ReusesUnmodifiedCompressedPayloads()
	{
		ChunkMap map = new();
		map.ApplyColumn(CreateColumn(0, 0, 1, BlockType.Stone));
		map.ApplyColumn(CreateColumn(1, 0, 1, BlockType.Grass));
		using MemoryStream first = new();
		WorldArchivePayloadCache initial = WorldArchive.Write(first, map, default);
		Assert.True(initial.TryGet(1, 0, 1, out byte[] unchangedPayload, out _));

		map.SetBlock(0, 0, 0, BlockType.Glowstone);
		using MemoryStream second = new();
		WorldArchivePayloadCache replacement = WorldArchive.Write(second, map, default, initial);

		Assert.True(replacement.TryGet(1, 0, 1, out byte[] reusedPayload, out _));
		Assert.Same(unchangedPayload, reusedPayload);
		Assert.True(replacement.TryGet(0, 0, 2, out _, out _));
	}

	[Fact]
	public void IncompatibleArchive_IsMovedToTimestampedBackup()
	{
		string directory = Path.Combine(Path.GetTempPath(), $"voxelgine-archive-{Guid.NewGuid():N}");
		Directory.CreateDirectory(directory);
		string path = Path.Combine(directory, "map.bin");
		try
		{
			File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });
			string backup = WorldArchive.MoveIncompatibleFileToBackup(
				path,
				new DateTime(2026, 7, 19, 12, 34, 56, 789));

			Assert.False(File.Exists(path));
			Assert.Equal(path + ".incompatible-20260719-123456-789.bak", backup);
			Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(backup));
		}
		finally
		{
			Directory.Delete(directory, recursive: true);
		}
	}

	[Fact]
	public void UnknownClientColumnsBlockPhysicsButAbortRaycasts()
	{
		ChunkMap map = new() { UnknownColumnsAreBoundaries = true };
		map.ApplyColumn(CreateColumn(0, 0, 1, BlockType.None));

		Assert.True(map.IsSolid(16, 20, 0));
		Assert.False(map.TryRaycast(
			new Vector3(15.5f, 20, 0.5f),
			Vector3.UnitX,
			10,
			out _));
	}

	[Fact]
	public void ReplicatedBlockChange_RequiresNextColumnRevision()
	{
		ChunkMap map = new();
		map.ApplyColumn(CreateColumn(0, 0, 4, BlockType.Stone));

		Assert.False(map.TryApplyReplicatedBlockChange(0, 0, 0, BlockType.Grass, 6));
		Assert.True(map.TryApplyReplicatedBlockChange(0, 0, 0, BlockType.Grass, 5));
		Assert.Equal(5, map.GetColumnRevision(0, 0));
		Assert.Equal(BlockType.Grass, map.GetBlock(0, 0, 0));
	}

	private static ChunkColumnSnapshot CreateColumn(
		int x,
		int z,
		long revision,
		BlockType firstBlock)
	{
		BlockType[] blocks = new BlockType[ChunkSnapshot.BlockCount];
		blocks[0] = firstBlock;
		blocks[^1] = BlockType.Glass;
		return new ChunkColumnSnapshot(
			x,
			z,
			revision,
			new[] { new ChunkSnapshot(x, 0, z, blocks) });
	}
}
