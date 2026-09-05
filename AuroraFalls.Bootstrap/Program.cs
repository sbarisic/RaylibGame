using System.Diagnostics;
using AuroraFalls.Update;

namespace AuroraFalls.Bootstrap;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        string root = AppContext.BaseDirectory;
        using var mutex = new Mutex(false, Installation.LockName(root));
        bool owns = false;
        try
        {
            try { owns = mutex.WaitOne(0); } catch (AbandonedMutexException) { owns = true; }
            if (!owns) { MessageBox.Show("Aurora Falls is already running or updating.", "Aurora Falls"); return; }
            var active = Installation.ReadActive(root);
            string executable = Installation.SafePath(Installation.VersionDirectory(root, active.Current), "updater/AuroraFalls.Updater.exe");
            var start = Installation.StartInfo(executable, args);
            start.Environment["AURORAFALLS_INSTALL_ROOT"] = Path.GetFullPath(root);
            start.Environment["AURORAFALLS_BOOTSTRAP_PID"] = Environment.ProcessId.ToString();
            using var process = Process.Start(start) ?? throw new IOException("Could not start the updater.");
            process.WaitForExit(); // Own the installation lock until updater AND game have exited.
        }
        catch (Exception error)
        {
            Installation.Log(error);
            MessageBox.Show("Aurora Falls could not start. Extract a fresh release ZIP into a writable folder. Your saved games are kept separately.\n\n" + error.Message, "Aurora Falls", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { if (owns) mutex.ReleaseMutex(); }
    }
}
