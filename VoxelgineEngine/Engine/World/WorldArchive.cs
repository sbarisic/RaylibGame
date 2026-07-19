using System;
using System.IO;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;

namespace Voxelgine.Graphics;

public readonly record struct WorldArchiveMetadata(
	int WorldSeed,
	Vector3 PlayerSpawn,
	Vector3 PickupSpawn,
	Vector3 NpcSpawn);

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
	public const ushort FormatVersion = 1;
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

		WorldArchiveMetadata metadata = new(
			reader.ReadInt32(),
			ReadVector3(reader),
			ReadVector3(reader),
			ReadVector3(reader));
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
		for (int index = 0; index < 9; index++)
			reader.ReadSingle();
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

	private readonly record struct ArchiveEntry(int X, int Z, long Offset, int Length, uint Checksum);
}
