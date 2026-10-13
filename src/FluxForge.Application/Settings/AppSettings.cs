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
