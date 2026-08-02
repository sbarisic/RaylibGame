using System;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Voxelgine.Engine;
using Voxelgine.Engine.World.Structures;

namespace Voxelgine.Graphics;

public readonly record struct WorldArchiveMetadata(
	int WorldSeed,
	Vector3 PlayerSpawn,
	Vector3 PickupSpawn,
	Vector3 NpcSpawn,
	WorldFeaturePlan GeneratedFeatures = null,
	PersistedMachineIntent[] MachineIntents = null,
	HabitatMilestone Milestone = HabitatMilestone.None,
	double AbsoluteGameHours = 8d);

public readonly record struct PersistedMachineIntent(MachineKey Key, bool RequestedEnabled);

public sealed class WorldArchiveReadResult
{
	internal WorldArchiveReadResult(
		WorldArchiveMetadata metadata,
		ChunkColumnSnapshot[] columns,
		WorldArchivePayloadCache payloadCache,
		WorldPlantRecord[] worldObjects,
		PersistentFurnitureRecord[] furniture,
		NpcLifeRecord[] npcLife,
		GeneratedTombstone[] tombstones)
	{
		Metadata = metadata;
		Columns = columns;
		PayloadCache = payloadCache;
		WorldObjects = worldObjects;
		Furniture = furniture;
		NpcLife = npcLife;
		Tombstones = tombstones;
	}

	public WorldArchiveMetadata Metadata { get; }
	public IReadOnlyList<ChunkColumnSnapshot> Columns { get; }
	public WorldArchivePayloadCache PayloadCache { get; }
	public IReadOnlyList<WorldPlantRecord> WorldObjects { get; }
	public IReadOnlyList<PersistentFurnitureRecord> Furniture { get; }
	public IReadOnlyList<NpcLifeRecord> NpcLife { get; }
	public IReadOnlyList<GeneratedTombstone> Tombstones { get; }
}

public sealed class WorldArchivePayloadCache
{
	private readonly Dictionary<(int X, int Z, long Revision), CachedPayload> payloads = new();

	public int Count => payloads.Count;

	internal bool TryGet(int x, int z, long revision, out byte[] payload, out uint checksum)
	{
		if (payloads.TryGetValue((x, z, revision), out CachedPayload cached))
		{
			payload = cached.Payload;
			checksum = cached.Checksum;
			return true;
		}
		payload = null;
		checksum = 0;
		return false;
	}

	internal void Set(int x, int z, long revision, byte[] payload, uint checksum) =>
		payloads[(x, z, revision)] = new CachedPayload(payload, checksum);

	private readonly record struct CachedPayload(byte[] Payload, uint Checksum);
}

public sealed class IncompatibleWorldArchiveException : IOException
{
	public IncompatibleWorldArchiveException(string message) : base(message)
	{
	}
}

/// <summary>Indexed, independently compressed world-column archive.</summary>
public static class WorldArchive
{
	public const uint Magic = 0x57584F56; // VOXW
	public const ushort FormatVersion = 6;
	public const int HeaderSize = 20;
	public const int SectionDirectoryEntrySize = 28;
	public const int MaximumSections = 64;
	public const uint MetadataSectionId = 1;
	public const uint VoxelColumnsSectionId = 2;
	public const uint PersistentFurnitureSectionId = 3;
	public const uint WorldObjectsSectionId = 4;
	public const uint TombstonesSectionId = 5;
	public const uint NpcLifeSectionId = 6;
	private const ushort MandatorySectionFlag = 1;
	private const ushort MetadataSectionVersion = 1;
	private const ushort VoxelColumnsSectionVersion = 1;
	private const int ColumnDirectoryEntrySize = 32;

	public static bool IsCompatible(Stream input)
	{
		ArgumentNullException.ThrowIfNull(input);
		if (!input.CanSeek || input.Length - input.Position < sizeof(uint) + sizeof(ushort))
			return false;

		long position = input.Position;
		using BinaryReader reader = new(input, System.Text.Encoding.UTF8, leaveOpen: true);
		uint magic = reader.ReadUInt32();
		ushort version = reader.ReadUInt16();
		input.Position = position;
		return magic == Magic && version == FormatVersion;
	}

	public static string MoveIncompatibleFileToBackup(
		string path,
		DateTime? timestamp = null)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(path);
		if (!File.Exists(path))
			return null;

		bool compatible;
		using (FileStream input = File.OpenRead(path))
			compatible = IsCompatible(input);
		if (compatible)
			return null;

		DateTime value = timestamp ?? DateTime.Now;
		string basePath = $"{path}.incompatible-{value:yyyyMMdd-HHmmss-fff}.bak";
		string backupPath = basePath;
		for (int suffix = 1; File.Exists(backupPath); suffix++)
			backupPath = $"{basePath}.{suffix}";
		File.Move(path, backupPath);
		return backupPath;
	}

	public static WorldArchivePayloadCache Write(
		Stream output,
		ChunkMap map,
		WorldArchiveMetadata metadata,
		WorldArchivePayloadCache previousPayloads = null,
		CancellationToken cancellationToken = default,
		IReadOnlyList<WorldPlantRecord> worldObjects = null,
		IReadOnlyList<PersistentFurnitureRecord> furniture = null,
		IReadOnlyList<NpcLifeRecord> npcLife = null,
		IReadOnlyList<GeneratedTombstone> tombstones = null)
	{
		ArgumentNullException.ThrowIfNull(output);
		ArgumentNullException.ThrowIfNull(map);
		if (!output.CanSeek)
			throw new ArgumentException("World archives require a seekable output stream.", nameof(output));
		if (output.Position != 0)
			throw new ArgumentException("World archives must be written from stream position zero.", nameof(output));

		ChunkColumnCoordinate[] coordinates = map.GetColumnCoordinates();
		ChunkColumnSnapshot[] columns = new ChunkColumnSnapshot[coordinates.Length];
		long[] revisions = new long[coordinates.Length];
		byte[][] payloads = new byte[coordinates.Length][];
		uint[] checksums = new uint[coordinates.Length];
		for (int index = 0; index < coordinates.Length; index++)
		{
			cancellationToken.ThrowIfCancellationRequested();
			ChunkColumnCoordinate coordinate = coordinates[index];
			long revision = map.GetColumnRevision(coordinate.X, coordinate.Z);
			revisions[index] = revision;
			if (previousPayloads?.TryGet(
				coordinate.X,
				coordinate.Z,
				revision,
				out byte[] cached,
				out uint checksum) == true)
			{
				payloads[index] = cached;
				checksums[index] = checksum;
			}
			else
			{
				columns[index] = map.CaptureColumn(coordinate.X, coordinate.Z);
			}
		}

		Parallel.For(
			0,
			columns.Length,
			new ParallelOptions { CancellationToken = cancellationToken },
			index =>
			{
				if (payloads[index] != null)
					return;
				payloads[index] = WorldColumnCodec.Encode(columns[index]);
				checksums[index] = WorldColumnCodec.ComputeChecksum(payloads[index]);
			});

		byte[] metadataPayload = EncodeMetadata(metadata);
		byte[] columnsPayload = EncodeColumns(coordinates, revisions, payloads, checksums);
		List<ArchiveSection> sections = new()
		{
			new(MetadataSectionId, MetadataSectionVersion, MandatorySectionFlag, metadataPayload),
			new(VoxelColumnsSectionId, VoxelColumnsSectionVersion, MandatorySectionFlag, columnsPayload),
		};
		if (worldObjects is { Count: > 0 })
			sections.Add(new ArchiveSection(WorldObjectsSectionId, 1, 0, EncodeWorldObjects(worldObjects)));
		if (furniture is { Count: > 0 })
			sections.Add(new ArchiveSection(PersistentFurnitureSectionId, 1, 0, EncodeFurniture(furniture)));
		if (npcLife is { Count: > 0 })
			sections.Add(new ArchiveSection(NpcLifeSectionId, 1, 0, EncodeNpcLife(npcLife)));
		if(tombstones is {Count:>0})sections.Add(new ArchiveSection(TombstonesSectionId,1,0,EncodeTombstones(tombstones)));

		using BinaryWriter writer = new(output, System.Text.Encoding.UTF8, leaveOpen: true);
		long directoryOffset = HeaderSize;
		long payloadOffset = checked(directoryOffset + sections.Count * (long)SectionDirectoryEntrySize);
		writer.Write(Magic);
		writer.Write(FormatVersion);
		writer.Write((ushort)0);
		writer.Write((uint)sections.Count);
		writer.Write(directoryOffset);
		foreach (ArchiveSection section in sections)
		{
			writer.Write(section.Id);
			writer.Write(section.Version);
			writer.Write(section.Flags);
			writer.Write(payloadOffset);
			writer.Write((long)section.Payload.Length);
			writer.Write(ComputeCrc32(section.Payload));
			payloadOffset = checked(payloadOffset + section.Payload.Length);
		}
		foreach (ArchiveSection section in sections)
			writer.Write(section.Payload);
		output.SetLength(output.Position);

		WorldArchivePayloadCache replacement = new();
		for (int index = 0; index < coordinates.Length; index++)
		{
			ChunkColumnCoordinate coordinate = coordinates[index];
			replacement.Set(
				coordinate.X,
				coordinate.Z,
				revisions[index],
				payloads[index],
				checksums[index]);
		}
		return replacement;
	}

	public static WorldArchiveReadResult Read(
		Stream input,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(input);
		cancellationToken.ThrowIfCancellationRequested();
		using MemoryStream copy = new();
		input.CopyTo(copy);
		byte[] archive = copy.ToArray();
		Dictionary<uint, SectionEntry> sections = ReadSectionDirectory(archive);
		SectionEntry metadataSection = GetRequiredSection(
			sections, MetadataSectionId, MetadataSectionVersion, "world metadata");
		SectionEntry columnsSection = GetRequiredSection(
			sections, VoxelColumnsSectionId, VoxelColumnsSectionVersion, "voxel columns");
		WorldArchiveMetadata metadata = DecodeMetadata(GetSectionPayload(archive, metadataSection));
		WorldPlantRecord[] worldObjects = sections.TryGetValue(WorldObjectsSectionId, out SectionEntry worldObjectSection) && worldObjectSection.Version == 1
			? DecodeWorldObjects(GetSectionPayload(archive, worldObjectSection))
			: Array.Empty<WorldPlantRecord>();
		PersistentFurnitureRecord[] furniture = sections.TryGetValue(PersistentFurnitureSectionId, out SectionEntry furnitureSection) && furnitureSection.Version == 1
			? DecodeFurniture(GetSectionPayload(archive, furnitureSection))
			: Array.Empty<PersistentFurnitureRecord>();
		NpcLifeRecord[] npcLife = sections.TryGetValue(NpcLifeSectionId, out SectionEntry npcLifeSection) && npcLifeSection.Version == 1
			? DecodeNpcLife(GetSectionPayload(archive,npcLifeSection))
			: Array.Empty<NpcLifeRecord>();
		GeneratedTombstone[] tombstones=sections.TryGetValue(TombstonesSectionId,out SectionEntry tombstoneSection)&&tombstoneSection.Version==1?DecodeTombstones(GetSectionPayload(archive,tombstoneSection)):Array.Empty<GeneratedTombstone>();
		ReadOnlyMemory<byte> columnSectionPayload = GetSectionPayload(archive, columnsSection).ToArray();
		ColumnSection columnSection = DecodeColumnSection(columnSectionPayload.Span);
		ChunkColumnSnapshot[] columns = new ChunkColumnSnapshot[columnSection.Entries.Length];
		WorldArchivePayloadCache payloadCache = new();
		Parallel.For(0, columns.Length, new ParallelOptions { CancellationToken = cancellationToken }, index =>
		{
			ColumnArchiveEntry entry = columnSection.Entries[index];
			ReadOnlySpan<byte> payload = columnSectionPayload.Span.Slice(checked((int)entry.Offset), entry.Length);
			uint checksum = WorldColumnCodec.ComputeChecksum(payload);
			if (checksum != entry.Checksum)
				throw new InvalidDataException($"Voxel columns section checksum mismatch for column ({entry.X}, {entry.Z}).");
			byte[] retained = payload.ToArray();
			columns[index] = WorldColumnCodec.Decode(entry.X, entry.Z, entry.Revision, retained);
			lock (payloadCache)
				payloadCache.Set(entry.X, entry.Z, entry.Revision, retained, checksum);
		});
		return new WorldArchiveReadResult(metadata, columns, payloadCache, worldObjects, furniture, npcLife, tombstones);
	}

	public static ChunkColumnSnapshot ReadColumn(
		Stream input,
		int columnX,
		int columnZ)
	{
		ArgumentNullException.ThrowIfNull(input);
		if (!input.CanSeek)
			throw new ArgumentException("Random-access column reads require a seekable stream.", nameof(input));
		input.Position = 0;
		using MemoryStream copy = new();
		input.CopyTo(copy);
		byte[] archive = copy.ToArray();
		Dictionary<uint, SectionEntry> sections = ReadSectionDirectory(archive);
		SectionEntry section = GetRequiredSection(
			sections, VoxelColumnsSectionId, VoxelColumnsSectionVersion, "voxel columns");
		ReadOnlySpan<byte> payload = GetSectionPayload(archive, section);
		ColumnSection decoded = DecodeColumnSection(payload);
		foreach (ColumnArchiveEntry entry in decoded.Entries)
		{
			if (entry.X != columnX || entry.Z != columnZ)
				continue;
			ReadOnlySpan<byte> columnPayload = payload.Slice(checked((int)entry.Offset), entry.Length);
			if (WorldColumnCodec.ComputeChecksum(columnPayload) != entry.Checksum)
				throw new InvalidDataException($"Voxel columns section checksum mismatch for column ({columnX}, {columnZ}).");
			return WorldColumnCodec.Decode(columnX, columnZ, entry.Revision, columnPayload);
		}
		throw new KeyNotFoundException($"Column ({columnX}, {columnZ}) is not present in the archive.");
	}

	private static byte[] EncodeMetadata(WorldArchiveMetadata metadata)
	{
		using MemoryStream stream = new();
		using BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, leaveOpen: true);
		writer.Write(metadata.WorldSeed);
		WriteVector3(writer, metadata.PlayerSpawn);
		WriteVector3(writer, metadata.PickupSpawn);
		WriteVector3(writer, metadata.NpcSpawn);
		WriteFeaturePlan(writer, metadata.GeneratedFeatures ?? WorldFeaturePlan.Empty);
		PersistedMachineIntent[] intents = metadata.MachineIntents ?? Array.Empty<PersistedMachineIntent>();
		writer.Write(intents.Length);
		foreach (PersistedMachineIntent intent in intents.OrderBy(static value => value.Key))
		{
			WriteMachineKey(writer, intent.Key);
			writer.Write(intent.RequestedEnabled);
		}
		writer.Write((byte)metadata.Milestone);
		writer.Write(metadata.AbsoluteGameHours);
		return stream.ToArray();
	}

	private static byte[] EncodeWorldObjects(IReadOnlyList<WorldPlantRecord> records)
	{
		if (records.Count > 1_000_000) throw new ArgumentOutOfRangeException(nameof(records));
		using MemoryStream stream = new();
		using BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, leaveOpen: true);
		writer.Write(records.Count);
		foreach (WorldPlantRecord record in records.OrderBy(static record => record.Position.X).ThenBy(static record => record.Position.Z).ThenBy(static record => record.Position.Y))
		{
			writer.Write((byte)record.Key.Kind);
			if (record.Key.Kind == PersistentWorldObjectKeyKind.Generated)
			{
				writer.Write(record.Key.GeneratedMarkerId.Site.Value);
				writer.Write(record.Key.GeneratedMarkerId.BlueprintMarkerId);
			}
			else
				writer.Write(record.Key.PersistentWorldObjectId.Value);
			writer.Write((byte)record.PlantType);
			writer.Write(record.GrowthProgress);
			writer.Write(record.Health);
			writer.Write(record.HarvestItem.Value);
			WriteCoordinate(writer, record.Support);
		}
		return stream.ToArray();
	}

	private static byte[] EncodeFurniture(IReadOnlyList<PersistentFurnitureRecord> records)
	{
		if (records.Count > 100_000) throw new ArgumentOutOfRangeException(nameof(records));
		using MemoryStream stream = new(); using BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, leaveOpen:true);
		writer.Write(records.Count);
		foreach (PersistentFurnitureRecord record in records.OrderBy(static record=>record.Anchor.X).ThenBy(static record=>record.Anchor.Z).ThenBy(static record=>record.Anchor.Y))
		{
			writer.Write((byte)record.Key.Kind);
			if(record.Key.Kind==PersistentFurnitureKeyKind.Generated){writer.Write(record.Key.GeneratedMarkerId.Site.Value);writer.Write(record.Key.GeneratedMarkerId.BlueprintMarkerId);}else writer.Write(record.Key.PersistentEntityId.Value);
			writer.Write((byte)record.Type); WriteCoordinate(writer,record.Anchor); writer.Write(record.Facing); writer.Write((ushort)record.Slots.Count);
			foreach(ItemStack stack in record.Slots){writer.Write(stack.Item.Value);writer.Write(stack.Count);}
		}
		return stream.ToArray();
	}

	private static byte[] EncodeNpcLife(IReadOnlyList<NpcLifeRecord> records)
	{
		if(records.Count>100_000)throw new ArgumentOutOfRangeException(nameof(records));using MemoryStream stream=new();using BinaryWriter writer=new(stream,System.Text.Encoding.UTF8,leaveOpen:true);writer.Write(records.Count);
		foreach(NpcLifeRecord record in records.OrderBy(static record=>record.NpcId.ToString(),StringComparer.Ordinal)){StableNpcId.Write(writer,record.NpcId);writer.Write(record.Fatigue);writer.Write(record.AssignedBed.HasValue);if(record.AssignedBed is PersistentFurnitureKey bed){writer.Write((byte)bed.Kind);if(bed.Kind==PersistentFurnitureKeyKind.Generated){writer.Write(bed.GeneratedMarkerId.Site.Value);writer.Write(bed.GeneratedMarkerId.BlueprintMarkerId);}else writer.Write(bed.PersistentEntityId.Value);}}
		return stream.ToArray();
	}

	private static byte[] EncodeTombstones(IReadOnlyList<GeneratedTombstone> records)
	{
		if(records.Count>1_000_000)throw new ArgumentOutOfRangeException(nameof(records));using MemoryStream stream=new();using BinaryWriter writer=new(stream,System.Text.Encoding.UTF8,leaveOpen:true);writer.Write(records.Count);foreach(GeneratedTombstone record in records.OrderBy(static value=>value.Kind).ThenBy(static value=>value.MarkerId.Site).ThenBy(static value=>value.MarkerId.BlueprintMarkerId,StringComparer.Ordinal)){writer.Write((byte)record.Kind);writer.Write(record.MarkerId.Site.Value);writer.Write(record.MarkerId.BlueprintMarkerId);}return stream.ToArray();
	}
	private static GeneratedTombstone[] DecodeTombstones(ReadOnlySpan<byte> payload)
	{
		using MemoryStream stream=new(payload.ToArray(),writable:false);using BinaryReader reader=new(stream);int count=ReadBoundedCount(reader,1_000_000,"tombstone");GeneratedTombstone[] records=new GeneratedTombstone[count];GeneratedTombstoneStore validator=new();for(int index=0;index<count;index++){GeneratedObjectKind kind=(GeneratedObjectKind)reader.ReadByte();if(!Enum.IsDefined(kind))throw new InvalidDataException("Generated tombstone kind is invalid.");GeneratedMarkerId marker=new(new GeneratedSiteId(reader.ReadString()),reader.ReadString());if(!validator.Add(kind,marker))throw new InvalidDataException("Generated tombstone is duplicated.");records[index]=new GeneratedTombstone(kind,marker);}if(stream.Position!=stream.Length)throw new InvalidDataException("Tombstone section contains trailing data.");return records;
	}

	private static NpcLifeRecord[] DecodeNpcLife(ReadOnlySpan<byte> payload)
	{
		using MemoryStream stream=new(payload.ToArray(),writable:false);using BinaryReader reader=new(stream);int count=ReadBoundedCount(reader,100_000,"NPC life");NpcLifeRecord[] records=new NpcLifeRecord[count];HashSet<StableNpcId> ids=new();
		for(int index=0;index<count;index++){StableNpcId npc=StableNpcId.Read(reader);ushort fatigue=reader.ReadUInt16();if(fatigue>10000||!ids.Add(npc))throw new InvalidDataException("NPC life record is invalid or duplicated.");PersistentFurnitureKey? bed=null;if(reader.ReadBoolean()){PersistentFurnitureKeyKind kind=(PersistentFurnitureKeyKind)reader.ReadByte();bed=kind==PersistentFurnitureKeyKind.Generated?PersistentFurnitureKey.Generated(new GeneratedMarkerId(new GeneratedSiteId(reader.ReadString()),reader.ReadString())):kind==PersistentFurnitureKeyKind.Placed?PersistentFurnitureKey.Placed(new PersistentEntityId(reader.ReadUInt64())):throw new InvalidDataException("NPC life bed key kind is invalid.");}records[index]=new NpcLifeRecord(npc,fatigue,bed);}
		if(stream.Position!=stream.Length)throw new InvalidDataException("NPC life section contains trailing data.");return records;
	}

	private static PersistentFurnitureRecord[] DecodeFurniture(ReadOnlySpan<byte> payload)
	{
		using MemoryStream stream=new(payload.ToArray(),writable:false); using BinaryReader reader=new(stream);
		int count=ReadBoundedCount(reader,100_000,"furniture"); PersistentFurnitureRecord[] records=new PersistentFurnitureRecord[count];
		for(int index=0;index<count;index++)
		{
			PersistentFurnitureKeyKind kind=(PersistentFurnitureKeyKind)reader.ReadByte();
			PersistentFurnitureKey key=kind switch { PersistentFurnitureKeyKind.Generated=>PersistentFurnitureKey.Generated(new GeneratedMarkerId(new GeneratedSiteId(reader.ReadString()),reader.ReadString())), PersistentFurnitureKeyKind.Placed=>PersistentFurnitureKey.Placed(new PersistentEntityId(reader.ReadUInt64())), _=>throw new InvalidDataException($"Invalid furniture key kind {kind}.")};
			FurnitureType type=(FurnitureType)reader.ReadByte(); BlockCoordinate anchor=ReadCoordinate(reader); byte facing=reader.ReadByte(); int slots=reader.ReadUInt16();
			if(slots>256) throw new InvalidDataException("Furniture slot count exceeds limit."); ItemStack[] contents=new ItemStack[slots];
			for(int slot=0;slot<slots;slot++){contents[slot]=new ItemStack(new ItemId(reader.ReadUInt16()),reader.ReadUInt16());if(!ItemCatalog.IsCanonical(contents[slot]))throw new InvalidDataException("Furniture contains a non-canonical stack.");}
			records[index]=new PersistentFurnitureRecord(key,type,anchor,facing,contents);
		}
		if(stream.Position!=stream.Length)throw new InvalidDataException("Furniture section contains trailing data.");
		FurnitureStore validator=new(); validator.Restore(records); return records;
	}

	private static WorldPlantRecord[] DecodeWorldObjects(ReadOnlySpan<byte> payload)
	{
		using MemoryStream stream = new(payload.ToArray(), writable: false);
		using BinaryReader reader = new(stream);
		int count = ReadBoundedCount(reader, 1_000_000, "world object");
		WorldPlantRecord[] records = new WorldPlantRecord[count];
		for (int index = 0; index < count; index++)
		{
			PersistentWorldObjectKeyKind kind = (PersistentWorldObjectKeyKind)reader.ReadByte();
			PersistentWorldObjectKey key = kind switch
			{
				PersistentWorldObjectKeyKind.Generated => PersistentWorldObjectKey.Generated(
					new GeneratedMarkerId(new GeneratedSiteId(reader.ReadString()), reader.ReadString())),
				PersistentWorldObjectKeyKind.Placed => PersistentWorldObjectKey.Placed(new PersistentWorldObjectId(reader.ReadUInt64())),
				_ => throw new InvalidDataException($"Invalid persistent world-object key kind {kind}."),
			};
			WorldPlantType plantType = (WorldPlantType)reader.ReadByte();
			ushort progress = reader.ReadUInt16();
			byte health = reader.ReadByte();
			ItemId harvest = new(reader.ReadUInt16());
			BlockCoordinate support = ReadCoordinate(reader);
			records[index] = new WorldPlantRecord(key, plantType, progress, health, harvest, support);
		}
		if (stream.Position != stream.Length) throw new InvalidDataException("World objects section contains trailing data.");
		return records;
	}

	private static WorldArchiveMetadata DecodeMetadata(ReadOnlySpan<byte> payload)
	{
		using MemoryStream stream = new(payload.ToArray(), writable: false);
		using BinaryReader reader = new(stream);
		int worldSeed = reader.ReadInt32();
		Vector3 playerSpawn = ReadVector3(reader);
		Vector3 pickupSpawn = ReadVector3(reader);
		Vector3 npcSpawn = ReadVector3(reader);
		WorldFeaturePlan features = ReadFeaturePlan(reader);
		int intentCount = ReadBoundedCount(reader, 100_000, "machine intent");
		PersistedMachineIntent[] intents = new PersistedMachineIntent[intentCount];
		for (int index = 0; index < intentCount; index++)
			intents[index] = new PersistedMachineIntent(ReadMachineKey(reader), reader.ReadBoolean());
		HabitatMilestone milestone = (HabitatMilestone)reader.ReadByte();
		if (!Enum.IsDefined(milestone))
			throw new InvalidDataException($"Invalid habitat milestone {milestone} in world metadata section.");
		double absoluteGameHours=reader.ReadDouble();if(!double.IsFinite(absoluteGameHours)||absoluteGameHours<0)throw new InvalidDataException("World metadata absolute game time is invalid.");
		if (stream.Position != stream.Length)
			throw new InvalidDataException("World metadata section contains trailing data.");
		return new WorldArchiveMetadata(worldSeed, playerSpawn, pickupSpawn, npcSpawn, features, intents, milestone, absoluteGameHours);
	}

	private static byte[] EncodeColumns(
		ChunkColumnCoordinate[] coordinates,
		long[] revisions,
		byte[][] payloads,
		uint[] checksums)
	{
		using MemoryStream stream = new();
		using BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, leaveOpen: true);
		writer.Write(coordinates.Length);
		long payloadOffset = checked(sizeof(int) + coordinates.Length * (long)ColumnDirectoryEntrySize);
		for (int index = 0; index < coordinates.Length; index++)
		{
			writer.Write(coordinates[index].X);
			writer.Write(coordinates[index].Z);
			writer.Write(revisions[index]);
			writer.Write(payloadOffset);
			writer.Write(payloads[index].Length);
			writer.Write(checksums[index]);
			payloadOffset = checked(payloadOffset + payloads[index].Length);
		}
		foreach (byte[] payload in payloads)
			writer.Write(payload);
		return stream.ToArray();
	}

	private static ColumnSection DecodeColumnSection(ReadOnlySpan<byte> payload)
	{
		using MemoryStream stream = new(payload.ToArray(), writable: false);
		using BinaryReader reader = new(stream);
		int count = ReadBoundedCount(reader, 1_000_000, "world column");
		long directoryEnd = checked(sizeof(int) + count * (long)ColumnDirectoryEntrySize);
		if (directoryEnd > payload.Length)
			throw new InvalidDataException("Voxel columns section directory is truncated.");
		ColumnArchiveEntry[] entries = new ColumnArchiveEntry[count];
		HashSet<(int X, int Z)> coordinates = new();
		for (int index = 0; index < count; index++)
		{
			ColumnArchiveEntry entry = new(
				reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt64(),
				reader.ReadInt64(), reader.ReadInt32(), reader.ReadUInt32());
			if (!coordinates.Add((entry.X, entry.Z)))
				throw new InvalidDataException($"Voxel columns section duplicates column ({entry.X}, {entry.Z}).");
			if (entry.Revision < 1 || entry.Offset < directoryEnd || entry.Offset > int.MaxValue ||
				entry.Length < 0 || entry.Offset + entry.Length > payload.Length)
				throw new InvalidDataException($"Voxel columns section has invalid bounds for column ({entry.X}, {entry.Z}).");
			entries[index] = entry;
		}
		ValidateNonOverlapping(entries.Select(static entry => (entry.Offset, (long)entry.Length, $"column ({entry.X}, {entry.Z})")));
		return new ColumnSection(entries);
	}

	private static Dictionary<uint, SectionEntry> ReadSectionDirectory(byte[] archive)
	{
		if (archive.Length < HeaderSize)
			throw new IncompatibleWorldArchiveException("World archive header is missing or truncated.");
		using MemoryStream stream = new(archive, writable: false);
		using BinaryReader reader = new(stream);
		uint magic = reader.ReadUInt32();
		ushort version = reader.ReadUInt16();
		if (magic != Magic || version != FormatVersion)
			throw new IncompatibleWorldArchiveException(
				$"Unsupported world archive magic=0x{magic:X8} version={version}; expected magic=0x{Magic:X8} version={FormatVersion}.");
		ushort headerFlags = reader.ReadUInt16();
		if (headerFlags != 0)
			throw new InvalidDataException($"World archive header has unsupported flags 0x{headerFlags:X4}.");
		uint sectionCount = reader.ReadUInt32();
		long directoryOffset = reader.ReadInt64();
		if (sectionCount > MaximumSections)
			throw new InvalidDataException($"World archive section count {sectionCount} exceeds {MaximumSections}.");
		long directoryLength = checked(sectionCount * (long)SectionDirectoryEntrySize);
		if (directoryOffset < HeaderSize || directoryOffset > archive.LongLength ||
			directoryLength > archive.LongLength - directoryOffset)
			throw new InvalidDataException("World archive section directory has invalid bounds.");

		stream.Position = directoryOffset;
		Dictionary<uint, SectionEntry> result = new();
		for (uint index = 0; index < sectionCount; index++)
		{
			SectionEntry entry = new(
				reader.ReadUInt32(), reader.ReadUInt16(), reader.ReadUInt16(),
				reader.ReadInt64(), reader.ReadInt64(), reader.ReadUInt32());
			if ((entry.Flags & ~MandatorySectionFlag) != 0)
				throw new InvalidDataException($"Archive section {entry.Id} has unsupported flags 0x{entry.Flags:X4}.");
			if (!result.TryAdd(entry.Id, entry))
				throw new InvalidDataException($"Archive section {entry.Id} is duplicated.");
			if (entry.Offset < HeaderSize || entry.ByteLength < 0 || entry.Offset > archive.LongLength ||
				entry.ByteLength > archive.LongLength - entry.Offset)
				throw new InvalidDataException($"Archive section {entry.Id} has invalid bounds.");
			long entryEnd = checked(entry.Offset + entry.ByteLength);
			long directoryEnd = checked(directoryOffset + directoryLength);
			if (entry.Offset < directoryEnd && entryEnd > directoryOffset)
				throw new InvalidDataException($"Archive section {entry.Id} overlaps the section directory.");
		}

		ValidateNonOverlapping(result.Values.Select(static entry =>
			(entry.Offset, entry.ByteLength, $"section {entry.Id}")));
		foreach (SectionEntry entry in result.Values)
		{
			ReadOnlySpan<byte> payload = GetSectionPayload(archive, entry);
			if (ComputeCrc32(payload) != entry.Checksum)
				throw new InvalidDataException($"Archive section {entry.Id} CRC32 mismatch.");
			bool supported = entry.Id switch
			{
				MetadataSectionId => entry.Version == MetadataSectionVersion,
				VoxelColumnsSectionId => entry.Version == VoxelColumnsSectionVersion,
				WorldObjectsSectionId => entry.Version == 1,
				PersistentFurnitureSectionId => entry.Version == 1,
				NpcLifeSectionId => entry.Version == 1,
				TombstonesSectionId => entry.Version == 1,
				_ => false,
			};
			if (!supported && (entry.Flags & MandatorySectionFlag) != 0)
				throw new InvalidDataException($"Mandatory archive section {entry.Id} has unsupported version {entry.Version}.");
		}
		return result;
	}

	private static SectionEntry GetRequiredSection(
		IReadOnlyDictionary<uint, SectionEntry> sections,
		uint id,
		ushort version,
		string description)
	{
		if (!sections.TryGetValue(id, out SectionEntry entry))
			throw new InvalidDataException($"Mandatory {description} section {id} is missing.");
		if ((entry.Flags & MandatorySectionFlag) == 0)
			throw new InvalidDataException($"Required {description} section {id} is not marked mandatory.");
		if (entry.Version != version)
			throw new InvalidDataException($"Mandatory {description} section {id} has unsupported version {entry.Version}.");
		return entry;
	}

	private static ReadOnlySpan<byte> GetSectionPayload(byte[] archive, SectionEntry entry) =>
		archive.AsSpan(checked((int)entry.Offset), checked((int)entry.ByteLength));

	private static void ValidateNonOverlapping(IEnumerable<(long Offset, long Length, string Name)> ranges)
	{
		(long Offset, long Length, string Name)[] ordered = ranges.OrderBy(static range => range.Offset).ToArray();
		long previousEnd = -1;
		string previousName = string.Empty;
		foreach ((long offset, long length, string name) in ordered)
		{
			if (offset < previousEnd)
				throw new InvalidDataException($"Archive {name} overlaps {previousName}.");
			previousEnd = checked(offset + length);
			previousName = name;
		}
	}

	private static uint ComputeCrc32(ReadOnlySpan<byte> payload)
	{
		uint crc = uint.MaxValue;
		foreach (byte value in payload)
		{
			crc ^= value;
			for (int bit = 0; bit < 8; bit++)
				crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
		}
		return ~crc;
	}

	private static void WriteVector3(BinaryWriter writer, Vector3 value)
	{
		writer.Write(value.X);
		writer.Write(value.Y);
		writer.Write(value.Z);
	}

	private static Vector3 ReadVector3(BinaryReader reader) =>
		new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());

	private static void WriteFeaturePlan(BinaryWriter writer, WorldFeaturePlan plan)
	{
		writer.Write(plan.Sites.Count);
		foreach (PlannedSite site in plan.Sites)
		{
			writer.Write(site.Id.Value);
			writer.Write((byte)site.Role);
			writer.Write(site.BlueprintId);
			WriteCoordinate(writer, site.Origin);
			writer.Write(site.Rotation);
			WriteBounds(writer, site.Reservation);
			writer.Write(site.EmergencyFallback);
			WriteBounds(writer, site.ModificationBounds);
			writer.Write(site.Markers.Length);
			foreach (PlannedMarker marker in site.Markers)
			{
				writer.Write(marker.Id.BlueprintMarkerId);
				writer.Write((byte)marker.Kind);
				WriteCoordinate(writer, marker.Position);
				writer.Write(marker.ExpectedBlock.HasValue);
				if (marker.ExpectedBlock.HasValue) writer.Write((ushort)marker.ExpectedBlock.Value);
				writer.Write(marker.Data ?? string.Empty);
			}
			writer.Write(site.Connectors.Length);
			foreach (PlannedConnector connector in site.Connectors)
			{
				writer.Write(connector.Id);
				writer.Write((byte)connector.Kind);
				WriteCoordinate(writer, connector.Position);
				WriteCoordinate(writer, connector.Direction);
			}
		}

		writer.Write(plan.Routes.Count);
		foreach (PlannedRoute route in plan.Routes)
		{
			writer.Write(route.Id);
			writer.Write((byte)route.Kind);
			writer.Write(route.SourceSite.Value);
			writer.Write(route.SourceConnector);
			writer.Write(route.DestinationSite.Value);
			writer.Write(route.DestinationConnector);
		}
	}

	private static WorldFeaturePlan ReadFeaturePlan(BinaryReader reader)
	{
		int siteCount = ReadBoundedCount(reader, 10_000, "generated site");
		PlannedSite[] sites = new PlannedSite[siteCount];
		for (int siteIndex = 0; siteIndex < siteCount; siteIndex++)
		{
			GeneratedSiteId id = new(reader.ReadString());
			StructureRole role = (StructureRole)reader.ReadByte();
			if(!Enum.IsDefined(role))throw new InvalidDataException("Generated site role is invalid.");
			string blueprintId = reader.ReadString();
			BlockCoordinate origin = ReadCoordinate(reader);
			int rotation = reader.ReadInt32();
			StructureBounds reservation = ReadBounds(reader);
			bool emergency = reader.ReadBoolean();
			StructureBounds modification = ReadBounds(reader);
			int markerCount = ReadBoundedCount(reader, 512, "generated marker");
			PlannedMarker[] markers = new PlannedMarker[markerCount];
			for (int markerIndex = 0; markerIndex < markerCount; markerIndex++)
			{
				string markerId = reader.ReadString();
				StructureMarkerKind kind = (StructureMarkerKind)reader.ReadByte();
				if(!Enum.IsDefined(kind))throw new InvalidDataException("Generated marker kind is invalid.");
				BlockCoordinate position = ReadCoordinate(reader);
				BlockType? expected = reader.ReadBoolean() ? (BlockType)reader.ReadUInt16() : null;
				if(expected.HasValue&&!Enum.IsDefined(expected.Value))throw new InvalidDataException("Generated marker expected block is invalid.");
				string data = reader.ReadString();
				markers[markerIndex] = new PlannedMarker(new GeneratedMarkerId(id, markerId), kind, position, expected, data);
			}
			int connectorCount = ReadBoundedCount(reader, 128, "generated connector");
			PlannedConnector[] connectors = new PlannedConnector[connectorCount];
			for (int connectorIndex = 0; connectorIndex < connectorCount; connectorIndex++)
			{
				string connectorId = reader.ReadString();
				StructureConnectorKind kind = (StructureConnectorKind)reader.ReadByte();
				if(!Enum.IsDefined(kind))throw new InvalidDataException("Generated connector kind is invalid.");
				connectors[connectorIndex] = new PlannedConnector(id, connectorId, kind, ReadCoordinate(reader), ReadCoordinate(reader));
			}
			sites[siteIndex] = new PlannedSite(id, role, blueprintId, origin, rotation, reservation, emergency, modification, markers, connectors);
		}

		int routeCount = ReadBoundedCount(reader, 100_000, "generated route");
		PlannedRoute[] routes = new PlannedRoute[routeCount];
		for (int index = 0; index < routeCount; index++)
		{
			routes[index] = new PlannedRoute(reader.ReadString(), (StructureConnectorKind)reader.ReadByte(),
				new GeneratedSiteId(reader.ReadString()), reader.ReadString(), new GeneratedSiteId(reader.ReadString()), reader.ReadString(), Array.Empty<BlockCoordinate>());
		}
		return new WorldFeaturePlan(sites, routes);
	}

	private static int ReadBoundedCount(BinaryReader reader, int maximum, string description)
	{
		int count = reader.ReadInt32();
		if (count < 0 || count > maximum)
			throw new InvalidDataException($"Invalid {description} count {count}.");
		return count;
	}

	private static void WriteMachineKey(BinaryWriter writer, MachineKey key)
	{
		WriteCoordinate(writer, key.FunctionCoordinate);
		writer.Write((byte)key.Function);
	}

	private static MachineKey ReadMachineKey(BinaryReader reader) =>
		new(ReadCoordinate(reader), (InfrastructureFunctionKind)reader.ReadByte());

	private static void WriteBounds(BinaryWriter writer, StructureBounds bounds)
	{
		WriteCoordinate(writer, bounds.Minimum);
		WriteCoordinate(writer, bounds.Maximum);
	}

	private static StructureBounds ReadBounds(BinaryReader reader) => new(ReadCoordinate(reader), ReadCoordinate(reader));

	private static void WriteCoordinate(BinaryWriter writer, BlockCoordinate coordinate)
	{
		writer.Write(coordinate.X);
		writer.Write(coordinate.Y);
		writer.Write(coordinate.Z);
	}

	private static BlockCoordinate ReadCoordinate(BinaryReader reader) =>
		new(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32());

	private readonly record struct ArchiveSection(uint Id, ushort Version, ushort Flags, byte[] Payload);
	private readonly record struct SectionEntry(
		uint Id,
		ushort Version,
		ushort Flags,
		long Offset,
		long ByteLength,
		uint Checksum);
	private readonly record struct ColumnArchiveEntry(
		int X,
		int Z,
		long Revision,
		long Offset,
		int Length,
		uint Checksum);
	private sealed record ColumnSection(ColumnArchiveEntry[] Entries);
}
