using System.Diagnostics;
using AuroraFalls.Update;

namespace AuroraFalls.Updater;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        try
        {
            // The updater resides at <root>/versions/<version>/updater/.
            string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../.."));
            if (!string.Equals(root.TrimEnd(Path.DirectorySeparatorChar), Environment.GetEnvironmentVariable("AURORAFALLS_INSTALL_ROOT")?.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
                || !int.TryParse(Environment.GetEnvironmentVariable("AURORAFALLS_BOOTSTRAP_PID"), out int bootstrapPid)
                || !IsBootstrapAlive(bootstrapPid, root))
            {
                Process.Start(Installation.StartInfo(Path.Combine(root, "AuroraFalls.exe"), args));
                return;
            }
            Application.Run(new UpdateWindow(root, args));
        }
        catch (Exception error) { Installation.Log(error); MessageBox.Show(error.Message, "Aurora Falls", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private static bool IsBootstrapAlive(int pid, string root)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return string.Equals(process.MainModule?.FileName, Path.Combine(root, "AuroraFalls.exe"), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }
}

internal sealed class UpdateWindow : Form
{
    private readonly Label status = new() { AutoSize = false, Dock = DockStyle.Top, Height = 45, Text = "Checking for updates…" };
    private readonly ProgressBar progressBar = new() { Dock = DockStyle.Top, Height = 20, Style = ProgressBarStyle.Marquee };
    private readonly Button play = new() { Text = "Play current version", AutoSize = true, Dock = DockStyle.Bottom, Height = 35 };
    private readonly CancellationTokenSource cancellation = new();
    private readonly string root;
    private readonly string[] arguments;
    private bool finished;

    public UpdateWindow(string root, string[] arguments)
    {
        this.root = root;
        this.arguments = arguments;
        Text = "Aurora Falls";
        ClientSize = new Size(420, 145);
        Padding = new Padding(20);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(progressBar); Controls.Add(status); Controls.Add(play);
        play.Click += (_, _) => { play.Enabled = false; status.Text = "Starting installed version…"; cancellation.Cancel(); };
        FormClosing += (_, e) => { if (!finished) { e.Cancel = true; cancellation.Cancel(); } };
    }

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        try
        {
            var current = Installation.ReadActive(root);
            using var http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
            var installer = new UpdateInstaller(root, new ReleaseClient(http));
            var progress = new Progress<UpdateProgress>(value =>
            {
                if (finished || cancellation.IsCancellationRequested) return;
                status.Text = value.Message;
                progressBar.Style = value.Total > 0 ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;
                if (value.Total > 0) progressBar.Value = (int)Math.Clamp(value.Downloaded * 100 / value.Total, 0, 100);
            });
            var selected = await Task.Run(async () =>
            {
                UpdateInstaller.Cleanup(root, current);
                return await installer.UpdateAsync(current, progress, cancellation.Token);
            });
            Process game;
            try { game = StartGame(selected); }
            catch (Exception error) when (selected.Current != current.Current)
            {
                Installation.Log(error);
                try { Installation.Activate(root, current); } catch (Exception rollbackError) { Installation.Log(rollbackError); }
                game = StartGame(current);
            }
            Hide();
            using (game) await game.WaitForExitAsync();
        }
        catch (Exception error) { Installation.Log(error); MessageBox.Show(error.Message, "Aurora Falls", MessageBoxButtons.OK, MessageBoxIcon.Error); }
        finally { finished = true; Close(); cancellation.Dispose(); }
    }

    private Process StartGame(ActiveVersion active)
    {
        string executable = Installation.SafePath(Installation.VersionDirectory(root, active.Current), "game/Voxelgine.exe");
        var start = Installation.StartInfo(executable, arguments);
        start.Environment["AURORAFALLS_GAME_CHILD"] = executable;
        return Process.Start(start) ?? throw new IOException("Could not start the game.");
    }
}
