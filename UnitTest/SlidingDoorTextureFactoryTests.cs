using FishGfx;
using Voxelgine.FishGfxClient.Entities;

namespace UnitTest;

public sealed class SlidingDoorTextureFactoryTests
{
	[Fact]
	public void RecreatesOpaqueLegacyWoodAtlas()
	{
		Color[] pixels = SlidingDoorTextureFactory.CreatePixels();

		Assert.Equal(64 * 64, pixels.Length);
		Assert.All(pixels, pixel => Assert.Equal(byte.MaxValue, pixel.A));
		Assert.Equal(new Color(101, 67, 33, 255), At(pixels, 0, 0));
		Assert.Equal(new Color(139, 90, 43, 255), At(pixels, 1, 1));
		Assert.Equal(new Color(160, 110, 60, 255), At(pixels, 2, 2));
		Assert.Equal(new Color(200, 170, 50, 255), At(pixels, 12, 15));
		Assert.Equal(new Color(160, 110, 60, 255), At(pixels, 20, 2));
		Assert.Equal(new Color(101, 67, 33, 255), At(pixels, 16, 10));
		Assert.Equal(new Color(101, 67, 33, 255), At(pixels, 34, 10));
		Assert.Equal(new Color(139, 90, 43, 255), At(pixels, 63, 63));
	}

	private static Color At(Color[] pixels, int x, int y)
	{
		return pixels[y * SlidingDoorTextureFactory.AtlasSize + x];
	}
}
