using CommunityToolkit.Mvvm.ComponentModel;
using FluxForge.Application.Jobs;
using Velopack;
using Velopack.Sources;

namespace FluxForge.Desktop.Services;

/// <summary>
/// Updates for FluxForge itself (not the Greaseweazle host tools), from this project's GitHub Releases.
/// Only works for installed builds; never applies an update while a disk job is running.
/// </summary>
public sealed partial class AppUpdateService(IJobActivity jobs) : ObservableObject
{
    private readonly UpdateManager _manager = new(new GithubSource(AppLinks.Repository, accessToken: null, prerelease: false));
    private UpdateInfo? _pending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateReady))]
    public partial string? AvailableVersion { get; set; }

    [ObservableProperty]
    public partial string? Status { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public bool IsInstalled => _manager.IsInstalled;
    public bool UpdateReady => AvailableVersion is not null;

    public string CurrentVersion =>
        _manager.IsInstalled ? _manager.CurrentVersion?.ToString() ?? "unknown"
        : (typeof(AppUpdateService).Assembly.GetName().Version?.ToString(3) ?? "dev") + " (development build)";

    public async Task CheckAsync(bool userInitiated)
    {
        if (!IsInstalled)
        {
            if (userInitiated) Status = "Updates are available in installed builds. This is a development build.";
            return;
        }
        IsBusy = true;
        try
        {
            _pending = await _manager.CheckForUpdatesAsync();
            AvailableVersion = _pending?.TargetFullRelease.Version.ToString();
            Status = AvailableVersion is { } v ? $"FluxForge {v} is available." : "FluxForge is up to date.";
        }
        catch (Exception e)
        {
            // Private repository, offline, rate-limited… never fatal.
            if (userInitiated) Status = "Couldn't check for FluxForge updates: " + e.Message;
        }
        finally { IsBusy = false; }
    }

    public async Task DownloadAndRestartAsync()
    {
        if (_pending is null) return;
        if (jobs.HasActiveJobs)
        {
            Status = "Finish or cancel the current disk job before updating.";
            return;
        }
        IsBusy = true;
        try
        {
            await _manager.DownloadUpdatesAsync(_pending, p => Status = $"Downloading update… {p}%");
            Status = "Restarting to finish the update…";
            _manager.ApplyUpdatesAndRestart(_pending.TargetFullRelease);
        }
        catch (Exception e) { Status = "The update failed: " + e.Message; }
        finally { IsBusy = false; }
    }
}
