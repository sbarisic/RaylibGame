using Voxelgine.Engine;

using System.IO;

namespace Voxelgine.Graphics
{
	public unsafe partial class Chunk
	{
		public void Write(BinaryWriter Writer)
		{
			for (int i = 0; i < Blocks.Length;)
			{
				PlacedBlock Cur = Blocks[i];
				ushort Count = 1;

				for (int j = i + 1; j < Blocks.Length; j++)
				{
					if (Blocks[j].Type == Cur.Type)
						Count++;
					else
						break;
				}

				Writer.Write(Count);
				Cur.Write(Writer);

				i += Count;
			}
		}

		public void Read(BinaryReader Reader)
		{
			for (int i = 0; i < Blocks.Length;)
			{
				ushort Count = Reader.ReadUInt16();

				PlacedBlock Block = new PlacedBlock(BlockType.None);
				Block.Read(Reader);

				for (int j = 0; j < Count; j++)
					Blocks[i + j] = new PlacedBlock(Block);

				i += Count;
			}

			Dirty = true;
		}
	}
}
