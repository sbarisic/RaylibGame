using System.Numerics;
using FishGfx.Voxels;
using Voxelgine.Engine;
using Voxelgine.FishGfxClient.Voxels;
using Voxelgine.Graphics;

namespace UnitTest;

public sealed class StairVoxelModelBuilderTests
{
	[Theory]
	[InlineData(BlockType.StoneStairs, 0)]
	[InlineData(BlockType.WoodStairs, 5)]
	[InlineData(BlockType.ConcreteStairs, 55)]
	public void EveryOrientationAndVerticalVariantUsesBoundaryFacesLocalUvsAndTangents(
		BlockType type,
		int tile)
	{
		for (byte state = 0; state < 8; state++)
		{
			VoxelModel model = StairVoxelModelBuilder.Create(new BlockValue(type, state), tile);
			Assert.Equal(22 * 6, model.Vertices.Count);
			Assert.All(model.Vertices, vertex =>
			{
				Assert.Equal(tile, vertex.TextureLayer);
				Assert.InRange(vertex.Position.X, 0, 1);
				Assert.InRange(vertex.Position.Y, 0, 1);
				Assert.InRange(vertex.Position.Z, 0, 1);
				Assert.InRange(vertex.TextureCoordinates.X, 0, 1);
				Assert.InRange(vertex.TextureCoordinates.Y, 0, 1);
				Assert.InRange(new Vector3(vertex.Tangent.X, vertex.Tangent.Y, vertex.Tangent.Z).Length(), 0.999f, 1.001f);
				Assert.Contains(vertex.Tangent.W, new[] { -1f, 1f });
			});

			for (int index = 0; index < model.Vertices.Count; index += 3)
			{
				Vector2[] uv = model.Vertices.Skip(index).Take(3).Select(static vertex => vertex.TextureCoordinates).ToArray();
				Assert.InRange(uv.Max(static value => value.X) - uv.Min(static value => value.X), 0, 0.50001f);
				Assert.InRange(uv.Max(static value => value.Y) - uv.Min(static value => value.Y), 0, 0.50001f);
			}
		}
	}

	[Fact]
	public void CubePickingMeshKeepsPerFaceTextureIdentity()
	{
		VoxelFaceTiles tiles = new(1, 2, 3, 4, 5, 6);
		VoxelModel model = StairVoxelModelBuilder.CreateCube(tiles);

		Assert.Equal(36, model.Vertices.Count);
		for (int face = 0; face < 6; face++)
			Assert.All(model.Vertices.Skip(face * 6).Take(6), vertex => Assert.Equal(face + 1, vertex.TextureLayer));
	}
}
