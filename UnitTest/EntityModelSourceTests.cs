using System.Drawing;
using Voxelgine.FishGfxClient.Entities;

namespace UnitTest;

public sealed class EntityModelSourceTests
{
	[Fact]
	public void HumanoidLogicalUvsAreConvertedToFishGfxOrientation()
	{
		EntityModelSource source = LoadHumanoid();
		EntityModelPartSource body = Assert.Single(source.Parts, part => part.Name == "body");

		Assert.Equal(11f / 16f, body.Vertices[0].UV.X, 5);
		Assert.Equal(1f, body.Vertices[0].UV.Y, 5);
		Assert.All(source.Parts.SelectMany(part => part.Vertices), vertex =>
		{
			Assert.InRange(vertex.UV.X, 0, 1);
			Assert.InRange(vertex.UV.Y, 0, 1);
		});
	}

	[Theory]
	[InlineData("humanoid.png")]
	[InlineData("humanoid2.png")]
	public void HumanoidFacesSampleOpaqueSkinPixels(string textureFileName)
	{
		EntityModelSource source = LoadHumanoid();
		string texturePath = Path.Combine(
			AppContext.BaseDirectory,
			"data",
			"textures",
			"npc",
			textureFileName
		);
		using Bitmap bitmap = new(texturePath);

		foreach (EntityModelPartSource part in source.Parts)
		{
			for (int vertexIndex = 0; vertexIndex < part.Vertices.Length; vertexIndex += 3)
			{
				System.Numerics.Vector2 uv = (
					part.Vertices[vertexIndex].UV
					+ part.Vertices[vertexIndex + 1].UV
					+ part.Vertices[vertexIndex + 2].UV
				) / 3;
				int x = Math.Clamp((int)(uv.X * bitmap.Width), 0, bitmap.Width - 1);
				int y = Math.Clamp((int)((1 - uv.Y) * bitmap.Height), 0, bitmap.Height - 1);
				Color sampled = bitmap.GetPixel(x, y);

				Assert.True(
					sampled.A > 0,
					$"{textureFileName} part {part.Name} sampled transparent pixel ({x}, {y})."
				);
			}
		}
	}

	private static EntityModelSource LoadHumanoid()
	{
		return EntityModelSource.LoadBlockModel(Path.Combine(
			AppContext.BaseDirectory,
			"data",
			"models",
			"npc",
			"humanoid.json"
		));
	}
}
