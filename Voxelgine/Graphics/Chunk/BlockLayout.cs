using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Graphics
{
	/// <summary>
	/// Stores both skylight and block light for a block face.
	/// Skylight comes from the sky and can be attenuated by time of day.
	/// Block light comes from light-emitting blocks like Glowstone/Campfire.
	/// The final light value is the max of (skylight * skyMultiplier) and blockLight.
	/// </summary>
	[StructLayout(LayoutKind.Explicit)]
	public struct BlockLight
	{
		public static readonly BlockLight Black = new BlockLight(0, 0);
		public static readonly BlockLight FullBright = new BlockLight(15, 15);

		/// <summary>
		/// When true, ToColor() always returns white (full brightness).
		/// Toggle this for fullbright/debug rendering mode.
		/// </summary>
		public static bool FullbrightMode = false;

		/// <summary>
		/// Multiplier for skylight (0.0 = night, 1.0 = full day).
		/// Affects how bright skylight appears. Block light is not affected.
		/// </summary>
		public static float SkyLightMultiplier = 1.0f;

		/// <summary>
		/// Minimum ambient light level (0-15). Prevents completely dark areas.
		/// </summary>
		public static byte AmbientLight = 2;

		/// <summary>
		/// Skylight level (0-15). Comes from sky exposure.
		/// </summary>
		[FieldOffset(0)]
		public byte Sky;

		/// <summary>
		/// Block light level (0-15). Comes from light-emitting blocks.
		/// </summary>
		[FieldOffset(1)]
		public byte Block;

		[FieldOffset(2)]
		byte Unused1;

		[FieldOffset(3)]
		byte Unused2;

		[FieldOffset(0)]
		public int LightInteger;

		const int MaxLight = 15;

		public BlockLight(byte sky, byte block)
		{
			LightInteger = Unused1 = Unused2 = 0;
			Sky = (byte)(sky > MaxLight ? MaxLight : sky);
			Block = (byte)(block > MaxLight ? MaxLight : block);
		}

		public BlockLight(byte amt) : this(amt, amt) { }

		/// <summary>
		/// Gets the combined R value (for backwards compatibility).
		/// Returns max of skylight (adjusted) and block light.
		/// </summary>
		public byte R => GetEffectiveLight();

		/// <summary>
		/// Gets the combined G value (same as R for white light).
		/// </summary>
		public byte G => GetEffectiveLight();

		/// <summary>
		/// Gets the combined B value (same as R for white light).
		/// </summary>
		public byte B => GetEffectiveLight();

		/// <summary>
		/// Calculates the effective light level considering sky multiplier and ambient.
		/// </summary>
		byte GetEffectiveLight()
		{
			// Apply sky multiplier to skylight
			int skyContrib = (int)(Sky * SkyLightMultiplier);
			// Take max of sky contribution and block light
			int combined = Math.Max(skyContrib, Block);
			// Apply ambient minimum
			combined = Math.Max(combined, AmbientLight);
			return (byte)(combined > MaxLight ? MaxLight : combined);
		}

		public void SetSkylight(byte amt)
		{
			Sky = (byte)(amt > MaxLight ? MaxLight : amt);
		}

		public void SetBlockLight(byte amt)
		{
			Block = (byte)(amt > MaxLight ? MaxLight : amt);
		}

		public void Set(byte amt)
		{
			byte clampedAmt = (byte)(amt > MaxLight ? MaxLight : amt);
			Sky = Block = clampedAmt;
		}

		public Color ToColor()
		{
			if (FullbrightMode)
			{
				return new Color((byte)255, (byte)255, (byte)255, (byte)255);
			}

			byte effectiveLight = GetEffectiveLight();
			// Scale from 0-15 to 0-255
			byte val = (byte)Utils.Clamp(effectiveLight * 17, 0, 255);
			return new Color(val, val, val, (byte)255);
		}

		public static BlockLight operator +(BlockLight BL, byte Amt)
		{
			byte newSky = (byte)(BL.Sky + Amt > MaxLight ? MaxLight : BL.Sky + Amt);
			byte newBlock = (byte)(BL.Block + Amt > MaxLight ? MaxLight : BL.Block + Amt);
			return new BlockLight(newSky, newBlock);
		}

		/// <summary>
		/// Creates a BlockLight with only skylight set.
		/// </summary>
		public static BlockLight FromSkylight(byte sky) => new BlockLight(sky, 0);

		/// <summary>
		/// Creates a BlockLight with only block light set.
		/// </summary>
		public static BlockLight FromBlockLight(byte block) => new BlockLight(0, block);
	}

}
