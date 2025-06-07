using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Graphics {
	[StructLayout(LayoutKind.Explicit)]
	public struct BlockLight {
		public static readonly BlockLight Black = new BlockLight(0, 0, 0);
		public static readonly BlockLight Ambient = new BlockLight(LightLevels);
		public static readonly BlockLight FullBright = new BlockLight(256 / LightLevels);

		[FieldOffset(0)]
		public byte R;

		[FieldOffset(1)]
		public byte G;

		[FieldOffset(2)]
		public byte B;

		[FieldOffset(3)]
		byte Unused;

		[FieldOffset(0)]
		public int LightInteger;

		const int LightLevels = 8;

		public BlockLight(byte R, byte G, byte B) {
			LightInteger = Unused = 0;

			this.R = R;
			this.G = G;
			this.B = B;
		}

		public BlockLight(byte Amt) {
			LightInteger = Unused = 0;

			R = G = B = Amt;
		}

		public void Increase(byte Amt) {
			if (R + Amt > 255)
				R = 255;
			else
				R += Amt;

			if (G + Amt > 255)
				G = 255;
			else
				G += Amt;

			if (B + Amt > 255)
				B = 255;
			else
				B += Amt;
		}

		public void SetMin(byte Amt) {
			if (R < Amt)
				R = Amt;

			if (G < Amt)
				G = Amt;

			if (B < Amt)
				B = Amt;
		}

		public void Set(byte Amt) {
			R = Amt;
			G = Amt;
			B = Amt;
		}

		public Color ToColor() {
			byte RR = (byte)Utils.Clamp(R * LightLevels, 0, 255);
			byte GG = (byte)Utils.Clamp(G * LightLevels, 0, 255);
			byte BB = (byte)Utils.Clamp(B * LightLevels, 0, 255);
			return new Color(RR, GG, BB);
		}

		public static BlockLight operator +(BlockLight BL, byte Amt) {
			byte Res = BL.R;

			if (Res + Amt > 255)
				Res = 255;
			else
				Res = (byte)(Res + Amt);

			return new BlockLight(Res);
		}
	}

}
