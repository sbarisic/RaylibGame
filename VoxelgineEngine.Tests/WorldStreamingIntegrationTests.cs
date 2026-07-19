using System.Net;
using System.Net.Sockets;
using System.Numerics;
using Voxelgine.Engine;
using Voxelgine.Engine.Server;
using Voxelgine.Graphics;

namespace VoxelgineEngine.Tests;

public sealed class WorldStreamingIntegrationTests
{
	[Fact]
	public void LossFreeLoopback_StreamsBootstrapWithinBoundedWindowsAndSettlesReliability()
	{
		ChunkMap map = CreateWorld();
		using NetServer server = new();
		using NetClient client = new();
		WorldStreamManager stream = new(server, map, null);
		float currentTime = 0;
		int playerId = -1;
		int streamId = 0;
		int expectedBootstrapColumns = 0;
		HashSet<ChunkColumnCoordinate> receivedBootstrapColumns = new();
		List<float> bootstrapDistances = new();
		bool mutatedOutstandingColumn = false;
		bool interestSent = false;
		int ordinaryColumnsReceived = 0;
		bool ordinaryArrivedBeforeReady = false;
		bool bootstrapComplete = false;
		bool readyAccepted = false;
		float nextReadyAttempt = 0;
		int maximumOutstanding = 0;
		int maximumInFlight = 0;

		server.OnClientConnected += connection =>
		{
			playerId = connection.PlayerId;
			stream.Begin(connection.PlayerId, new Vector3(8, 24, 8), 1234, currentTime);
		};
		server.OnPacketReceived += (connection, packet) =>
		{
			switch (packet)
			{
				case WorldColumnAppliedPacket applied:
					stream.HandleApplied(connection.PlayerId, applied);
					break;
				case ClientWorldReadyPacket ready:
					stream.HandleReady(connection.PlayerId, ready);
					break;
				case ChunkInterestPacket interest:
					stream.HandleInterest(connection.PlayerId, interest);
					break;
			}
		};
		stream.ClientReady += _ => readyAccepted = true;

		client.OnPacketReceived += packet =>
		{
			switch (packet)
			{
				case WorldStreamBeginPacket begin:
					streamId = begin.StreamId;
					expectedBootstrapColumns = begin.BootstrapCoreColumns + begin.BootstrapHaloColumns;
					Assert.Equal(
						CountIntersectingColumns(map, begin.FocusPosition, WorldStreamManager.BootstrapRadiusBlocks),
						begin.BootstrapCoreColumns);
					Assert.Equal(
						CountIntersectingColumns(
							map,
							begin.FocusPosition,
							WorldStreamManager.BootstrapRadiusBlocks + WorldStreamManager.BootstrapHaloBlocks) -
						begin.BootstrapCoreColumns,
						begin.BootstrapHaloColumns);
					break;
				case WorldColumnPacket column:
					Assert.Equal(WorldColumnCodec.ComputeChecksum(column.Payload), column.Checksum);
					ChunkColumnSnapshot decoded = WorldColumnCodec.Decode(
						column.X,
						column.Z,
						column.Revision,
						column.Payload);
					Assert.NotEmpty(decoded.Chunks);
					if (column.Kind == WorldColumnStreamKind.Ordinary)
					{
						ordinaryColumnsReceived++;
						ordinaryArrivedBeforeReady |= !readyAccepted;
					}
					else
					{
						if (receivedBootstrapColumns.Add(new ChunkColumnCoordinate(column.X, column.Z)))
							bootstrapDistances.Add(ColumnDistanceSquared(column.X, column.Z, new Vector3(8, 24, 8)));
						if (!mutatedOutstandingColumn)
						{
							mutatedOutstandingColumn = true;
							map.SetBlock(column.X * Chunk.ChunkSize, 0, column.Z * Chunk.ChunkSize, BlockType.Glowstone);
						}
					}
					client.Send(new WorldColumnAppliedPacket
					{
						StreamId = column.StreamId,
						X = column.X,
						Z = column.Z,
						Revision = column.Revision,
					}, true, currentTime);
					break;
				case WorldBootstrapCompletePacket:
					bootstrapComplete = true;
					break;
			}
		};

		int port = ReserveUdpPort();
		server.WorldSeed = 1234;
		server.Start(port);
		client.Connect(IPAddress.Loopback.ToString(), port, "stream-test", currentTime);

		for (int iteration = 0; iteration < 5000 && !readyAccepted; iteration++)
		{
			currentTime += 0.01f;
			server.Tick(currentTime);
			stream.Tick(currentTime);
			client.Tick(currentTime);
			if (playerId >= 0)
			{
				maximumOutstanding = Math.Max(maximumOutstanding, stream.GetOutstandingColumnCount(playerId));
				maximumInFlight = Math.Max(
					maximumInFlight,
					server.GetConnection(playerId)?.Diagnostics.ReliableInFlight ?? 0);
			}
			if (streamId != 0 && !interestSent)
			{
				interestSent = true;
				client.Send(new ChunkInterestPacket
				{
					StreamId = streamId,
					CenterX = 8,
					CenterZ = 8,
					RadiusBlocks = 80,
				}, true, currentTime);
			}

			if (bootstrapComplete && receivedBootstrapColumns.Count == expectedBootstrapColumns &&
				currentTime >= nextReadyAttempt)
			{
				nextReadyAttempt = currentTime + 0.25f;
				client.Send(new ClientWorldReadyPacket { StreamId = streamId }, true, currentTime);
			}
			Thread.Sleep(1);
		}

		Assert.True(readyAccepted);
		Assert.Equal(expectedBootstrapColumns, receivedBootstrapColumns.Count);
		Assert.True(mutatedOutstandingColumn);
		Assert.False(ordinaryArrivedBeforeReady);
		Assert.True(bootstrapDistances.SequenceEqual(bootstrapDistances.OrderBy(static distance => distance)));
		Assert.InRange(maximumOutstanding, 1, WorldStreamManager.MaximumUnappliedColumns);
		Assert.InRange(maximumInFlight, 1, ReliableChannel.WindowSize);

		for (int iteration = 0; iteration < 1000 && ordinaryColumnsReceived == 0; iteration++)
		{
			currentTime += 0.01f;
			server.Tick(currentTime);
			stream.Tick(currentTime);
			client.Tick(currentTime);
			Thread.Sleep(1);
		}
		Assert.True(ordinaryColumnsReceived > 0);

		for (int iteration = 0; iteration < 500 &&
			(client.Diagnostics.ReliableInFlight != 0 ||
			 server.GetConnection(playerId)?.Diagnostics.ReliableInFlight != 0); iteration++)
		{
			currentTime += 0.01f;
			server.Tick(currentTime);
			client.Tick(currentTime);
			Thread.Sleep(1);
		}

		Assert.Equal(0, client.Diagnostics.ReliableInFlight);
		Assert.Equal(0, server.GetConnection(playerId)?.Diagnostics.ReliableInFlight);
		Assert.Equal(0, client.Diagnostics.RetransmissionsSent);
		Assert.Equal(0, server.GetConnection(playerId)?.Diagnostics.RetransmissionsSent);
	}

	private static ChunkMap CreateWorld()
	{
		ChunkMap map = new();
		for (int z = -3; z <= 3; z++)
		{
			for (int x = -3; x <= 3; x++)
			{
				BlockType[] blocks = new BlockType[ChunkSnapshot.BlockCount];
				uint random = unchecked((uint)(x * 73856093 ^ z * 19349663 ^ 0x51ED270B));
				for (int index = 0; index < blocks.Length; index++)
				{
					random = unchecked(random * 1664525u + 1013904223u);
					blocks[index] = (BlockType)(1 + random % 22);
				}
				map.ApplyColumn(new ChunkColumnSnapshot(
					x,
					z,
					1,
					new[] { new ChunkSnapshot(x, 0, z, blocks) }));
			}
		}
		return map;
	}

	private static int ReserveUdpPort()
	{
		using UdpClient reservation = new(0);
		return ((IPEndPoint)reservation.Client.LocalEndPoint!).Port;
	}

	private static int CountIntersectingColumns(ChunkMap map, Vector3 focus, int radius)
	{
		float radiusSquared = radius * radius;
		return map.GetColumnCoordinates().Count(coordinate =>
			ColumnDistanceSquared(coordinate.X, coordinate.Z, focus) <= radiusSquared);
	}

	private static float ColumnDistanceSquared(int x, int z, Vector3 focus)
	{
		float minimumX = x * Chunk.ChunkSize;
		float minimumZ = z * Chunk.ChunkSize;
		float nearestX = Math.Clamp(focus.X, minimumX, minimumX + Chunk.ChunkSize);
		float nearestZ = Math.Clamp(focus.Z, minimumZ, minimumZ + Chunk.ChunkSize);
		return Vector2.DistanceSquared(
			new Vector2(focus.X, focus.Z),
			new Vector2(nearestX, nearestZ));
	}
}
