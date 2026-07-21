using System.Net;
using System.Threading;
using Voxelgine.Engine;

namespace VoxelgineEngine.Tests;

public sealed class NetworkCorrectnessTests
{
	[Fact]
	public void PacketFragmenter_RejectsEmptyPacketsAndInvalidConfiguration()
	{
		PacketFragmenter fragmenter = new(32);

		Assert.Throws<ArgumentException>(() => fragmenter.Split(Array.Empty<byte>()));
		Assert.Throws<ArgumentOutOfRangeException>(() => new PacketFragmenter(-1));
	}

	[Fact]
	public void PacketFragmenter_DropsConflictingGroupShape()
	{
		PacketFragmenter fragmenter = new(32);
		Assert.Null(fragmenter.HandleReceived(CreateFragment(7, 0, 2, 1), 0));
		Assert.Equal(1, fragmenter.PendingGroupCount);

		Assert.Null(fragmenter.HandleReceived(CreateFragment(7, 1, 3, 2), 0.1f));
		Assert.Equal(0, fragmenter.PendingGroupCount);
	}

	[Fact]
	public void PacketFragmenter_BoundsIncompleteGroups()
	{
		PacketFragmenter fragmenter = new(32);
		for (int index = 0; index < PacketFragmenter.MaximumPendingGroups + 20; index++)
		{
			fragmenter.HandleReceived(
				CreateFragment((ushort)index, 0, 2, (byte)index),
				index * 0.001f
			);
		}

		Assert.Equal(PacketFragmenter.MaximumPendingGroups, fragmenter.PendingGroupCount);
	}

	[Fact]
	public void UdpTransport_ReceiveHandlerFailureDoesNotStopLoop()
	{
		using UdpTransport receiver = new();
		using UdpTransport sender = new();
		using ManualResetEventSlim completed = new();
		int receives = 0;
		receiver.OnDataReceived += (_, _) =>
		{
			if (Interlocked.Increment(ref receives) == 1)
				throw new InvalidOperationException("test failure");
			completed.Set();
		};
		receiver.Bind(0);
		sender.Open();

		IPEndPoint target = new(IPAddress.Loopback, receiver.LocalEndPoint.Port);
		sender.SendTo(new byte[] { 1 }, target);
		Assert.True(SpinWait.SpinUntil(() => Volatile.Read(ref receives) >= 1, 2_000));
		sender.SendTo(new byte[] { 2 }, target);

		Assert.True(completed.Wait(2_000));
		Assert.True(receiver.IsActive);
	}

	[Fact]
	public void UdpTransport_CanCloseFromReceiveCallback()
	{
		using UdpTransport receiver = new();
		using UdpTransport sender = new();
		using ManualResetEventSlim completed = new();
		receiver.OnDataReceived += (_, _) =>
		{
			receiver.Close();
			completed.Set();
		};
		receiver.Bind(0);
		sender.Open();

		IPEndPoint target = new(IPAddress.Loopback, receiver.LocalEndPoint.Port);
		sender.SendTo(new byte[] { 1 }, target);

		Assert.True(completed.Wait(2_000));
		Assert.False(receiver.IsActive);
	}

	private static byte[] CreateFragment(
		ushort groupId,
		byte index,
		byte total,
		byte payload)
	{
		return new[]
		{
			PacketFragmenter.FragmentMarker,
			(byte)(groupId & 0xff),
			(byte)(groupId >> 8),
			index,
			total,
			payload,
		};
	}
}
