using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Engine;

namespace Voxelgine.Graphics
{
	/// <summary>
	/// Fixed-size inline array of 6 BlockLight values, one per face direction.
	/// Stored directly within the PlacedBlock struct (no heap allocation).
	/// </summary>
	[InlineArray(6)]
	public struct BlockLightArray
	{
		private BlockLight _element0;
	}

	/// <summary>
	/// Represents a block placed in the world with its type and per-face lighting.
	/// Each block stores 6 light values (one per face) supporting dual-channel lighting.
	/// Stored as a value type with inline light data for cache-friendly array access.
	/// </summary>
	public struct PlacedBlock
	{
		/// <summary>The type of block (determines texture, transparency, solidity).</summary>
		public BlockType Type;

		/// <summary>
		/// Light values for each face direction (6 total).
		/// Each BlockLight contains skylight and blocklight channels.
		/// Stored inline within the struct for cache-friendly access.
		/// </summary>
		public BlockLightArray Lights;

		public PlacedBlock(BlockType Type, BlockLight DefaultLight)
		{
			this.Type = Type;
			for (int i = 0; i < 6; i++)
				Lights[i] = DefaultLight;
		}

		public PlacedBlock(BlockType Type) : this(Type, BlockLight.Black)
		{
		}

		public void SetAllLights(BlockLight L)
		{
			for (int i = 0; i < 6; i++)
				Lights[i] = L;
		}

		public void SetBlockLight(BlockLight L)
		{
			SetAllLights(L);
		}

		public void SetBlockLight(Vector3 Dir, BlockLight L)
		{
			Lights[Utils.DirToByte(Dir)] = L;
		}

		public void SetSkylight(byte level)
		{
			for (int i = 0; i < 6; i++)
			{
				BlockLight light = Lights[i];
				light.SetSkylight(level);
				Lights[i] = light;
			}
		}

		public void SetBlockLightLevel(byte level)
		{
			for (int i = 0; i < 6; i++)
			{
				BlockLight light = Lights[i];
				light.SetBlockLight(level);
				Lights[i] = light;
			}
		}

		public BlockLight GetBlockLight(Vector3 Dir)
		{
			return Lights[Utils.DirToByte(Dir)];
		}

		/// <summary>
		/// Gets the maximum skylight level from all faces.
		/// </summary>
		public byte GetMaxSkylight()
		{
			byte max = 0;
			for (int i = 0; i < 6; i++)
				if (Lights[i].Sky > max) max = Lights[i].Sky;
			return max;
		}

		/// <summary>
		/// Gets the maximum block light level from all faces.
		/// </summary>
		public byte GetMaxBlockLight()
		{
			byte max = 0;
			for (int i = 0; i < 6; i++)
				if (Lights[i].Block > max) max = Lights[i].Block;
			return max;
		}

		public Color GetColor(Vector3 Normal)
		{
			return Lights[Utils.DirToByte(Normal)].ToColor();
		}

		// Serialization stuff

		public void Write(BinaryWriter Writer)
		{
			Writer.Write((ushort)Type);

			/*for (int i = 0; i < Lights.Length; i++)
				Writer.Write(Lights[i].LightInteger);*/
		}

		public void Read(BinaryReader Reader)
		{
			Type = (BlockType)Reader.ReadUInt16();

			/*for (int i = 0; i < Lights.Length; i++)
				Lights[i].LightInteger = Reader.ReadInt32();*/
		}

		public override string ToString()
		{
			BlockLight BL = GetBlockLight(new Vector3(0, 1, 0));
			return string.Format("{0} - Sky {1}, Block {2}, Effective {3}", Type, BL.Sky, BL.Block, BL.R);
		}
	}

}
