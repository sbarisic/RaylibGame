using Voxelgine.Engine;
using Voxelgine.States;

namespace UnitTest;

public sealed class DeferredColumnAcknowledgementTests
{
	[Fact]
	public void ColumnIsNotReadyUntilMatchingRenderRevisionCompletes()
	{
		DeferredColumnAcknowledgements acknowledgements = new();
		WorldColumnPacket packet = CreatePacket(streamId: 4, x: -2, z: 7, revision: 11);

		Assert.True(acknowledgements.RegisterReceived(packet));
		Assert.False(acknowledgements.TryDequeueReady(out _));
		Assert.False(acknowledgements.MarkReady(4, -2, 7, revision: 10));
		Assert.True(acknowledgements.MarkReady(4, -2, 7, revision: 11));
		Assert.True(acknowledgements.TryDequeueReady(out WorldColumnPacket ready));
		Assert.Same(packet, ready);
		Assert.Equal(0, acknowledgements.Count);
	}

	[Fact]
	public void DuplicateReliableDeliveryDoesNotRequireAnotherRenderCompletion()
	{
		DeferredColumnAcknowledgements acknowledgements = new();
		WorldColumnPacket packet = CreatePacket(streamId: 2, x: 1, z: 3, revision: 8);

		Assert.True(acknowledgements.RegisterReceived(packet));
		Assert.False(acknowledgements.RegisterReceived(packet));
		Assert.Equal(1, acknowledgements.Count);
		Assert.True(acknowledgements.MarkReady(2, 1, 3, 8));
		Assert.True(acknowledgements.TryDequeueReady(out _));
		Assert.False(acknowledgements.TryDequeueReady(out _));

		Assert.False(acknowledgements.RegisterReceived(packet));
		Assert.True(acknowledgements.TryDequeueReady(out WorldColumnPacket repeated));
		Assert.Same(packet, repeated);
	}

	[Fact]
	public void ClearDropsWaitingAndReadyColumnsAcrossReconnect()
	{
		DeferredColumnAcknowledgements acknowledgements = new();
		acknowledgements.RegisterReceived(CreatePacket(1, 0, 0, 1));
		acknowledgements.RegisterReceived(CreatePacket(1, 1, 0, 1));
		acknowledgements.MarkReady(1, 0, 0, 1);

		acknowledgements.Clear();

		Assert.Equal(0, acknowledgements.Count);
		Assert.False(acknowledgements.TryDequeueReady(out _));
	}

	private static WorldColumnPacket CreatePacket(
		int streamId,
		int x,
		int z,
		long revision) => new()
	{
		StreamId = streamId,
		X = x,
		Z = z,
		Revision = revision,
		Payload = Array.Empty<byte>(),
	};
}
