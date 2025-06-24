using Raylib_cs;
using System.Numerics;

using Voxelgine.Engine;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Voxelgine.Graphics {
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

		//List<Vector3> SunRayOrigins = new List<Vector3>();
		//Vector3 SunDir = -Vector3.UnitY;

		//public Vector3 Position;

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

		/*public void SetPosition(Vector3 Pos) {
			Position = Pos;
		}*/

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

			/*SunDir = Fwd;
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
			}*/

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
			int Hits = 0;

			if (BlockInfo.IsOpaque(WorldMap.GetBlock(GlobalBlockPos + A)))
				Hits++;

			if (BlockInfo.IsOpaque(WorldMap.GetBlock(GlobalBlockPos + B)))
				Hits++;

			if (BlockInfo.IsOpaque(WorldMap.GetBlock(GlobalBlockPos + C)))
				Hits++;

			if (Hits != 0)
				return Utils.Color(1.0f - (Hits * 0.2f)); // 0.9f*/

			return Utils.Color(1.0f);

			//return Color.White;
		}

		void SetBlockTextureUV(BlockType BlockType, Vector3 FaceNormal, MeshBuilder Verts) {
			int BlockID = BlockInfo.GetBlockID(BlockType, FaceNormal);
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
			AOColor = Utils.ColorMul(AOColor, AOColor);

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

							if (BlockInfo.CustomModel(CurBlock.Type)) {
								if (!XPosSkipFace || !XNegSkipFace || !YPosSkipFace || !YNegSkipFace || !ZPosSkipFace || !ZNegSkipFace) {

									Model Mdl = BlockInfo.GetCustomModel(CurBlock.Type);

									SetBlockTextureUV(CurBlock.Type, Vector3.UnitY, Vertices);

									for (int j = 0; j < Mdl.MeshCount; j++) {
										for (int i = 0; i < Mdl.Meshes[j].VertexCount; i++) {
											Vector3 Vert = ((Vector3*)Mdl.Meshes[j].Vertices)[i];
											Vector2 UV = new Vector2(0, 1) + ((Vector2*)Mdl.Meshes[j].TexCoords)[i] * new Vector2(1, -1);
											Vertices.Add(Vert + new Vector3(0.5f, 0, 0.5f), UV, Vector3.Zero, Color.White);
										}
									}



									Console.WriteLine("!");

								}
							} else {

								// X++
								if (!XPosSkipFace) {
									Vector3 CurDir = new Vector3(1, 0, 0);
									//Color FaceClr = Color.White; 
									Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();

									SetBlockTextureUV(CurBlock.Type, CurDir, Vertices);

									Vertices.Add(new Vector3(1, 1, 0), new Vector2(1, 1), new Vector3(1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(1, 0, -1), new Vector3(1, 1, -1), new Vector3(1, 1, 0))));
									Vertices.Add(new Vector3(1, 1, 1), new Vector2(0, 1), new Vector3(1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(1, 0, 1))));
									Vertices.Add(new Vector3(1, 0, 1), new Vector2(0, 0), new Vector3(1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(1, -1, 0), new Vector3(1, -1, 1), new Vector3(1, 0, 1))));
									Vertices.Add(new Vector3(1, 0, 0), new Vector2(1, 0), new Vector3(1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(1, 0, -1), new Vector3(1, -1, -1), new Vector3(1, -1, 0))));
									Vertices.Add(new Vector3(1, 1, 0), new Vector2(1, 1), new Vector3(1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(1, 0, -1), new Vector3(1, 1, -1), new Vector3(1, 1, 0))));
									Vertices.Add(new Vector3(1, 0, 1), new Vector2(0, 0), new Vector3(1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(1, -1, 0), new Vector3(1, -1, 1), new Vector3(1, 0, 1))));
								}

								// X--
								if (!XNegSkipFace) {
									Vector3 CurDir = new Vector3(-1, 0, 0);
									//Color FaceClr = Color.White; 
									Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
									SetBlockTextureUV(CurBlock.Type, CurDir, Vertices);

									Vertices.Add(new Vector3(0, 1, 1), new Vector2(1, 1), new Vector3(-1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 1, 0), new Vector3(-1, 1, 1), new Vector3(-1, 0, 1))));
									Vertices.Add(new Vector3(0, 1, 0), new Vector2(0, 1), new Vector3(-1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 0, -1), new Vector3(-1, 1, -1), new Vector3(-1, 1, 0))));
									Vertices.Add(new Vector3(0, 0, 0), new Vector2(0, 0), new Vector3(-1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 0, -1), new Vector3(-1, -1, -1), new Vector3(-1, -1, 0))));
									Vertices.Add(new Vector3(0, 0, 1), new Vector2(1, 0), new Vector3(-1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, -1, 0), new Vector3(-1, -1, 1), new Vector3(-1, 0, 1))));
									Vertices.Add(new Vector3(0, 1, 1), new Vector2(1, 1), new Vector3(-1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 1, 0), new Vector3(-1, 1, 1), new Vector3(-1, 0, 1))));
									Vertices.Add(new Vector3(0, 0, 0), new Vector2(0, 0), new Vector3(-1, 0, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 0, -1), new Vector3(-1, -1, -1), new Vector3(-1, -1, 0))));
								}

								// Y++
								if (!YPosSkipFace) {
									Vector3 CurDir = new Vector3(0, 1, 0);
									//Color FaceClr = Color.White; 
									Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
									SetBlockTextureUV(CurBlock.Type, CurDir, Vertices);

									Vertices.Add(new Vector3(1, 1, 0), new Vector2(1, 1), new Vector3(0, 1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, 1, -1), new Vector3(1, 1, -1), new Vector3(1, 1, 0))));
									Vertices.Add(new Vector3(0, 1, 0), new Vector2(0, 1), new Vector3(0, 1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, 1, -1), new Vector3(-1, 1, -1), new Vector3(-1, 1, 0))));
									Vertices.Add(new Vector3(0, 1, 1), new Vector2(0, 0), new Vector3(0, 1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 1, 0), new Vector3(-1, 1, 1), new Vector3(0, 1, 1))));
									Vertices.Add(new Vector3(1, 1, 1), new Vector2(1, 0), new Vector3(0, 1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(1, 1, 0), new Vector3(1, 1, 1), new Vector3(0, 1, 1))));
									Vertices.Add(new Vector3(1, 1, 0), new Vector2(1, 1), new Vector3(0, 1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, 1, -1), new Vector3(1, 1, -1), new Vector3(1, 1, 0))));
									Vertices.Add(new Vector3(0, 1, 1), new Vector2(0, 0), new Vector3(0, 1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 1, 0), new Vector3(-1, 1, 1), new Vector3(0, 1, 1))));
								}

								// Y--
								if (!YNegSkipFace) {
									Vector3 CurDir = new Vector3(0, -1, 0);
									//Color FaceClr = Color.White; 
									Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
									SetBlockTextureUV(CurBlock.Type, CurDir, Vertices);

									Vertices.Add(new Vector3(1, 0, 1), new Vector2(0, 0), new Vector3(0, -1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, -1, 1), new Vector3(1, -1, 1), new Vector3(1, -1, 0))));
									Vertices.Add(new Vector3(0, 0, 1), new Vector2(1, 0), new Vector3(0, -1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, -1, 1), new Vector3(-1, -1, 1), new Vector3(-1, -1, 0))));
									Vertices.Add(new Vector3(0, 0, 0), new Vector2(1, 1), new Vector3(0, -1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, -1, 0), new Vector3(-1, -1, -1), new Vector3(0, -1, -1))));
									Vertices.Add(new Vector3(1, 0, 0), new Vector2(0, 1), new Vector3(0, -1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(1, -1, 0), new Vector3(1, -1, -1), new Vector3(0, -1, -1))));
									Vertices.Add(new Vector3(1, 0, 1), new Vector2(0, 0), new Vector3(0, -1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, -1, 1), new Vector3(1, -1, 1), new Vector3(1, -1, 0))));
									Vertices.Add(new Vector3(0, 0, 0), new Vector2(1, 1), new Vector3(0, -1, 0), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, -1, 0), new Vector3(-1, -1, -1), new Vector3(0, -1, -1))));
								}

								// Z++
								if (!ZPosSkipFace) {
									Vector3 CurDir = new Vector3(0, 0, 1);
									//Color FaceClr = Color.White; 
									Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
									SetBlockTextureUV(CurBlock.Type, CurDir, Vertices);

									Vertices.Add(new Vector3(1, 0, 1), new Vector2(1, 0), new Vector3(0, 0, 1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, -1, 1), new Vector3(1, -1, 1), new Vector3(1, 0, 1))));
									Vertices.Add(new Vector3(1, 1, 1), new Vector2(1, 1), new Vector3(0, 0, 1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, 1, 1), new Vector3(1, 1, 1), new Vector3(1, 0, 1))));
									Vertices.Add(new Vector3(0, 1, 1), new Vector2(0, 1), new Vector3(0, 0, 1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, 1, 1), new Vector3(-1, 1, 1), new Vector3(-1, 0, 1))));
									Vertices.Add(new Vector3(0, 0, 1), new Vector2(0, 0), new Vector3(0, 0, 1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 0, 1), new Vector3(-1, -1, 1), new Vector3(0, -1, 1))));
									Vertices.Add(new Vector3(1, 0, 1), new Vector2(1, 0), new Vector3(0, 0, 1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, -1, 1), new Vector3(1, -1, 1), new Vector3(1, 0, 1))));
									Vertices.Add(new Vector3(0, 1, 1), new Vector2(0, 1), new Vector3(0, 0, 1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, 1, 1), new Vector3(-1, 1, 1), new Vector3(-1, 0, 1))));
								}

								// Z--
								if (!ZNegSkipFace) {
									Vector3 CurDir = new Vector3(0, 0, -1);
									//Color FaceClr = Color.White; 
									Color FaceClr = CurBlock.GetBlockLight(CurDir).ToColor();
									SetBlockTextureUV(CurBlock.Type, CurDir, Vertices);

									Vertices.Add(new Vector3(1, 1, 0), new Vector2(0, 1), new Vector3(0, 0, -1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, 1, -1), new Vector3(1, 1, -1), new Vector3(1, 0, -1))));
									Vertices.Add(new Vector3(1, 0, 0), new Vector2(0, 0), new Vector3(0, 0, -1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, -1, -1), new Vector3(1, -1, -1), new Vector3(1, 0, -1))));
									Vertices.Add(new Vector3(0, 0, 0), new Vector2(1, 0), new Vector3(0, 0, -1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, -1, -1), new Vector3(-1, -1, -1), new Vector3(-1, 0, -1))));
									Vertices.Add(new Vector3(0, 1, 0), new Vector2(1, 1), new Vector3(0, 0, -1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(-1, 0, -1), new Vector3(-1, 1, -1), new Vector3(0, 1, -1))));
									Vertices.Add(new Vector3(1, 1, 0), new Vector2(0, 1), new Vector3(0, 0, -1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, 1, -1), new Vector3(1, 1, -1), new Vector3(1, 0, -1))));
									Vertices.Add(new Vector3(0, 0, 0), new Vector2(1, 0), new Vector3(0, 0, -1), Utils.ColorMul(FaceClr, CalcAOColor(GlobalBlockPos, new Vector3(0, -1, -1), new Vector3(-1, -1, -1), new Vector3(-1, 0, -1))));
								}
							}
						}

						//GraphicsUtils.AppendBlockVertices(Verts, new Vector3(x, y, z) * BlockSize, new Vector3(BlockSize));
					}
				}
			}

			/*//Mesh M = new Mesh();
			Mesh M = Raylib.GenMeshCube(1, 1, 1);
			M.Normals = null;

			Vertex3[] Verts = Vertices.ToArray();
			M.TriangleCount = Verts.Length / 3;
			M.VertexCount = Verts.Length;
			M.AllocVertices();
			M.AllocTexCoords();
			M.AllocIndices();

			M.Colors = (byte*)Marshal.AllocHGlobal(sizeof(Vector4) * Verts.Length);


			for (int i = 0; i < Verts.Length; i++) {
				M.Indices[i] = (ushort)i;
				((Vector3*)M.Vertices)[i] = Verts[i].Position;
				((Vector2*)M.TexCoords)[i] = Verts[i].UV;
				((Vector4*)M.Colors)[i] = Utils.ColorVec(Verts[i].Color);
			}

			Raylib.UploadMesh(ref M, false);*/

			return Vertices.ToMesh();
		}

		Model GetModel() {
			if (!Dirty)
				return CachedModelOpaque;

			Dirty = false;

			if (ModelValid) {
				// Set texture ID to 1 to disable texture unloading? Does that even do anything?
				CachedModelOpaque.Materials[0].Maps[0].Texture.Id = 0;
				Raylib.UnloadModel(CachedModelOpaque);
			}

			CachedMeshOpaque = GenMesh();
			CachedModelOpaque = Raylib.LoadModelFromMesh(CachedMeshOpaque);
			CachedModelOpaque.Materials[0].Maps[0].Texture = ResMgr.AtlasTexture;
			CachedModelOpaque.Materials[0].Shader = ResMgr.GetShader("default");

			//ComputeLighting();

			ModelValid = true;
			return CachedModelOpaque;
		}

		public RayCollision Collide(Vector3 ChunkPosition, Ray R) {
			Matrix4x4 Transform = Matrix4x4.Transpose(Matrix4x4.CreateTranslation(ChunkPosition));
			return Raylib.GetRayCollisionMesh(R, CachedMeshOpaque, Transform);
		}

		public void Draw(Vector3 ChunkPosition) {
			Raylib.DrawModel(GetModel(), ChunkPosition, BlockSize, ChunkColor);
		}

		public void DrawTransparent() {

		}
	}
}
