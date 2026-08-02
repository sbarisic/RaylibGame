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
		/// <summary>The complete block value before the change.</summary>
		public readonly BlockValue OldValue;
		/// <summary>The complete block value after the change.</summary>
		public readonly BlockValue NewValue;
		/// <summary>Authoritative revision of the containing horizontal column.</summary>
		public readonly long ColumnRevision;

		public BlockChange(
			int x,
			int y,
			int z,
			BlockValue oldValue,
			BlockValue newValue,
			long columnRevision = 0)
		{
			X = x;
			Y = y;
			Z = z;
			OldValue = oldValue;
			NewValue = newValue;
			ColumnRevision = columnRevision;
		}

		public BlockChange(
			int x,
			int y,
			int z,
			BlockType oldType,
			BlockType newType,
			long columnRevision = 0)
			: this(x, y, z, new BlockValue(oldType), new BlockValue(newType), columnRevision) { }

		public BlockType OldType => OldValue.Type;

		public BlockType NewType => NewValue.Type;
	}
}
