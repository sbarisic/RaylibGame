using System.Security.Cryptography;
using Voxelgine.Graphics;
using Voxelgine.WorldGeneration;
using Xunit;

namespace VoxelgineEngine.Tests;

public class WorldGenerationPerformanceTests
{
    [Fact]
    public void FeatureRichMaterializationPreservesFogAndCancellationLeavesDestinationUntouched()
    {
        var catalog = Voxelgine.Engine.World.Structures.StructureBlueprintCatalog.LoadDirectory(Path.Combine(AppContext.BaseDirectory, "data/world/structures"));
        var ceramic = Voxelgine.Engine.World.Structures.CeramicVillageCatalog.Load(Path.Combine(AppContext.BaseDirectory, "data/world/ceramic-fish/village.json"));
        var plan = WorldPlanMaterializer.GeneratePlan(256, 256, 123456, catalog, ceramicFish: ceramic);
        Assert.NotEmpty(plan.Sites);
        ChunkMap reference = new();
        WorldPlanMaterializer.MaterializeAtomically(reference, plan, catalog, ceramicFish: ceramic, terrainWorkers: 1);
        var expected = reference.CaptureChunks();
        foreach (int workers in new[] { 2, 4 })
        {
            ChunkMap map = new();
            WorldPlanMaterializer.MaterializeAtomically(map, plan, catalog, ceramicFish: ceramic, terrainWorkers: workers);
            var actual = map.CaptureChunks();
            Assert.Equal(expected.Count, actual.Count);
            for (int i = 0; i < expected.Count; i++)
            {
                Assert.Equal(expected[i].Values, actual[i].Values);
                Assert.Equal(expected[i].Fog, actual[i].Fog);
            }
        }
        ChunkMap destination = new(); destination.SetBlock(-1, 2, -3, Voxelgine.Engine.BlockType.Bricks);
        using CancellationTokenSource cancellation = new();
        Assert.ThrowsAny<OperationCanceledException>(() => WorldPlanMaterializer.MaterializeAtomically(destination, plan, catalog,
            cancellation.Token, ceramic, stageCompleted: (_, _) => cancellation.Cancel()));
        Assert.Equal(Voxelgine.Engine.BlockType.Bricks, destination.GetBlock(-1, 2, -3));
        Assert.Single(destination.CaptureChunks());
    }

    [Theory]
    [InlineData(123456, "0434F5F3650B68191F412BDA2E75F43E7213384F22A18FFA46DB74614C3A17C3")]
    [InlineData(66791, "63631985A928D91B0700AFD6E56BE37FAE99E0D08AF49829F94CBA5BB493207C")]
    [InlineData(8142, "35D4C6BC53C26E78E3E9EDE80B57D9BA4096494F41201A521C7108E79F3B7F6A")]
    public void MaterializationMatchesPublishedBaselineAcrossWorkerCounts(int seed, string expected)
    {
        WorldPlan plan = WorldPlanGenerator.Generate(new(seed, 256, 256, 64));
        foreach (int workers in new[] { 1, 2, 4 })
        {
            ChunkMap map = new();
            WorldPlanMaterializer.MaterializeAtomically(map, plan, null, terrainWorkers: workers);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var chunk in map.CaptureChunks().OrderBy(c => c.ChunkX).ThenBy(c => c.ChunkY).ThenBy(c => c.ChunkZ))
            {
                hash.AppendData(BitConverter.GetBytes(chunk.ChunkX)); hash.AppendData(BitConverter.GetBytes(chunk.ChunkY)); hash.AppendData(BitConverter.GetBytes(chunk.ChunkZ));
                foreach (var value in chunk.Values) { hash.AppendData(BitConverter.GetBytes((ushort)value.Type)); hash.AppendData(new[] { value.State }); }
            }
            Assert.Equal(expected, Convert.ToHexString(hash.GetHashAndReset()));
        }
    }

    [Fact]
    public void ColumnSamplerMatchesLegacySolidPredicate()
    {
        WorldPlan plan = WorldPlanGenerator.Generate(new(8142, 96, 80, 64));
        var sampler = WorldPlanGenerator.CreateVolumeSampler(plan);
        for (int x = -1; x <= plan.Width; x++) for (int z = -1; z <= plan.Length; z++)
        for (int y = -1; y <= 64; y++)
        {
            bool expected = false;
            if ((uint)x < plan.Width && (uint)z < plan.Length && y >= 0 && y < 64 && plan.IsLand(x, z))
            {
                double nx = (x + .5 - plan.Width * .5) / (plan.Width * .5), nz = (z + .5 - plan.Length * .5) / (plan.Length * .5);
                int thickness = 12 + (int)Math.Round(Math.Pow(Math.Clamp(1 - Math.Sqrt(nx * nx + nz * nz) / .98, 0, 1), 1.35) * (plan.WorldHeight - 16));
                expected = y <= plan.GetHeight(x, z) && y >= Math.Max(0, plan.GetHeight(x, z) - thickness + 1);
            }
            Assert.Equal(expected, sampler.IsSolid(x, y, z));
        }
    }
}
