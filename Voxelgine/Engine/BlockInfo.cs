using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Graphics;

namespace Voxelgine.Engine
{
	/// <summary>
	/// Defines all block types in the voxel world.
	/// Block types determine texture, transparency, solidity, and light emission.
	/// </summary>
	public enum BlockType : ushort
	{
		/// <summary>Empty/air block.</summary>
		None,
		Stone,
		Dirt,
		StoneBrick,
		Sand,
		Bricks,
		Plank,
		EndStoneBrick,
		/// <summary>Transparent solid block.</summary>
		Ice,
		Test,
		Leaf,
		/// <summary>Transparent non-solid block (swimmable).</summary>
		Water,
		/// <summary>Transparent solid block.</summary>
		Glass,
		/// <summary>Light-emitting block (emits level 15 light).</summary>
		Glowstone,
		Test2,

		// Blocks with different sides (multi-texture) go here
		Grass,
		Wood,
		CraftingTable,
		Barrel,
		Campfire,
		Torch,
		Foliage,

		Gravel
	}

	/// <summary>
	/// Icon types for inventory items and GUI elements.
	/// </summary>
	public enum IconType : ushort
	{
		None,

		Particle1,
		Gun,

		Apple,
		Hammer,
		HeartEmpty,
		HeartFull,
		HeartHalf,
		Lava,
		Pickaxe,
		Torch
	}

	static class BlockInfo
	{
		// Pre-computed lookup tables for hot-path block queries (indexed by BlockType).
		// Built once at startup from the canonical switch-based methods below.
		static readonly bool[] _isRendered;
		static readonly bool[] _isOpaque;
		static readonly bool[] _isSolid;
		static readonly bool[] _emitsLight;
		static readonly byte[] _lightEmission;

		static BlockInfo()
		{
			var values = Enum.GetValues<BlockType>();
			int count = 0;
			foreach (var v in values)
				if ((int)v >= count) count = (int)v + 1;

			_isRendered = new bool[count];
			_isOpaque = new bool[count];
			_isSolid = new bool[count];
			_emitsLight = new bool[count];
			_lightEmission = new byte[count];

			foreach (var v in values)
			{
				int i = (int)v;
				_isRendered[i] = IsRenderedSwitch(v);
				_isOpaque[i] = IsOpaqueSwitch(v);
				_isSolid[i] = IsSolidSwitch(v);
				_emitsLight[i] = EmitsLightSwitch(v);
				_lightEmission[i] = GetLightEmissionSwitch(v);
			}
		}

		public static bool IsRendered(BlockType T) => _isRendered[(int)T];
		public static bool IsOpaque(BlockType T) => _isOpaque[(int)T];
		public static bool IsSolid(BlockType T) => _isSolid[(int)T];
		public static bool EmitsLight(BlockType T) => _emitsLight[(int)T];
		public static byte GetLightEmission(BlockType T) => _lightEmission[(int)T];

		static bool IsRenderedSwitch(BlockType T)
		{
			switch (T)
			{
				case BlockType.None:
					return false;

				default:
					return true;
			}
		}

		static bool IsOpaqueSwitch(BlockType T)
		{
			switch (T)
			{
				case BlockType.None:
					return false;

				// Custom mesh blocks
				case BlockType.Campfire:
				case BlockType.Torch:
				case BlockType.Foliage:
					return false;

				// Transparent square blocks
				case BlockType.Water:
				case BlockType.Glass:
				case BlockType.Ice:
				case BlockType.Leaf:
					return false;

				default:
					return true;
			}
		}

		static bool IsSolidSwitch(BlockType T)
		{
			switch (T)
			{
				case BlockType.None:
				case BlockType.Water:
				case BlockType.Foliage:
				case BlockType.Torch:
					return false;
			}

			return true;
		}

		/// <summary>
		/// Returns true if the block is a swimmable liquid (water).
		/// </summary>
		public static bool IsWater(BlockType T)
		{
			return T == BlockType.Water;
		}

		static bool EmitsLightSwitch(BlockType T)
		{
			switch (T)
			{
				case BlockType.Campfire:
				case BlockType.Glowstone:
				case BlockType.Torch:
					return true;

				default:
					return false;
			}
		}

		/// <summary>
		/// Returns the light emission level (0-15) for a block type.
		/// </summary>
		static byte GetLightEmissionSwitch(BlockType T)
		{
			switch (T)
			{
				case BlockType.Glowstone:
					return 15;

				case BlockType.Campfire:
					return 14;

				case BlockType.Torch:
					return 10;

				default:
					return 0;
			}
		}

		/// <summary>
		/// Returns true if the block type should render backfaces (double-sided).
		/// This applies to glass-like blocks that should be visible from both sides.
		/// </summary>
		public static bool NeedsBackfaceRendering(BlockType T)
		{
			switch (T)
			{
				case BlockType.Glass:
				case BlockType.Ice:
					return true;

				default:
					return false;
			}
		}

		public static void GetIconTexCoords(IconType Icon, out Texture2D Texture, out Vector2 UVSize, out Vector2 UVPos, out float Scale)
		{
			UVSize = Vector2.One;
			UVPos = Vector2.Zero;
			Scale = 3.8f;

			switch (Icon)
			{
				case IconType.Particle1:
					Texture = ResMgr.GetTexture("items/particle1.png", TextureFilter.Point);
					return;

				case IconType.Gun:
					Scale = 1.8f;
					Texture = ResMgr.GetTexture("items/gun.png", TextureFilter.Point);
					return;

				case IconType.Apple:
					Texture = ResMgr.GetTexture("items/apple.png", TextureFilter.Point);
					return;

				case IconType.Hammer:
					Texture = ResMgr.GetTexture("items/hammer.png", TextureFilter.Point);
					return;

				case IconType.HeartEmpty:
					Texture = ResMgr.GetTexture("items/heart_empty.png", TextureFilter.Point);
					return;

				case IconType.HeartFull:
					Texture = ResMgr.GetTexture("items/heart_full.png", TextureFilter.Point);
					return;

				case IconType.HeartHalf:
					Texture = ResMgr.GetTexture("items/heart_half.png", TextureFilter.Point);
					return;

				case IconType.Lava:
					Texture = ResMgr.GetTexture("items/lava.png", TextureFilter.Point);
					return;

				case IconType.Pickaxe:
					Texture = ResMgr.GetTexture("items/pickaxe.png", TextureFilter.Point);
					return;

				case IconType.Torch:
					Texture = ResMgr.GetTexture("items/torch.png", TextureFilter.Point);
					return;
			}

			int IconID = (int)Icon - 1;
			int IconX = IconID % Chunk.AtlasSize;
			int IconY = IconID / Chunk.AtlasSize;

			Texture = ResMgr.ItemTexture;
			UVSize = new Vector2(1.0f / ResMgr.ItemSize, 1.0f / ResMgr.ItemSize);
			UVPos = UVSize * new Vector2(IconX, IconY);
		}

		public static void GetBlockTexCoords(BlockType BlockType, Vector3 FaceNormal, out Vector2 UVSize, out Vector2 UVPos)
		{
			int BlockID = BlockInfo.GetBlockID(BlockType, FaceNormal);
			int BlockX = BlockID % Chunk.AtlasSize;
			int BlockY = BlockID / Chunk.AtlasSize;

			UVSize = new Vector2(1.0f / Chunk.AtlasSize, 1.0f / Chunk.AtlasSize);
			UVPos = UVSize * new Vector2(BlockX, BlockY);
		}

		public static int GetBlockID(BlockType BType, Vector3 Face)
		{
			int BlockID = (int)BType - 1;

			if (BType == BlockType.Grass)
			{
				if (Face.Y == 1)
					BlockID = 240;
				else if (Face.Y == 0)
					BlockID = 241;
				else
					BlockID = 1;
			}
			else if (BType == BlockType.Wood)
			{
				if (Face.Y == 0)
					BlockID = 242;
				else
					BlockID = 243;
			}
			else if (BType == BlockType.CraftingTable)
			{
				if (Face.Y == 1)
					BlockID = 244;
				else if (Face.Y == -1)
					BlockID = 247;
				else if (Face.X == 1 || Face.X == -1)
					BlockID = 245;
				else
					BlockID = 246;
			}
			else if (BType == BlockType.Barrel)
			{
				BlockID = 8;
			}

			return BlockID;
		}

		public static bool CustomModel(BlockType BType)
		{
			switch (BType)
			{
				case BlockType.Barrel:
				case BlockType.Campfire:
				case BlockType.Torch:
				case BlockType.Foliage:
					return true;

				default:
					return false;
			}
		}

		static Dictionary<BlockType, CustomModel> _blockJsonModelCache = new();
		static CustomModel[] _foliageVariants;
		const int FoliageVariantCount = 3;

		/// <summary>
		/// Returns the cached custom model for a block type.
		/// For <see cref="BlockType.Foliage"/>, pass global block coordinates
		/// to select a deterministic grass variant per position.
		/// </summary>
		public static CustomModel GetBlockJsonModel(BlockType BType, int globalX = 0, int globalY = 0, int globalZ = 0)
		{
			if (BType == BlockType.Foliage)
				return GetFoliageVariant(globalX, globalY, globalZ);

			if (_blockJsonModelCache.TryGetValue(BType, out var cached))
				return cached;

			string jsonPath, texPath;
			switch (BType)
			{
				case BlockType.Barrel:
					jsonPath = "barrel/barrel.json";
					texPath = "barrel/barrel_tex.png";
					break;
				case BlockType.Campfire:
					jsonPath = "campfire/campfire.json";
					texPath = "campfire/campfire_tex.png";
					break;
				case BlockType.Torch:
					jsonPath = "torch/torch.json";
					texPath = "torch/torch_tex.png";
					break;
				default:
					throw new NotImplementedException();
			}

			MinecraftModel jMdl = ResMgr.GetJsonModel(jsonPath);
			CustomModel model = MeshGenerator.Generate(jMdl);
			Texture2D texture = ResMgr.GetModelTexture(texPath);
			model.SetTexture(texture);

			_blockJsonModelCache[BType] = model;
			return model;
		}

		static CustomModel GetFoliageVariant(int x, int y, int z)
		{
			if (_foliageVariants == null)
			{
				_foliageVariants = new CustomModel[FoliageVariantCount];
				for (int i = 0; i < FoliageVariantCount; i++)
				{
					string jsonPath = $"grass/grass{i + 1}.json";
					MinecraftModel jMdl = ResMgr.GetJsonModel(jsonPath);
					CustomModel model = MeshGenerator.Generate(jMdl);
					model.SetTexture(ResMgr.GetModelTexture("grass/grass1_tex.png"));
					_foliageVariants[i] = model;
				}
			}

			int hash = (x * 73856093) ^ (y * 19349663) ^ (z * 83492791);
			int variant = ((hash % FoliageVariantCount) + FoliageVariantCount) % FoliageVariantCount;
			return _foliageVariants[variant];
		}
	}
}
