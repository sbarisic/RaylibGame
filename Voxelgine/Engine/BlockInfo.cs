using Raylib_cs;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

using Voxelgine.Graphics;

namespace Voxelgine.Engine {
	public enum BlockType : ushort {
		None,
		Stone,
		Dirt,
		StoneBrick,
		Sand,
		Bricks,
		Plank,
		EndStoneBrick,
		Ice,
		Test,
		Leaf,
		Water,
		Glass,
		Glowstone,
		Test2,

		// Blocks with different sides go here
		Grass,
		Wood,
		CraftingTable,
		Barrel,
		Campfire
	}

	public enum IconType : ushort {
		None,

		Particle1,
		Gun,

		Apple,
		Hammer,
		HeartEmpty,
		HeartFull,
		HeartHalf,
		Lava,
		Pickaxe
	}

	static class BlockInfo {
		public static bool IsOpaque(BlockType T) {
			switch (T) {
				case BlockType.None:
					return false;

				case BlockType.Campfire:
					return false;

				case BlockType.Water:
				case BlockType.Glass:
				case BlockType.Ice:
					return false;

				default:
					return true;
			}
		}

		public static bool IsSolid(BlockType T) {
			switch (T) {
				case BlockType.None:
				case BlockType.Water:
					return false;
			}

			return true;
		}

		/// <summary>
		/// Returns true if the block is a swimmable liquid (water).
		/// </summary>
		public static bool IsWater(BlockType T) {
			return T == BlockType.Water;
		}

		public static bool EmitsLight(BlockType T) {
			switch (T) {
				case BlockType.Campfire:
				case BlockType.Glowstone:
					return true;

				default:
					return false;
			}
		}

		/// <summary>
		/// Returns true if the block type should render backfaces (double-sided).
		/// This applies to glass-like blocks that should be visible from both sides.
		/// </summary>
		public static bool NeedsBackfaceRendering(BlockType T) {
			switch (T) {
				case BlockType.Glass:
				case BlockType.Ice:
					return true;

				default:
					return false;
			}
		}

		public static void GetIconTexCoords(IconType Icon, out Texture2D Texture, out Vector2 UVSize, out Vector2 UVPos, out float Scale) {
			UVSize = Vector2.One;
			UVPos = Vector2.Zero;
			Scale = 3.8f;

			switch (Icon) {
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
			}

			int IconID = (int)Icon - 1;
			int IconX = IconID % Chunk.AtlasSize;
			int IconY = IconID / Chunk.AtlasSize;

			Texture = ResMgr.ItemTexture;
			UVSize = new Vector2(1.0f / ResMgr.ItemSize, 1.0f / ResMgr.ItemSize);
			UVPos = UVSize * new Vector2(IconX, IconY);
		}

		public static void GetBlockTexCoords(BlockType BlockType, Vector3 FaceNormal, out Vector2 UVSize, out Vector2 UVPos) {
			int BlockID = BlockInfo.GetBlockID(BlockType, FaceNormal);
			int BlockX = BlockID % Chunk.AtlasSize;
			int BlockY = BlockID / Chunk.AtlasSize;

			UVSize = new Vector2(1.0f / Chunk.AtlasSize, 1.0f / Chunk.AtlasSize);
			UVPos = UVSize * new Vector2(BlockX, BlockY);
		}

		public static int GetBlockID(BlockType BType, Vector3 Face) {
			int BlockID = (int)BType - 1;

			if (BType == BlockType.Grass) {
				if (Face.Y == 1)
					BlockID = 240;
				else if (Face.Y == 0)
					BlockID = 241;
				else
					BlockID = 1;
			} else if (BType == BlockType.Wood) {
				if (Face.Y == 0)
					BlockID = 242;
				else
					BlockID = 243;
			} else if (BType == BlockType.CraftingTable) {
				if (Face.Y == 1)
					BlockID = 244;
				else if (Face.Y == -1)
					BlockID = 247;
				else if (Face.X == 1 || Face.X == -1)
					BlockID = 245;
				else
					BlockID = 246;
			} else if (BType == BlockType.Barrel) {
				BlockID = 8;
			}

			return BlockID;
		}

		public static bool CustomModel(BlockType BType) {
			if (BType == BlockType.Barrel)
				return true;

			if (BType == BlockType.Campfire)
				return true;

			return false;
		}

		public static Model GetCustomModel(BlockType BType) {
			if (BType == BlockType.Barrel) {
				return ResMgr.GetModel("barrel/barrel.obj");
			} else if (BType == BlockType.Campfire) {
				return ResMgr.GetModel("campfire/campfire.obj");
			} else
				throw new NotImplementedException();
		}
	}
}
