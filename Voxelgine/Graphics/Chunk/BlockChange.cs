using Voxelgine.Engine;

namespace Voxelgine.Graphics
{
	/// <summary>
	/// Records a single block change in the world for network delta synchronization.
	/// The server reads pending changes each tick and broadcasts them to clients.
	/// </summary>
	public readonly struct BlockChange
	{
		/// <summary>World-space X coordinate of the changed block.</summary>
		public readonly int X;
		/// <summary>World-space Y coordinate of the changed block.</summary>
		public readonly int Y;
		/// <summary>World-space Z coordinate of the changed block.</summary>
		public readonly int Z;
		/// <summary>The block type before the change.</summary>
		public readonly BlockType OldType;
		/// <summary>The block type after the change.</summary>
		public readonly BlockType NewType;

		public BlockChange(int x, int y, int z, BlockType oldType, BlockType newType)
		{
			X = x;
			Y = y;
			Z = z;
			OldType = oldType;
			NewType = newType;
		}
	}
}
