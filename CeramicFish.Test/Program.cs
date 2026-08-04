using Voxelgine.WorldGeneration;

namespace CeramicFish.TestHarness;

internal static class Program
{
	private static async Task<int> Main(string[] args)
	{
		if (!TryParseArguments(args, out Options options, out string? error))
		{
			Console.Error.WriteLine(error);
			PrintUsage();
			return 2;
		}
		if (options.Help)
		{
			PrintUsage();
			return 0;
		}

		int seed = options.Seed ?? (int)Random.Shared.NextInt64(
			int.MinValue, (long)int.MaxValue + 1);
		string outputDirectory = Path.GetFullPath(options.OutputDirectory);
		Directory.CreateDirectory(outputDirectory);
		string definitionPath = Path.Combine(outputDirectory, "ceramic-fish.json");
		string imagePath = Path.Combine(outputDirectory, "village.png");
		if (File.Exists(imagePath)) File.Delete(imagePath);

		Console.WriteLine($"CeramicFish seed: {seed}");
		Console.WriteLine($"Output directory: {outputDirectory}");

		CeramicFishDefinition source = CeramicTestCatalog.CreateDefinition();
		ICeramicFishJsonStorage storage = new CeramicFishJsonStorage();
		await storage.SaveAsync(definitionPath, source).ConfigureAwait(false);
		CeramicFishDefinition loaded = await storage.LoadAsync(definitionPath).ConfigureAwait(false);
		CeramicTestCatalog.VerifyRoundTrip(source, loaded);
		Console.WriteLine($"Saved and reloaded {loaded.Prefabs.Count} prefabs: {definitionPath}");

		CeramicGenerationRequest request = CeramicTestCatalog.CreateRequest(seed);
		ICeramicFish generator = new EmptyCeramicFish();
		CeramicGenerationResult result = generator.Generate(request, loaded);

		await CeramicImageRenderer.SaveAsync(imagePath, loaded, request, result).ConfigureAwait(false);
		Console.WriteLine($"Generated image: {imagePath}");
		return 0;
	}

	private static bool TryParseArguments(
		IReadOnlyList<string> args,
		out Options options,
		out string? error)
	{
		int? seed = null;
		string output = Path.Combine(Environment.CurrentDirectory, "artifacts", "ceramic-fish-test");
		bool help = false;
		for (int index = 0; index < args.Count; index++)
		{
			switch (args[index])
			{
				case "--seed" when index + 1 < args.Count:
					if (!int.TryParse(args[++index], out int value))
					{
						options = null!;
						error = "Invalid --seed. Use a 32-bit signed integer.";
						return false;
					}
					seed = value;
					break;
				case "--output" when index + 1 < args.Count:
					output = args[++index];
					break;
				case "--help" or "-h":
					help = true;
					break;
				default:
					options = null!;
					error = $"Unknown or incomplete option '{args[index]}'.";
					return false;
			}
		}
		options = new(seed, output, help);
		error = null;
		return true;
	}

	private static void PrintUsage()
	{
		Console.WriteLine("CeramicFish.Test - CeramicFish JSON and 256x256 image harness");
		Console.WriteLine("Usage: dotnet run --project CeramicFish.Test -- [options]");
		Console.WriteLine("  --seed <integer>     Deterministic generation seed; random when omitted");
		Console.WriteLine("  --output <directory> Artifact directory (default: artifacts/ceramic-fish-test)");
		Console.WriteLine("  --help, -h           Show this help");
	}

	private sealed record Options(int? Seed, string OutputDirectory, bool Help);
}
