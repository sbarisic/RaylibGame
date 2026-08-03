using System.Text;

namespace Voxelgine.Engine;

public sealed class ConsoleCommandRequestPacket : Packet
{
	public override PacketType Type => PacketType.ConsoleCommandRequest;
	public uint RequestId { get; set; }
	public string CommandName { get; set; } = string.Empty;
	public string[] Arguments { get; set; } = [];

	public override void Write(BinaryWriter writer)
	{
		Validate();
		writer.Write(RequestId);
		ConsoleCommandPacketCodec.WriteString(writer, CommandName, ConsoleCommandCatalog.MaximumCommandNameLength);
		writer.Write((byte)Arguments.Length);
		foreach (string argument in Arguments)
			ConsoleCommandPacketCodec.WriteString(writer, argument, ConsoleCommandCatalog.MaximumArgumentLength);
	}

	public override void Read(BinaryReader reader)
	{
		RequestId = reader.ReadUInt32();
		CommandName = ConsoleCommandPacketCodec.ReadString(reader, ConsoleCommandCatalog.MaximumCommandNameLength);
		int count = reader.ReadByte();
		if (count > ConsoleCommandCatalog.MaximumArgumentCount)
			throw new InvalidDataException("Console command contains too many arguments.");
		Arguments = new string[count];
		for (int i = 0; i < count; i++)
			Arguments[i] = ConsoleCommandPacketCodec.ReadString(reader, ConsoleCommandCatalog.MaximumArgumentLength);
		Validate();
	}

	private void Validate()
	{
		if (RequestId == 0 || string.IsNullOrWhiteSpace(CommandName)
			|| CommandName.Length > ConsoleCommandCatalog.MaximumCommandNameLength
			|| CommandName.Any(static character => char.IsWhiteSpace(character) || character is '"' or '\''))
			throw new InvalidDataException("Console command request identity is invalid.");
		Arguments ??= [];
		if (Arguments.Length > ConsoleCommandCatalog.MaximumArgumentCount
			|| Arguments.Any(static argument => argument is null || argument.Length > ConsoleCommandCatalog.MaximumArgumentLength)
			|| CommandName.Length + Arguments.Sum(static argument => argument.Length) > ConsoleCommandCatalog.MaximumCommandTextLength)
			throw new InvalidDataException("Console command request exceeds text limits.");
	}
}

public sealed class ConsoleCommandResultPacket : Packet
{
	public override PacketType Type => PacketType.ConsoleCommandResult;
	public uint RequestId { get; set; }
	public bool Success { get; set; }
	public string[] Lines { get; set; } = [];

	public override void Write(BinaryWriter writer)
	{
		Validate();
		writer.Write(RequestId);
		writer.Write(Success);
		writer.Write((byte)Lines.Length);
		foreach (string line in Lines)
			ConsoleCommandPacketCodec.WriteString(writer, line, ConsoleCommandCatalog.MaximumResultLineLength);
	}

	public override void Read(BinaryReader reader)
	{
		RequestId = reader.ReadUInt32();
		Success = reader.ReadBoolean();
		int count = reader.ReadByte();
		if (count > ConsoleCommandCatalog.MaximumResultLines)
			throw new InvalidDataException("Console command result contains too many lines.");
		Lines = new string[count];
		for (int i = 0; i < count; i++)
			Lines[i] = ConsoleCommandPacketCodec.ReadString(reader, ConsoleCommandCatalog.MaximumResultLineLength);
		Validate();
	}

	private void Validate()
	{
		Lines ??= [];
		if (RequestId == 0 || Lines.Length > ConsoleCommandCatalog.MaximumResultLines
			|| Lines.Any(static line => line is null || line.Length > ConsoleCommandCatalog.MaximumResultLineLength)
			|| Lines.Sum(static line => line.Length) > ConsoleCommandCatalog.MaximumResultTextLength)
			throw new InvalidDataException("Console command result exceeds limits.");
	}
}

internal static class ConsoleCommandPacketCodec
{
	private static readonly Encoding Utf8 = new UTF8Encoding(false, true);

	public static void WriteString(BinaryWriter writer, string value, int maximumCharacters)
	{
		if (value is null || value.Length > maximumCharacters)
			throw new InvalidDataException("Console command string exceeds its character limit.");
		byte[] bytes = Utf8.GetBytes(value);
		int maximumBytes = checked(maximumCharacters * 4);
		if (bytes.Length > maximumBytes)
			throw new InvalidDataException("Console command string exceeds its byte limit.");
		writer.Write((ushort)bytes.Length);
		writer.Write(bytes);
	}

	public static string ReadString(BinaryReader reader, int maximumCharacters)
	{
		int byteLength = reader.ReadUInt16();
		if (byteLength > maximumCharacters * 4)
			throw new InvalidDataException("Console command string exceeds its byte limit.");
		byte[] bytes = reader.ReadBytes(byteLength);
		if (bytes.Length != byteLength)
			throw new EndOfStreamException();
		string value;
		try
		{
			value = Utf8.GetString(bytes);
		}
		catch (DecoderFallbackException exception)
		{
			throw new InvalidDataException("Console command string is not valid UTF-8.", exception);
		}
		if (value.Length > maximumCharacters)
			throw new InvalidDataException("Console command string exceeds its character limit.");
		return value;
	}
}
