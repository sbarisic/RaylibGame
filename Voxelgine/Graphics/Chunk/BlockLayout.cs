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
		public static readonly BlockLight FullBright = new BlockLight(15); // Set to max light level

		/// <summary>
		/// When true, ToColor() always returns white (full brightness).
		/// Toggle this for fullbright/debug rendering mode.
		/// </summary>
		public static bool FullbrightMode = false;

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

		const int LightLevels = 16;

		public BlockLight(byte R, byte G, byte B) {
			LightInteger = Unused = 0;

			this.R = R;
			this.G = G;
			this.B = B;
		}

		public BlockLight(byte Amt) {
			LightInteger = Unused = 0;

			byte clampedAmt = (byte)(Amt > 15 ? 15 : Amt);
			R = G = B = clampedAmt;
		}

		public void Increase(byte Amt) {
			R = (byte)(R + Amt > 15 ? 15 : R + Amt);
			G = (byte)(G + Amt > 15 ? 15 : G + Amt);
			B = (byte)(B + Amt > 15 ? 15 : B + Amt);
		}

		public void SetMin(byte Amt) {
			byte clampedAmt = (byte)(Amt > 15 ? 15 : Amt);
			if (R < clampedAmt) R = clampedAmt;
			if (G < clampedAmt) G = clampedAmt;
			if (B < clampedAmt) B = clampedAmt;
		}

		public void Set(byte Amt) {
			byte clampedAmt = (byte)(Amt > 15 ? 15 : Amt);
			R = G = B = clampedAmt;
		}

		public Color ToColor() {
			// In fullbright mode, return max brightness
			if (FullbrightMode) {
				return new Color((byte)255, (byte)255, (byte)255, (byte)255);
			}

			// Scale from 0-15 to 0-255
			byte RR = (byte)Utils.Clamp(R * 17, 0, 255); // 17 = ~255/15
			byte GG = (byte)Utils.Clamp(G * 17, 0, 255);
			byte BB = (byte)Utils.Clamp(B * 17, 0, 255);
			return new Color(RR, GG, BB, (byte)255);
		}

		public static BlockLight operator +(BlockLight BL, byte Amt) {
			byte newR = (byte)(BL.R + Amt > 15 ? 15 : BL.R + Amt);
			return new BlockLight(newR);
		}
	}

}
