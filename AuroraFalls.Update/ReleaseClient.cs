using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace AuroraFalls.Update;

public sealed record ReleaseAsset(string Version, Uri Url, string Sha256, long Size);
public sealed record UpdateProgress(string Message, long Downloaded = 0, long Total = 0);

public sealed class ReleaseClient(HttpClient http)
{
    public static readonly Uri Latest = new($"https://api.github.com/repos/{Installation.Repository}/releases/latest");
    public const long MaximumDownloadBytes = 4L * 1024 * 1024 * 1024;
    internal TimeSpan MetadataTimeout { get; init; } = TimeSpan.FromSeconds(5);
    internal TimeSpan DownloadStallTimeout { get; init; } = TimeSpan.FromSeconds(30);

    public async Task<ReleaseAsset?> FindUpdateAsync(string installed, CancellationToken cancellation)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        timeout.CancelAfter(MetadataTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, Latest);
        request.Headers.UserAgent.ParseAdd("AuroraFalls-Updater/1.0");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        // Metadata is small; cap it to avoid buffering arbitrary server responses.
        await response.Content.LoadIntoBufferAsync(1024 * 1024, timeout.Token);
        using var document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(timeout.Token), cancellationToken: timeout.Token);
        return ParseRelease(document.RootElement, installed);
    }

    public static ReleaseAsset? ParseRelease(JsonElement release, string installed)
    {
        if (release.GetProperty("draft").GetBoolean() || release.GetProperty("prerelease").GetBoolean()) return null;
        string? tag = release.GetProperty("tag_name").GetString();
        Version? version = Installation.ParseVersion(tag);
        var current = Installation.ParseVersion(installed) ?? throw new InvalidDataException("Invalid installed version.");
        if (version is null || version <= current) return null;
        string name = $"AuroraFalls-{tag}-win-x64.zip";
        var matches = release.GetProperty("assets").EnumerateArray().Where(a => a.GetProperty("name").GetString() == name).ToArray();
        if (matches.Length != 1) throw new InvalidDataException("Release must contain exactly one Windows package.");
        var asset = matches[0];
        string digest = asset.GetProperty("digest").GetString() ?? "";
        string expectedUrl = $"https://github.com/{Installation.Repository}/releases/download/{tag}/{name}";
        if (asset.GetProperty("browser_download_url").GetString() != expectedUrl
            || !System.Text.RegularExpressions.Regex.IsMatch(digest, "^sha256:[a-fA-F0-9]{64}$"))
            throw new InvalidDataException("Release has an invalid download URL or missing SHA-256 digest.");
        long size = asset.GetProperty("size").GetInt64();
        if (size <= 0 || size > MaximumDownloadBytes) throw new InvalidDataException("Invalid package size.");
        return new(tag!, new Uri(expectedUrl), digest[7..], size);
    }

    public async Task DownloadAsync(ReleaseAsset asset, string destination, IProgress<UpdateProgress>? progress, CancellationToken cancellation)
    {
        using var stall = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        stall.CancelAfter(DownloadStallTimeout);
        using var request = new HttpRequestMessage(HttpMethod.Get, asset.Url);
        request.Headers.UserAgent.ParseAdd("AuroraFalls-Updater/1.0");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, stall.Token);
        response.EnsureSuccessStatusCode();
        using var input = await response.Content.ReadAsStreamAsync(stall.Token);
        using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 128 * 1024, true);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        byte[] buffer = new byte[128 * 1024];
        long total = 0;
        while (true)
        {
            stall.CancelAfter(DownloadStallTimeout);
            int count = await input.ReadAsync(buffer, stall.Token);
            if (count == 0) break;
            total += count;
            if (total > asset.Size || total > MaximumDownloadBytes) throw new InvalidDataException("Download exceeds declared size.");
            await output.WriteAsync(buffer.AsMemory(0, count), cancellation);
            hash.AppendData(buffer, 0, count);
            progress?.Report(new($"Downloading {asset.Version}", total, asset.Size));
        }
        if (total != asset.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(asset.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Downloaded package failed SHA-256/size verification.");
    }
}
