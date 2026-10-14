using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using FluxForge.Application.Firmware;
using FluxForge.Application.Gw;
using FluxForge.Application.Tools;

namespace FluxForge.Infrastructure.Tools;

/// <summary>
/// Reads official stable releases from github.com/keirf/greaseweazle (host tools) and
/// keirf/greaseweazle-firmware (what <c>gw update</c> installs) over an HTTPS host allowlist.
/// </summary>
public sealed class GitHubReleaseClient(HttpClient http) : IFirmwareReleaseSource
{
    public static readonly Uri LatestFirmwareApi = new("https://api.github.com/repos/keirf/greaseweazle-firmware/releases/latest");

    public const string Repository = "keirf/greaseweazle";
    public static readonly Uri LatestReleaseApi = new($"https://api.github.com/repos/{Repository}/releases/latest");
    public static readonly Uri ReleasesApi = new($"https://api.github.com/repos/{Repository}/releases?per_page=50");
    public const long MaxDownloadBytes = 150L * 1024 * 1024;

    private static readonly HashSet<string> AllowedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "api.github.com", "github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com",
    };

    public async Task<ReleaseInfo> GetLatestAsync(CancellationToken ct) => ParseRelease(await GetJsonAsync(LatestReleaseApi, ct).ConfigureAwait(false));

    /// <summary>The same "latest" release gw update downloads when given no tag.</summary>
    public async Task<ReleaseInfo> GetLatestFirmwareAsync(CancellationToken ct) => ParseRelease(await GetJsonAsync(LatestFirmwareApi, ct).ConfigureAwait(false));

    /// <summary>Stable releases, newest first, so users can install an older version they depend on.</summary>
    public async Task<IReadOnlyList<ReleaseInfo>> GetReleasesAsync(CancellationToken ct) =>
        ParseReleaseList(await GetJsonAsync(ReleasesApi, ct).ConfigureAwait(false));

    private async Task<string> GetJsonAsync(Uri uri, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, ct).ConfigureAwait(false); }
        catch (HttpRequestException e) { throw new ToolsInstallException("Could not reach GitHub. Check your connection or proxy, or import a downloaded ZIP instead.", e); }

        using (response)
        {
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests &&
                response.Headers.TryGetValues("X-RateLimit-Remaining", out var remaining) && remaining.FirstOrDefault() == "0")
                throw new ToolsInstallException("GitHub's API rate limit was reached. Try again in an hour, or import a downloaded ZIP.");
            if (!response.IsSuccessStatusCode)
                throw new ToolsInstallException($"GitHub returned {(int)response.StatusCode} {response.ReasonPhrase}.");
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
    }

    public static ReleaseInfo ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        if (IsUnstable(doc.RootElement))
            throw new ToolsInstallException("The latest release is not a stable release.");
        return ParseRelease(doc.RootElement);
    }

    public static IReadOnlyList<ReleaseInfo> ParseReleaseList(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var releases = new List<ReleaseInfo>();
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            if (IsUnstable(r)) continue;
            try { releases.Add(ParseRelease(r)); }
            catch (ToolsInstallException) { } // unrecognised tag: not a host-tools release
        }
        return [.. releases.OrderByDescending(r => r.Version)];
    }

    private static bool IsUnstable(JsonElement r) => r.GetProperty("draft").GetBoolean() || r.GetProperty("prerelease").GetBoolean();

    private static ReleaseInfo ParseRelease(JsonElement r)
    {
        var tag = r.GetProperty("tag_name").GetString() ?? "";
        if (!GwVersion.TryParse(tag, out var version))
            throw new ToolsInstallException($"Unrecognised release tag '{tag}'.");

        var assets = r.GetProperty("assets").EnumerateArray().Select(a => new ReleaseAsset(
            a.GetProperty("name").GetString() ?? "",
            new Uri(a.GetProperty("browser_download_url").GetString() ?? throw new ToolsInstallException("Asset has no download URL.")),
            a.GetProperty("size").GetInt64(),
            a.TryGetProperty("digest", out var d) && d.GetString() is { } digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? digest["sha256:".Length..].ToLowerInvariant() : null)).ToList();

        return new ReleaseInfo(tag, version,
            r.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag,
            new Uri(r.GetProperty("html_url").GetString()!),
            r.GetProperty("published_at").GetDateTimeOffset(),
            r.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
            SelectWindowsAsset(assets));
    }

    /// <summary>Picks the Windows host-tools ZIP by inspecting names rather than assuming a fixed filename.</summary>
    public static ReleaseAsset? SelectWindowsAsset(IEnumerable<ReleaseAsset> assets) =>
        assets
            .Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                        && a.Name.Contains("win", StringComparison.OrdinalIgnoreCase)
                        && !a.Name.Contains("src", StringComparison.OrdinalIgnoreCase)
                        && a.DownloadUrl.Scheme == Uri.UriSchemeHttps
                        && AllowedHosts.Contains(a.DownloadUrl.Host))
            .OrderByDescending(a => a.Name.Contains("64", StringComparison.Ordinal))
            .FirstOrDefault();

    public async Task DownloadAsync(ReleaseAsset asset, string destination, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        if (asset.Size > MaxDownloadBytes)
            throw new ToolsInstallException($"Release asset is unexpectedly large ({asset.Size:N0} bytes).");

        Exception? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                await DownloadOnceAsync(asset, destination, progress, ct).ConfigureAwait(false);
                return;
            }
            catch (Exception e) when (e is HttpRequestException or IOException && !ct.IsCancellationRequested)
            {
                last = e;
                progress?.Report(new InstallProgress($"Download failed, retrying ({attempt}/3)…", null));
                await Task.Delay(TimeSpan.FromSeconds(2 * attempt), ct).ConfigureAwait(false);
            }
        }
        throw new ToolsInstallException("Download failed after 3 attempts.", last);
    }

    private async Task DownloadOnceAsync(ReleaseAsset asset, string destination, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        using var response = await http.GetAsync(asset.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        var finalUri = response.RequestMessage?.RequestUri;
        if (finalUri is null || finalUri.Scheme != Uri.UriSchemeHttps || !AllowedHosts.Contains(finalUri.Host))
            throw new ToolsInstallException($"Download was redirected to an untrusted host ({finalUri?.Host}).");
        response.EnsureSuccessStatusCode();

        var temp = destination + ".part";
        await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
        await using (var output = new FileStream(temp, FileMode.Create, FileAccess.Write))
        {
            var buffer = new byte[81920];
            long total = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                total += read;
                if (total > MaxDownloadBytes || (asset.Size > 0 && total > asset.Size))
                    throw new ToolsInstallException("Download is larger than the release metadata says.");
                await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                progress?.Report(new InstallProgress("Downloading…", asset.Size > 0 ? (double)total / asset.Size : null));
            }
            if (asset.Size > 0 && total != asset.Size)
                throw new IOException($"Download truncated ({total:N0} of {asset.Size:N0} bytes).");
        }

        if (asset.Sha256Digest is { } expected)
        {
            // Integrity check against GitHub's published digest. This catches corruption, not a compromised release.
            var actual = await Sha256Async(temp, ct).ConfigureAwait(false);
            if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(temp);
                throw new ToolsInstallException("Downloaded file does not match GitHub's published SHA-256 digest.");
            }
        }
        File.Move(temp, destination, overwrite: true);
    }

    public static async Task<string> Sha256Async(string path, CancellationToken ct)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
    }
}
