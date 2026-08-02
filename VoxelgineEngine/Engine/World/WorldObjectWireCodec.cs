using System.IO;
using Voxelgine.Engine;
using Voxelgine.Engine.World.Structures;

namespace Voxelgine.Graphics;

public static class WorldObjectWireCodec
{
	public static byte[] EncodeRecords(IReadOnlyList<WorldPlantRecord> records)
	{
		using MemoryStream stream = new();
		using BinaryWriter writer = new(stream);
		foreach (WorldPlantRecord record in records) WriteRecord(writer, record);
		return stream.ToArray();
	}

	public static WorldPlantRecord[] DecodeRecords(ReadOnlySpan<byte> payload, int recordCount)
	{
		if (recordCount is < 0 or > WorldObjectStore.MaximumColumnRecords) throw new InvalidDataException("Invalid world-object record count.");
		using MemoryStream stream = new(payload.ToArray(), writable: false);
		using BinaryReader reader = new(stream);
		WorldPlantRecord[] records = new WorldPlantRecord[recordCount];
		for (int index = 0; index < records.Length; index++) records[index] = ReadRecord(reader);
		if (stream.Position != stream.Length) throw new InvalidDataException("World-object record payload contains trailing bytes.");
		return records;
	}

	public static byte[] EncodeOperations(IReadOnlyList<WorldObjectOperation> operations)
	{
		using MemoryStream stream = new(); using BinaryWriter writer = new(stream);
		foreach (WorldObjectOperation operation in operations)
		{
			writer.Write((byte)operation.Kind);
			if (operation.Kind == WorldObjectOperationKind.Upsert) WriteRecord(writer, operation.Record);
			else WriteKey(writer, operation.Key);
		}
		return stream.ToArray();
	}

	public static WorldObjectOperation[] DecodeOperations(ReadOnlySpan<byte> payload, int count)
	{
		if (count is < 0 or > 1024) throw new InvalidDataException("Invalid world-object operation count.");
		using MemoryStream stream = new(payload.ToArray(), writable: false); using BinaryReader reader = new(stream);
		WorldObjectOperation[] operations = new WorldObjectOperation[count];
		for (int index = 0; index < count; index++)
		{
			WorldObjectOperationKind kind = (WorldObjectOperationKind)reader.ReadByte();
			operations[index] = kind switch
			{
				WorldObjectOperationKind.Upsert => new(kind, ReadRecord(reader), default),
				WorldObjectOperationKind.Remove => new(kind, default, ReadKey(reader)),
				_ => throw new InvalidDataException("Invalid world-object operation kind."),
			};
		}
		if (stream.Position != stream.Length) throw new InvalidDataException("World-object delta contains trailing bytes.");
		return operations;
	}

	private static void WriteRecord(BinaryWriter writer, WorldPlantRecord record)
	{
		WriteKey(writer, record.Key); writer.Write((byte)record.PlantType); writer.Write(record.GrowthProgress);
		writer.Write(record.Health); writer.Write(record.HarvestItem.Value);
		writer.Write(record.Support.X); writer.Write(record.Support.Y); writer.Write(record.Support.Z);
	}

	private static WorldPlantRecord ReadRecord(BinaryReader reader) => new(
		ReadKey(reader), (WorldPlantType)reader.ReadByte(), reader.ReadUInt16(), reader.ReadByte(),
		new ItemId(reader.ReadUInt16()), new BlockCoordinate(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32()));

	private static void WriteKey(BinaryWriter writer, PersistentWorldObjectKey key)
	{
		writer.Write((byte)key.Kind);
		if (key.Kind == PersistentWorldObjectKeyKind.Generated)
		{ writer.Write(key.GeneratedMarkerId.Site.Value); writer.Write(key.GeneratedMarkerId.BlueprintMarkerId); }
		else writer.Write(key.PersistentWorldObjectId.Value);
	}

	private static PersistentWorldObjectKey ReadKey(BinaryReader reader) => (PersistentWorldObjectKeyKind)reader.ReadByte() switch
	{
		PersistentWorldObjectKeyKind.Generated => PersistentWorldObjectKey.Generated(new GeneratedMarkerId(new GeneratedSiteId(reader.ReadString()), reader.ReadString())),
		PersistentWorldObjectKeyKind.Placed => PersistentWorldObjectKey.Placed(new PersistentWorldObjectId(reader.ReadUInt64())),
		_ => throw new InvalidDataException("Invalid world-object key kind."),
	};
}
