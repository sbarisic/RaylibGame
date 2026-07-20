using Voxelgine.FishGfxClient.Voxels;

namespace UnitTest;

public sealed class VoxelAtlasArrayBuilderTests
{
	[Fact]
	public void PackedSurfaceLevelUsesNormalXySpecularAndRoughness()
	{
		byte[] normal = { 10, 20, 30, 40, 50, 60, 70, 80 };
		byte[] specular = { 90, 1, 2, 3, 100, 4, 5, 6 };
		byte[] roughness = { 110, 7, 8, 9, 120, 10, 11, 12 };

		byte[] packed = VoxelAtlasArrayBuilder.PackSurfaceLevel(
			normal, specular, roughness);

		Assert.Equal(new byte[] { 10, 20, 90, 110, 50, 60, 100, 120 }, packed);
	}

	[Fact]
	public void LayerInfoFindsNeutralNormalAndZeroSpecularLayers()
	{
		byte[] normal =
		{
			128, 128, 255, 255,
			128, 128, 255, 255,
			100, 128, 250, 255,
			128, 128, 255, 255,
		};
		byte[] specular =
		{
			0, 0, 0, 255,
			0, 0, 0, 255,
			0, 0, 0, 255,
			8, 0, 0, 255,
		};

		int[] flags = VoxelAtlasArrayBuilder.BuildLayerInfo(normal, specular, 2, 2);

		Assert.Equal(3, flags[0]);
		Assert.Equal(0, flags[1]);
	}

	[Fact]
	public void SliceLevelZeroUsesRowMajorLayersAndFlipsEachTileForOpenGl()
	{
		byte[] atlas = new byte[4 * 4 * 4];

		for (int y = 0; y < 4; y++)
		{
			for (int x = 0; x < 4; x++)
			{
				int offset = (y * 4 + x) * 4;
				atlas[offset] = (byte)(10 * y + x);
				atlas[offset + 3] = 255;
			}
		}

		byte[] layers = VoxelAtlasArrayBuilder.SliceLevelZero(
			atlas,
			4,
			2,
			2,
			2,
			2
		);

		Assert.Equal(10, layers[0]);
		Assert.Equal(11, layers[4]);
		Assert.Equal(0, layers[8]);
		Assert.Equal(1, layers[12]);
		Assert.Equal(12, layers[16]);
		Assert.Equal(13, layers[20]);
		Assert.Equal(32, layers[48]);
	}

	[Fact]
	public void BaseColorMipAveragesRgbInLinearLight()
	{
		byte[] source =
		{
			0, 0, 0, 255,
			255, 255, 255, 255,
			0, 0, 0, 255,
			255, 255, 255, 255,
		};

		byte[] mip = VoxelAtlasArrayBuilder.Downsample(
			source,
			2,
			2,
			1,
			VoxelAtlasMipKind.BaseColor,
			null
		);

		Assert.InRange(mip[0], 187, 188);
		Assert.Equal(mip[0], mip[1]);
		Assert.Equal(mip[0], mip[2]);
		Assert.Equal(255, mip[3]);
	}

	[Fact]
	public void NormalMipRenormalizesTheAveragedVector()
	{
		byte[] source =
		{
			255, 128, 128, 255,
			128, 255, 128, 255,
			255, 128, 128, 255,
			128, 255, 128, 255,
		};

		byte[] mip = VoxelAtlasArrayBuilder.Downsample(
			source,
			2,
			2,
			1,
			VoxelAtlasMipKind.Normal,
			null
		);

		Assert.InRange(mip[0], 217, 218);
		Assert.InRange(mip[1], 217, 218);
		Assert.InRange(mip[2], 127, 129);
		Assert.Equal(255, mip[3]);
	}

	[Fact]
	public void ScalarMipUsesLinearChannelAverages()
	{
		byte[] source =
		{
			0, 4, 8, 12,
			100, 104, 108, 112,
			200, 204, 208, 212,
			255, 255, 255, 255,
		};

		byte[] mip = VoxelAtlasArrayBuilder.Downsample(
			source,
			2,
			2,
			1,
			VoxelAtlasMipKind.Linear,
			null
		);

		Assert.Equal(new byte[] { 139, 142, 145, 148 }, mip);
	}

	[Fact]
	public void AlphaTestMipPreservesCoverageAtTheConfiguredCutoff()
	{
		byte[] source = new byte[4 * 4 * 4];

		for (int pixel = 0; pixel < 16; pixel++)
		{
			source[pixel * 4 + 3] = pixel < 8 ? byte.MaxValue : (byte)0;
		}

		byte[] mip = VoxelAtlasArrayBuilder.Downsample(
			source,
			4,
			4,
			1,
			VoxelAtlasMipKind.BaseColor,
			new Dictionary<int, float> { [0] = 0.5f }
		);

		int sourceCoverage = source.Where((_, index) => index % 4 == 3)
			.Count(alpha => alpha >= 128);
		int mipCoverage = mip.Where((_, index) => index % 4 == 3)
			.Count(alpha => alpha >= 128);
		Assert.Equal(8, sourceCoverage);
		Assert.Equal(2, mipCoverage);
	}
}
