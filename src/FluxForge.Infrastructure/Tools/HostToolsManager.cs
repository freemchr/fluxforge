using FluxForge.Application.Gw;
using FluxForge.Application.Jobs;
using FluxForge.Application.Processes;
using FluxForge.Application.Settings;
using FluxForge.Application.Tools;
using FluxForge.Infrastructure.Storage;

namespace FluxForge.Infrastructure.Tools;

internal sealed record ToolsManifest(string Version, string ExecutableRelativePath, string Source, string? ArchiveSha256, DateTimeOffset InstalledAt);

/// <summary>
/// Discovers, installs, activates and rolls back Greaseweazle host tools in a per-user managed folder
/// (PRD §8). Activation is a pointer in settings, so a failed install never disturbs the active version.
/// </summary>
public sealed class HostToolsManager(
    IProcessRunner runner,
    ISettingsStore settings,
    AppPaths paths,
    GitHubReleaseClient releases,
    IJobActivity jobs,
    Func<string, string?>? environment = null) : IHostToolsManager
{
    public const string ManifestFile = "fluxforge-tools.json";
    private static readonly TimeSpan SmokeTimeout = TimeSpan.FromSeconds(20);
    private readonly Func<string, string?> _env = environment ?? Environment.GetEnvironmentVariable;
    private readonly SemaphoreSlim _installGate = new(1, 1);

    public async Task<HostToolsStatus> DiscoverAsync(CancellationToken ct)
    {
        var s = settings.Load();

        if (!string.IsNullOrWhiteSpace(s.CustomGwPath))
        {
            var custom = await ProbeAsync(s.CustomGwPath, HostToolsSource.Custom, ct).ConfigureAwait(false);
            return custom is not null
                ? Found(custom, "Using your own Greaseweazle installation.")
                : new HostToolsStatus(HostToolsState.Failed, null, $"Your custom gw.exe ({s.CustomGwPath}) could not be validated. Fix or clear it in Settings.");
        }

        if (s.ActiveManagedVersion is { } active && ManagedExecutable(active) is { } managedExe)
        {
            var managed = await ProbeAsync(managedExe, HostToolsSource.Managed, ct).ConfigureAwait(false);
            if (managed is not null) return Found(managed, $"Managed host tools {managed.Version}.");
            return new HostToolsStatus(HostToolsState.Failed, null,
                $"Managed host tools {active} failed their self-test." + (s.PreviousManagedVersion is not null ? " You can roll back in Settings." : " Reinstall from Settings."));
        }

        foreach (var candidate in PathCandidates())
        {
            var onPath = await ProbeAsync(candidate, HostToolsSource.Path, ct).ConfigureAwait(false);
            if (onPath is not null) return Found(onPath, $"Found gw.exe on PATH ({candidate}).");
        }

        return new HostToolsStatus(HostToolsState.Missing, null, "Greaseweazle host tools are not installed.");
    }

    public Task<ReleaseInfo> GetLatestReleaseAsync(CancellationToken ct) => releases.GetLatestAsync(ct);

    public Task<IReadOnlyList<ReleaseInfo>> GetReleasesAsync(CancellationToken ct) => releases.GetReleasesAsync(ct);

    public async Task<HostToolsStatus> ActivateAsync(string version, CancellationToken ct)
    {
        GuardIdle();
        var s = settings.Load();
        if (s.ActiveManagedVersion == version && string.IsNullOrEmpty(s.CustomGwPath))
            return await DiscoverAsync(ct).ConfigureAwait(false);
        var exe = ManagedExecutable(version) ?? throw new ToolsInstallException($"Host tools {version} are not installed.");
        var probe = await ProbeAsync(exe, HostToolsSource.Managed, ct).ConfigureAwait(false)
                    ?? throw new ToolsInstallException($"Host tools {version} failed their self-test.");
        // Choosing a managed version also stops using a custom gw.exe (the custom file itself is untouched).
        settings.Save(s with { ActiveManagedVersion = version, PreviousManagedVersion = s.ActiveManagedVersion ?? s.PreviousManagedVersion, CustomGwPath = null });
        return Found(probe, $"Now using host tools {version}.");
    }

    public void Remove(string version)
    {
        GuardIdle();
        var s = settings.Load();
        if (s.ActiveManagedVersion == version)
            throw new ToolsInstallException("The active version can't be removed. Switch to another version first.");
        var dir = Path.GetFullPath(Path.Combine(paths.ToolsRoot, version));
        if (!dir.StartsWith(Path.GetFullPath(paths.ToolsRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(Path.Combine(dir, ManifestFile)))
            throw new ToolsInstallException($"Host tools {version} are not a FluxForge-managed installation.");
        Directory.Delete(dir, recursive: true);
        if (s.PreviousManagedVersion == version)
            settings.Save(s with { PreviousManagedVersion = null });
    }

    public async Task<HostToolsStatus> InstallAsync(ReleaseInfo release, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        var asset = release.WindowsAsset
                    ?? throw new ToolsInstallException($"Release {release.Tag} has no Windows build that FluxForge recognises. Download it manually from {release.HtmlUrl} and use Import ZIP.");
        await _installGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            GuardIdle();
            Directory.CreateDirectory(paths.Downloads);
            var zip = Path.Combine(paths.Downloads, Path.GetFileName(asset.Name));
            await releases.DownloadAsync(asset, zip, progress, ct).ConfigureAwait(false);
            try
            {
                return await InstallFromZipAsync(zip, release.Version, asset.DownloadUrl.ToString(), progress, ct).ConfigureAwait(false);
            }
            finally { TryDeleteFile(zip); }
        }
        finally { _installGate.Release(); }
    }

    public async Task<HostToolsStatus> ImportZipAsync(string zipPath, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        await _installGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            GuardIdle();
            return await InstallFromZipAsync(zipPath, null, "Imported: " + zipPath, progress, ct).ConfigureAwait(false);
        }
        finally { _installGate.Release(); }
    }

    public async Task<HostToolsStatus> RollbackAsync(CancellationToken ct)
    {
        GuardIdle();
        var s = settings.Load();
        if (s.PreviousManagedVersion is not { } previous || ManagedExecutable(previous) is not { } exe)
            throw new ToolsInstallException("There is no previous managed version to roll back to.");
        var probe = await ProbeAsync(exe, HostToolsSource.Managed, ct).ConfigureAwait(false)
                    ?? throw new ToolsInstallException($"The previous version ({previous}) also fails its self-test.");
        settings.Save(s with { ActiveManagedVersion = previous, PreviousManagedVersion = s.ActiveManagedVersion });
        return Found(probe, $"Rolled back to host tools {previous}.");
    }

    public IReadOnlyList<ManagedVersion> ListManaged()
    {
        if (!Directory.Exists(paths.ToolsRoot)) return [];
        var s = settings.Load();
        return [.. Directory.EnumerateDirectories(paths.ToolsRoot)
            .Where(d => !Path.GetFileName(d).StartsWith('.') && File.Exists(Path.Combine(d, ManifestFile)))
            .Select(d => Path.GetFileName(d))
            .Select(v => new ManagedVersion(v, Path.Combine(paths.ToolsRoot, v), v == s.ActiveManagedVersion, v == s.PreviousManagedVersion))
            .OrderByDescending(v => GwVersion.TryParse(v.Version, out var p) ? p : default)];
    }

    private async Task<HostToolsStatus> InstallFromZipAsync(string zip, GwVersion? expected, string source, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        Directory.CreateDirectory(paths.ToolsRoot);
        var staging = Path.Combine(paths.ToolsRoot, $".staging-{Guid.NewGuid():N}");
        try
        {
            progress?.Report(new InstallProgress("Checking and extracting archive…", null));
            var archiveHash = await GitHubReleaseClient.Sha256Async(zip, ct).ConfigureAwait(false);
            try { SafeZipExtractor.Extract(zip, staging); }
            catch (InvalidDataException e) { throw new ToolsInstallException("The file is not a valid ZIP archive.", e); }
            catch (UnsafeArchiveException e) { throw new ToolsInstallException("The archive was rejected: " + e.Message, e); }

            var exes = Directory.GetFiles(staging, "gw.exe", SearchOption.AllDirectories);
            if (exes.Length != 1)
                throw new ToolsInstallException(exes.Length == 0 ? "The archive does not contain gw.exe." : "The archive contains more than one gw.exe.");

            progress?.Report(new InstallProgress("Running self-test…", null));
            var probe = await ProbeAsync(exes[0], HostToolsSource.Managed, ct).ConfigureAwait(false)
                        ?? throw new ToolsInstallException("gw.exe from the archive failed its self-test.");
            if (probe.Version is not { } version)
                throw new ToolsInstallException("Could not confirm the version of gw.exe in the archive.");
            if (expected is { } e2 && version != e2)
                throw new ToolsInstallException($"gw.exe reports version {version}, but the release is {e2}.");

            var versionKey = version.ToString();
            var s = settings.Load();
            var target = Path.Combine(paths.ToolsRoot, versionKey);
            if (Directory.Exists(target))
            {
                if (s.ActiveManagedVersion == versionKey)
                    return Found(probe with { ExecutablePath = ManagedExecutable(versionKey) ?? probe.ExecutablePath }, $"Host tools {versionKey} are already installed.");
                Directory.Delete(target, recursive: true); // inactive managed copy of the same version: replace it
            }

            var relative = Path.GetRelativePath(staging, exes[0]);
            Json.WriteAtomic(Path.Combine(staging, ManifestFile), new ToolsManifest(versionKey, relative, source, archiveHash, DateTimeOffset.UtcNow));
            Directory.Move(staging, target);

            // Atomic activation: flip the pointer, keep the last known-good version for rollback.
            var previous = s.ActiveManagedVersion != versionKey ? s.ActiveManagedVersion : s.PreviousManagedVersion;
            settings.Save(s with { ActiveManagedVersion = versionKey, PreviousManagedVersion = previous });
            progress?.Report(new InstallProgress("Installed.", 1));
            return Found(probe with { ExecutablePath = Path.Combine(target, relative) }, $"Installed Greaseweazle host tools {versionKey}.");
        }
        finally
        {
            if (Directory.Exists(staging))
                try { Directory.Delete(staging, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// Benign self-test. <c>gw info</c> prints the host-tools version before looking for hardware, so it
    /// identifies the tools even with no board attached; the exit code is irrelevant here.
    /// </summary>
    private async Task<HostToolsInstallation?> ProbeAsync(string exe, HostToolsSource source, CancellationToken ct)
    {
        if (!File.Exists(exe) || !string.Equals(Path.GetFileName(exe), "gw.exe", StringComparison.OrdinalIgnoreCase)) return null;
        var result = await runner.RunAsync(new ProcessRequest(exe, GwCommands.Info(), Timeout: SmokeTimeout), null, ct).ConfigureAwait(false);
        var info = GwOutputParser.ParseInfo(result.AllText);
        if (info.HostToolsVersion is null) return null;
        return new HostToolsInstallation(Path.GetFullPath(exe), info.HostToolsVersion, source, info.IsSimulated);
    }

    private string? ManagedExecutable(string version)
    {
        var dir = Path.Combine(paths.ToolsRoot, version);
        var manifest = Json.Read<ToolsManifest>(Path.Combine(dir, ManifestFile));
        if (manifest is null) return null;
        var exe = Path.GetFullPath(Path.Combine(dir, manifest.ExecutableRelativePath));
        return exe.StartsWith(Path.GetFullPath(dir), StringComparison.OrdinalIgnoreCase) && File.Exists(exe) ? exe : null;
    }

    private IEnumerable<string> PathCandidates() =>
        (_env("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(dir => { try { return Path.Combine(dir, "gw.exe"); } catch (ArgumentException) { return null; } })
            .OfType<string>()
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private void GuardIdle()
    {
        if (jobs.HasActiveJobs)
            throw new ToolsInstallException("Host tools can't be changed while a disk job is running.");
    }

    private static HostToolsStatus Found(HostToolsInstallation i, string message) => new(HostToolsState.Found, i, message);

    private static void TryDeleteFile(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
