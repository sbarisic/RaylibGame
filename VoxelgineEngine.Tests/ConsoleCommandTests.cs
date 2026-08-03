using System.Text;

namespace Voxelgine.Engine.Tests;

public sealed class ConsoleCommandTests
{
	[Fact]
	public void PacketIdsAndCatalogsKeepPlayerAndHostAuthoritySeparate()
	{
		Assert.Equal(0x51, (byte)PacketType.ConsoleCommandRequest);
		Assert.Equal(0x52, (byte)PacketType.ConsoleCommandResult);
		List<string> expectedPlayer = ["comehere", "day", "night", "speak", "machine"];
#if DEBUG
		expectedPlayer.AddRange(["give", "fog", "structure"]);
#endif
		Assert.Equal(expectedPlayer, ConsoleCommandCatalog.PlayerCommands.Select(static command => command.Name));
		Assert.Equal(
			["say", "time", "save", "status", "players", "stop"],
			ConsoleCommandCatalog.HostCommands.Select(static command => command.Name));
		Assert.False(ConsoleCommandCatalog.TryGetPlayerCommand("save", out _));
		Assert.False(ConsoleCommandCatalog.TryGetHostCommand("kick", out _));
		Assert.False(ConsoleCommandCatalog.TryGetHostCommand("ban", out _));
		Assert.True(ConsoleCommandCatalog.TryGetHostCommand("quit", out ConsoleCommandDefinition stop));
		Assert.Equal("stop", stop.Name);
	}

	[Fact]
	public void RequestPacketRoundTripsParsedArguments()
	{
		ConsoleCommandRequestPacket source = new()
		{
			RequestId = 17,
			CommandName = "speak",
			Arguments = ["hello world", "again"],
		};

		ConsoleCommandRequestPacket decoded = Assert.IsType<ConsoleCommandRequestPacket>(Packet.Deserialize(source.Serialize()));

		Assert.Equal(source.RequestId, decoded.RequestId);
		Assert.Equal(source.CommandName, decoded.CommandName);
		Assert.Equal(source.Arguments, decoded.Arguments);
	}

	[Fact]
	public void ResultPacketRoundTripsStructuredOutput()
	{
		ConsoleCommandResultPacket source = new()
		{
			RequestId = 22,
			Success = false,
			Lines = ["Usage: fog clear <radiusX> <height> <radiusZ>", "Try again."],
		};

		ConsoleCommandResultPacket decoded = Assert.IsType<ConsoleCommandResultPacket>(Packet.Deserialize(source.Serialize()));

		Assert.Equal(source.RequestId, decoded.RequestId);
		Assert.False(decoded.Success);
		Assert.Equal(source.Lines, decoded.Lines);
	}

	[Fact]
	public void PacketsRejectZeroIdsAndConfiguredLimits()
	{
		Assert.Throws<InvalidDataException>(() => new ConsoleCommandRequestPacket
		{
			RequestId = 0,
			CommandName = "day",
		}.Serialize());
		Assert.Throws<InvalidDataException>(() => new ConsoleCommandRequestPacket
		{
			RequestId = 1,
			CommandName = "day",
			Arguments = Enumerable.Repeat("x", ConsoleCommandCatalog.MaximumArgumentCount + 1).ToArray(),
		}.Serialize());
		Assert.Throws<InvalidDataException>(() => new ConsoleCommandResultPacket
		{
			RequestId = 1,
			Lines = [new string('x', ConsoleCommandCatalog.MaximumResultLineLength + 1)],
		}.Serialize());
	}

	[Fact]
	public void DecoderRejectsOversizedArgumentCountBeforeReadingArguments()
	{
		using MemoryStream stream = new();
		using (BinaryWriter writer = new(stream, Encoding.UTF8, leaveOpen: true))
		{
			writer.Write((byte)PacketType.ConsoleCommandRequest);
			writer.Write(1u);
			writer.Write((ushort)3);
			writer.Write(Encoding.UTF8.GetBytes("day"));
			writer.Write((byte)(ConsoleCommandCatalog.MaximumArgumentCount + 1));
		}

		Assert.Throws<InvalidDataException>(() => Packet.Deserialize(stream.ToArray()));
	}
}
