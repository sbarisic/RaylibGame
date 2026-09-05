using System.IO.Compression;

namespace AuroraFalls.Update;

public sealed class UpdateInstaller(string root, ReleaseClient releases)
{
    public const long MaximumExpandedBytes = 8L * 1024 * 1024 * 1024;
    internal Action<string>? Checkpoint { get; init; }

    public async Task<ActiveVersion> UpdateAsync(ActiveVersion current, IProgress<UpdateProgress>? progress, CancellationToken cancellation)
    {
        string? staging = null;
        try
        {
            progress?.Report(new("Checking for updates…"));
            var asset = await releases.FindUpdateAsync(current.Current, cancellation);
            if (asset is null) return current;
            staging = Installation.SafePath(root, ".update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            File.WriteAllText(Path.Combine(staging, ".aurorafalls-staging"), "1");
            string zip = Path.Combine(staging, "download.zip");
            await releases.DownloadAsync(asset, zip, progress, cancellation);
            progress?.Report(new("Installing update…"));
            Checkpoint?.Invoke("downloaded");
            string unpacked = Path.Combine(staging, "package");
            await Task.Run(() => Extract(zip, unpacked, cancellation), cancellation);
            string versionDirectory = Installation.VersionDirectory(unpacked, asset.Version);
            Installation.ValidatePackage(versionDirectory, asset.Version, verifyHashes: true);
            cancellation.ThrowIfCancellationRequested();
            string destination = Installation.VersionDirectory(root, asset.Version);
            if (Directory.Exists(destination))
            {
                // A previous attempt may have finished extraction but not pointer activation.
                Installation.ValidatePackage(destination, asset.Version, verifyHashes: true);
            }
            else
            {
                Directory.CreateDirectory(Installation.SafePath(root, "versions"));
                Directory.Move(versionDirectory, destination);
            }
            var next = new ActiveVersion(1, asset.Version, current.Current);
            Checkpoint?.Invoke("promoted");
            Installation.Activate(root, next);
            return next;
        }
        catch (Exception error) { Installation.Log(error); return current; }
        finally { if (staging is not null) TryRemoveStaging(staging); }
    }

    public static void Extract(string zip, string destination, CancellationToken cancellation)
    {
        using var archive = ZipFile.OpenRead(zip);
        if (archive.Entries.Count > 100_000) throw new InvalidDataException("Too many archive entries.");
        long expanded = 0;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in archive.Entries)
        {
            cancellation.ThrowIfCancellationRequested();
            string relative = entry.FullName.TrimEnd('/');
            string path = Installation.SafePath(destination, relative);
            if (!names.Add(relative) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000
                || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Duplicate or linked archive entry.");
            expanded = checked(expanded + entry.Length);
            if (expanded > MaximumExpandedBytes) throw new InvalidDataException("Expanded package is too large.");
            if (entry.FullName.EndsWith('/')) { Directory.CreateDirectory(path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            using var input = entry.Open();
            using var output = new FileStream(path, FileMode.CreateNew);
            byte[] buffer = new byte[128 * 1024];
            long written = 0;
            int count;
            while ((count = input.Read(buffer)) != 0)
            {
                cancellation.ThrowIfCancellationRequested();
                written += count;
                if (written > entry.Length) throw new InvalidDataException("Invalid expanded file size.");
                output.Write(buffer, 0, count);
            }
            if (written != entry.Length) throw new InvalidDataException("Truncated archive entry.");
        }
    }

    public static void Cleanup(string root, ActiveVersion active)
    {
        try
        {
            foreach (string path in Directory.EnumerateDirectories(root, ".update-*")) TryRemoveStaging(path);
            string versions = Installation.SafePath(root, "versions");
            foreach (string path in Directory.EnumerateDirectories(versions))
            {
                string version = Path.GetFileName(path);
                if (version == active.Current || version == active.Previous || Installation.ParseVersion(version) is null) continue;
                // The embedded inventory identifies folders owned by this updater. Unknown folders stay untouched.
                try { Installation.ValidatePackage(path, version, verifyHashes: true); SafeDeleteTree(versions, path); }
                catch (Exception e) { Installation.Log(e); }
            }
        }
        catch (Exception error) { Installation.Log(error); }
    }

    private static void TryRemoveStaging(string path)
    {
        try
        {
            if (!System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(path), "^\\.update-[a-f0-9]{32}$")) return;
            Installation.SafePath(Path.GetDirectoryName(path)!, Path.GetFileName(path));
            if (File.ReadAllText(Installation.SafePath(path, ".aurorafalls-staging")) == "1")
                SafeDeleteTree(Path.GetDirectoryName(path)!, path);
        }
        catch (Exception error) { Installation.Log(error); }
    }

    private static void SafeDeleteTree(string parent, string target)
    {
        // Validate each level without following reparse points before any recursive deletion.
        Installation.SafePath(parent, Path.GetFileName(target));
        void Check(string directory)
        {
            foreach (string path in Directory.EnumerateFileSystemEntries(directory))
            {
                Installation.SafePath(directory, Path.GetFileName(path));
                if (Directory.Exists(path)) Check(path);
            }
        }
        Check(target);
        Directory.Delete(target, recursive: true);
    }
}
