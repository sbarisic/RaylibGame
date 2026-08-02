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
	public void BlockChangePacket_PreservesBoundedColumnBatchAndRevision()
	{
		BlockChangePacket source = new()
		{
			ColumnX = -1,
			ColumnZ = 0,
			ColumnRevision = long.MaxValue,
			Changes = new[]
			{
				new BlockChangeEntry(-1, 2, 3, (ushort)BlockType.Stone, 0),
				new BlockChangeEntry(-16, 5, 15, (ushort)BlockType.Dirt, 0),
			},
		};

		BlockChangePacket decoded = Assert.IsType<BlockChangePacket>(Packet.Deserialize(source.Serialize()));

		Assert.Equal(source.ColumnX, decoded.ColumnX);
		Assert.Equal(source.ColumnZ, decoded.ColumnZ);
		Assert.Equal(source.Changes, decoded.Changes);
		Assert.Equal(long.MaxValue, decoded.ColumnRevision);
	}

	[Fact]
	public void FogChangePacketPreservesPackedFogAndRevision()
	{
		FogChangePacket source = new()
		{
			X = -17,
			Y = 42,
			Z = 31,
			Fog = 0x80604020,
			ColumnRevision = 991,
		};

		FogChangePacket decoded = Assert.IsType<FogChangePacket>(
			Packet.Deserialize(source.Serialize())
		);
		Assert.Equal(source.X, decoded.X);
		Assert.Equal(source.Y, decoded.Y);
		Assert.Equal(source.Z, decoded.Z);
		Assert.Equal(source.Fog, decoded.Fog);
		Assert.Equal(source.ColumnRevision, decoded.ColumnRevision);
	}

	[Fact]
	public void BlockPlacementPackets_PreserveBlockValues()
	{
		BlockPlaceRequestPacket placement = new() { BlockType = (ushort)BlockType.Stone, BlockState = 0 };
		DebugPlaceBlockRequestPacket debugPlacement = new() { BlockType = (ushort)BlockType.Dirt, BlockState = 0 };

		BlockPlaceRequestPacket decodedPlacement = Assert.IsType<BlockPlaceRequestPacket>(Packet.Deserialize(placement.Serialize()));
		DebugPlaceBlockRequestPacket decodedDebug = Assert.IsType<DebugPlaceBlockRequestPacket>(Packet.Deserialize(debugPlacement.Serialize()));
		Assert.Equal(placement.BlockType, decodedPlacement.BlockType);
		Assert.Equal(placement.BlockState, decodedPlacement.BlockState);
		Assert.Equal(debugPlacement.BlockType, decodedDebug.BlockType);
		Assert.Equal(debugPlacement.BlockState, decodedDebug.BlockState);
	}

	[Fact]
	public void ItemPickupSoundPacketRoundTripsPositionAndSource()
	{
		SoundEventPacket source = new()
		{
			EventType = (byte)SoundEventType.ItemPickup,
			Position = new Vector3(3.5f, 4.25f, -8.5f),
			SourcePlayerId = 17,
		};

		SoundEventPacket decoded = Assert.IsType<SoundEventPacket>(
			Packet.Deserialize(source.Serialize()));

		Assert.Equal((byte)SoundEventType.ItemPickup, decoded.EventType);
		Assert.Equal(source.Position, decoded.Position);
		Assert.Equal(source.SourcePlayerId, decoded.SourcePlayerId);
	}

	[Fact]
	public void PhaseOneFarmingPacketIdsAreStable()
	{
		Assert.Equal(0x34, (byte)PacketType.WorldInteractRequest);
		Assert.Equal(0x35, (byte)PacketType.WorldObjectPlaceRequest);
		Assert.Equal(0x36, (byte)PacketType.WorldObjectColumn);
		Assert.Equal(0x37, (byte)PacketType.WorldObjectDelta);
		Assert.Equal(0x38, (byte)PacketType.WorldObjectResyncRequest);
		Assert.Equal(0x39, (byte)PacketType.WorldObjectColumnApplied);
		Assert.Equal(0x94, (byte)PacketType.CraftRequest);
		Assert.Equal(0x95, (byte)PacketType.CraftResult);
	}

	[Fact]
	public void WorldInteractRequest_RoundTripsFurnitureRemovalIntent()
	{
		WorldInteractRequestPacket source = new()
		{
			X = -12,
			Y = 65,
			Z = 48,
			Interaction = WorldInteractionKind.RemoveFurniture,
		};

		WorldInteractRequestPacket decoded = Assert.IsType<WorldInteractRequestPacket>(
			Packet.Deserialize(source.Serialize()));

		Assert.Equal(source.X, decoded.X);
		Assert.Equal(source.Y, decoded.Y);
		Assert.Equal(source.Z, decoded.Z);
		Assert.Equal(WorldInteractionKind.RemoveFurniture, decoded.Interaction);
	}

	[Fact]
	public void WorldObjectPacketsRoundTripOrderingAndPayloadFields()
	{
		WorldObjectColumnPacket source = new()
		{
			StreamId = 7, X = -2, Z = 4, Epoch = 11, Revision = 22, SnapshotId = 33,
			PartIndex = 1, PartCount = 2, TotalRecordCount = 3, TotalDecodedBytes = 5,
			FullPayloadChecksum = 44, PartRecordCount = 1, Payload = new byte[] { 1, 2 },
		};

		WorldObjectColumnPacket decoded = Assert.IsType<WorldObjectColumnPacket>(Packet.Deserialize(source.Serialize()));
		Assert.Equal(source.StreamId, decoded.StreamId);
		Assert.Equal(source.Epoch, decoded.Epoch);
		Assert.Equal(source.Revision, decoded.Revision);
		Assert.Equal(source.SnapshotId, decoded.SnapshotId);
		Assert.Equal(source.PartIndex, decoded.PartIndex);
		Assert.Equal(source.Payload, decoded.Payload);
	}
}
