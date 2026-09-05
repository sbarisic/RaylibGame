using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.IO;
using Voxelgine.Engine.World.Structures;
using Voxelgine.Graphics;
using Voxelgine.WorldGeneration;

namespace BenchmarkSuite1;

internal static class VoxelGenerationProbe
{
    internal static void Run(string[] args)
    {
        int size = args.Length > 1 ? int.Parse(args[1]) : 256;
        int samples = args.Length > 2 ? int.Parse(args[2]) : 5;
        if (samples < 1) throw new ArgumentOutOfRangeException(nameof(samples));
        bool features = args.Contains("--features");
        var catalog = features ? StructureBlueprintCatalog.LoadDirectory("Voxelgine/data/world/structures") : null;
        var ceramic = features ? CeramicVillageCatalog.Load("Voxelgine/data/world/ceramic-fish/village.json") : null;
        foreach (int seed in new[] { 123456, 66791, 8142 })
        {
            WorldPlan plan = null;
            PlanningProgress progress = args.Contains("--stage-profile") ? new() : null;
            Measure("plan", () => plan = WorldPlanMaterializer.GeneratePlan(size, size, seed, catalog, progress: progress, ceramicFish: ceramic));
            if (progress != null)
                foreach (var stage in progress.Stages)
                    Console.WriteLine(JsonSerializer.Serialize(new { stage = "Plan: " + stage.Name, seed, size, features, elapsedMs = stage.Milliseconds }));
            Measure("materialize", () => WorldPlanMaterializer.MaterializeAtomically(new ChunkMap(), plan, catalog, ceramicFish: ceramic));
            ChunkMap map = new(); WorldPlanMaterializer.MaterializeAtomically(map, plan, catalog, ceramicFish: ceramic);
            if (args.Contains("--stage-profile"))
            {
                var method = typeof(WorldPlanMaterializer).GetMethod("MaterializeAtomically");
                var parameters = method.GetParameters();
                if (parameters.Any(p => p.Name == "stageCompleted"))
                {
                    object[] invocation = parameters.Select(p => p.DefaultValue).ToArray();
                    invocation[0] = new ChunkMap(); invocation[1] = plan; invocation[2] = catalog;
                    invocation[3] = default(System.Threading.CancellationToken); invocation[4] = ceramic;
                    invocation[^1] = (Action<string, TimeSpan>)((stage, elapsed) => Console.WriteLine(JsonSerializer.Serialize(new { stage, seed, size, features, elapsedMs = elapsed.TotalMilliseconds })));
                    method.Invoke(null, invocation);
                }
            }
            using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            foreach (var chunk in map.CaptureChunks().OrderBy(c => c.ChunkX).ThenBy(c => c.ChunkY).ThenBy(c => c.ChunkZ))
            {
                hash.AppendData(BitConverter.GetBytes(chunk.ChunkX)); hash.AppendData(BitConverter.GetBytes(chunk.ChunkY)); hash.AppendData(BitConverter.GetBytes(chunk.ChunkZ));
                foreach (var value in chunk.Values) { hash.AppendData(BitConverter.GetBytes((ushort)value.Type)); hash.AppendData(new[] { value.State }); }
            }
            Console.WriteLine(JsonSerializer.Serialize(new { seed, size, voxelSha256 = Convert.ToHexString(hash.GetHashAndReset()) }));
            void Measure(string stage, Action action)
            {
                action();
                double[] times = new double[samples]; long bytes = 0;
                for (int i = 0; i < samples; i++)
                {
                    GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                    long before = GC.GetTotalAllocatedBytes(true), start = Stopwatch.GetTimestamp();
                    action(); times[i] = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
                    bytes += GC.GetTotalAllocatedBytes(true) - before;
                }
                Array.Sort(times);
                Console.WriteLine(JsonSerializer.Serialize(new { stage, seed, size, samples, features,
                    medianMs = times[samples / 2], p95Ms = times[Math.Max(0, (int)Math.Ceiling(samples * .95) - 1)],
                    p99Ms = times[Math.Max(0, (int)Math.Ceiling(samples * .99) - 1)], bytesPerOperation = bytes / samples,
                    peakWorkingSet = Process.GetCurrentProcess().PeakWorkingSet64 }));
            }
        }
    }

    private sealed class PlanningProgress : IProgress<WorldGenerationProgress>
    {
        internal readonly System.Collections.Generic.List<(string Name, double Milliseconds)> Stages = new();
        private long started;
        private string previous;
        public void Report(WorldGenerationProgress value)
        {
            long now = Stopwatch.GetTimestamp();
            if (value.Stage == "Terrain") Stages.Clear();
            else if (previous != null) Stages.Add((previous, (now - started) * 1000.0 / Stopwatch.Frequency));
            started = now; previous = value.Stage;
        }
    }
}
