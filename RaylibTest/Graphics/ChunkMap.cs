using Newtonsoft.Json;

using RaylibSharp;

using RaylibTest.Engine;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RaylibTest.Graphics {
	struct GlobalPlacedBlock {
		public Vector3 GlobalPos;
		public PlacedBlock Block;
		public Chunk Chunk;

		public GlobalPlacedBlock(Vector3 GlobalPos, PlacedBlock Block, Chunk Chunk) {
			this.GlobalPos = GlobalPos;
			this.Block = Block;
			this.Chunk = Chunk;
		}
	}

	class ChunkMap {
		Dictionary<Vector3, Chunk> Chunks;
		Random Rnd = new Random();

		public ChunkMap() {
			Chunks = new Dictionary<Vector3, Chunk>();
		}

		/*public void LoadFromChunk(string FileName) {
			string[] Lines = File.ReadAllText(FileName).Replace("\r", "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
			ChunkPalette Palette = new ChunkPalette();

			foreach (var L in Lines) {
				string Line = L.Trim();
				if (Line.StartsWith("#"))
					continue;

				string[] XYZT = Line.Split(new[] { ' ' });

				// Swapped for reasons
				int Z = int.Parse(XYZT[0]);
				int X = int.Parse(XYZT[1]);
				int Y = int.Parse(XYZT[2]);

				SetBlock(X, Y, Z, Palette.GetBlock(XYZT[3]));
			}
		}*/

		public void Write(Stream Output) {
			using (GZipStream ZipStream = new GZipStream(Output, CompressionMode.Compress, true)) {
				using (BinaryWriter Writer = new BinaryWriter(ZipStream)) {
					KeyValuePair<Vector3, Chunk>[] ChunksArray = Chunks.ToArray();
					Writer.Write(ChunksArray.Length);

					for (int i = 0; i < ChunksArray.Length; i++) {
						Writer.Write((int)ChunksArray[i].Key.X);
						Writer.Write((int)ChunksArray[i].Key.Y);
						Writer.Write((int)ChunksArray[i].Key.Z);

						ChunksArray[i].Value.Write(Writer);
					}
				}
			}
		}

		public void Read(Stream Input) {
			using (GZipStream ZipStream = new GZipStream(Input, CompressionMode.Decompress, true)) {
				using (BinaryReader Reader = new BinaryReader(ZipStream)) {
					int Count = Reader.ReadInt32();

					for (int i = 0; i < Count; i++) {
						int CX = Reader.ReadInt32();
						int CY = Reader.ReadInt32();
						int CZ = Reader.ReadInt32();
						Vector3 ChunkIndex = new Vector3(CX, CY, CZ);

						Chunk Chk = new Chunk(ChunkIndex, this);
						Chk.Read(Reader);
						Chunks.Add(ChunkIndex, Chk);
					}
				}
			}
		}

		float Simplex(int Octaves, float X, float Y, float Z, float Scale) {
			float Val = 0.0f;

			for (int i = 0; i < Octaves; i++)
				Val += Noise.CalcPixel3D(X * Math.Pow(2, i), Y * Math.Pow(2, i), Z * Math.Pow(2, i), Scale);

			return (Val / Octaves) / 255;
		}

		void MinMax(float Val, ref float Min, ref float Max) {
			if (Val < Min)
				Min = Val;

			if (Val > Max)
				Max = Val;
		}

		public Chunk[] GetAllChunks() {
			return Chunks.Values.ToArray();
		}

		public void GenerateFloatingIsland(int Width, int Length, int Seed = 666) {
			Noise.Seed = Seed;
			float Scale = 0.02f;
			int WorldHeight = 64;

			Vector3 Center = new Vector3(Width, 0, Length) / 2;
			float CenterRadius = Math.Min(Width / 2, Length / 2);

			for (int x = 0; x < Width; x++)
				for (int z = 0; z < Length; z++)
					for (int y = 0; y < WorldHeight; y++) {
						//float YScale = 1.0f - (float)Math.Pow((float)y / WorldHeight, 0.5);

						Vector3 Pos = new Vector3(x, (WorldHeight - y), z);
						float CenterFalloff = 1.0f - Utils.Clamp(((Center - Pos).Length() / CenterRadius) / 1.2f, 0, 1);

						float Height = (float)y / WorldHeight;
						// float HeightScale = Utils.Clamp(Height * 0.5f, 0.0f, 1.0f) * 256;

						const float HeightFallStart = 0.8f;
						const float HeightFallEnd = 1.0f;
						const float HeightFallRange = HeightFallEnd - HeightFallStart;

						float HeightFalloff = 1;
						if (Height <= HeightFallStart)
							HeightFalloff = 1.0f;
						else if (Height > HeightFallStart && Height < HeightFallEnd)
							HeightFalloff = 1.0f - (Height - HeightFallStart) * (HeightFallRange * 10);
						else
							HeightFalloff = 0;

						float Density = Simplex(2, x, y * 0.5f, z, Scale) * CenterFalloff * HeightFalloff;

						if (Density > 0.1f) {
							float Caves = Simplex(1, x, y, z, Scale * 4) * HeightFalloff;

							if (Caves < 0.65f)
								SetBlock(x, y, z, BlockType.Stone);
						}
					}

			for (int x = 0; x < Width; x++)
				for (int z = 0; z < Length; z++) {
					int DownRayHits = 0;

					for (int y = WorldHeight - 1; y >= 0; y--) {

						if (GetBlock(x, y, z) != BlockType.None) {
							DownRayHits++;

							if (DownRayHits == 1)
								SetBlock(x, y, z, BlockType.Grass);
							else if (DownRayHits < 5)
								SetBlock(x, y, z, BlockType.Dirt);
						} else {
							if (DownRayHits != 0)
								break;
						}
					}
				}

			ComputeLighting();
		}

		void TransPosScalar(int S, out int ChunkIndex, out int BlockPos) {
			ChunkIndex = (int)Math.Floor((float)S / Chunk.ChunkSize);
			BlockPos = Utils.Mod(S, Chunk.ChunkSize);
		}

		void TranslateChunkPos(int X, int Y, int Z, out Vector3 ChunkIndex, out Vector3 BlockPos) {
			TransPosScalar(X, out int ChkX, out int BlkX);
			TransPosScalar(Y, out int ChkY, out int BlkY);
			TransPosScalar(Z, out int ChkZ, out int BlkZ);

			ChunkIndex = new Vector3(ChkX, ChkY, ChkZ);
			BlockPos = new Vector3(BlkX, BlkY, BlkZ);
		}

		public void GetWorldPos(int X, int Y, int Z, Vector3 ChunkIndex, out Vector3 GlobalPos) {
			GlobalPos = ChunkIndex * Chunk.ChunkSize + new Vector3(X, Y, Z);
		}

		void MarkDirty(int ChunkX, int ChunkY, int ChunkZ) {
			Vector3 ChunkIndex = new Vector3(ChunkX, ChunkY, ChunkZ);

			if (Chunks.ContainsKey(ChunkIndex))
				Chunks[ChunkIndex].MarkDirty();
		}

		public void SetPlacedBlock(int X, int Y, int Z, PlacedBlock Block) {
			TranslateChunkPos(X, Y, Z, out Vector3 ChunkIndex, out Vector3 BlockPos);
			int XX = (int)BlockPos.X;
			int YY = (int)BlockPos.Y;
			int ZZ = (int)BlockPos.Z;
			int CX = (int)ChunkIndex.X;
			int CY = (int)ChunkIndex.Y;
			int CZ = (int)ChunkIndex.Z;

			const int MaxBlock = Chunk.ChunkSize - 1;

			// Edge cases, literally
			if (XX == 0)
				MarkDirty(CX - 1, CY, CZ);
			if (YY == 0)
				MarkDirty(CX, CY - 1, CZ);
			if (ZZ == 0)
				MarkDirty(CX, CY, CZ - 1);
			if (XX == MaxBlock)
				MarkDirty(CX + 1, CY, CZ);
			if (YY == MaxBlock)
				MarkDirty(CX, CY + 1, CZ);
			if (ZZ == MaxBlock)
				MarkDirty(CX, CY, CZ + 1);

			// Corners
			if (XX == 0 && YY == 0 && ZZ == 0)
				MarkDirty(CX - 1, CY - 1, CZ - 1);
			if (XX == 0 && YY == 0 && ZZ == MaxBlock)
				MarkDirty(CX - 1, CY - 1, CZ + 1);
			if (XX == 0 && YY == MaxBlock && ZZ == 0)
				MarkDirty(CX - 1, CY + 1, CZ - 1);
			if (XX == 0 && YY == MaxBlock && ZZ == MaxBlock)
				MarkDirty(CX - 1, CY + 1, CZ + 1);
			if (XX == MaxBlock && YY == 0 && ZZ == 0)
				MarkDirty(CX + 1, CY - 1, CZ - 1);
			if (XX == MaxBlock && YY == 0 && ZZ == MaxBlock)
				MarkDirty(CX + 1, CY - 1, CZ + 1);
			if (XX == MaxBlock && YY == MaxBlock && ZZ == 0)
				MarkDirty(CX + 1, CY + 1, CZ - 1);
			if (XX == MaxBlock && YY == MaxBlock && ZZ == MaxBlock)
				MarkDirty(CX + 1, CY + 1, CZ + 1);

			// Diagonals
			if (XX == 0 && YY == 0)
				MarkDirty(CX - 1, CY - 1, CZ);
			if (XX == MaxBlock && YY == MaxBlock)
				MarkDirty(CX + 1, CY + 1, CZ);
			if (XX == 0 && YY == MaxBlock)
				MarkDirty(CX - 1, CY + 1, CZ);
			if (XX == MaxBlock && YY == 0)
				MarkDirty(CX + 1, CY - 1, CZ);
			if (YY == 0 && ZZ == 0)
				MarkDirty(CX, CY - 1, CZ - 1);
			if (YY == MaxBlock && ZZ == MaxBlock)
				MarkDirty(CX, CY + 1, CZ + 1);
			if (YY == 0 && ZZ == MaxBlock)
				MarkDirty(CX, CY - 1, CZ + 1);
			if (YY == MaxBlock && ZZ == 0)
				MarkDirty(CX, CY + 1, CZ - 1);



			/*for (int x = -1; x < 2; x++)
				for (int y = -1; y < 2; y++)
					for (int z = -1; z < 2; z++)
						MarkDirty(ChunkIndex + new Vector3(x, y, z));*/

			if (!Chunks.ContainsKey(ChunkIndex)) {
				Chunk Chk = new Chunk(ChunkIndex, this);
				Chunks.Add(ChunkIndex, Chk);
			}

			Chunks[ChunkIndex].SetBlock(XX, YY, ZZ, Block);
		}

		public void SetBlock(int X, int Y, int Z, BlockType T) {
			/*if (BlockInfo.EmitsLight(T)) {
				SetPlacedBlock(X, Y, Z, new PlacedBlock(T, BlockLight.FullBright));

				Vector3 Origin = new Vector3(X, Y, Z);
				int CastDist = 20;

				Utils.RaycastSphere(Origin, CastDist, (XX, YY, ZZ, Norm) => {
					PlacedBlock Cur = GetPlacedBlock(XX, YY, ZZ, out Chunk Chk);

					// Ray hit something solid
					if (BlockInfo.IsOpaque(Cur.Type)) {
						float Dist = (new Vector3(XX, YY, ZZ) - Origin).Length();
						float Amt = Utils.Clamp(1.0f - (Dist / CastDist), 0, 1);

						Cur.Lights[Utils.DirToByte(Norm)].SetMin((byte)(Amt * 32));
						Chk.MarkDirty();
						//return true;
					}

					return false;
				}, 256);
			} else*/

			SetPlacedBlock(X, Y, Z, new PlacedBlock(T));

			//ComputeLighting();
		}

		IEnumerable<GlobalPlacedBlock> GetAllExistingBlocks() {
			foreach (var C in Chunks) {
				for (int X = 0; X < Chunk.ChunkSize; X++) {
					for (int Y = 0; Y < Chunk.ChunkSize; Y++) {
						for (int Z = 0; Z < Chunk.ChunkSize; Z++) {
							PlacedBlock CurBlock = C.Value.GetBlock(X, Y, Z);
							if (CurBlock.Type == BlockType.None)
								continue;

							GetWorldPos(X, Y, Z, C.Key, out Vector3 GlobalPos);
							yield return new GlobalPlacedBlock(GlobalPos, CurBlock, C.Value);
						}
					}
				}
			}
		}

		public int CountHits(int X, int Y, int Z, int Distance, Vector3 Dir, out int MaxHits) {
			int Hits = Utils.RaycastHalfSphere(new Vector3(X, Y, Z), Dir, Distance, (Orig, Face) => {
				if (GetBlock(Orig) != BlockType.None)
					return true;

				return false;
			}, out MaxHits, 12);

			return Hits;
		}

		public int CountSphereHits(int X, int Y, int Z, int Distance, int Slices = 16) {
			int Hits = Utils.RaycastSphere(new Vector3(X, Y, Z), Distance, (Orig, Face) => {
				if (GetBlock(Orig) != BlockType.None)
					return true;

				return false;
			}, Slices);

			return Hits;
		}

		public int CountSphereHits(Vector3 Origin, int Distance, int Slices = 16) {
			return CountSphereHits((int)Origin.X, (int)Origin.Y, (int)Origin.Z, Distance, Slices);
		}

		public int CountHits(Vector3 Origin, int Distance, Vector3 Dir, out int MaxHits) {
			return CountHits((int)Origin.X, (int)Origin.Y, (int)Origin.Z, Distance, Dir, out MaxHits);
		}

		bool Chk(int X, int Y, int Z) {
			return GetBlock(X, Y, Z) != BlockType.None;
		}

		IEnumerable<Vector3> GetPlaneNeighbours(int X, int Y, int Z, Vector3 faceNormal) {
			//Vector3 BlockPos = new Vector3(X, Y, Z) + new Vector3(0.5f);

			int Range = 1;

			for (int yy = -Range; yy < Range + 1; yy++) {
				for (int xx = -Range; xx < Range + 1; xx++) {
					if (faceNormal.Y == 1 || faceNormal.Y == -1) {
						bool C1 = Chk(X + xx, Y, Z + yy);
						bool C2 = Chk(X + xx, (int)(Y + faceNormal.Y), Z + yy);

						if (C1 && !C2)
							yield return new Vector3(X + xx, Y, Z + yy);
						/*else if (C1 && C2)
							yield break;*/

					} else if (faceNormal.X == 1 || faceNormal.X == -1) {
						bool C1 = Chk(X, Y + xx, Z + yy);
						bool C2 = Chk((int)(X + faceNormal.X), Y + xx, Z + yy);

						if (C1 && !C2)
							yield return new Vector3(X, Y + xx, Z + yy);
						/*else if (C1 && C2)
							yield break;*/

					} else if (faceNormal.Z == 1 || faceNormal.Z == -1) {
						bool C1 = Chk(X + xx, Y + yy, Z);
						bool C2 = Chk(X + xx, Y + yy, (int)(Z + faceNormal.Z));

						if (C1 && !C2)
							yield return new Vector3(X + xx, Y + yy, Z);
						/*else if (C1 && C2)
							yield break;*/

					}
				}



			}
		}

		public int RayTest(Vector3 Start, Vector3 End, Vector3[] IgnorePos = null, BlockType[] IgnoreBlocks = null) {
			//Start = new Vector3((int)Start.X, (int)Start.Y, (int)Start.Z);
			//End = new Vector3((int)End.X, (int)End.Y, (int)End.Z);

			Vector3 Half = Vector3.Zero; //new Vector3(0.5f, 0.5f, 0.5f);
			Start += Half;
			End += Half;

			float Dist = (End - Start).Length() - 0f;

			// Dist = Math.Max(0.1f, Dist);
			int Count = 0;

			Vector3 Dir = Vector3.Normalize(End - Start);
			Utils.Raycast2(Start, Dir, Dist, 10, (HitPos, Face) => {
				BlockType BT = BlockType.None;

				if (IgnorePos != null) {
					foreach (var Ign in IgnorePos) {
						if ((int)Ign.X == (int)HitPos.X && (int)Ign.Y == (int)HitPos.Y && (int)Ign.Z == (int)HitPos.Z)
							return false;
					}
				}

				if ((BT = GetBlock((int)HitPos.X, (int)HitPos.Y, (int)HitPos.Z)) != BlockType.None) {
					if (IgnoreBlocks != null && IgnoreBlocks.Contains(BT))
						return false;

					//return true;
					Count++;
					return true;
				}

				return false;
			});

			return Count;
		}

		public bool Raycast(int X, int Y, int Z, float Distance, Vector3 Dir) {
			/*return Utils.Raycast(new Vector3(X, Y, Z), Dir, Distance, (XX, YY, ZZ, Face) => {
				if (GetBlock(XX, YY, ZZ) != BlockType.None)
					return true;

				return false;
			});*/


			if (Utils.Raycast2(new Vector3(X, Y, Z), Dir, Distance, 10, (HitPos, Face) => {
				if (GetBlock((int)HitPos.X, (int)HitPos.Y, (int)HitPos.Z) != BlockType.None) {
					return true;
				}

				return false;
			})) {
				return true;
			}

			return false;
		}

		public bool Raycast(Vector3 Origin, float Distance, Vector3 Dir) {
			return Raycast((int)Origin.X, (int)Origin.Y, (int)Origin.Z, Distance, Dir);
		}

		public Vector3 RaycastPos(Vector3 Origin, float Distance, Vector3 Dir, out Vector3 FaceDir) {
			Vector3 RetPos = Vector3.Zero;
			Vector3 OutFaceDir = Vector3.Zero;

			if (Utils.Raycast2(Origin, Dir, Distance, 20, (HitPos, Face) => {
				if (Face == Vector3.Zero)
					return false;

				if (GetBlock((int)HitPos.X, (int)HitPos.Y, (int)HitPos.Z) != BlockType.None) {
					RetPos = HitPos;
					OutFaceDir = Face;
					return true;
				}

				return false;
			})) {

			}

			FaceDir = OutFaceDir;
			return RetPos;
		}

		public Vector3 RaycastPosEx(Vector3 Origin, float Distance, Vector3 Dir, out Vector3 FaceDir, out Vector3 Block) {
			Vector3 RetPos = Vector3.Zero;
			Vector3 OutFaceDir = Vector3.Zero;
			Vector3 Blk = Vector3.Zero;

			if (Utils.Raycast2(Origin, Dir, Distance, 20, (HitPos, Face) => {

				if (GetBlock((int)HitPos.X, (int)HitPos.Y, (int)HitPos.Z) != BlockType.None) {
					RetPos = HitPos;
					OutFaceDir = Face;
					Blk = new Vector3((int)HitPos.X, (int)HitPos.Y, (int)HitPos.Z);
					return true;
				}

				return false;
			})) {

			}

			FaceDir = OutFaceDir;
			Block = Blk;
			return RetPos;
		}

		public int RaycastSphere(Vector3 Origin, float Distance, Raycast2CallbackFunc OnHit, BlockType[] SkipArray = null, int Slices = 26) {
			return Utils.RaycastSphere(Origin, Distance, (Orig, Face) => {
				BlockType BT = BlockType.None;

				if ((BT = GetBlock(Orig)) != BlockType.None) {
					if (SkipArray != null && SkipArray.Contains(BT))
						return false;

					OnHit(Orig, Face);
					return true;
				}

				return false;
			}, Slices);
		}

		public bool RaycastPoint(Vector3 Origin) {
			if (GetBlock((int)Origin.X, (int)Origin.Y, (int)Origin.Z) != BlockType.None)
				return true;

			return false;
		}

		public int CountAmbientHits(Vector3 Pos) {
			int Hits = 0;

			for (int i = 0; i < Utils.MainDirs.Length; i++) {
				if (BlockInfo.IsOpaque(GetBlock(Pos + Utils.MainDirs[i])))
					Hits++;
			}

			return Hits;
		}

		public bool IsCovered(int X, int Y, int Z) {
			for (int i = 0; i < Utils.MainDirs.Length; i++) {
				int XX = (int)(X + Utils.MainDirs[i].X);
				int YY = (int)(Y + Utils.MainDirs[i].Y);
				int ZZ = (int)(Z + Utils.MainDirs[i].Z);

				if (GetBlock(XX, YY, ZZ) == BlockType.None)
					return false;
			}

			return true;
		}

		List<Vector3> SunRayOrigins = new List<Vector3>();
		Vector3 SunDir = -Vector3.UnitY;

		void PerformLightBounce2(Vector3 Origin, float Distance) {
			Vector3 SphereCenter = Origin;
			float SphereRadius = Distance;

			IEnumerable<Tuple<PlacedBlock, Vector3>> BlocksAround = GetBlocksInRange(SphereCenter, SphereRadius, new[] { BlockType.None });
			foreach (var BA in BlocksAround) {
				if (IsCovered((int)BA.Item2.X, (int)BA.Item2.Y, (int)BA.Item2.Z))
					continue;

				float Dist = Vector3.Distance(SphereCenter, BA.Item2);

				if (Dist > SphereRadius)
					continue;

				foreach (var Face in GetVisibleFaces(BA.Item2, SphereCenter)) {
					if (IsCovered((int)BA.Item2.X, (int)BA.Item2.Y, (int)BA.Item2.Z))
						continue;

					if (GetBlock(BA.Item2 + Face) != BlockType.None)
						continue;

					int RT = RayTest(SphereCenter, BA.Item2 + new Vector3(0.5f, 0.5f, 0.5f) + Face * 0.6f, null, new[] { BlockType.None, BlockType.Glowstone });
					if (RT > 0)
						continue;

					//byte TargetLight = Math.Max((byte)(SphereRadius - Dist), (byte)0);
					//float TgtLightPerc = 1 - (float)(Dist / SphereRadius);
					byte AdditiveLightLevel = (byte)1;

					// byte TargetLight = (byte)(TgtLightPerc * 28);
					byte TargetLight = (byte)(BA.Item1.Lights[Utils.DirToByte(Face)].R + AdditiveLightLevel);

					if (TargetLight > 28)
						TargetLight = 28;

					BA.Item1.Lights[Utils.DirToByte(Face)] = new BlockLight(TargetLight);
				}
			}
		}

		// HitPos + FaceNormal * 0.1f
		// 4
		void PerformLightBounce(Vector3 Origin, float Distance) {
			RaycastSphere(Origin, Distance, (Orig, Norm) => {
				PlacedBlock PB = GetPlacedBlock((int)Orig.X, (int)Orig.Y, (int)Orig.Z, out Chunk Chk2);

				if (PB.Type != BlockType.None && Norm != Vector3.Zero) {

					if (PB.Lights[Utils.DirToByte(Norm)].R <= 27) {
						PB.Lights[Utils.DirToByte(Norm)] += 1;
					}

				}

				return false;
			});
		}

		IEnumerable<Tuple<PlacedBlock, Vector3>> GetBlocksInRange(Vector3 Pos, float Range, BlockType[] IgnoreBlocks = null) {
			int Rang = (int)Range;//(int)Math.Ceiling(Range / 2) + 1;

			for (int zz = -Rang; zz < Rang + 1; zz++)
				for (int yy = -Rang; yy < Rang + 1; yy++)
					for (int xx = -Rang; xx < Rang + 1; xx++) {
						Vector3 Offset = new Vector3(xx, yy, zz);
						Vector3 Pos2 = Pos + Offset;

						if (Vector3.Distance(Pos2, Pos) > Range)
							continue;

						PlacedBlock PB = GetPlacedBlock((int)Pos2.X, (int)Pos2.Y, (int)Pos2.Z, out Chunk Chk);

						if (IgnoreBlocks != null && IgnoreBlocks.Contains(PB.Type))
							continue;

						yield return new Tuple<PlacedBlock, Vector3>(PB, new Vector3((int)Pos2.X, (int)Pos2.Y, (int)Pos2.Z));
					}

			/*foreach (Chunk C in GetAllChunks()) {
				for (int i = 0; i < C.Blocks.Length; i++) {
					PlacedBlock B = C.Blocks[i];

					if (IgnoreBlocks != null && IgnoreBlocks.Contains(B.Type))
						continue;

					C.To3D(i, out int LX, out int LY, out int LZ);
					GetWorldPos(LX, LY, LZ, C.GlobalChunkIndex, out Vector3 BPos);

					if (Vector3.Distance(Pos, BPos) <= Range)
						yield return new Tuple<PlacedBlock, Vector3>(B, BPos);
				}
			}*/
		}

		IEnumerable<Vector3> GetVisibleFaces(Vector3 BlockPos, Vector3 CamPos) {
			Vector3 Delta = CamPos - BlockPos;

			if (Delta.X > 0)
				yield return new Vector3(1, 0, 0);
			else if (Delta.X < 0)
				yield return new Vector3(-1, 0, 0);

			if (Delta.Y > 0)
				yield return new Vector3(0, 1, 0);
			else if (Delta.Y < 0)
				yield return new Vector3(0, -1, 0);

			if (Delta.Z > 0)
				yield return new Vector3(0, 0, 1);
			else if (Delta.Z < 0)
				yield return new Vector3(0, 0, -1);
		}

		public void ComputeLighting() {
			foreach (Chunk C in GetAllChunks()) {
				foreach (PlacedBlock B in C.Blocks) {
					if (B.Type == BlockType.None)
						continue;

					B.SetBlockLight(new BlockLight(8));
				}
			}

			/*if (!Utils.HasRecord()) {
				Console.WriteLine("Begin recording!");
				Utils.BeginRaycastRecord();
			}*/

			foreach (Chunk C in GetAllChunks()) {
				for (int i = 0; i < C.Blocks.Length; i++) {
					PlacedBlock B = C.Blocks[i];

					if (B.Type == BlockType.None)
						continue;

					C.To3D(i, out int LX, out int LY, out int LZ);
					GetWorldPos(LX, LY, LZ, C.GlobalChunkIndex, out Vector3 BPos);

					if (IsCovered((int)BPos.X, (int)BPos.Y, (int)BPos.Z))
						continue;

					if (B.Type == BlockType.Glowstone) {
						Console.WriteLine("Glowstone!");

						B.SetBlockLight(new BlockLight(28));

						Vector3 SphereCenter = BPos + new Vector3(0.5f, 0.5f, 0.5f);
						float SphereRadius = 8;



						IEnumerable<Tuple<PlacedBlock, Vector3>> BlocksAround = GetBlocksInRange(SphereCenter, SphereRadius, new[] { BlockType.None, BlockType.Glowstone });

						foreach (var BA in BlocksAround) {
							if (IsCovered((int)BA.Item2.X, (int)BA.Item2.Y, (int)BA.Item2.Z))
								continue;

							float Dist = Vector3.Distance(SphereCenter, BA.Item2);

							if (Dist > SphereRadius)
								continue;

							foreach (var Face in GetVisibleFaces(BA.Item2, SphereCenter)) {
								if (IsCovered((int)BA.Item2.X, (int)BA.Item2.Y, (int)BA.Item2.Z))
									continue;

								if (GetBlock(BA.Item2 + Face) != BlockType.None)
									continue;

								int RT = RayTest(SphereCenter, BA.Item2 + new Vector3(0.5f, 0.5f, 0.5f) + Face * 0.6f, null, new[] { BlockType.Glowstone });
								if (RT > 0)
									continue;

								//byte TargetLight = Math.Max((byte)(SphereRadius - Dist), (byte)0);
								float TgtLightPerc = 1 - (float)(Dist / SphereRadius);
								byte AdditiveLightLevel = (byte)(TgtLightPerc * 28);

								// byte TargetLight = (byte)(TgtLightPerc * 28);
								byte TargetLight = (byte)(BA.Item1.Lights[Utils.DirToByte(Face)].R + AdditiveLightLevel);

								if (TargetLight > 28)
									TargetLight = 28;

								BA.Item1.Lights[Utils.DirToByte(Face)] = new BlockLight(TargetLight);
							}
						}
					}
				}
			}

			//Utils.EndRaycastRecord();

			float SunReach = 128;
			Matrix4x4 LookAtRot = Matrix4x4.CreateFromYawPitchRoll(Utils.ToRad(-25), Utils.ToRad(90 - 25), Utils.ToRad(0));


			//Vector3 Left = Vector3.Normalize(Vector3.Transform(Vector3.UnitX, LookAtRot));
			//Vector3 Up = Vector3.Normalize(Vector3.Transform(Vector3.UnitY, LookAtRot));
			Vector3 Fwd = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, LookAtRot));
			SunDir = Fwd;

			SunRayOrigins.Clear();

			/*int MaxSize = 30;
			float StepScale = 1.0f;

			Vector3 CamPos = FPSCamera.Position;
			Vector2 CamPos2D = new Vector2(CamPos.X, CamPos.Z);

			Rnd = new Random(42096);

			for (int yy = 0; yy < MaxSize; yy++) {
				for (int xx = 0; xx < MaxSize; xx++) {
					Vector3 PosOffset = new Vector3(CamPos2D.X + (xx - (MaxSize / 2)) * StepScale, 100, CamPos2D.Y + (yy - (MaxSize / 2)) * StepScale);

					PosOffset.X += (float)((Rnd.Next(0, 2) == 1 ? -1 : 1) * Rnd.NextDouble() * 0.5f);
					PosOffset.Z += (float)((Rnd.Next(0, 2) == 1 ? -1 : 1) * Rnd.NextDouble() * 0.5f);

					SunRayOrigins.Add(PosOffset);

					Vector3 HitPos = RaycastPosEx(PosOffset, SunReach, Fwd, out Vector3 FaceNormal, out Vector3 BlokPos);
					if (HitPos != Vector3.Zero && FaceNormal != Vector3.Zero) {
						PlacedBlock Blk = GetPlacedBlock((int)BlokPos.X, (int)BlokPos.Y, (int)BlokPos.Z, out Chunk Chk);
						Blk.Lights[Utils.DirToByte(FaceNormal)] = new BlockLight(28);
						Chk.MarkDirty();

						Vector3[] Neigh = GetPlaneNeighbours((int)BlokPos.X, (int)BlokPos.Y, (int)BlokPos.Z, FaceNormal).ToArray();

						foreach (var Neig in Neigh) {
							PlacedBlock PB = GetPlacedBlock((int)Neig.X, (int)Neig.Y, (int)Neig.Z, out Chunk Chk2);
							PB.Lights[Utils.DirToByte(FaceNormal)] = new BlockLight(28);

						}

						Console.WriteLine(Neigh);
					}
				}
			}*/

			foreach (Chunk C in GetAllChunks()) {
				C.MarkDirty();
			}
		}

		public PlacedBlock GetPlacedBlock(int X, int Y, int Z, out Chunk Chk) {
			TranslateChunkPos(X, Y, Z, out Vector3 ChunkIndex, out Vector3 BlockPos);

			if (Chunks.ContainsKey(ChunkIndex)) {
				return (Chk = Chunks[ChunkIndex]).GetBlock((int)BlockPos.X, (int)BlockPos.Y, (int)BlockPos.Z);
			}

			Chk = null;
			return new PlacedBlock(BlockType.None);
		}

		public BlockType GetBlock(int X, int Y, int Z) {
			return GetPlacedBlock(X, Y, Z, out Chunk Chk).Type;
		}

		public BlockType GetBlock(Vector3 Pos) {
			return GetBlock((int)Pos.X, (int)Pos.Y, (int)Pos.Z);
		}

		public void Draw() {
			foreach (var KV in Chunks) {
				Vector3 ChunkPos = KV.Key * new Vector3(Chunk.ChunkSize);
				KV.Value.Draw(ChunkPos);
			}

			/*foreach (Vector3 Orig in SunRayOrigins) {
				Vector3 Dst = Orig + SunDir * 64;

				Raylib.DrawLine3D(Orig, Dst, Color.Orange);
			}*/

			Utils.DrawRaycastRecord();
		}

		public void DrawTransparent() {
			foreach (var KV in Chunks) {
				Vector3 ChunkPos = KV.Key * new Vector3(Chunk.ChunkSize);
				KV.Value.DrawTransparent(ChunkPos);
			}
		}
	}
}
