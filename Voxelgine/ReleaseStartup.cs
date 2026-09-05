using System.Diagnostics;

namespace Voxelgine;

internal static class ReleaseStartup
{
    internal static bool RedirectToLauncher(string[] args)
    {
#if AURORAFALLS_RELEASE_PACKAGE
        if (Debugger.IsAttached) return false;
        string child = Environment.GetEnvironmentVariable("AURORAFALLS_GAME_CHILD");
        Environment.SetEnvironmentVariable("AURORAFALLS_GAME_CHILD", null);
        if (string.Equals(child, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return false;
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../.."));
        var start = new ProcessStartInfo(Path.Combine(root, "AuroraFalls.exe")) { UseShellExecute = false, WorkingDirectory = root };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        Process.Start(start);
        return true;
#else
        return false;
#endif
    }
}
