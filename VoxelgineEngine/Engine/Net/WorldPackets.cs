using System;
using System.IO;
using System.Numerics;

namespace Voxelgine.Engine;

public enum WorldColumnStreamKind : byte
{
	Ordinary,
	BootstrapCore,
	BootstrapHalo,
}

public sealed class BlockChangePacket : Packet
{
	public const int MaximumChanges = 4096;
	public const int MaximumDecodedBytes = 256 * 1024;
	private const int ChangeDecodedBytes = sizeof(int) * 3 + sizeof(ushort) + sizeof(byte);

	public override PacketType Type => PacketType.BlockChange;
	public int ColumnX { get; set; }
	public int ColumnZ { get; set; }
	public long ColumnRevision { get; set; }
	public BlockChangeEntry[] Changes { get; set; } = Array.Empty<BlockChangeEntry>();

	public override void Write(BinaryWriter writer)
	{
		Validate();
		writer.Write(ColumnX);
		writer.Write(ColumnZ);
		writer.Write(ColumnRevision);
		writer.Write(Changes.Length);
		foreach (BlockChangeEntry change in Changes)
		{
			writer.Write(change.X);
			writer.Write(change.Y);
			writer.Write(change.Z);
			writer.Write(change.BlockType);
			writer.Write(change.BlockState);
		}
	}

	public override void Read(BinaryReader reader)
	{
		ColumnX = reader.ReadInt32();
		ColumnZ = reader.ReadInt32();
		ColumnRevision = reader.ReadInt64();
		int count = reader.ReadInt32();
		if (count is < 1 or > MaximumChanges || count * ChangeDecodedBytes > MaximumDecodedBytes)
			throw new InvalidDataException($"Invalid block-change count {count}.");
		Changes = new BlockChangeEntry[count];
		for (int index = 0; index < count; index++)
		{
			Changes[index] = new BlockChangeEntry(
				reader.ReadInt32(),
				reader.ReadInt32(),
				reader.ReadInt32(),
				reader.ReadUInt16(),
				reader.ReadByte());
		}
		Validate();
	}

	private void Validate()
	{
		ArgumentNullException.ThrowIfNull(Changes);
		if (Changes.Length is < 1 or > MaximumChanges ||
			Changes.Length * ChangeDecodedBytes > MaximumDecodedBytes)
			throw new InvalidDataException($"Invalid block-change count {Changes.Length}.");
		foreach (BlockChangeEntry change in Changes)
		{
			int columnX = Math.DivRem(change.X, 16, out int remainderX);
			if (remainderX < 0) columnX--;
			int columnZ = Math.DivRem(change.Z, 16, out int remainderZ);
			if (remainderZ < 0) columnZ--;
			if (columnX != ColumnX || columnZ != ColumnZ)
				throw new InvalidDataException("A block-change batch crosses a column boundary.");
			try
			{
				_ = new Voxelgine.Graphics.BlockValue((BlockType)change.BlockType, change.BlockState);
			}
			catch (ArgumentOutOfRangeException exception)
			{
				throw new InvalidDataException("A block-change batch contains an invalid block value.", exception);
			}
		}
	}
}

public readonly record struct BlockChangeEntry(
	int X,
	int Y,
	int Z,
	ushort BlockType,
	byte BlockState);

public sealed class BlockPlaceRequestPacket : Packet
{
	public override PacketType Type => PacketType.BlockPlaceRequest;
	public uint ItemUseActionId { get; set; }
	public int CommandTick { get; set; }
	public ItemUseChannel Channel { get; set; }
	public int X { get; set; }
	public int Y { get; set; }
	public int Z { get; set; }
	public ushort BlockType { get; set; }
	public byte BlockState { get; set; }

	public override void Write(BinaryWriter writer)
	{
		writer.Write(ItemUseActionId);
		writer.Write(CommandTick);
		writer.Write((byte)Channel);
		writer.Write(X);
		writer.Write(Y);
		writer.Write(Z);
		writer.Write(BlockType);
		writer.Write(BlockState);
	}

	public override void Read(BinaryReader reader)
	{
		ItemUseActionId = reader.ReadUInt32();
		CommandTick = reader.ReadInt32();
		Channel = (ItemUseChannel)reader.ReadByte();
		X = reader.ReadInt32();
		Y = reader.ReadInt32();
		Z = reader.ReadInt32();
		BlockType = reader.ReadUInt16();
		BlockState = reader.ReadByte();
	}
}

public sealed class FogChangePacket : Packet
{
	public override PacketType Type => PacketType.FogChange;
	public int X { get; set; }
	public int Y { get; set; }
	public int Z { get; set; }
	public uint Fog { get; set; }
	public long ColumnRevision { get; set; }

	public override void Write(BinaryWriter writer)
	{
		writer.Write(X);
		writer.Write(Y);
		writer.Write(Z);
		writer.Write(Fog);
		writer.Write(ColumnRevision);
	}

	public override void Read(BinaryReader reader)
	{
		X = reader.ReadInt32();
		Y = reader.ReadInt32();
		Z = reader.ReadInt32();
		Fog = reader.ReadUInt32();
		ColumnRevision = reader.ReadInt64();
	}
}

public sealed class BlockRemoveRequestPacket : Packet
{
	public override PacketType Type => PacketType.BlockRemoveRequest;
	public uint ItemUseActionId { get; set; }
	public int CommandTick { get; set; }
	public ItemUseChannel Channel { get; set; }
	public int X { get; set; }
	public int Y { get; set; }
	public int Z { get; set; }

	public override void Write(BinaryWriter writer)
	{
		writer.Write(ItemUseActionId);
		writer.Write(CommandTick);
		writer.Write((byte)Channel);
		writer.Write(X);
		writer.Write(Y);
		writer.Write(Z);
	}

	public override void Read(BinaryReader reader)
	{
		ItemUseActionId = reader.ReadUInt32();
		CommandTick = reader.ReadInt32();
		Channel = (ItemUseChannel)reader.ReadByte();
		X = reader.ReadInt32();
		Y = reader.ReadInt32();
		Z = reader.ReadInt32();
	}
}

public sealed class WorldStreamBeginPacket : Packet
{
	public override PacketType Type => PacketType.WorldStreamBegin;
	public int StreamId { get; set; }
	public Vector3 FocusPosition { get; set; }
	public int WorldSeed { get; set; }
	public int TotalColumns { get; set; }
	public int BootstrapCoreColumns { get; set; }
	public int BootstrapHaloColumns { get; set; }

	public override void Write(BinaryWriter writer)
	{
		writer.Write(StreamId);
		writer.WriteVector3(FocusPosition);
		writer.Write(WorldSeed);
		writer.Write(TotalColumns);
		writer.Write(BootstrapCoreColumns);
		writer.Write(BootstrapHaloColumns);
	}

	public override void Read(BinaryReader reader)
	{
		StreamId = reader.ReadInt32();
		FocusPosition = reader.ReadVector3();
		WorldSeed = reader.ReadInt32();
		TotalColumns = reader.ReadInt32();
		BootstrapCoreColumns = reader.ReadInt32();
		BootstrapHaloColumns = reader.ReadInt32();
	}
}

public sealed class WorldColumnPacket : Packet
{
	public override PacketType Type => PacketType.WorldColumn;
	public int StreamId { get; set; }
	public int X { get; set; }
	public int Z { get; set; }
	public long Revision { get; set; }
	public WorldColumnStreamKind Kind { get; set; }
	public uint Checksum { get; set; }
	public byte[] Payload { get; set; } = Array.Empty<byte>();

	public override void Write(BinaryWriter writer)
	{
		writer.Write(StreamId);
		writer.Write(X);
		writer.Write(Z);
		writer.Write(Revision);
		writer.Write((byte)Kind);
		writer.Write(Checksum);
		writer.Write(Payload.Length);
		writer.Write(Payload);
	}

	public override void Read(BinaryReader reader)
	{
		StreamId = reader.ReadInt32();
		X = reader.ReadInt32();
		Z = reader.ReadInt32();
		Revision = reader.ReadInt64();
		Kind = (WorldColumnStreamKind)reader.ReadByte();
		Checksum = reader.ReadUInt32();
		int length = reader.ReadInt32();
		if (length < 0 || length > 16 * 1024 * 1024)
			throw new InvalidDataException($"Invalid world-column payload length {length}.");
		Payload = reader.ReadBytes(length);
		if (Payload.Length != length)
			throw new EndOfStreamException("World-column payload is truncated.");
	}
}

public abstract class WorldStreamIdPacket : Packet
{
	public int StreamId { get; set; }
	public override void Write(BinaryWriter writer) => writer.Write(StreamId);
	public override void Read(BinaryReader reader) => StreamId = reader.ReadInt32();
}

public sealed class WorldBootstrapCompletePacket : WorldStreamIdPacket
{
	public override PacketType Type => PacketType.WorldBootstrapComplete;
}

public sealed class ClientWorldReadyPacket : WorldStreamIdPacket
{
	public override PacketType Type => PacketType.ClientWorldReady;
}

public class WorldColumnAppliedPacket : WorldStreamIdPacket
{
	public override PacketType Type => PacketType.WorldColumnApplied;
	public int X { get; set; }
	public int Z { get; set; }
	public long Revision { get; set; }

	public override void Write(BinaryWriter writer)
	{
		base.Write(writer);
		writer.Write(X);
		writer.Write(Z);
		writer.Write(Revision);
	}

	public override void Read(BinaryReader reader)
	{
		base.Read(reader);
		X = reader.ReadInt32();
		Z = reader.ReadInt32();
		Revision = reader.ReadInt64();
	}
}

public sealed class WorldColumnResyncRequestPacket : WorldStreamIdPacket
{
	public override PacketType Type => PacketType.WorldColumnResyncRequest;
	public int X { get; set; }
	public int Z { get; set; }
	public long Revision { get; set; }

	public override void Write(BinaryWriter writer)
	{
		base.Write(writer);
		writer.Write(X);
		writer.Write(Z);
		writer.Write(Revision);
	}

	public override void Read(BinaryReader reader)
	{
		base.Read(reader);
		X = reader.ReadInt32();
		Z = reader.ReadInt32();
		Revision = reader.ReadInt64();
	}
}

public sealed class ChunkInterestPacket : WorldStreamIdPacket
{
	public override PacketType Type => PacketType.ChunkInterest;
	public int CenterX { get; set; }
	public int CenterZ { get; set; }
	public int RadiusBlocks { get; set; }

	public override void Write(BinaryWriter writer)
	{
		base.Write(writer);
		writer.Write(CenterX);
		writer.Write(CenterZ);
		writer.Write(RadiusBlocks);
	}

	public override void Read(BinaryReader reader)
	{
		base.Read(reader);
		CenterX = reader.ReadInt32();
		CenterZ = reader.ReadInt32();
		RadiusBlocks = reader.ReadInt32();
	}
}

public sealed class ClientWorldStartPacket : WorldStreamIdPacket
{
	public override PacketType Type => PacketType.ClientWorldStart;
	public int ServerTick { get; set; }
	public float Health { get; set; }
	public PlayerPhysicsState PhysicsState { get; set; }

	public override void Write(BinaryWriter writer)
	{
		base.Write(writer);
		writer.Write(ServerTick);
		writer.Write(Health);
		writer.WritePlayerPhysicsState(PhysicsState);
	}

	public override void Read(BinaryReader reader)
	{
		base.Read(reader);
		ServerTick = reader.ReadInt32();
		Health = reader.ReadSingle();
		PhysicsState = reader.ReadPlayerPhysicsState();
	}
}
