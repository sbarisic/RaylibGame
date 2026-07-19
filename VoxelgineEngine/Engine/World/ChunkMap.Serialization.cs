using System.IO;
using System.IO.Compression;
using System.Numerics;
using System.Threading;

namespace Voxelgine.Graphics
{
	public unsafe partial class ChunkMap
	{
		public void Write(Stream Output)
		{
			using (GZipStream ZipStream = new GZipStream(Output, CompressionMode.Compress, true))
			using (var Writer = new BinaryWriter(ZipStream))
			{
				Writer.Write(Chunks.Count);

				foreach (var chunk in Chunks.Items)
				{
					Writer.Write((int)chunk.Key.X);
					Writer.Write((int)chunk.Key.Y);
					Writer.Write((int)chunk.Key.Z);

					chunk.Value.Write(Writer);
				}
			}
		}

		public void Read(Stream Input, CancellationToken cancellationToken = default)
		{
			ExecuteWorldReset(() => ReadCore(Input, cancellationToken));
		}

		private void ReadCore(Stream Input, CancellationToken cancellationToken)
		{
			cancellationToken.ThrowIfCancellationRequested();
			using (GZipStream ZipStream = new GZipStream(Input, CompressionMode.Decompress, true))
			using (var Reader = new BinaryReader(ZipStream))
			{
				int Count = Reader.ReadInt32();

				for (int i = 0; i < Count; i++)
				{
					cancellationToken.ThrowIfCancellationRequested();
					int CX = Reader.ReadInt32();
					int CY = Reader.ReadInt32();
					int CZ = Reader.ReadInt32();

					Vector3 ChunkIndex = new Vector3(CX, CY, CZ);

					Chunk Chk = new Chunk(ChunkIndex, this);
					Chk.Read(Reader);

					Chunks.Add(ChunkIndex, Chk);
				}
			}
		}
	}
}
