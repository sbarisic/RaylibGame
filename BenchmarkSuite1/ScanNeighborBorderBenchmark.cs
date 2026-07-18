using BenchmarkDotNet.Attributes;
using Voxelgine.Graphics;
using Voxelgine.Engine;
using Microsoft.VSDiagnostics;

namespace LightingBenchmarks
{
    [CPUUsageDiagnoser]
    public class ScanNeighborBorderBenchmark
    {
        private ChunkMap _worldMap;
        [GlobalSetup]
        public void Setup()
        {
			_worldMap = new ChunkMap();
            const int CS = Chunk.ChunkSize;
            // Create chunks by placing a Water block (no lighting trigger) in each chunk position
            for (int cx = -1; cx <= 1; cx++)
                for (int cy = -1; cy <= 1; cy++)
                    for (int cz = -1; cz <= 1; cz++)
                    {
                        int baseX = cx * CS;
                        int baseY = cy * CS;
                        int baseZ = cz * CS;
                        _worldMap.SetBlock(baseX, baseY, baseZ, BlockType.Water);
                    }

            // Fill bottom half of each chunk with stone using SetPlacedBlockNoLighting
            for (int cx = -1; cx <= 1; cx++)
                for (int cy = -1; cy <= 1; cy++)
                    for (int cz = -1; cz <= 1; cz++)
                    {
                        int baseX = cx * CS;
                        int baseY = cy * CS;
                        int baseZ = cz * CS;
                        for (int x = 0; x < CS; x++)
                            for (int y = 0; y < CS / 2; y++)
                                for (int z = 0; z < CS; z++)
                                    _worldMap.SetPlacedBlockNoLighting(baseX + x, baseY + y, baseZ + z, new PlacedBlock(BlockType.Stone));
                    }

            // Place Glowstone blocks in neighbor chunks near borders facing center
            for (int cx = -1; cx <= 1; cx++)
                for (int cy = -1; cy <= 1; cy++)
                    for (int cz = -1; cz <= 1; cz++)
                    {
                        if (cx == 0 && cy == 0 && cz == 0)
                            continue;
                        int baseX = cx * CS;
                        int baseY = cy * CS;
                        int baseZ = cz * CS;
                        for (int i = 0; i < 4; i++)
                        {
                            int lx = cx == -1 ? CS - 1 : (cx == 1 ? 0 : i * 4);
                            int ly = cy == -1 ? CS - 1 : (cy == 1 ? 0 : CS / 2);
                            int lz = cz == -1 ? CS - 1 : (cz == 1 ? 0 : i * 4);
                            _worldMap.SetPlacedBlockNoLighting(baseX + lx, baseY + ly, baseZ + lz, new PlacedBlock(BlockType.Glowstone));
                        }
                    }
        }

        [Benchmark]
        public void ComputeLighting()
        {
            _worldMap.ComputeLighting();
        }
    }
}
