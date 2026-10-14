using FluxForge.Application.Gw;

namespace FluxForge.Application.Tools;

public enum HostToolsState { Unknown, Found, Missing, UpdateAvailable, Failed }

public enum HostToolsSource { Custom, Managed, Path }

public sealed record HostToolsInstallation(string ExecutablePath, GwVersion? Version, HostToolsSource Source, bool IsSimulated);

public sealed record HostToolsStatus(HostToolsState State, HostToolsInstallation? Active, string Message);

public sealed record ReleaseAsset(string Name, Uri DownloadUrl, long Size, string? Sha256Digest);

public sealed record ReleaseInfo(string Tag, GwVersion Version, string Name, Uri HtmlUrl, DateTimeOffset PublishedAt, string Notes, ReleaseAsset? WindowsAsset);

public sealed record InstallProgress(string Stage, double? Fraction);

public sealed record ManagedVersion(string Version, string Directory, bool IsActive, bool IsPrevious);

public sealed class ToolsInstallException(string message, Exception? inner = null) : Exception(message, inner);

public interface IHostToolsManager
{
    /// <summary>Custom path → managed active → PATH, each validated by a benign smoke test.</summary>
    Task<HostToolsStatus> DiscoverAsync(CancellationToken ct);

    /// <summary>Latest stable (non-draft, non-prerelease) release from the official repository.</summary>
    Task<ReleaseInfo> GetLatestReleaseAsync(CancellationToken ct);

    Task<HostToolsStatus> InstallAsync(ReleaseInfo release, IProgress<InstallProgress>? progress, CancellationToken ct);

    /// <summary>Offline path: install from a ZIP the user downloaded manually.</summary>
    Task<HostToolsStatus> ImportZipAsync(string zipPath, IProgress<InstallProgress>? progress, CancellationToken ct);

    Task<HostToolsStatus> RollbackAsync(CancellationToken ct);

    /// <summary>Stable releases newest first, including older versions.</summary>
    Task<IReadOnlyList<ReleaseInfo>> GetReleasesAsync(CancellationToken ct);

    /// <summary>Switch to an already-installed managed version (newer or older).</summary>
    Task<HostToolsStatus> ActivateAsync(string version, CancellationToken ct);

    /// <summary>Delete an inactive managed version.</summary>
    void Remove(string version);

    IReadOnlyList<ManagedVersion> ListManaged();
}
