using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Voxelgine.Engine;

namespace Voxelgine.Graphics
{
	[InlineArray(6)]
	public struct BlockLightArray
	{
		private BlockLight _element0;
	}

	[StructLayout(LayoutKind.Explicit)]
	public struct BlockLight
	{
		public static readonly BlockLight Black = new(0, 0);
		public static readonly BlockLight FullBright = new(15, 15);
		public static bool FullbrightMode;
		public static float SkyLightMultiplier = 1f;
		public static byte AmbientLight = 2;

		[FieldOffset(0)] public byte Sky;
		[FieldOffset(1)] public byte Block;
		[FieldOffset(2)] private byte _unused1;
		[FieldOffset(3)] private byte _unused2;
		[FieldOffset(0)] public int LightInteger;

		public BlockLight(byte sky, byte block)
		{
			LightInteger = 0;
			_unused1 = 0;
			_unused2 = 0;
			Sky = (byte)Math.Min((int)sky, 15);
			Block = (byte)Math.Min((int)block, 15);
		}

		public BlockLight(byte amount) : this(amount, amount) { }

		public byte R => GetEffectiveLight();
		public byte G => GetEffectiveLight();
		public byte B => GetEffectiveLight();

		private byte GetEffectiveLight()
		{
			int combined = Math.Max((int)(Sky * SkyLightMultiplier), Block);
			return (byte)Math.Min(Math.Max(combined, AmbientLight), 15);
		}

		public void SetSkylight(byte amount) => Sky = (byte)Math.Min((int)amount, 15);
		public void SetBlockLight(byte amount) => Block = (byte)Math.Min((int)amount, 15);
		public void Set(byte amount) => Sky = Block = (byte)Math.Min((int)amount, 15);

		public Rgba32 ToColor()
		{
			if (FullbrightMode)
				return Rgba32.White;

			byte value = (byte)(GetEffectiveLight() * 17);
			return new Rgba32(value, value, value, 255);
		}

		public static BlockLight operator +(BlockLight light, byte amount) =>
			new((byte)Math.Min(light.Sky + amount, 15), (byte)Math.Min(light.Block + amount, 15));

		public static BlockLight FromSkylight(byte sky) => new(sky, 0);
		public static BlockLight FromBlockLight(byte block) => new(0, block);
	}

	public struct PlacedBlock
	{
		public Voxelgine.Engine.BlockType Type;
		public BlockLightArray Lights;

		public PlacedBlock(Voxelgine.Engine.BlockType type, BlockLight defaultLight)
		{
			Type = type;
			Lights = default;
			for (int i = 0; i < 6; i++)
				Lights[i] = defaultLight;
		}

		public PlacedBlock(Voxelgine.Engine.BlockType type) : this(type, BlockLight.Black) { }

		public void SetAllLights(BlockLight light)
		{
			for (int i = 0; i < 6; i++)
				Lights[i] = light;
		}

		public void SetBlockLight(BlockLight light) => SetAllLights(light);
		public void SetBlockLight(Vector3 direction, BlockLight light) => Lights[Voxelgine.Utils.DirToByte(direction)] = light;

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

		public BlockLight GetBlockLight(Vector3 direction) => Lights[Voxelgine.Utils.DirToByte(direction)];

		public byte GetMaxSkylight()
		{
			byte max = 0;
			for (int i = 0; i < 6; i++)
				max = Math.Max(max, Lights[i].Sky);
			return max;
		}

		public byte GetMaxBlockLight()
		{
			byte max = 0;
			for (int i = 0; i < 6; i++)
				max = Math.Max(max, Lights[i].Block);
			return max;
		}

		public BlockLight GetBlockLightRaw(int index) => Lights[index];

		public Rgba32 GetColor(Vector3 normal) => GetBlockLight(normal).ToColor();

		public void Write(BinaryWriter writer) => writer.Write((ushort)Type);
		public void Read(BinaryReader reader) => Type = (Voxelgine.Engine.BlockType)reader.ReadUInt16();
	}
}
