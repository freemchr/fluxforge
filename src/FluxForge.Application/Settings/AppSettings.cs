namespace FluxForge.Application.Settings;

public sealed record AppSettings
{
    /// <summary>User override ("Use my own installation"). Never modified or overwritten by FluxForge.</summary>
    public string? CustomGwPath { get; init; }

    /// <summary>Pointer to the active managed host-tools version folder.</summary>
    public string? ActiveManagedVersion { get; init; }

    /// <summary>Last known-good managed version, kept for one-click rollback.</summary>
    public string? PreviousManagedVersion { get; init; }

    public DateTimeOffset? LastReleaseCheck { get; init; }

    /// <summary>Check GitHub for newer host tools at most once a day. Users who need a fixed version can turn this off.</summary>
    public bool CheckForUpdates { get; init; } = true;

    /// <summary>A release the user chose not to be reminded about ("Skip this version").</summary>
    public string? SkippedVersion { get; init; }

    /// <summary>Result of the last successful release check, reused until the next daily check.</summary>
    public Tools.ReleaseInfo? CachedLatestRelease { get; init; }
    public string? DevicePort { get; init; }
    public string Drive { get; init; } = "A";
    public string? DefaultOutputFolder { get; init; }
    public string Theme { get; init; } = "Dark";
}

public interface ISettingsStore
{
    AppSettings Load();
    void Save(AppSettings settings);
}
