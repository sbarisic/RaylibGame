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
	HabitatMilestone Milestone = HabitatMilestone.None);

public readonly record struct PersistedMachineIntent(MachineKey Key, bool RequestedEnabled);

public sealed class WorldArchiveReadResult
{
	internal WorldArchiveReadResult(
		WorldArchiveMetadata metadata,
		ChunkColumnSnapshot[] columns,
		WorldArchivePayloadCache payloadCache)
	{
		Metadata = metadata;
		Columns = columns;
		PayloadCache = payloadCache;
	}

	public WorldArchiveMetadata Metadata { get; }
	public IReadOnlyList<ChunkColumnSnapshot> Columns { get; }
	public WorldArchivePayloadCache PayloadCache { get; }
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
	public const ushort FormatVersion = 5;
	private const int DirectoryEntrySize = 24;

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
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(output);
		ArgumentNullException.ThrowIfNull(map);
		if (!output.CanSeek)
			throw new ArgumentException("World archives require a seekable output stream.", nameof(output));

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

		using BinaryWriter writer = new(output, System.Text.Encoding.UTF8, leaveOpen: true);
		writer.Write(Magic);
		writer.Write(FormatVersion);
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
		writer.Write(columns.Length);

		long payloadOffset = output.Position + (long)DirectoryEntrySize * columns.Length;
		for (int index = 0; index < columns.Length; index++)
		{
			ChunkColumnCoordinate coordinate = coordinates[index];
			byte[] payload = payloads[index];
			writer.Write(coordinate.X);
			writer.Write(coordinate.Z);
			writer.Write(payloadOffset);
			writer.Write(payload.Length);
			writer.Write(checksums[index]);
			payloadOffset += payload.Length;
		}

		foreach (byte[] payload in payloads)
			writer.Write(payload);

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
		using MemoryStream stream = new(archive, writable: false);
		using BinaryReader reader = new(stream);
		if (stream.Length < sizeof(uint) + sizeof(ushort))
			throw new IncompatibleWorldArchiveException("World archive header is missing.");

		uint magic = reader.ReadUInt32();
		ushort version = reader.ReadUInt16();
		if (magic != Magic || version != FormatVersion)
		{
			throw new IncompatibleWorldArchiveException(
				$"Unsupported world archive magic=0x{magic:X8} version={version}; expected magic=0x{Magic:X8} version={FormatVersion}.");
		}

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
			throw new InvalidDataException($"Invalid habitat milestone {milestone}.");
		WorldArchiveMetadata metadata = new(worldSeed, playerSpawn, pickupSpawn, npcSpawn, features, intents, milestone);
		int count = reader.ReadInt32();
		if (count < 0 || count > 1_000_000)
			throw new InvalidDataException($"Invalid world column count {count}.");

		ArchiveEntry[] entries = new ArchiveEntry[count];
		for (int index = 0; index < count; index++)
		{
			ArchiveEntry entry = new(
				reader.ReadInt32(),
				reader.ReadInt32(),
				reader.ReadInt64(),
				reader.ReadInt32(),
				reader.ReadUInt32());
			if (entry.Offset < 0 || entry.Offset > int.MaxValue || entry.Length < 0 || entry.Offset + entry.Length > archive.LongLength)
				throw new InvalidDataException($"Column ({entry.X}, {entry.Z}) has invalid archive bounds.");
			entries[index] = entry;
		}

		ChunkColumnSnapshot[] columns = new ChunkColumnSnapshot[count];
		WorldArchivePayloadCache payloadCache = new();
		Parallel.For(
			0,
			count,
			new ParallelOptions { CancellationToken = cancellationToken },
			index =>
			{
				ArchiveEntry entry = entries[index];
				ReadOnlySpan<byte> payload = archive.AsSpan((int)entry.Offset, entry.Length);
				uint checksum = WorldColumnCodec.ComputeChecksum(payload);
				if (checksum != entry.Checksum)
					throw new InvalidDataException($"Checksum mismatch for column ({entry.X}, {entry.Z}).");
				byte[] retainedPayload = payload.ToArray();
				columns[index] = WorldColumnCodec.Decode(entry.X, entry.Z, revision: 1, retainedPayload);
				lock (payloadCache)
					payloadCache.Set(entry.X, entry.Z, 1, retainedPayload, checksum);
			});

		return new WorldArchiveReadResult(metadata, columns, payloadCache);
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
		using BinaryReader reader = new(input, System.Text.Encoding.UTF8, leaveOpen: true);
		if (reader.ReadUInt32() != Magic || reader.ReadUInt16() != FormatVersion)
			throw new IncompatibleWorldArchiveException("Unsupported world archive format.");
		reader.ReadInt32();
		for (int index = 0; index < 9; index++) reader.ReadSingle();
		ReadFeaturePlan(reader);
		int intentCount = ReadBoundedCount(reader, 100_000, "machine intent");
		for (int index = 0; index < intentCount; index++)
		{
			ReadMachineKey(reader);
			reader.ReadBoolean();
		}
		reader.ReadByte();
		int count = reader.ReadInt32();
		for (int index = 0; index < count; index++)
		{
			ArchiveEntry entry = new(
				reader.ReadInt32(),
				reader.ReadInt32(),
				reader.ReadInt64(),
				reader.ReadInt32(),
				reader.ReadUInt32());
			if (entry.X != columnX || entry.Z != columnZ)
				continue;
			if (entry.Offset < 0 || entry.Length < 0 || entry.Offset + entry.Length > input.Length)
				throw new InvalidDataException($"Column ({columnX}, {columnZ}) has invalid archive bounds.");
			input.Position = entry.Offset;
			byte[] payload = reader.ReadBytes(entry.Length);
			if (payload.Length != entry.Length || WorldColumnCodec.ComputeChecksum(payload) != entry.Checksum)
				throw new InvalidDataException($"Checksum mismatch for column ({columnX}, {columnZ}).");
			return WorldColumnCodec.Decode(columnX, columnZ, revision: 1, payload);
		}
		throw new KeyNotFoundException($"Column ({columnX}, {columnZ}) is not present in the archive.");
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
				BlockCoordinate position = ReadCoordinate(reader);
				BlockType? expected = reader.ReadBoolean() ? (BlockType)reader.ReadUInt16() : null;
				string data = reader.ReadString();
				markers[markerIndex] = new PlannedMarker(new GeneratedMarkerId(id, markerId), kind, position, expected, data);
			}
			int connectorCount = ReadBoundedCount(reader, 128, "generated connector");
			PlannedConnector[] connectors = new PlannedConnector[connectorCount];
			for (int connectorIndex = 0; connectorIndex < connectorCount; connectorIndex++)
			{
				string connectorId = reader.ReadString();
				StructureConnectorKind kind = (StructureConnectorKind)reader.ReadByte();
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

	private readonly record struct ArchiveEntry(int X, int Z, long Offset, int Length, uint Checksum);
}
