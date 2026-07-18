using System.Numerics;
using Voxelgine.Engine;

namespace VoxelgineEngine.Tests;

public sealed class BlockTypeContractTests
{
	[Fact]
	public void PersistedBlockIdsRemainStable()
	{
		BlockType[] values = Enum.GetValues<BlockType>();
		Assert.Equal(23, values.Length);
		for (int index = 0; index < values.Length; index++)
			Assert.Equal(index, (int)values[index]);
	}

	[Theory]
	[InlineData(BlockType.Glowstone, 15)]
	[InlineData(BlockType.Campfire, 14)]
	[InlineData(BlockType.Torch, 10)]
	[InlineData(BlockType.Stone, 0)]
	public void LightEmissionContractIsExplicit(BlockType type, byte expected)
	{
		Assert.Equal(expected, BlockInfo.GetLightEmission(type));
		Assert.Equal(expected != 0, BlockInfo.EmitsLight(type));
	}

	[Fact]
	public void MultiFaceAtlasIdsRemainCompatible()
	{
		Assert.Equal(240, BlockInfo.GetBlockID(BlockType.Grass, Vector3.UnitY));
		Assert.Equal(241, BlockInfo.GetBlockID(BlockType.Grass, Vector3.UnitX));
		Assert.Equal(1, BlockInfo.GetBlockID(BlockType.Grass, -Vector3.UnitY));
		Assert.Equal(244, BlockInfo.GetBlockID(BlockType.CraftingTable, Vector3.UnitY));
		Assert.Equal(247, BlockInfo.GetBlockID(BlockType.CraftingTable, -Vector3.UnitY));
		Assert.Equal(245, BlockInfo.GetBlockID(BlockType.CraftingTable, Vector3.UnitX));
		Assert.Equal(246, BlockInfo.GetBlockID(BlockType.CraftingTable, Vector3.UnitZ));
	}
}
