using System.Numerics;
using System.Drawing;
using Voxelgine.Engine;

namespace UnitTest;

public sealed class PhaseOneBlockPresentationTests
{
	[Theory]
	[InlineData(BlockType.DryFarmland, 53, 1)]
	[InlineData(BlockType.WetFarmland, 54, 1)]
	public void FarmlandUsesDedicatedTopAndDirtSides(BlockType type, int top, int side)
	{
		Assert.Equal(top, BlockInfo.GetBlockID(type, Vector3.UnitY));
		Assert.Equal(side, BlockInfo.GetBlockID(type, Vector3.UnitX));
		Assert.Equal(side, BlockInfo.GetBlockID(type, -Vector3.UnitY));
	}

	[Theory]
	[InlineData(BlockType.Concrete, 55)]
	[InlineData(BlockType.StoneStairs, 0)]
	[InlineData(BlockType.WoodStairs, 5)]
	[InlineData(BlockType.ConcreteStairs, 55)]
	public void ConstructionBlocksInheritTheirSourceTile(BlockType type, int tile)
	{
		Assert.Equal(tile, BlockInfo.GetBlockID(type, Vector3.UnitX));
		Assert.Equal(tile, BlockInfo.GetBlockID(type, Vector3.UnitY));
	}

	[Fact]
	public void ReservedPhaseOneCellsContainAuthoredDataInEveryCompanionAtlas()
	{
		string textureDirectory = Path.Combine(AppContext.BaseDirectory, "data", "textures");
		foreach (string name in new[] { "atlas.png", "atlas_normal.png", "atlas_specular.png", "atlas_roughness.png" })
		{
			using Bitmap atlas = new(Path.Combine(textureDirectory, name));
			foreach (int tile in Enumerable.Range(53, 11))
			{
				int startX = tile % 16 * 32;
				int startY = tile / 16 * 32;
				bool containsData = false;
				for (int y = startY; y < startY + 32 && !containsData; y++)
				for (int x = startX; x < startX + 32; x++)
					containsData |= atlas.GetPixel(x, y).A != 0;
				Assert.True(containsData, $"{name} tile {tile} contains no authored pixels.");
			}
		}
	}
}
