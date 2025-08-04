using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Engine;

namespace Voxelgine.Graphics {
	public class PlacedBlock {
		public BlockType Type;
		//public OnBlockActivateFunc OnBlockActivate;

		// Recalculated, always 6
		public BlockLight[] Lights;

		public PlacedBlock(BlockType Type, BlockLight DefaultLight) {
			Lights = new BlockLight[6];

			for (int i = 0; i < Lights.Length; i++)
				Lights[i] = DefaultLight;

			this.Type = Type;
		}

		public PlacedBlock(BlockType Type) : this(Type, BlockLight.FullBright) {
		}

		public PlacedBlock(PlacedBlock Copy) : this(Copy.Type) {
			for (int i = 0; i < Lights.Length; i++)
				Lights[i] = Copy.Lights[i];
		}

		public void SetBlockLight(BlockLight L) {
			for (int i = 0; i < Lights.Length; i++)
				Lights[i] = L;
		}

		public void SetBlockLight(Vector3 Dir, BlockLight L) {
			Lights[Utils.DirToByte(Dir)] = L;
		}

		public BlockLight GetBlockLight(Vector3 Dir) {
			return Lights[Utils.DirToByte(Dir)];
		}

		public Color GetColor(Vector3 Normal) {
			return Lights[Utils.DirToByte(Normal)].ToColor();
		}

		// Serialization stuff

		public void Write(BinaryWriter Writer) {
			Writer.Write((ushort)Type);

			/*for (int i = 0; i < Lights.Length; i++)
				Writer.Write(Lights[i].LightInteger);*/
		}

		public void Read(BinaryReader Reader) {
			Type = (BlockType)Reader.ReadUInt16();

			/*for (int i = 0; i < Lights.Length; i++)
				Lights[i].LightInteger = Reader.ReadInt32();*/
		}

		public override string ToString() {
			BlockLight BL = GetBlockLight(new Vector3(0, 1, 0));
			return string.Format("{0} - R {1}, Light {2}", Type, BL.R, BL.LightInteger);
		}
	}

}
