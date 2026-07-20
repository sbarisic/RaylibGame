#if WINDOWS
using FishGfx;
using FishGfx.Graphics;

namespace Voxelgine.FishGfxClient.Entities;

internal static class SlidingDoorTextureFactory
{
	internal const int AtlasSize = 64;
	private static readonly Color WoodBase = new(139, 90, 43, 255);
	private static readonly Color WoodDark = new(101, 67, 33, 255);
	private static readonly Color WoodLight = new(160, 110, 60, 255);
	private static readonly Color Handle = new(200, 170, 50, 255);

	public static Texture Create(GraphicsContext graphics)
	{
		ArgumentNullException.ThrowIfNull(graphics);
		Color[] pixels = CreatePixels();
		FlipVertically(pixels);

		Texture texture = graphics.CreateTexture(new TextureDescriptor(
			AtlasSize,
			AtlasSize,
			TextureFormat.SRGB8Alpha8
		));
		try
		{
			texture.Write(pixels, TextureDataFormat.RGBA8Unorm);
			return texture;
		}
		catch
		{
			texture.Dispose();
			throw;
		}
	}

	internal static Color[] CreatePixels()
	{
		Color[] pixels = new Color[AtlasSize * AtlasSize];
		Array.Fill(pixels, WoodBase);

		DrawRectangle(pixels, 0, 0, 16, 1, WoodDark);
		DrawRectangle(pixels, 0, 31, 16, 1, WoodDark);
		DrawRectangle(pixels, 0, 0, 1, 32, WoodDark);
		DrawRectangle(pixels, 15, 0, 1, 32, WoodDark);
		DrawRectangle(pixels, 0, 15, 16, 2, WoodDark);
		DrawRectangle(pixels, 2, 2, 12, 12, WoodLight);
		DrawRectangle(pixels, 2, 18, 12, 12, WoodLight);
		DrawRectangle(pixels, 12, 14, 2, 3, Handle);

		DrawRectangle(pixels, 18, 0, 16, 1, WoodDark);
		DrawRectangle(pixels, 18, 31, 16, 1, WoodDark);
		DrawRectangle(pixels, 18, 0, 1, 32, WoodDark);
		DrawRectangle(pixels, 33, 0, 1, 32, WoodDark);
		DrawRectangle(pixels, 18, 15, 16, 2, WoodDark);
		DrawRectangle(pixels, 20, 2, 12, 12, WoodLight);
		DrawRectangle(pixels, 20, 18, 12, 12, WoodLight);

		DrawRectangle(pixels, 16, 0, 2, 32, WoodDark);
		DrawRectangle(pixels, 34, 0, 2, 32, WoodDark);

		return pixels;
	}

	private static void DrawRectangle(
		Color[] pixels,
		int x,
		int y,
		int width,
		int height,
		Color color
	)
	{
		for (int row = y; row < y + height; row++)
		{
			for (int column = x; column < x + width; column++)
			{
				pixels[row * AtlasSize + column] = color;
			}
		}
	}

	private static void FlipVertically(Color[] pixels)
	{
		for (int top = 0; top < AtlasSize / 2; top++)
		{
			int bottom = AtlasSize - top - 1;
			for (int column = 0; column < AtlasSize; column++)
			{
				int topIndex = top * AtlasSize + column;
				int bottomIndex = bottom * AtlasSize + column;
				(pixels[topIndex], pixels[bottomIndex]) = (pixels[bottomIndex], pixels[topIndex]);
			}
		}
	}
}
#endif
