using RaylibSharp;

using RaylibTest.Engine;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace RaylibTest.Graphics {
	class ChunkPalette {
		Dictionary<int, BlockType> Palette;

		public ChunkPalette() {
			Palette = new Dictionary<int, BlockType>() {
				{ 0, BlockType.None },
				{ 0x847E87, BlockType.Stone }, // floor grey
				{ 0x8A6F30, BlockType.Dirt }, // wall brown
				{ 0x9BADB7, BlockType.Plank }, // metal blue
				{ 0x6ABE30, BlockType.Leaf }, // green
			};
		}

		public BlockType GetBlock(string Str) {
			int BlockClr = int.Parse(Str, System.Globalization.NumberStyles.HexNumber);

			if (Palette.ContainsKey(BlockClr))
				return Palette[BlockClr];

			return BlockType.Sand;
		}
	}

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

	class PlacedBlock {
		public BlockType Type;

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
	}

	unsafe class Chunk {
		public const int ChunkSize = 16;
		public const float BlockSize = 1;
		public const int AtlasSize = 16;

		public PlacedBlock[] Blocks;
		bool Dirty;
		bool ModelValid;

		Model CachedModelOpaque;
		Mesh CachedMeshOpaque;

		public Color ChunkColor = Color.White;

		public Vector3 GlobalChunkIndex;
		ChunkMap WorldMap;

		List<Vector3> SunRayOrigins = new List<Vector3>();
		Vector3 SunDir = -Vector3.UnitY;

		public Chunk(Vector3 GlobalChunkIndex, ChunkMap WorldMap) {
			this.GlobalChunkIndex = GlobalChunkIndex;
			this.WorldMap = WorldMap;

			Blocks = new PlacedBlock[ChunkSize * ChunkSize * ChunkSize];
			for (int i = 0; i < Blocks.Length; i++)
				Blocks[i] = new PlacedBlock(BlockType.None);

			Dirty = true;
			ModelValid = false;

			//int TileTexSize = AtlasTex.width / 16;
		}

		public void Write(BinaryWriter Writer) {
			for (int i = 0; i < Blocks.Length;) {
				PlacedBlock Cur = Blocks[i];
				ushort Count = 1;

				for (int j = i + 1; j < Blocks.Length; j++) {
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

		public void Read(BinaryReader Reader) {
			for (int i = 0; i < Blocks.Length;) {
				ushort Count = Reader.ReadUInt16();

				PlacedBlock Block = new PlacedBlock(BlockType.None);
				Block.Read(Reader);

				for (int j = 0; j < Count; j++)
					Blocks[i + j] = new PlacedBlock(Block);

				i += Count;
			}

			Dirty = true;
		}

		public PlacedBlock GetBlock(int X, int Y, int Z) {
			/*if (X < 0 || X >= ChunkSize)
				return PlacedBlock.None;

			if (Y < 0 || Y >= ChunkSize)
				return PlacedBlock.None;

			if (Z < 0 || Z >= ChunkSize)
				return PlacedBlock.None;*/

			if (X < 0 || X >= ChunkSize || Y < 0 || Y >= ChunkSize || Z < 0 || Z >= ChunkSize) {
				WorldMap.GetWorldPos(0, 0, 0, GlobalChunkIndex, out Vector3 GlobalBlockPos);
				return WorldMap.GetPlacedBlock((int)GlobalBlockPos.X + X, (int)GlobalBlockPos.Y + Y, (int)GlobalBlockPos.Z + Z, out Chunk Chk);
			}

			return Blocks[X + ChunkSize * (Y + ChunkSize * Z)];
		}

		public PlacedBlock GetBlock(Vector3 Orig) {
			return GetBlock((int)Orig.X, (int)Orig.Y, (int)Orig.Z);
		}

		public void SetBlock(int X, int Y, int Z, PlacedBlock Block) {
			Blocks[X + ChunkSize * (Y + ChunkSize * Z)] = Block;
			Dirty = true;
		}

		public void Fill(PlacedBlock Block) {
			for (int i = 0; i < Blocks.Length; i++)
				Blocks[i] = Block;
			Dirty = true;
		}

		public void Fill(BlockType T) {
			Fill(new PlacedBlock(T));
		}

		public void MarkDirty() {
			Dirty = true;
		}

		void PrintVert(Vector3 V) {
			Console.WriteLine("new Vector3({0}, {1}, {2}) * Size + Pos,", V.X, V.Y, V.Z);
		}


		public void To3D(int Idx, out int X, out int Y, out int Z) {
			Z = Idx / (ChunkSize * ChunkSize);
			Idx -= (Z * ChunkSize * ChunkSize);
			Y = Idx / ChunkSize;
			X = Idx % ChunkSize;
		}

		bool IsCovered(int X, int Y, int Z) {
			for (int i = 0; i < Utils.MainDirs.Length; i++) {
				int XX = (int)(X + Utils.MainDirs[i].X);
				int YY = (int)(Y + Utils.MainDirs[i].Y);
				int ZZ = (int)(Z + Utils.MainDirs[i].Z);

				if (GetBlock(XX, YY, ZZ).Type == BlockType.None)
					return false;
			}

			return true;
		}

		public void ComputeLighting() {
			for (int i = 0; i < Blocks.Length; i++) {
				if (Blocks[i].Type == BlockType.None)
					continue;

				Blocks[i].SetBlockLight(new BlockLight(2));
			}

			Vector3 SunPos = new Vector3(29.63366f, 100.57468f, 23.19503f);
			Vector3 SunTgt = new Vector3(31.13366f, 63.97468f, 24.74503f);
			//Vector3  SunDir = Vector3.Normalize(SunTgt - SunPos);

			Matrix4x4 LookAtRot = Matrix4x4.CreateLookAt(SunPos, SunTgt, Vector3.UnitY);
			Vector3 Left = Vector3.Transform(Vector3.UnitX, LookAtRot);
			Vector3 Up = Vector3.Transform(Vector3.UnitY, LookAtRot);
			Vector3 Fwd = Vector3.Transform(Vector3.UnitZ, LookAtRot);

			SunDir = Fwd;
			SunRayOrigins.Clear();

			for (int yy = 0; yy < 20; yy++) {
				for (int xx = 0; xx < 20; xx++) {
					Vector3 PosOffset = (Left * (xx - 10)) + (Up * (yy - 10));

					SunRayOrigins.Add(SunPos + PosOffset);

					Vector3 HitPos = WorldMap.RaycastPos(SunPos + PosOffset, 64, Fwd, out Vector3 FaceNormal);
					if (HitPos != Vector3.Zero) {

						Vector3 BlokPos = HitPos - (FaceNormal * 0.5f);
						PlacedBlock Blk = WorldMap.GetPlacedBlock((int)BlokPos.X, (int)BlokPos.Y, (int)BlokPos.Z, out Chunk Chk);

						Blk.Lights[Utils.DirToByte(FaceNormal)] = new BlockLight(28);

					}
				}
			}

			for (int i = 0; i < Blocks.Length; i++) {
				if (Blocks[i].Type == BlockType.None)
					continue;

				PlacedBlock Block = Blocks[i];

				To3D(i, out int LocalX, out int LocalY, out int LocalZ);
				if (IsCovered(LocalX, LocalY, LocalZ))
					continue;

				WorldMap.GetWorldPos(LocalX, LocalY, LocalZ, GlobalChunkIndex, out Vector3 GlobalPos);

				int X = (int)GlobalPos.X;
				int Y = (int)GlobalPos.Y;
				int Z = (int)GlobalPos.Z;

				if (WorldMap.IsCovered(X, Y, Z))
					continue;



				// Ambient occlusion
				for (int j = 0; j < /*Utils.MainDirs.Length*/ -1; j++) {
					Vector3 Origin = new Vector3(X, Y, Z) + Utils.MainDirs[j];

					if (WorldMap.GetBlock(Origin) != BlockType.None)
						continue; // Side covered


					int AmbientLight = 5;
					if (!WorldMap.Raycast(Origin, 128, new Vector3(0, 1, 0))) {
						AmbientLight = 28;

						/*WorldMap.RaycastSphere(Origin + new Vector3(0, 0.5f, 0), 6, (Orig, Norm) => {
							PlacedBlock PB = GetBlock(Orig);
							PB.Lights[Utils.DirToByte(Norm)] = PB.Lights[Utils.DirToByte(Norm)] + 2;

							return false;
						});*/
					}

					//if (Utils.MainDirs[j] == new Vector3(0, 1, 0)) {
					const int AddLight = 2;

					//int SkyHits = WorldMap.CountHits(Origin, 128, new Vector3(0, 1, 0), out int MaxSkyHits);
					//float SkyPerc = (float)SkyHits / MaxSkyHits;

					//int MaxSkyHits = 12;
					//int SkyHits = WorldMap.CountSphereHits(Origin, 128, MaxSkyHits);
					//float SkyPerc = (float)(MaxSkyHits - SkyHits) / MaxSkyHits;

					//AmbientLight += (int)(SkyPerc * 16);

					/*if (AmbientLight < 28 && !WorldMap.Raycast(Origin, 128, Vector3.Normalize(new Vector3(1, 1, 0))))
						AmbientLight += AddLight;

					if (AmbientLight < 28 && !WorldMap.Raycast(Origin, 128, Vector3.Normalize(new Vector3(-1, 1, 0))))
						AmbientLight += AddLight;

					if (AmbientLight < 28 && !WorldMap.Raycast(Origin, 128, Vector3.Normalize(new Vector3(0, 1, 1))))
						AmbientLight += AddLight;

					if (AmbientLight < 28 && !WorldMap.Raycast(Origin, 128, Vector3.Normalize(new Vector3(0, 1, -1))))
						AmbientLight += AddLight;

					if (AmbientLight < 28 && !WorldMap.Raycast(Origin, 128, Vector3.Normalize(new Vector3(1, 1, 1))))
						AmbientLight += AddLight;

					if (AmbientLight < 28 && !WorldMap.Raycast(Origin, 128, Vector3.Normalize(new Vector3(-1, 1, 1))))
						AmbientLight += AddLight;

					if (AmbientLight < 28 && !WorldMap.Raycast(Origin, 128, Vector3.Normalize(new Vector3(1, 1, -1))))
						AmbientLight += AddLight;

					if (AmbientLight < 28 && !WorldMap.Raycast(Origin, 128, Vector3.Normalize(new Vector3(-1, 1, -1))))
						AmbientLight += AddLight;*/


					//}

					if (AmbientLight > 28)
						AmbientLight = 28;

					// float AmbientHitRatio = (float)WorldMap.CountHits((int)Origin.X, (int)Origin.Y, (int)Origin.Z, 3, Utils.MainDirs[j], out int MaxHits) / MaxHits;

					// float AmbientHitRatio = ((float)WorldMap.CountAmbientHits(Origin) - 1) / 5;

					int Light = AmbientLight;//- (int)(AmbientHitRatio * (AmbientLight - 4));

					//int Light = (int)(AmbientLight * 32) - (24 - (int)(AmbientHitRatio * 24));

					if (Light < 0)
						Light = 0;
					if (Light > 32)
						Light = 32;

					if (Light > 0)
						Block.Lights[Utils.DirToByte(Utils.MainDirs[j])] += (byte)Light;
				}

				// TODO: Actual lights

				// Set block back into world
				// SetPlacedBlock(X, Y, Z, Block, false);
			}
		}

		Color CalcAOColor(Vector3 GlobalBlockPos, Vector3 A, Vector3 B, Vector3 C) {
			/*int Hits = 0;

			if (BlockInfo.IsOpaque(WorldMap.GetBlock(GlobalBlockPos + A)))
				Hits++;

			if (BlockInfo.IsOpaque(WorldMap.GetBlock(GlobalBlockPos + B)))
				Hits++;

			if (BlockInfo.IsOpaque(WorldMap.GetBlock(GlobalBlockPos + C)))
				Hits++;

			if (Hits != 0)
				return new Color(0.8f); // 0.9f*/

			return Color.White;
		}

		void SetBlockTextureUV(BlockType BlockType, Vector3 FaceNormal, MeshBuilder Verts) {
			int BlockID = (int)BlockType - 1;

			if (BlockType == BlockType.Grass) {
				if (FaceNormal.Y == 1)
					BlockID = 240;
				else if (FaceNormal.Y == 0)
					BlockID = 241;
				else
					BlockID = 1;
			} else if (BlockType == BlockType.Grass) {
				if (FaceNormal.Y == 0)
					BlockID = 242;
				else
					BlockID = 243;
			}

			int BlockX = BlockID % AtlasSize;
			int BlockY = BlockID / AtlasSize;

			Vector2 UVSize = new Vector2(1.0f / AtlasSize, 1.0f / AtlasSize);
			Vector2 UVPos = UVSize * new Vector2(BlockX, BlockY);
			Verts.SetUVOffsetSize(UVPos + new Vector2(0, UVSize.Y), UVSize * new Vector2(1, -1));
		}

		Mesh GenMesh() {
			MeshBuilder Vertices = new MeshBuilder();
			Vector3 Size = new Vector3(BlockSize);
			Color AOColor = new Color(128, 128, 128);
			AOColor = AOColor * AOColor;

			for (int x = 0; x < ChunkSize; x++) {
				for (int y = 0; y < ChunkSize; y++) {
					for (int z = 0; z < ChunkSize; z++) {
						WorldMap.GetWorldPos(x, y, z, GlobalChunkIndex, out Vector3 GlobalBlockPos);

						PlacedBlock CurBlock = null;
						if ((CurBlock = GetBlock(x, y, z)).Type != BlockType.None) {
							Vertices.SetPositionOffset(new Vector3(x, y, z) * BlockSize);

							BlockType XPosType = GetBlock(x + 1, y, z).Type;
							BlockType XNegType = GetBlock(x - 1, y, z).Type;
							BlockType YPosType = GetBlock(x, y + 1, z).Type;
							BlockType YNegType = GetBlock(x, y - 1, z).Type;
							BlockType ZPosType = GetBlock(x, y, z + 1).Type;
							BlockType ZNegType = GetBlock(x, y, z - 1).Type;

							bool XPosSkipFace = false;
							bool XNegSkipFace = false;
							bool YPosSkipFace = false;
							bool YNegSkipFace = false;
							bool ZPosSkipFace = false;
							bool ZNegSkipFace = false;

							if (BlockInfo.IsOpaque(CurBlock.Type)) {
								XPosSkipFace = BlockInfo.IsOpaque(XPosType);
								XNegSkipFace = BlockInfo.IsOpaque(XNegType);
								YPosSkipFace = BlockInfo.IsOpaque(YPosType);
								YNegSkipFace = BlockInfo.IsOpaque(YNegType);
								ZPosSkipFace = BlockInfo.IsOpaque(ZPosType);
								ZNegSkipFace = BlockInfo.IsOpaque(ZNegType);
							}

							// X++
							if (!XPosSkipFace) {
								Vector3 CurDir = new Vector3(1, 0, 0);
								//Color FaceClr = Color.White; 
								Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();

								SetBlockTextureUV(CurBlock.Type, CurDir, Vertices);

								Vertices.Add(new Vector3(1, 1, 0), new Vector2(1, 1), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(1, 0, -1), new Vector3(1, 1, -1), new Vector3(1, 1, 0)));
								Vertices.Add(new Vector3(1, 1, 1), new Vector2(0, 1), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1)));
								Vertices.Add(new Vector3(1, 0, 1), new Vector2(0, 0), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(1, -1, 0), new Vector3(1, -1, 1), new Vector3(1, 0, 1)));
								Vertices.Add(new Vector3(1, 0, 0), new Vector2(1, 0), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(1, 0, -1), new Vector3(1, -1, -1), new Vector3(1, -1, 0)));
								Vertices.Add(new Vector3(1, 1, 0), new Vector2(1, 1), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(1, 0, -1), new Vector3(1, 1, -1), new Vector3(1, 1, 0)));
								Vertices.Add(new Vector3(1, 0, 1), new Vector2(0, 0), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(1, -1, 0), new Vector3(1, -1, 1), new Vector3(1, 0, 1)));
							}

							// X--
							if (!XNegSkipFace) {
								Vector3 CurDir = new Vector3(-1, 0, 0);
								//Color FaceClr = Color.White; 
								Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
								SetBlockTextureUV(CurBlock.Type, CurDir, Vertices);

								Vertices.Add(new Vector3(0, 1, 1), new Vector2(1, 1), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(-1, 1, 0), new Vector3(-1, 1, 1), new Vector3(-1, 0, 1)));
								Vertices.Add(new Vector3(0, 1, 0), new Vector2(0, 1), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(-1, 0, -1), new Vector3(-1, 1, -1), new Vector3(-1, 1, 0)));
								Vertices.Add(new Vector3(0, 0, 0), new Vector2(0, 0), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(-1, 0, -1), new Vector3(-1, -1, -1), new Vector3(-1, -1, 0)));
								Vertices.Add(new Vector3(0, 0, 1), new Vector2(1, 0), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(-1, -1, 0), new Vector3(-1, -1, 1), new Vector3(-1, 0, 1)));
								Vertices.Add(new Vector3(0, 1, 1), new Vector2(1, 1), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(-1, 1, 0), new Vector3(-1, 1, 1), new Vector3(-1, 0, 1)));
								Vertices.Add(new Vector3(0, 0, 0), new Vector2(0, 0), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(-1, 0, -1), new Vector3(-1, -1, -1), new Vector3(-1, -1, 0)));
							}

							// Y++
							if (!YPosSkipFace) {
								Vector3 CurDir = new Vector3(0, 1, 0);
								//Color FaceClr = Color.White; 
								Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
								SetBlockTextureUV(CurBlock.Type, CurDir, Vertices);

								Vertices.Add(new Vector3(1, 1, 0), new Vector2(1, 1), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(0, 1, -1), new Vector3(1, 1, -1), new Vector3(1, 1, 0)));
								Vertices.Add(new Vector3(0, 1, 0), new Vector2(0, 1), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(0, 1, -1), new Vector3(-1, 1, -1), new Vector3(-1, 1, 0)));
								Vertices.Add(new Vector3(0, 1, 1), new Vector2(0, 0), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(-1, 1, 0), new Vector3(-1, 1, 1), new Vector3(0, 1, 1)));
								Vertices.Add(new Vector3(1, 1, 1), new Vector2(1, 0), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(0, 1, 1)));
								Vertices.Add(new Vector3(1, 1, 0), new Vector2(1, 1), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(0, 1, -1), new Vector3(1, 1, -1), new Vector3(1, 1, 0)));
								Vertices.Add(new Vector3(0, 1, 1), new Vector2(0, 0), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(-1, 1, 0), new Vector3(-1, 1, 1), new Vector3(0, 1, 1)));
							}

							// Y--
							if (!YNegSkipFace) {
								Vector3 CurDir = new Vector3(0, -1, 0);
								//Color FaceClr = Color.White; 
								Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
								SetBlockTextureUV(CurBlock.Type, CurDir, Vertices);

								Vertices.Add(new Vector3(1, 0, 1), new Vector2(0, 0), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(0, -1, 1), new Vector3(1, -1, 1), new Vector3(1, -1, 0)));
								Vertices.Add(new Vector3(0, 0, 1), new Vector2(1, 0), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(0, -1, 1), new Vector3(-1, -1, 1), new Vector3(-1, -1, 0)));
								Vertices.Add(new Vector3(0, 0, 0), new Vector2(1, 1), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(-1, -1, 0), new Vector3(-1, -1, -1), new Vector3(0, -1, -1)));
								Vertices.Add(new Vector3(1, 0, 0), new Vector2(0, 1), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(1, -1, 0), new Vector3(1, -1, -1), new Vector3(0, -1, -1)));
								Vertices.Add(new Vector3(1, 0, 1), new Vector2(0, 0), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(0, -1, 1), new Vector3(1, -1, 1), new Vector3(1, -1, 0)));
								Vertices.Add(new Vector3(0, 0, 0), new Vector2(1, 1), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(-1, -1, 0), new Vector3(-1, -1, -1), new Vector3(0, -1, -1)));
							}

							// Z++
							if (!ZPosSkipFace) {
								Vector3 CurDir = new Vector3(0, 0, 1);
								//Color FaceClr = Color.White; 
								Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
								SetBlockTextureUV(CurBlock.Type, CurDir, Vertices);

								Vertices.Add(new Vector3(1, 0, 1), new Vector2(1, 0), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(0, -1, 1), new Vector3(1, -1, 1), new Vector3(1, 0, 1)));
								Vertices.Add(new Vector3(1, 1, 1), new Vector2(1, 1), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(0, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 0, 1)));
								Vertices.Add(new Vector3(0, 1, 1), new Vector2(0, 1), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(0, 1, 1), new Vector3(-1, 1, 1), new Vector3(-1, 0, 1)));
								Vertices.Add(new Vector3(0, 0, 1), new Vector2(0, 0), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(-1, 0, 1), new Vector3(-1, -1, 1), new Vector3(0, -1, 1)));
								Vertices.Add(new Vector3(1, 0, 1), new Vector2(1, 0), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(0, -1, 1), new Vector3(1, -1, 1), new Vector3(1, 0, 1)));
								Vertices.Add(new Vector3(0, 1, 1), new Vector2(0, 1), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(0, 1, 1), new Vector3(-1, 1, 1), new Vector3(-1, 0, 1)));
							}

							// Z--
							if (!ZNegSkipFace) {
								Vector3 CurDir = new Vector3(0, 0, -1);
								//Color FaceClr = Color.White; 
								Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
								SetBlockTextureUV(CurBlock.Type, CurDir, Vertices);

								Vertices.Add(new Vector3(1, 1, 0), new Vector2(0, 1), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(0, 1, -1), new Vector3(1, 1, -1), new Vector3(1, 0, -1)));
								Vertices.Add(new Vector3(1, 0, 0), new Vector2(0, 0), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(0, -1, -1), new Vector3(1, -1, -1), new Vector3(1, 0, -1)));
								Vertices.Add(new Vector3(0, 0, 0), new Vector2(1, 0), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(0, -1, -1), new Vector3(-1, -1, -1), new Vector3(-1, 0, -1)));
								Vertices.Add(new Vector3(0, 1, 0), new Vector2(1, 1), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(-1, 0, -1), new Vector3(-1, 1, -1), new Vector3(0, 1, -1)));
								Vertices.Add(new Vector3(1, 1, 0), new Vector2(0, 1), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(0, 1, -1), new Vector3(1, 1, -1), new Vector3(1, 0, -1)));
								Vertices.Add(new Vector3(0, 0, 0), new Vector2(1, 0), FaceClr * CalcAOColor(GlobalBlockPos, new Vector3(0, -1, -1), new Vector3(-1, -1, -1), new Vector3(-1, 0, -1)));
							}
						}

						//GraphicsUtils.AppendBlockVertices(Verts, new Vector3(x, y, z) * BlockSize, new Vector3(BlockSize));
					}
				}
			}

			return Raylib.GenMeshRaw(Vertices.ToArray());
		}

		Model GetModel() {
			if (!Dirty)
				return CachedModelOpaque;

			Dirty = false;

			if (ModelValid) {
				CachedModelOpaque.materials[0].maps[0].texture.id = 1;
				Raylib.UnloadModel(CachedModelOpaque);
			}

			CachedMeshOpaque = GenMesh();
			CachedModelOpaque = Raylib.LoadModelFromMesh(CachedMeshOpaque);
			CachedModelOpaque.materials[0].maps[0].texture = ResMgr.AtlasTexture;

			//ComputeLighting();

			ModelValid = true;
			return CachedModelOpaque;
		}

		public void Draw(Vector3 Position) {
			Raylib.DrawModel(GetModel(), Position, BlockSize, ChunkColor);
		}

		public void DrawTransparent(Vector3 Position) {

		}
	}
}
