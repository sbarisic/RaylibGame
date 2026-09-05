using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AuroraFalls.Update.Tests;

public sealed class UpdaterTests : IDisposable
{
    private readonly string temporary = Path.Combine(Path.GetTempPath(), "AuroraFalls-tests-" + Guid.NewGuid().ToString("N"));
    public UpdaterTests() => Directory.CreateDirectory(temporary);
    public void Dispose() => Directory.Delete(temporary, true);

    [Theory]
    [InlineData("v0.1.0", true)] [InlineData("v1.22.333", true)]
    [InlineData("v00.1.0", false)] [InlineData("v0.1.0-beta", false)]
    [InlineData("build_debug_0_0_2", false)] [InlineData("../v0.1.0", false)]
    [InlineData("v9999999999999999.1.0", false)]
    public void VersionsAreStrict(string value, bool valid) => Assert.Equal(valid, Installation.ParseVersion(value) is not null);

    [Theory]
    [InlineData("v0.1.0", false, false, false)]
    [InlineData("v0.0.9", false, false, false)]
    [InlineData("v0.2.0", false, false, true)]
    [InlineData("v0.2.0", true, false, false)]
    [InlineData("v0.2.0", false, true, false)]
    [InlineData("build_debug", false, false, false)]
    public void OnlyNewPublishedVersionsAreSelected(string tag, bool draft, bool prerelease, bool expected)
    {
        using var doc = JsonDocument.Parse(Metadata(tag, new byte[] { 1 }, draft, prerelease));
        Assert.Equal(expected, ReleaseClient.ParseRelease(doc.RootElement, "v0.1.0") is not null);
    }

    [Theory]
    [InlineData("digest", "")]
    [InlineData("browser_download_url", "https://example.com/file.zip")]
    public void RejectsMissingDigestOrUnexpectedOrigin(string property, string value)
    {
        var node = System.Text.Json.Nodes.JsonNode.Parse(Metadata("v0.2.0", [1]))!;
        node["assets"]![0]![property] = value;
        using var doc = JsonDocument.Parse(node.ToJsonString());
        Assert.Throws<InvalidDataException>(() => ReleaseClient.ParseRelease(doc.RootElement, "v0.1.0"));
    }

    [Theory]
    [InlineData("../escape.txt")] [InlineData("/absolute.txt")] [InlineData("C:/escape.txt")]
    [InlineData("a\\..\\escape.txt")] [InlineData("a/file:stream")] [InlineData("a/CON.txt")]
    [InlineData("a/file. ")] [InlineData("a//file")]
    public void RejectsUnsafeArchivePaths(string path)
    {
        string zip = Path.Combine(temporary, "unsafe.zip");
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(archive.CreateEntry(path).Open())) writer.Write("unsafe");
        Assert.Throws<InvalidDataException>(() => UpdateInstaller.Extract(zip, Path.Combine(temporary, "unpack"), default));
    }

    [Fact]
    public void RejectsCaseCollisionsAndLinks()
    {
        foreach (bool link in new[] { false, true })
        {
            string zip = Path.Combine(temporary, $"{link}.zip");
            using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("a.txt");
                if (link) entry.ExternalAttributes = 0xA000 << 16;
                else archive.CreateEntry("A.txt");
            }
            Assert.Throws<InvalidDataException>(() => UpdateInstaller.Extract(zip, Path.Combine(temporary, $"out-{link}"), default));
        }
    }

    [Fact]
    public async Task TwoVersionsUpdateOverHttpAndPreserveRuntimeData()
    {
        string root = CreateInstallation("installed", "v0.1.0");
        string save = Path.Combine(temporary, "runtime-save.bin"); File.WriteAllText(save, "identity and saves");
        byte[] zip = CreateZip("v0.2.0");
        using var fixture = new HttpFixture(Metadata("v0.2.0", zip), zip);
        using var http = fixture.Client();
        var result = await new UpdateInstaller(root, new ReleaseClient(http)).UpdateAsync(Installation.ReadActive(root), null, default);
        Assert.Equal("v0.2.0", result.Current);
        Assert.Equal("v0.1.0", result.Previous);
        Assert.Equal(result, Installation.ReadActive(root));
        Assert.Equal("v0.2.0:updater/AuroraFalls.Updater.exe", File.ReadAllText(Path.Combine(root, "versions/v0.2.0/updater/AuroraFalls.Updater.exe")));
        Assert.True(Directory.Exists(Path.Combine(root, "versions/v0.1.0")));
        Assert.Equal("identity and saves", File.ReadAllText(save));
        Assert.Empty(Directory.GetDirectories(root, ".update-*"));
        Assert.Equal(2, fixture.RequestCount);
        var again = await new UpdateInstaller(root, new ReleaseClient(http)).UpdateAsync(result, null, default);
        Assert.Equal(result, again);
        Assert.Equal(3, fixture.RequestCount); // No second download.
    }

    [Theory]
    [InlineData(403)] [InlineData(404)] [InlineData(429)] [InlineData(500)]
    public async Task GitHubErrorsLaunchInstalledVersion(int status)
    {
        string root = CreateInstallation("installed", "v0.1.0");
        using var fixture = new HttpFixture("{}", [], status);
        using var http = fixture.Client();
        var current = Installation.ReadActive(root);
        Assert.Equal(current, await new UpdateInstaller(root, new ReleaseClient(http)).UpdateAsync(current, null, default));
    }

    [Theory]
    [InlineData("malformed")] [InlineData("corrupt-download")] [InlineData("missing-file")]
    [InlineData("bad-file-hash")] [InlineData("wrong-platform")] [InlineData("wrong-protocol")]
    public async Task InvalidUpdatesLeavePointerUntouched(string failure)
    {
        string root = CreateInstallation("installed", "v0.1.0");
        byte[] zip = CreateZip("v0.2.0", failure);
        string metadata = failure == "malformed" ? "not json" : Metadata("v0.2.0", zip);
        if (failure == "corrupt-download") zip[0] ^= 1;
        using var fixture = new HttpFixture(metadata, zip);
        using var http = fixture.Client();
        var current = Installation.ReadActive(root);
        Assert.Equal(current, await new UpdateInstaller(root, new ReleaseClient(http)).UpdateAsync(current, null, default));
        Assert.Equal(current, Installation.ReadActive(root));
    }

    [Theory]
    [InlineData("downloaded")] [InlineData("promoted")]
    public async Task DiskFailureBeforeActivationKeepsOldVersionAndAllowsRetry(string checkpoint)
    {
        string root = CreateInstallation("installed", "v0.1.0");
        byte[] zip = CreateZip("v0.2.0");
        using var fixture = new HttpFixture(Metadata("v0.2.0", zip), zip);
        using var http = fixture.Client();
        var current = Installation.ReadActive(root);
        var installer = new UpdateInstaller(root, new ReleaseClient(http))
        { Checkpoint = point => { if (point == checkpoint) throw new IOException("Simulated disk full", unchecked((int)0x80070070)); } };
        Assert.Equal(current, await installer.UpdateAsync(current, null, default));
        Assert.Equal(current, Installation.ReadActive(root));
        var retry = await new UpdateInstaller(root, new ReleaseClient(http)).UpdateAsync(current, null, default);
        Assert.Equal("v0.2.0", retry.Current);
    }

    [Fact]
    public async Task LockedPointerCannotActivateAndCanBeRetried()
    {
        string root = CreateInstallation("installed", "v0.1.0");
        byte[] zip = CreateZip("v0.2.0");
        using var fixture = new HttpFixture(Metadata("v0.2.0", zip), zip);
        using var http = fixture.Client();
        var current = Installation.ReadActive(root);
        using (var locked = new FileStream(Path.Combine(root, "current.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.Equal(current, await new UpdateInstaller(root, new ReleaseClient(http)).UpdateAsync(current, null, default));
        Assert.Equal(current, Installation.ReadActive(root));
        Assert.Equal("v0.2.0", (await new UpdateInstaller(root, new ReleaseClient(http)).UpdateAsync(current, null, default)).Current);
    }

    [Fact]
    public async Task CancellationUsesInstalledVersion()
    {
        string root = CreateInstallation("installed", "v0.1.0");
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        using var fixture = new HttpFixture("{}", []);
        using var http = fixture.Client();
        var current = Installation.ReadActive(root);
        Assert.Equal(current, await new UpdateInstaller(root, new ReleaseClient(http)).UpdateAsync(current, null, cancellation.Token));
    }

    [Fact]
    public async Task CancellationDuringDownloadRemovesPartialPackage()
    {
        string root = CreateInstallation("installed", "v0.1.0");
        byte[] zip = CreateZip("v0.2.0");
        using var fixture = new HttpFixture(Metadata("v0.2.0", zip), zip);
        using var http = fixture.Client();
        using var cancellation = new CancellationTokenSource();
        var current = Installation.ReadActive(root);
        var progress = new InlineProgress(p => { if (p.Downloaded > 0) cancellation.Cancel(); });
        Assert.Equal(current, await new UpdateInstaller(root, new ReleaseClient(http)).UpdateAsync(current, progress, cancellation.Token));
        Assert.Equal(current, Installation.ReadActive(root));
        Assert.Empty(Directory.GetDirectories(root, ".update-*"));
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task MetadataAndDownloadStallsHaveBoundedTimeouts(bool metadataStall)
    {
        string root = CreateInstallation("installed", "v0.1.0");
        using var http = new HttpClient(new StallHandler(Metadata("v0.2.0", [1]), metadataStall));
        var client = new ReleaseClient(http) { MetadataTimeout = TimeSpan.FromMilliseconds(100), DownloadStallTimeout = TimeSpan.FromMilliseconds(100) };
        var current = Installation.ReadActive(root);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.Equal(current, await new UpdateInstaller(root, client).UpdateAsync(current, null, default));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
        Assert.Equal(current, Installation.ReadActive(root));
    }

    [Fact]
    public async Task UnwritableActivationTargetFallsBack()
    {
        string root = CreateInstallation("installed", "v0.1.0");
        Directory.CreateDirectory(Path.Combine(root, "current.next.json")); // FileStream throws UnauthorizedAccessException.
        byte[] zip = CreateZip("v0.2.0");
        using var fixture = new HttpFixture(Metadata("v0.2.0", zip), zip);
        using var http = fixture.Client();
        var current = Installation.ReadActive(root);
        Assert.Equal(current, await new UpdateInstaller(root, new ReleaseClient(http)).UpdateAsync(current, null, default));
        Assert.Equal(current, Installation.ReadActive(root));
    }

    private sealed class InlineProgress(Action<UpdateProgress> report) : IProgress<UpdateProgress>
    { public void Report(UpdateProgress value) => report(value); }

    private sealed class StallHandler(string metadata, bool metadataStall) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (metadataStall) await Task.Delay(Timeout.Infinite, cancellationToken);
            return new(HttpStatusCode.OK) { Content = request.RequestUri!.AbsolutePath.EndsWith("latest")
                ? new StringContent(metadata) : new StreamContent(new StalledStream()) };
        }
    }

    private sealed class StalledStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        { await Task.Delay(Timeout.Infinite, cancellationToken); return 0; }
    }

    [Fact]
    public void InterruptedPointerRecoversAndCleanupPreservesUnknownFolders()
    {
        string root = CreateInstallation("installed", "v0.1.0");
        CreateVersion(root, "v0.2.0");
        Installation.Activate(root, new(1, "v0.2.0", "v0.1.0"));
        File.WriteAllText(Path.Combine(root, "current.json"), "interrupted");
        Assert.Equal("v0.1.0", Installation.ReadActive(root).Current);
        Installation.Activate(root, new(1, "v0.2.0", "v0.1.0"));
        CreateVersion(root, "v0.0.9");
        string unknown = Path.Combine(root, "versions/v0.0.8"); Directory.CreateDirectory(unknown); File.WriteAllText(Path.Combine(unknown, "keep"), "user");
        string stage = Path.Combine(root, ".update-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(stage); File.WriteAllText(Path.Combine(stage, ".aurorafalls-staging"), "1");
        string unmarked = Path.Combine(root, ".update-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(unmarked);
        UpdateInstaller.Cleanup(root, Installation.ReadActive(root));
        Assert.False(Directory.Exists(stage)); Assert.True(Directory.Exists(unmarked)); Assert.True(Directory.Exists(unknown));
        Assert.False(Directory.Exists(Path.Combine(root, "versions/v0.0.9")));
        Assert.True(Directory.Exists(Path.Combine(root, "versions/v0.1.0")));
    }

    [Fact]
    public void PerInstallationMutexExcludesAnotherThread()
    {
        string name = Installation.LockName(temporary);
        // Mutex ownership is thread-affine; keep acquire/release on the same thread.
        using var mutex = new Mutex(true, name);
        bool acquired = true;
        var thread = new Thread(() => { using var other = new Mutex(false, name); acquired = other.WaitOne(0); if (acquired) other.ReleaseMutex(); });
        thread.Start(); thread.Join();
        mutex.ReleaseMutex();
        Assert.False(acquired);
        Assert.NotEqual(name, Installation.LockName(Path.Combine(temporary, "other")));
    }

    [Fact]
    public void LaunchArgumentsAreNotReparsedAsShellCommands()
    {
        string executable = Path.Combine(temporary, "spaces and ž", "Voxelgine.exe");
        string[] arguments = ["--data-root", "C:\\save folder\\ž", "literal\"quote", "$(not-a-command)", ""];
        var start = Installation.StartInfo(executable, arguments);
        Assert.Equal(arguments, start.ArgumentList);
        Assert.False(start.UseShellExecute);
        Assert.Equal(Path.GetDirectoryName(executable), start.WorkingDirectory);
    }

    private string CreateInstallation(string name, string version)
    {
        string root = Path.Combine(temporary, name); Directory.CreateDirectory(root);
        CreateVersion(root, version); Installation.Activate(root, new(1, version)); return root;
    }

    private static void CreateVersion(string root, string version)
    {
        string directory = Installation.VersionDirectory(root, version);
        string[] paths = ["updater/AuroraFalls.Updater.exe", "updater/AuroraFalls.Updater.dll", "updater/AuroraFalls.Updater.runtimeconfig.json",
            "updater/AuroraFalls.Updater.deps.json", "updater/coreclr.dll", "updater/hostpolicy.dll", "updater/System.Private.CoreLib.dll",
            "game/Voxelgine.exe", "game/Voxelgine.dll", "game/Voxelgine.runtimeconfig.json", "game/Voxelgine.deps.json",
            "game/hostpolicy.dll", "game/System.Private.CoreLib.dll", "game/VoxelAudioNative.dll", "game/glfw3.dll", "game/coreclr.dll", "game/data/audio/audio-bank.json"];
        var inventory = new Dictionary<string, string>();
        foreach (string path in paths)
        {
            string file = Installation.SafePath(directory, path); Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, version + ":" + path);
            inventory[path] = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file)));
        }
        File.WriteAllText(Path.Combine(directory, "manifest.json"), JsonSerializer.Serialize(new PackageManifest(1, 1, version, "win-x64", inventory), Installation.Json));
    }

    private byte[] CreateZip(string version, string? failure = null)
    {
        string source = CreateInstallation("source-" + Guid.NewGuid().ToString("N"), version);
        string directory = Installation.VersionDirectory(source, version);
        string manifestPath = Path.Combine(directory, "manifest.json");
        var manifest = Installation.Read<PackageManifest>(manifestPath);
        if (failure == "missing-file") File.Delete(Path.Combine(directory, "game/glfw3.dll"));
        if (failure == "bad-file-hash") File.AppendAllText(Path.Combine(directory, "game/glfw3.dll"), "changed");
        if (failure == "wrong-platform") manifest = manifest with { Platform = "linux-x64" };
        if (failure == "wrong-protocol") manifest = manifest with { BootstrapProtocol = 2 };
        File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest, Installation.Json));
        string zip = Path.Combine(temporary, Guid.NewGuid() + ".zip"); ZipFile.CreateFromDirectory(source, zip);
        return File.ReadAllBytes(zip);
    }

    private static string Metadata(string tag, byte[] zip, bool draft = false, bool prerelease = false) => JsonSerializer.Serialize(new
    {
        tag_name = tag, draft, prerelease,
        assets = new[] { new { name = $"AuroraFalls-{tag}-win-x64.zip", browser_download_url = $"https://github.com/{Installation.Repository}/releases/download/{tag}/AuroraFalls-{tag}-win-x64.zip", digest = "sha256:" + Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant(), size = zip.Length } }
    });
}

// Real loopback HTTP exercises streaming/status handling without live GitHub or production URL overrides.
internal sealed class HttpFixture : IDisposable
{
    private readonly HttpListener listener = new();
    private readonly Task server;
    private readonly string endpoint;
    private int requests;
    public int RequestCount => Volatile.Read(ref requests);

    public HttpFixture(string metadata, byte[] zip, int status = 200)
    {
        var reservation = new TcpListener(IPAddress.Loopback, 0); reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port; reservation.Stop();
        endpoint = $"http://localhost:{port}/";
        listener.Prefixes.Add(endpoint); listener.Start();
        server = Task.Run(async () =>
        {
            try
            {
                while (listener.IsListening)
                {
                    var context = await listener.GetContextAsync(); Interlocked.Increment(ref requests);
                    byte[] data = context.Request.RawUrl!.Contains("latest") ? Encoding.UTF8.GetBytes(metadata) : zip;
                    context.Response.StatusCode = status; context.Response.ContentLength64 = data.Length;
                    await context.Response.OutputStream.WriteAsync(data); context.Response.Close();
                }
            }
            catch (Exception e) when (e is HttpListenerException or ObjectDisposedException or IOException) { }
        });
    }

    public HttpClient Client() => new(new Redirect(endpoint)) { Timeout = Timeout.InfiniteTimeSpan };
    private sealed class Redirect(string endpoint) : DelegatingHandler(new HttpClientHandler { UseProxy = false })
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            request.RequestUri = new Uri(endpoint + (request.RequestUri!.AbsolutePath.EndsWith("latest") ? "latest" : "package"));
            return base.SendAsync(request, cancellationToken);
        }
    }
    public void Dispose() { listener.Close(); server.GetAwaiter().GetResult(); }
}
