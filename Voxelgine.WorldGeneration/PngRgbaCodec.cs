using System.Buffers.Binary;
using System.IO.Compression;

namespace Voxelgine.WorldGeneration;

public static class PngRgbaCodec
{
	private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

	public static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgba)
	{
		if (width <= 0 || height <= 0 || rgba.Length != checked(width * height * 4)) throw new ArgumentException("Invalid RGBA raster.");
		using MemoryStream output = new();
		output.Write(Signature);
		Span<byte> header = stackalloc byte[13];
		BinaryPrimitives.WriteInt32BigEndian(header, width);
		BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
		header[8] = 8; header[9] = 6; // RGBA8
		WriteChunk(output, "IHDR"u8, header);
		using MemoryStream compressed = new();
		using (ZLibStream zlib = new(compressed, CompressionLevel.Optimal, leaveOpen: true))
		{
			int stride = width * 4;
			for (int y = 0; y < height; y++)
			{
				zlib.WriteByte(0);
				zlib.Write(rgba.Slice(y * stride, stride));
			}
		}
		WriteChunk(output, "IDAT"u8, compressed.ToArray());
		WriteChunk(output, "IEND"u8, []);
		return output.ToArray();
	}

	public static (int Width, int Height, byte[] Pixels) Decode(ReadOnlySpan<byte> bytes)
	{
		if (bytes.Length < Signature.Length || !bytes[..8].SequenceEqual(Signature)) throw new InvalidDataException("Invalid PNG signature.");
		int offset = 8, width = 0, height = 0;
		using MemoryStream idat = new();
		bool sawHeader = false, sawEnd = false;
		while (offset < bytes.Length)
		{
			if (bytes.Length - offset < 12) throw new InvalidDataException("Truncated PNG chunk.");
			int length = BinaryPrimitives.ReadInt32BigEndian(bytes.Slice(offset, 4)); offset += 4;
			if (length < 0 || bytes.Length - offset < length + 8) throw new InvalidDataException("Invalid PNG chunk length.");
			ReadOnlySpan<byte> type = bytes.Slice(offset, 4); offset += 4;
			ReadOnlySpan<byte> data = bytes.Slice(offset, length); offset += length;
			uint expected = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(offset, 4)); offset += 4;
			byte[] crcInput = new byte[4 + length]; type.CopyTo(crcInput); data.CopyTo(crcInput.AsSpan(4));
			if (Crc32(crcInput) != expected) throw new InvalidDataException("PNG chunk checksum mismatch.");
			if (type.SequenceEqual("IHDR"u8))
			{
				if (sawHeader || length != 13) throw new InvalidDataException("Invalid PNG header.");
				width = BinaryPrimitives.ReadInt32BigEndian(data); height = BinaryPrimitives.ReadInt32BigEndian(data[4..]);
				if (width <= 0 || height <= 0 || data[8] != 8 || data[9] != 6 || data[10] != 0 || data[11] != 0 || data[12] != 0)
					throw new InvalidDataException("World-plan PNGs must be non-interlaced RGBA8.");
				sawHeader = true;
			}
			else if (type.SequenceEqual("IDAT"u8)) idat.Write(data);
			else if (type.SequenceEqual("IEND"u8)) { sawEnd = true; break; }
		}
		if (!sawHeader || !sawEnd) throw new InvalidDataException("Incomplete PNG.");
		int stride = checked(width * 4), expectedRaw = checked((stride + 1) * height);
		byte[] raw = new byte[expectedRaw];
		idat.Position = 0;
		using (ZLibStream zlib = new(idat, CompressionMode.Decompress))
		{
			int read = 0;
			while (read < raw.Length) { int count = zlib.Read(raw, read, raw.Length - read); if (count == 0) break; read += count; }
			if (read != raw.Length || zlib.ReadByte() != -1) throw new InvalidDataException("PNG payload length mismatch.");
		}
		byte[] pixels = new byte[checked(stride * height)];
		for (int y = 0; y < height; y++)
		{
			int source = y * (stride + 1), destination = y * stride;
			byte filter = raw[source++];
			for (int x = 0; x < stride; x++)
			{
				byte left = x >= 4 ? pixels[destination + x - 4] : (byte)0;
				byte above = y > 0 ? pixels[destination + x - stride] : (byte)0;
				byte upperLeft = y > 0 && x >= 4 ? pixels[destination + x - stride - 4] : (byte)0;
				pixels[destination + x] = filter switch
				{
					0 => raw[source + x], 1 => unchecked((byte)(raw[source + x] + left)),
					2 => unchecked((byte)(raw[source + x] + above)),
					3 => unchecked((byte)(raw[source + x] + ((left + above) >> 1))),
					4 => unchecked((byte)(raw[source + x] + Paeth(left, above, upperLeft))),
					_ => throw new InvalidDataException($"Unsupported PNG filter {filter}.")
				};
			}
		}
		return (width, height, pixels);
	}

	internal static uint Crc32(ReadOnlySpan<byte> data)
	{
		uint crc = 0xffffffff;
		foreach (byte value in data)
		{
			crc ^= value;
			for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ (0xedb88320u & unchecked((uint)-(int)(crc & 1)));
		}
		return ~crc;
	}

	private static void WriteChunk(Stream output, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
	{
		Span<byte> length = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(length, data.Length); output.Write(length);
		output.Write(type); output.Write(data);
		byte[] crcInput = new byte[4 + data.Length]; type.CopyTo(crcInput); data.CopyTo(crcInput.AsSpan(4));
		Span<byte> crc = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(crcInput)); output.Write(crc);
	}

	private static byte Paeth(byte a, byte b, byte c)
	{
		int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
		return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
	}
}
