using System.Numerics;

namespace Voxelgine.Graphics;

public unsafe partial class ChunkMap
{
	/// <summary>Returns whether one vertical chunk is present in the local map.</summary>
	public bool IsChunkResident(int chunkX, int chunkY, int chunkZ) =>
		Chunks.ContainsKey(new Vector3(chunkX, chunkY, chunkZ));
}
