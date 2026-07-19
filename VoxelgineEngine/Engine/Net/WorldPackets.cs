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
	public override PacketType Type => PacketType.BlockChange;
	public int X { get; set; }
	public int Y { get; set; }
	public int Z { get; set; }
	public ushort BlockType { get; set; }
	public long ColumnRevision { get; set; }

	public override void Write(BinaryWriter writer)
	{
		writer.Write(X);
		writer.Write(Y);
		writer.Write(Z);
		writer.Write(BlockType);
		writer.Write(ColumnRevision);
	}

	public override void Read(BinaryReader reader)
	{
		X = reader.ReadInt32();
		Y = reader.ReadInt32();
		Z = reader.ReadInt32();
		BlockType = reader.ReadUInt16();
		ColumnRevision = reader.ReadInt64();
	}
}

public sealed class BlockPlaceRequestPacket : Packet
{
	public override PacketType Type => PacketType.BlockPlaceRequest;
	public int X { get; set; }
	public int Y { get; set; }
	public int Z { get; set; }
	public ushort BlockType { get; set; }

	public override void Write(BinaryWriter writer)
	{
		writer.Write(X);
		writer.Write(Y);
		writer.Write(Z);
		writer.Write(BlockType);
	}

	public override void Read(BinaryReader reader)
	{
		X = reader.ReadInt32();
		Y = reader.ReadInt32();
		Z = reader.ReadInt32();
		BlockType = reader.ReadUInt16();
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
	public int X { get; set; }
	public int Y { get; set; }
	public int Z { get; set; }

	public override void Write(BinaryWriter writer)
	{
		writer.Write(X);
		writer.Write(Y);
		writer.Write(Z);
	}

	public override void Read(BinaryReader reader)
	{
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
