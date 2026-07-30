namespace Voxelgine;

internal static class Program
{
	private static void Main(string[] args)
	{
		using ClientApplication application = new(args);
		application.Run();
	}
}
