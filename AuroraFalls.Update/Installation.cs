using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace AuroraFalls.Update;

public sealed record ActiveVersion(int SchemaVersion, string Current, string? Previous = null);
public sealed record PackageManifest(int SchemaVersion, int BootstrapProtocol, string Version, string Platform, Dictionary<string, string> Files);

public static partial class Installation
{
    public const string Repository = "sbarisic/RaylibGame";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    [GeneratedRegex(@"^v(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$")]
    private static partial Regex VersionPattern();

    public static Version? ParseVersion(string? value) => value is not null && VersionPattern().IsMatch(value)
        && Version.TryParse(value[1..], out var version) ? version : null;

    public static string VersionDirectory(string root, string version)
    {
        if (ParseVersion(version) is null) throw new InvalidDataException("Invalid release version.");
        return SafePath(root, "versions/" + version);
    }

    // ZIP entries, manifests and on-disk pointers use the same strict Windows path rules.
    public static string SafePath(string root, string relative)
    {
        if (string.IsNullOrEmpty(relative) || relative.Contains('\\') || Path.IsPathRooted(relative))
            throw new InvalidDataException("Unsafe package path.");
        foreach (string segment in relative.Split('/'))
        {
            if (segment.Length == 0 || segment is "." or ".." || segment.EndsWith('.') || segment.EndsWith(' ')
                || segment.Any(c => c < 32 || "<>:\"|?*".Contains(c))
                || Regex.IsMatch(segment.Split('.')[0], @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])$", RegexOptions.IgnoreCase))
                throw new InvalidDataException("Unsafe package path.");
        }
        var parent = Path.GetFullPath(root);
        RejectLink(parent);
        string result = parent;
        foreach (string segment in relative.Split('/')) { result = Path.Combine(result, segment); RejectLink(result); }
        return result;
    }

    private static void RejectLink(string path)
    {
        if ((File.Exists(path) || Directory.Exists(path)) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Update paths cannot contain junctions or symbolic links.");
    }

    public static T Read<T>(string path) => JsonSerializer.Deserialize<T>(File.ReadAllText(path), Json)
        ?? throw new InvalidDataException("Empty update metadata.");

    public static ActiveVersion ReadActive(string root)
    {
        try { return ValidateActive(root, Read<ActiveVersion>(SafePath(root, "current.json"))); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or ArgumentException)
        {
            // File.Replace leaves the old pointer here. Do not guess by scanning version folders.
            return ValidateActive(root, Read<ActiveVersion>(SafePath(root, "current.previous.json")));
        }
    }

    private static ActiveVersion ValidateActive(string root, ActiveVersion active)
    {
        if (active.SchemaVersion != 1 || (active.Previous is not null && ParseVersion(active.Previous) is null))
            throw new InvalidDataException("Unsupported installation pointer.");
        ValidatePackage(VersionDirectory(root, active.Current), active.Current, verifyHashes: false);
        return active;
    }

    public static void Activate(string root, ActiveVersion active)
    {
        ValidateActive(root, active);
        string temporary = SafePath(root, "current.next.json");
        using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        { JsonSerializer.Serialize(stream, active, Json); stream.Flush(flushToDisk: true); }
        string current = SafePath(root, "current.json");
        if (File.Exists(current)) File.Replace(temporary, current, SafePath(root, "current.previous.json"));
        else File.Move(temporary, current);
    }

    public static void ValidatePackage(string directory, string version, bool verifyHashes)
    {
        var manifest = Read<PackageManifest>(SafePath(directory, "manifest.json"));
        if (manifest.SchemaVersion != 1 || manifest.BootstrapProtocol != 1 || manifest.Version != version
            || ParseVersion(version) is null || manifest.Platform != "win-x64" || manifest.Files is null || manifest.Files.Count == 0)
            throw new InvalidDataException("Incompatible release package.");
        foreach (var required in new[] { "updater/AuroraFalls.Updater.exe", "updater/AuroraFalls.Updater.dll",
                     "updater/AuroraFalls.Updater.runtimeconfig.json", "updater/AuroraFalls.Updater.deps.json",
                     "updater/coreclr.dll", "updater/hostpolicy.dll", "updater/System.Private.CoreLib.dll",
                     "game/Voxelgine.exe", "game/Voxelgine.dll", "game/Voxelgine.runtimeconfig.json", "game/Voxelgine.deps.json",
                     "game/hostpolicy.dll", "game/System.Private.CoreLib.dll", "game/VoxelAudioNative.dll",
                     "game/glfw3.dll", "game/coreclr.dll", "game/data/audio/audio-bank.json" })
            if (!manifest.Files.ContainsKey(required)) throw new InvalidDataException($"Package is missing {required}.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (relative, hash) in manifest.Files)
        {
            if (!(relative.StartsWith("game/", StringComparison.Ordinal) || relative.StartsWith("updater/", StringComparison.Ordinal))
                || !names.Add(relative) || !Regex.IsMatch(hash ?? "", "^[a-fA-F0-9]{64}$"))
                throw new InvalidDataException("Invalid package inventory.");
            string path = SafePath(directory, relative);
            if (!File.Exists(path)) throw new InvalidDataException($"Package file missing: {relative}");
            if (verifyHashes)
            {
                using var stream = File.OpenRead(path);
                if (!Convert.ToHexString(SHA256.HashData(stream)).Equals(hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"Package checksum failed: {relative}");
            }
        }
        if (verifyHashes)
            foreach (string path in EnumerateSafeFiles(directory))
            {
                string relative = Path.GetRelativePath(directory, path).Replace('\\', '/');
                SafePath(directory, relative);
                if (relative != "manifest.json" && !names.Contains(relative)) throw new InvalidDataException("Unlisted package file.");
            }
    }

    private static IEnumerable<string> EnumerateSafeFiles(string directory)
    {
        foreach (string path in Directory.EnumerateFileSystemEntries(directory))
        {
            SafePath(directory, Path.GetFileName(path));
            if (Directory.Exists(path))
            {
                foreach (string child in EnumerateSafeFiles(path)) yield return child;
            }
            else yield return path;
        }
    }

    public static string LockName(string root) => "Local\\AuroraFalls-" + Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar).ToUpperInvariant())));

    public static ProcessStartInfo StartInfo(string executable, IEnumerable<string> arguments)
    {
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(executable)! };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        return start;
    }

    public static void Log(Exception error)
    {
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AuroraFallsUpdater", "logs");
            Directory.CreateDirectory(path);
            string file = Path.Combine(path, "updater.log");
            if (File.Exists(file) && new FileInfo(file).Length > 2_000_000) File.Move(file, file + ".previous", true);
            File.AppendAllText(file, $"{DateTimeOffset.UtcNow:O} {error.GetType().Name}: {error.Message}{Environment.NewLine}");
        }
        catch { /* Logging must never prevent offline startup. */ }
    }
}
