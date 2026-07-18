using System.Numerics;
using Voxelgine.Graphics;

namespace Voxelgine.Engine;

internal static class BlockPresentationInfo
{
	public static void GetBlockTexCoords(
		BlockType blockType,
		Vector3 faceNormal,
		out Vector2 uvSize,
		out Vector2 uvPosition
	)
	{
		int blockId = BlockInfo.GetBlockID(blockType, faceNormal);
		int blockX = blockId % Chunk.AtlasSize;
		int blockY = blockId / Chunk.AtlasSize;
		uvSize = new Vector2(1f / Chunk.AtlasSize);
		uvPosition = uvSize * new Vector2(blockX, blockY);
	}
}
