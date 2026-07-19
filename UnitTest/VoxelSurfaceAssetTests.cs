using System.Drawing;

namespace UnitTest;

public sealed class VoxelSurfaceAssetTests
{
	[Fact]
	public void DeployedVoxelSurfaceAtlasesExistAndShareDimensions()
	{
		string textureDirectory = Path.Combine(AppContext.BaseDirectory, "data", "textures");
		string[] names =
		{
			"atlas.png",
			"atlas_normal.png",
			"atlas_specular.png",
			"atlas_roughness.png",
		};

		foreach (string name in names)
		{
			string path = Path.Combine(textureDirectory, name);
			Assert.True(File.Exists(path), $"Missing voxel surface atlas: {path}");
			using Bitmap bitmap = new(path);
			Assert.Equal(512, bitmap.Width);
			Assert.Equal(512, bitmap.Height);
		}
	}
}
