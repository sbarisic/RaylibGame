using System.Numerics;
using Voxelgine.Engine;

namespace VoxelgineEngine.Tests;

public sealed class WorldPacketTests
{
	[Fact]
	public void WorldColumnPacket_RoundTripsArchivePayload()
	{
		WorldColumnPacket source = new()
		{
			StreamId = 17,
			X = -4,
			Z = 9,
			Revision = 123456789,
			Kind = WorldColumnStreamKind.BootstrapHalo,
			Checksum = 0xDEADBEEF,
			Payload = new byte[] { 1, 3, 5, 7 },
		};

		WorldColumnPacket decoded = Assert.IsType<WorldColumnPacket>(Packet.Deserialize(source.Serialize()));

		Assert.Equal(source.StreamId, decoded.StreamId);
		Assert.Equal(source.X, decoded.X);
		Assert.Equal(source.Z, decoded.Z);
		Assert.Equal(source.Revision, decoded.Revision);
		Assert.Equal(source.Kind, decoded.Kind);
		Assert.Equal(source.Checksum, decoded.Checksum);
		Assert.Equal(source.Payload, decoded.Payload);
	}

	[Fact]
	public void WorldStreamBeginPacket_RoundTripsFocusAndCounts()
	{
		WorldStreamBeginPacket source = new()
		{
			StreamId = 3,
			FocusPosition = new Vector3(-2.5f, 70, 33.25f),
			WorldSeed = 666,
			TotalColumns = 4096,
			BootstrapCoreColumns = 21,
			BootstrapHaloColumns = 16,
		};

		WorldStreamBeginPacket decoded = Assert.IsType<WorldStreamBeginPacket>(Packet.Deserialize(source.Serialize()));

		Assert.Equal(source.StreamId, decoded.StreamId);
		Assert.Equal(source.FocusPosition, decoded.FocusPosition);
		Assert.Equal(source.TotalColumns, decoded.TotalColumns);
		Assert.Equal(source.BootstrapCoreColumns, decoded.BootstrapCoreColumns);
		Assert.Equal(source.BootstrapHaloColumns, decoded.BootstrapHaloColumns);
	}

	[Fact]
	public void BlockChangePacket_PreservesUshortBlockIdAndRevision()
	{
		BlockChangePacket source = new()
		{
			X = 1,
			Y = 2,
			Z = 3,
			BlockType = ushort.MaxValue,
			ColumnRevision = long.MaxValue,
		};

		BlockChangePacket decoded = Assert.IsType<BlockChangePacket>(Packet.Deserialize(source.Serialize()));

		Assert.Equal(ushort.MaxValue, decoded.BlockType);
		Assert.Equal(long.MaxValue, decoded.ColumnRevision);
	}

	[Fact]
	public void BlockPlacementPackets_PreserveUshortBlockIds()
	{
		BlockPlaceRequestPacket placement = new() { BlockType = ushort.MaxValue };
		DebugPlaceBlockRequestPacket debugPlacement = new() { BlockType = ushort.MaxValue };

		Assert.Equal(
			ushort.MaxValue,
			Assert.IsType<BlockPlaceRequestPacket>(Packet.Deserialize(placement.Serialize())).BlockType);
		Assert.Equal(
			ushort.MaxValue,
			Assert.IsType<DebugPlaceBlockRequestPacket>(Packet.Deserialize(debugPlacement.Serialize())).BlockType);
	}
}
