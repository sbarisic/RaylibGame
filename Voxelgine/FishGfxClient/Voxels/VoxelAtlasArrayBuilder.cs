#if WINDOWS
using System.Drawing;
using System.Drawing.Imaging;
using System.Numerics;
using System.Runtime.InteropServices;
using FishGfx.Graphics;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace Voxelgine.FishGfxClient.Voxels;

internal enum VoxelAtlasMipKind
{
	BaseColor,
	Normal,
	Linear,
}

internal static class VoxelAtlasArrayBuilder
{
	internal static Texture CreatePackedSurfaceMaps(
		GraphicsContext graphics,
		Bitmap normalAtlas,
		Bitmap specularAtlas,
		Bitmap roughnessAtlas,
		int columns,
		int rows,
		out int[] layerInfo
	)
	{
		ArgumentNullException.ThrowIfNull(graphics);
		ArgumentNullException.ThrowIfNull(normalAtlas);
		ArgumentNullException.ThrowIfNull(specularAtlas);
		ArgumentNullException.ThrowIfNull(roughnessAtlas);

		if (normalAtlas.Size != specularAtlas.Size
			|| normalAtlas.Size != roughnessAtlas.Size)
		{
			throw new ArgumentException("Packed voxel surface atlases must have identical dimensions.");
		}

		ValidateGrid(normalAtlas, columns, rows, out int tileWidth, out int tileHeight);
		int layers = checked(columns * rows);
		int mipLevels = 1 + (int)MathF.Log2(tileWidth);
		Texture texture = graphics.CreateTexture(new TextureDescriptor(
			tileWidth,
			tileHeight,
			TextureFormat.RGBA8Unorm,
			TextureUsageFlags.Sampled | TextureUsageFlags.TransferDestination,
			TextureDimension.Texture2DArray,
			mipLevels,
			sampling: new TextureSamplingState(
				TextureFilter.NearestMipmapLinear,
				TextureFilter.Nearest,
				TextureWrap.ClampToEdge,
				TextureWrap.ClampToEdge),
			arrayLayers: layers));

		try
		{
			byte[] normal = SliceLevelZero(
				ReadRgba(normalAtlas), normalAtlas.Width, columns, rows, tileWidth, tileHeight);
			byte[] specular = SliceLevelZero(
				ReadRgba(specularAtlas), specularAtlas.Width, columns, rows, tileWidth, tileHeight);
			byte[] roughness = SliceLevelZero(
				ReadRgba(roughnessAtlas), roughnessAtlas.Width, columns, rows, tileWidth, tileHeight);
			layerInfo = BuildLayerInfo(normal, specular, layers, tileWidth * tileHeight);
			int width = tileWidth;
			int height = tileHeight;

			for (int mip = 0; mip < mipLevels; mip++)
			{
				byte[] packed = PackSurfaceLevel(normal, specular, roughness);
				texture.Write(
					packed,
					TextureDataFormat.RGBA8Unorm,
					new TextureArrayRegion(0, 0, 0, width, height, layers),
					mip);

				if (mip + 1 < mipLevels)
				{
					normal = Downsample(normal, width, height, layers, VoxelAtlasMipKind.Normal, null);
					specular = Downsample(specular, width, height, layers, VoxelAtlasMipKind.Linear, null);
					roughness = Downsample(roughness, width, height, layers, VoxelAtlasMipKind.Linear, null);
					width = Math.Max(1, width / 2);
					height = Math.Max(1, height / 2);
				}
			}

			return texture;
		}
		catch
		{
			texture.Dispose();
			throw;
		}
	}

	internal static int[] BuildLayerInfo(
		ReadOnlySpan<byte> normal,
		ReadOnlySpan<byte> specular,
		int layers,
		int pixelsPerLayer)
	{
		int layerStride = checked(pixelsPerLayer * 4);
		if (layers <= 0
			|| pixelsPerLayer <= 0
			|| normal.Length != checked(layers * layerStride)
			|| specular.Length != normal.Length)
		{
			throw new ArgumentException("Voxel layer-info source data is invalid.");
		}

		int[] result = new int[layers];
		for (int layer = 0; layer < layers; layer++)
		{
			bool flatNormal = true;
			bool zeroSpecular = true;
			int end = (layer + 1) * layerStride;
			for (int offset = layer * layerStride; offset < end; offset += 4)
			{
				flatNormal &= Math.Abs(normal[offset] - 128) <= 1
					&& Math.Abs(normal[offset + 1] - 128) <= 1;
				zeroSpecular &= specular[offset] == 0;
			}

			result[layer] = (flatNormal ? 1 : 0) | (zeroSpecular ? 2 : 0);
		}
		return result;
	}

	internal static Texture Create(
		GraphicsContext graphics,
		Bitmap atlas,
		int columns,
		int rows,
		TextureFormat format,
		VoxelAtlasMipKind mipKind,
		IReadOnlyDictionary<int, float> alphaCutoffs = null
	)
	{
		ArgumentNullException.ThrowIfNull(graphics);
		ArgumentNullException.ThrowIfNull(atlas);

		ValidateGrid(atlas, columns, rows, out int tileWidth, out int tileHeight);

		int layers = checked(columns * rows);
		int mipLevels = 1 + (int)MathF.Log2(tileWidth);
		TextureSamplingState sampling = new(
			TextureFilter.NearestMipmapLinear,
			TextureFilter.Nearest,
			TextureWrap.ClampToEdge,
			TextureWrap.ClampToEdge
		);
		Texture texture = graphics.CreateTexture(new TextureDescriptor(
			tileWidth,
			tileHeight,
			format,
			TextureUsageFlags.Sampled | TextureUsageFlags.TransferDestination,
			TextureDimension.Texture2DArray,
			mipLevels,
			sampling: sampling,
			arrayLayers: layers
		));

		try
		{
			byte[] atlasPixels = ReadRgba(atlas);
			byte[] level = SliceLevelZero(
				atlasPixels,
				atlas.Width,
				columns,
				rows,
				tileWidth,
				tileHeight
			);
			int width = tileWidth;
			int height = tileHeight;

			for (int mip = 0; mip < mipLevels; mip++)
			{
				texture.Write(
					level,
					TextureDataFormat.RGBA8Unorm,
					new TextureArrayRegion(0, 0, 0, width, height, layers),
					mip
				);

				if (mip + 1 < mipLevels)
				{
					level = Downsample(
						level,
						width,
						height,
						layers,
						mipKind,
						alphaCutoffs
					);
					width = Math.Max(1, width / 2);
					height = Math.Max(1, height / 2);
				}
			}

			return texture;
		}
		catch
		{
			texture.Dispose();
			throw;
		}
	}

	private static void ValidateGrid(
		Bitmap atlas,
		int columns,
		int rows,
		out int tileWidth,
		out int tileHeight)
	{
		if (columns <= 0 || rows <= 0
			|| atlas.Width % columns != 0
			|| atlas.Height % rows != 0)
		{
			throw new ArgumentException("The atlas must divide evenly into its tile grid.");
		}

		tileWidth = atlas.Width / columns;
		tileHeight = atlas.Height / rows;

		if (tileWidth != tileHeight || (tileWidth & (tileWidth - 1)) != 0)
		{
			throw new ArgumentException("Voxel texture-array tiles must be square powers of two.");
		}
	}

	internal static byte[] PackSurfaceLevel(
		ReadOnlySpan<byte> normal,
		ReadOnlySpan<byte> specular,
		ReadOnlySpan<byte> roughness)
	{
		if (normal.Length != specular.Length
			|| normal.Length != roughness.Length
			|| normal.Length % 4 != 0)
		{
			throw new ArgumentException("Packed voxel surface levels must have matching RGBA data.");
		}

		byte[] packed = new byte[normal.Length];

		for (int offset = 0; offset < packed.Length; offset += 4)
		{
			packed[offset] = normal[offset];
			packed[offset + 1] = normal[offset + 1];
			packed[offset + 2] = specular[offset];
			packed[offset + 3] = roughness[offset];
		}

		return packed;
	}

	private static byte[] ReadRgba(Bitmap source)
	{
		Rectangle rectangle = new(0, 0, source.Width, source.Height);
		using Bitmap converted = source.Clone(rectangle, PixelFormat.Format32bppArgb);
		BitmapData data = converted.LockBits(
			rectangle,
			ImageLockMode.ReadOnly,
			PixelFormat.Format32bppArgb
		);

		try
		{
			byte[] rgba = new byte[checked(source.Width * source.Height * 4)];
			byte[] row = new byte[checked(source.Width * 4)];

			for (int y = 0; y < source.Height; y++)
			{
				Marshal.Copy(IntPtr.Add(data.Scan0, y * data.Stride), row, 0, row.Length);
				int destination = y * row.Length;

				for (int x = 0; x < source.Width; x++)
				{
					int sourcePixel = x * 4;
					int destinationPixel = destination + sourcePixel;
					rgba[destinationPixel] = row[sourcePixel + 2];
					rgba[destinationPixel + 1] = row[sourcePixel + 1];
					rgba[destinationPixel + 2] = row[sourcePixel];
					rgba[destinationPixel + 3] = row[sourcePixel + 3];
				}
			}

			return rgba;
		}
		finally
		{
			converted.UnlockBits(data);
		}
	}

	internal static byte[] SliceLevelZero(
		byte[] atlas,
		int atlasWidth,
		int columns,
		int rows,
		int tileWidth,
		int tileHeight
	)
	{
		byte[] result = new byte[checked(columns * rows * tileWidth * tileHeight * 4)];
		int layerStride = tileWidth * tileHeight * 4;

		for (int tileRow = 0; tileRow < rows; tileRow++)
		{
			for (int tileColumn = 0; tileColumn < columns; tileColumn++)
			{
				int layer = tileRow * columns + tileColumn;

				for (int y = 0; y < tileHeight; y++)
				{
					int source = ((tileRow * tileHeight + y) * atlasWidth
						+ tileColumn * tileWidth) * 4;
					int destination = layer * layerStride
						+ (tileHeight - 1 - y) * tileWidth * 4;
					Buffer.BlockCopy(atlas, source, result, destination, tileWidth * 4);
				}
			}
		}

		return result;
	}

	internal static byte[] Downsample(
		byte[] source,
		int width,
		int height,
		int layers,
		VoxelAtlasMipKind kind,
		IReadOnlyDictionary<int, float> alphaCutoffs
	)
	{
		int nextWidth = Math.Max(1, width / 2);
		int nextHeight = Math.Max(1, height / 2);
		int sourceLayerStride = width * height * 4;
		int destinationLayerStride = nextWidth * nextHeight * 4;
		byte[] destination = new byte[checked(destinationLayerStride * layers)];
		Span<int> offsets = stackalloc int[4];

		for (int layer = 0; layer < layers; layer++)
		{
			for (int y = 0; y < nextHeight; y++)
			{
				for (int x = 0; x < nextWidth; x++)
				{
					int offsetCount = GatherOffsets(
						offsets,
						layer * sourceLayerStride,
						width,
						height,
						x,
						y
					);
					int output = layer * destinationLayerStride + (y * nextWidth + x) * 4;
					WriteAverage(source, offsets[..offsetCount], destination, output, kind);
				}
			}

			if (kind == VoxelAtlasMipKind.BaseColor
				&& alphaCutoffs != null
				&& alphaCutoffs.TryGetValue(layer, out float cutoff))
			{
				PreserveAlphaCoverage(
					source.AsSpan(layer * sourceLayerStride, sourceLayerStride),
					destination.AsSpan(layer * destinationLayerStride, destinationLayerStride),
					cutoff
				);
			}
		}

		return destination;
	}

	private static int GatherOffsets(
		Span<int> offsets,
		int layerOffset,
		int width,
		int height,
		int destinationX,
		int destinationY
	)
	{
		int count = 0;

		for (int y = 0; y < 2; y++)
		{
			int sourceY = Math.Min(height - 1, destinationY * 2 + y);

			for (int x = 0; x < 2; x++)
			{
				int sourceX = Math.Min(width - 1, destinationX * 2 + x);
				offsets[count++] = layerOffset + (sourceY * width + sourceX) * 4;
			}
		}

		return count;
	}

	private static void WriteAverage(
		byte[] source,
		ReadOnlySpan<int> offsets,
		byte[] destination,
		int output,
		VoxelAtlasMipKind kind
	)
	{
		if (kind == VoxelAtlasMipKind.Normal)
		{
			Vector3 vector = Vector3.Zero;

			foreach (int offset in offsets)
			{
				vector += new Vector3(
					source[offset] / 127.5f - 1,
					source[offset + 1] / 127.5f - 1,
					source[offset + 2] / 127.5f - 1
				);
			}

			vector = vector.LengthSquared() > 0.000001f
				? Vector3.Normalize(vector)
				: Vector3.UnitZ;
			destination[output] = EncodeNormal(vector.X);
			destination[output + 1] = EncodeNormal(vector.Y);
			destination[output + 2] = EncodeNormal(vector.Z);
			destination[output + 3] = AverageChannel(source, offsets, 3);
			return;
		}

		for (int channel = 0; channel < 4; channel++)
		{
			if (kind == VoxelAtlasMipKind.BaseColor && channel < 3)
			{
				float linear = 0;

				foreach (int offset in offsets)
				{
					linear += SrgbToLinear(source[offset + channel] / 255f);
				}

				destination[output + channel] = ToByte(
					LinearToSrgb(linear / offsets.Length)
				);
			}
			else
			{
				destination[output + channel] = AverageChannel(source, offsets, channel);
			}
		}
	}

	private static byte AverageChannel(byte[] source, ReadOnlySpan<int> offsets, int channel)
	{
		int total = 0;

		foreach (int offset in offsets)
		{
			total += source[offset + channel];
		}

		return (byte)((total + offsets.Length / 2) / offsets.Length);
	}

	private static void PreserveAlphaCoverage(
		ReadOnlySpan<byte> source,
		Span<byte> destination,
		float cutoff
	)
	{
		byte threshold = ToByte(cutoff);
		float target = Coverage(source, threshold, 1);
		float low = 0;
		float high = 4;

		for (int iteration = 0; iteration < 12; iteration++)
		{
			float middle = (low + high) * 0.5f;

			if (Coverage(destination, threshold, middle) < target)
			{
				low = middle;
			}
			else
			{
				high = middle;
			}
		}

		float scale = (low + high) * 0.5f;

		for (int index = 3; index < destination.Length; index += 4)
		{
			destination[index] = (byte)Math.Clamp(
				(int)MathF.Round(destination[index] * scale),
				0,
				255
			);
		}
	}

	private static float Coverage(ReadOnlySpan<byte> pixels, byte threshold, float scale)
	{
		int covered = 0;
		int count = pixels.Length / 4;

		for (int index = 3; index < pixels.Length; index += 4)
		{
			if (pixels[index] * scale >= threshold)
			{
				covered++;
			}
		}

		return count == 0 ? 0 : covered / (float)count;
	}

	private static byte EncodeNormal(float value)
	{
		return ToByte(value * 0.5f + 0.5f);
	}

	private static byte ToByte(float value)
	{
		return (byte)Math.Clamp((int)MathF.Round(value * 255), 0, 255);
	}

	private static float SrgbToLinear(float value)
	{
		return value <= 0.04045f
			? value / 12.92f
			: MathF.Pow((value + 0.055f) / 1.055f, 2.4f);
	}

	private static float LinearToSrgb(float value)
	{
		return value <= 0.0031308f
			? value * 12.92f
			: 1.055f * MathF.Pow(value, 1 / 2.4f) - 0.055f;
	}
}
#endif
