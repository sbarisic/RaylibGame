using BenchmarkDotNet.Running;

namespace BenchmarkSuite1
{
    internal class Program
    {
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--voxel-generation") { VoxelGenerationProbe.Run(args); return; }
            var _ = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
        }
    }
}
