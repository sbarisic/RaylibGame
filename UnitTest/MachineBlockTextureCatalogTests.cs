using System.Drawing;
using System.Numerics;
using Voxelgine.Engine;

namespace UnitTest;

public sealed class MachineBlockTextureCatalogTests
{
	public static TheoryData<BlockType, int, int, int, int, int, int> FaceAssignments => new()
	{
		{ BlockType.SteelFrame, 22, 22, 22, 22, 22, 22 },
		{ BlockType.MachineCasing, 23, 23, 24, 24, 23, 23 },
		{ BlockType.PowerCell, 25, 25, 26, 26, 25, 25 },
		{ BlockType.PowerConduit, 40, 40, 41, 41, 40, 40 },
		{ BlockType.ControlTerminal, 31, 31, 31, 31, 31, 39 },
		{ BlockType.LogicCore, 27, 27, 28, 28, 27, 27 },
		{ BlockType.RelayEmitter, 42, 42, 43, 43, 42, 42 },
		{ BlockType.GravityCoil, 44, 44, 45, 45, 44, 44 },
		{ BlockType.LinearActuator, 46, 46, 47, 47, 46, 46 },
		{ BlockType.FabricatorCore, 29, 29, 30, 30, 29, 29 },
	};

	[Theory]
	[MemberData(nameof(FaceAssignments))]
	public void CatalogDefinesEveryFace(
		BlockType block,
		int positiveX,
		int negativeX,
		int positiveY,
		int negativeY,
		int positiveZ,
		int negativeZ)
	{
		Assert.True(MachineBlockTextureCatalog.TryGet(block, out MachineBlockTextureDefinition definition));
		Assert.Equal(positiveX, definition.Faces.GetTile(Vector3.UnitX));
		Assert.Equal(negativeX, definition.Faces.GetTile(-Vector3.UnitX));
		Assert.Equal(positiveY, definition.Faces.GetTile(Vector3.UnitY));
		Assert.Equal(negativeY, definition.Faces.GetTile(-Vector3.UnitY));
		Assert.Equal(positiveZ, definition.Faces.GetTile(Vector3.UnitZ));
		Assert.Equal(negativeZ, definition.Faces.GetTile(-Vector3.UnitZ));
		Assert.False(BlockInfo.CustomModel(block));
	}

	[Fact]
	public void CatalogCoversExactlyTheInfrastructureBlockRange()
	{
		BlockType[] expected = Enum.GetValues<BlockType>()
			.Where(static block => block is >= BlockType.SteelFrame and <= BlockType.FabricatorCore)
			.ToArray();

		Assert.Equal(expected, MachineBlockTextureCatalog.All.Select(static definition => definition.Block));
	}

	[Fact]
	public void AssignedAtlasCellsContainPlaceholderDataInEverySurfaceMap()
	{
		int[] assignedTiles = MachineBlockTextureCatalog.All
			.SelectMany(static definition => new[]
			{
				definition.Faces.PositiveX,
				definition.Faces.NegativeX,
				definition.Faces.PositiveY,
				definition.Faces.NegativeY,
				definition.Faces.PositiveZ,
				definition.Faces.NegativeZ,
			})
			.Distinct()
			.Order()
			.ToArray();
		Assert.Equal(Enumerable.Range(22, 10).Concat(Enumerable.Range(39, 9)), assignedTiles);

		string textureDirectory = Path.Combine(AppContext.BaseDirectory, "data", "textures");
		foreach (string name in new[] { "atlas.png", "atlas_normal.png", "atlas_specular.png", "atlas_roughness.png" })
		{
			using Bitmap atlas = new(Path.Combine(textureDirectory, name));
			foreach (int tile in assignedTiles)
				AssertTileContainsDetail(atlas, name, tile);
		}
	}

	private static void AssertTileContainsDetail(Bitmap atlas, string atlasName, int tile)
	{
		const int atlasColumns = 16;
		const int tileSize = 32;
		int startX = tile % atlasColumns * tileSize;
		int startY = tile / atlasColumns * tileSize;
		int firstPixel = atlas.GetPixel(startX, startY).ToArgb();
		for (int y = startY; y < startY + tileSize; y++)
		{
			for (int x = startX; x < startX + tileSize; x++)
			{
				if (atlas.GetPixel(x, y).ToArgb() != firstPixel)
					return;
			}
		}

		Assert.Fail($"{atlasName} tile {tile} is blank or uniform.");
	}
}
