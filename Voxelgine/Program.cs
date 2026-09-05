namespace Voxelgine;

internal static class Program
{
	private static void Main(string[] args)
	{
		if (ReleaseStartup.RedirectToLauncher(args)) return;
		using ClientApplication application = new(args);
		application.Run();
	}
}
