using System.Numerics;
using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using Raylib_cs;
using Voxelgine.Engine;
using RaylibGame.States;

namespace Voxelgine.Graphics {
    // Spatial hash grid for chunk storage
    class SpatialHashGrid<T>
    {
        private readonly int bucketSize;
        private readonly Dictionary<long, List<(Vector3, T)>> buckets = new();

        public SpatialHashGrid(int bucketSize = 64)
        {
            this.bucketSize = bucketSize;
        }

        private long Hash(Vector3 pos)
        {
            int x = (int)pos.X / bucketSize;
            int y = (int)pos.Y / bucketSize;
            int z = (int)pos.Z / bucketSize;
            return ((long)x & 0x1FFFFF) | (((long)y & 0x1FFFFF) << 21) | (((long)z & 0x1FFFFF) << 42);
        }

        public void Add(Vector3 pos, T value)
        {
            long h = Hash(pos);
            if (!buckets.TryGetValue(h, out var list))
            {
                list = new List<(Vector3, T)>();
                buckets[h] = list;
            }
            list.Add((pos, value));
        }

        public bool TryGetValue(Vector3 pos, out T value)
        {
            long h = Hash(pos);
            if (buckets.TryGetValue(h, out var list))
            {
                foreach (var (p, v) in list)
                {
                    if (p == pos)
                    {
                        value = v;
                        return true;
                    }
                }
            }
            value = default;
            return false;
        }

        public bool ContainsKey(Vector3 pos) => TryGetValue(pos, out _);

        public void Clear() => buckets.Clear();

        public IEnumerable<T> Values {
            get {
                foreach (var list in buckets.Values)
                    foreach (var (_, v) in list)
                        yield return v;
            }
        }

        public IEnumerable<KeyValuePair<Vector3, T>> Items {
            get {
                foreach (var list in buckets.Values)
                    foreach (var (p, v) in list)
                        yield return new KeyValuePair<Vector3, T>(p, v);
            }
        }
    }

    unsafe class ChunkMap {
        SpatialHashGrid<Chunk> Chunks;
        Random Rnd = new Random();

        public ChunkMap(GameState GS) {
            Chunks = new SpatialHashGrid<Chunk>(1);
        }

        public void Write(System.IO.Stream Output) {
            using (GZipStream ZipStream = new GZipStream(Output, CompressionMode.Compress, true))
            using (var Writer = new System.IO.BinaryWriter(ZipStream)) {
                var ChunksArray = Chunks.Items.ToArray();
                Writer.Write(ChunksArray.Length);
                foreach (var chunk in ChunksArray) {
                    Writer.Write((int)chunk.Key.X);
                    Writer.Write((int)chunk.Key.Y);
                    Writer.Write((int)chunk.Key.Z);
                    chunk.Value.Write(Writer);
                }
            }
        }

        public void Read(System.IO.Stream Input) {
            using (GZipStream ZipStream = new GZipStream(Input, CompressionMode.Decompress, true))
            using (var Reader = new System.IO.BinaryReader(ZipStream)) {
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

        public Chunk[] GetAllChunks() => Chunks.Values.ToArray();

        public void GenerateFloatingIsland(int Width, int Length, int Seed = 666) {
            Noise.Seed = Seed;
            float Scale = 0.02f;
            int WorldHeight = 64;
            Vector3 Center = new Vector3(Width, 0, Length) / 2;
            float CenterRadius = Math.Min(Width / 2, Length / 2);
            for (int x = 0; x < Width; x++)
                for (int z = 0; z < Length; z++)
                    for (int y = 0; y < WorldHeight; y++) {
                        Vector3 Pos = new Vector3(x, (WorldHeight - y), z);
                        float CenterFalloff = 1.0f - Utils.Clamp(((Center - Pos).Length() / CenterRadius) / 1.2f, 0, 1);
                        float Height = (float)y / WorldHeight;
                        const float HeightFallStart = 0.8f;
                        const float HeightFallEnd = 1.0f;
                        const float HeightFallRange = HeightFallEnd - HeightFallStart;
                        float HeightFalloff = Height <= HeightFallStart ? 1.0f : (Height > HeightFallStart && Height < HeightFallEnd ? 1.0f - (Height - HeightFallStart) * (HeightFallRange * 10) : 0);
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
                        } else if (DownRayHits != 0) break;
                    }
                }
            ComputeLighting();
        }

        float Simplex(int Octaves, float X, float Y, float Z, float Scale) {
            float Val = 0.0f;
            for (int i = 0; i < Octaves; i++)
                Val += Noise.CalcPixel3D(X * Math.Pow(2, i), Y * Math.Pow(2, i), Z * Math.Pow(2, i), Scale);
            return (Val / Octaves) / 255;
        }

        public void SetPlacedBlock(int X, int Y, int Z, PlacedBlock Block) {
            TranslateChunkPos(X, Y, Z, out Vector3 ChunkIndex, out Vector3 BlockPos);
            int XX = (int)BlockPos.X, YY = (int)BlockPos.Y, ZZ = (int)BlockPos.Z;
            const int MaxBlock = Chunk.ChunkSize - 1;
            HashSet<Vector3> affectedChunks = new HashSet<Vector3> { ChunkIndex };
            int[] xOffsets = { 0 }, yOffsets = { 0 }, zOffsets = { 0 };
            if (XX == 0) xOffsets = xOffsets.Concat(new[] { -1 }).ToArray();
            if (XX == MaxBlock) xOffsets = xOffsets.Concat(new[] { 1 }).ToArray();
            if (YY == 0) yOffsets = yOffsets.Concat(new[] { -1 }).ToArray();
            if (YY == MaxBlock) yOffsets = yOffsets.Concat(new[] { 1 }).ToArray();
            if (ZZ == 0) zOffsets = zOffsets.Concat(new[] { -1 }).ToArray();
            if (ZZ == MaxBlock) zOffsets = zOffsets.Concat(new[] { 1 }).ToArray();
            foreach (int xOffset in xOffsets)
                foreach (int yOffset in yOffsets)
                    foreach (int zOffset in zOffsets)
                        affectedChunks.Add(ChunkIndex + new Vector3(xOffset, yOffset, zOffset));
            foreach (var chunkPos in affectedChunks)
                if (Chunks.TryGetValue(chunkPos, out var chunk))
                    chunk.MarkDirty();
            if (!Chunks.ContainsKey(ChunkIndex))
                Chunks.Add(ChunkIndex, new Chunk(ChunkIndex, this));
            Chunks.TryGetValue(ChunkIndex, out var targetChunk);
            targetChunk.SetBlock(XX, YY, ZZ, Block);
        }

        public void SetBlock(int X, int Y, int Z, BlockType T) => SetPlacedBlock(X, Y, Z, new PlacedBlock(T));

        public PlacedBlock GetPlacedBlock(int X, int Y, int Z, out Chunk Chk) {
            TranslateChunkPos(X, Y, Z, out Vector3 ChunkIndex, out Vector3 BlockPos);
            if (Chunks.TryGetValue(ChunkIndex, out Chk))
                return Chk.GetBlock((int)BlockPos.X, (int)BlockPos.Y, (int)BlockPos.Z);
            Chk = null;
            return new PlacedBlock(BlockType.None);
        }

        public BlockType GetBlock(int X, int Y, int Z) => GetPlacedBlock(X, Y, Z, out _).Type;
        public BlockType GetBlock(Vector3 Pos) => GetBlock((int)Pos.X, (int)Pos.Y, (int)Pos.Z);

        void TranslateChunkPos(int X, int Y, int Z, out Vector3 ChunkIndex, out Vector3 BlockPos) {
            TransPosScalar(X, out int ChkX, out int BlkX);
            TransPosScalar(Y, out int ChkY, out int BlkY);
            TransPosScalar(Z, out int ChkZ, out int BlkZ);
            ChunkIndex = new Vector3(ChkX, ChkY, ChkZ);
            BlockPos = new Vector3(BlkX, BlkY, BlkZ);
        }
        void TransPosScalar(int S, out int ChunkIndex, out int BlockPos) {
            ChunkIndex = (int)Math.Floor((float)S / Chunk.ChunkSize);
            BlockPos = Utils.Mod(S, Chunk.ChunkSize);
        }
        public void GetWorldPos(int X, int Y, int Z, Vector3 ChunkIndex, out Vector3 GlobalPos) {
            GlobalPos = ChunkIndex * Chunk.ChunkSize + new Vector3(X, Y, Z);
        }

        public void ComputeLighting() {
            foreach (Chunk C in GetAllChunks())
                C.ComputeLighting();
            foreach (Chunk C in GetAllChunks())
                C.MarkDirty();
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

        public void Tick() { }

        public void Draw() {
            foreach (var KV in Chunks.Items) {
                Vector3 ChunkPos = KV.Key * new Vector3(Chunk.ChunkSize);
                KV.Value.Draw(ChunkPos);
            }
            Utils.DrawRaycastRecord();
        }

        public void DrawTransparent() {
            foreach (var KV in Chunks.Items)
                KV.Value.DrawTransparent();
        }

        // Add back RaycastPos and Collide for GameState compatibility
        // RaycastPos: Returns the first solid block hit by a block-based raycast, or Vector3.Zero if none is found.
        public Vector3 RaycastPos(Vector3 Origin, float Distance, Vector3 Dir, out Vector3 FaceDir) {
            // Block-based raycast: returns the first solid block hit, or Vector3.Zero if none
            Vector3 hitPos = Vector3.Zero;
            Vector3 hitFace = Vector3.Zero;
            bool found = Voxelgine.Utils.Raycast(Origin, Dir, Distance, (x, y, z, face) => {
                if (GetBlock(x, y, z) != BlockType.None) {
                    hitPos = new Vector3(x, y, z);
                    hitFace = face;
                    return true;
                }
                return false;
            });
            FaceDir = hitFace;
            return found ? hitPos : Vector3.Zero;
        }
        public RayCollision Collide(Ray R) {
            // Find the closest chunk intersected by the ray and return its collision
            RayCollision closest = new RayCollision { Hit = false, Distance = float.MaxValue };
            foreach (var kv in Chunks.Items) {
                Vector3 chunkPos = kv.Key * new Vector3(Chunk.ChunkSize);
                RayCollision col = kv.Value.Collide(chunkPos, R);
                if (col.Hit && col.Distance < closest.Distance) {
                    closest = col;
                }
            }
            return closest;
        }
        // Add back Collide overload for GameState compatibility
        // Collide: Checks if the position is inside a solid block, or if moving in ProbeDir hits a block. Returns true and the collision normal if a block is hit, otherwise false.
        public bool Collide(Vector3 Pos, Vector3 ProbeDir, out Vector3 PickNormal) {
            // Check if the position is inside a solid block, or if moving in ProbeDir hits a block
            Vector3 probe = Pos + ProbeDir * 0.1f;
            if (GetBlock((int)MathF.Floor(probe.X), (int)MathF.Floor(probe.Y), (int)MathF.Floor(probe.Z)) != BlockType.None) {
                PickNormal = -Vector3.Normalize(ProbeDir);
                return true;
            }
            PickNormal = Vector3.Zero;
            return false;
        }
    }
}
