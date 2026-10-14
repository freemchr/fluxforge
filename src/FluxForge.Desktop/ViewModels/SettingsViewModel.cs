using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxForge.Application.Settings;
using FluxForge.Application.Tools;
using FluxForge.Desktop.Services;
using FluxForge.Infrastructure.Storage;

namespace FluxForge.Desktop.ViewModels;

public sealed record ReleaseRow(ReleaseInfo Release, bool Installed, bool Active, bool Latest)
{
    public string Version => Release.Version.ToString();
    public string Published => Release.PublishedAt.ToString("d MMM yyyy", System.Globalization.CultureInfo.CurrentCulture);
    public string Tags => string.Join(" · ", new[] { Latest ? "latest" : null, Active ? "in use" : Installed ? "installed" : null }.OfType<string>());
    public bool CanInstall => !Installed && Release.WindowsAsset is not null;
}

public sealed partial class SettingsViewModel(
    AppState state, ToolsSetupViewModel setup, IHostToolsManager tools, ISettingsStore settings,
    ThemeService theme, AppPaths paths, IDialogService dialogs) : ObservableObject, IPageLifecycle
{
    public AppState State { get; } = state;
    public ToolsSetupViewModel Setup { get; } = setup;
    public ObservableCollection<ManagedVersion> ManagedVersions { get; } = [];
    public ObservableCollection<ReleaseRow> Releases { get; } = [];
    public string DataFolder => paths.Root;

    [ObservableProperty] public partial string? CustomGwPath { get; set; }
    [ObservableProperty] public partial string DevicePort { get; set; } = "";
    [ObservableProperty] public partial bool IsDarkTheme { get; set; } = true;
    [ObservableProperty] public partial bool CheckForUpdates { get; set; } = true;
    [ObservableProperty] public partial string? SkippedVersion { get; set; }
    [ObservableProperty] public partial bool IsLoadingReleases { get; set; }
    [ObservableProperty] public partial string? ReleasesError { get; set; }
    [ObservableProperty] public partial string? VersionError { get; set; }

    public string AppVersion => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString() ?? "";

    public void OnNavigatedTo()
    {
        var s = settings.Load();
        CustomGwPath = s.CustomGwPath;
        DevicePort = s.DevicePort ?? "";
        IsDarkTheme = theme.IsDark;
        CheckForUpdates = s.CheckForUpdates;
        SkippedVersion = s.SkippedVersion;
        RefreshManaged();
    }

    private void RefreshManaged()
    {
        ManagedVersions.Clear();
        foreach (var v in tools.ListManaged()) ManagedVersions.Add(v);
        if (Releases.Count > 0) RebuildReleaseRows([.. Releases.Select(r => r.Release)]);
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        if (value != theme.IsDark) theme.Apply(value);
    }

    partial void OnCheckForUpdatesChanged(bool value)
    {
        if (value != settings.Load().CheckForUpdates) State.SetCheckForUpdates(value);
    }

    [RelayCommand]
    private void ClearSkippedVersion()
    {
        settings.Save(settings.Load() with { SkippedVersion = null });
        SkippedVersion = null;
        State.UpdateDismissed = false;
    }

    [RelayCommand]
    private async Task LoadReleasesAsync()
    {
        IsLoadingReleases = true;
        ReleasesError = null;
        try
        {
            var releases = await tools.GetReleasesAsync(CancellationToken.None);
            RebuildReleaseRows(releases);
            if (releases.Count > 0 && (State.LatestRelease is null || releases[0].Version > State.LatestRelease.Version))
                State.LatestRelease = releases[0];
        }
        catch (Exception e) when (e is ToolsInstallException or HttpRequestException or System.Text.Json.JsonException)
        {
            ReleasesError = e.Message;
        }
        finally { IsLoadingReleases = false; }
    }

    private void RebuildReleaseRows(IReadOnlyList<ReleaseInfo> releases)
    {
        var installed = tools.ListManaged().ToDictionary(v => v.Version);
        var latest = releases.FirstOrDefault()?.Version;
        Releases.Clear();
        foreach (var r in releases)
        {
            var key = r.Version.ToString();
            Releases.Add(new ReleaseRow(r, installed.ContainsKey(key), installed.TryGetValue(key, out var m) && m.IsActive && CustomGwPath is null, r.Version == latest));
        }
    }

    [RelayCommand]
    private async Task InstallReleaseAsync(ReleaseRow row)
    {
        if (await Setup.ReviewAndInstallAsync(row.Release)) RefreshManaged();
    }

    [RelayCommand]
    private async Task UseVersionAsync(ManagedVersion version)
    {
        VersionError = null;
        try
        {
            State.ToolsStatus = await tools.ActivateAsync(version.Version, CancellationToken.None);
            CustomGwPath = null;
            await State.DetectDeviceAsync();
        }
        catch (ToolsInstallException e) { VersionError = e.Message; }
        RefreshManaged();
    }

    [RelayCommand]
    private async Task RemoveVersionAsync(ManagedVersion version)
    {
        if (!await dialogs.ConfirmAsync($"Remove host tools {version.Version}?",
                $"Deletes {version.Directory}. You can reinstall it from the list of available versions.", "Remove")) return;
        VersionError = null;
        try { tools.Remove(version.Version); }
        catch (Exception e) when (e is ToolsInstallException or IOException or UnauthorizedAccessException) { VersionError = e.Message; }
        RefreshManaged();
    }

    [RelayCommand]
    private async Task ClearCustomPathAsync()
    {
        settings.Save(settings.Load() with { CustomGwPath = null });
        CustomGwPath = null;
        await State.RefreshToolsAsync();
        await State.DetectDeviceAsync();
    }

    [RelayCommand]
    private async Task SaveDevicePortAsync()
    {
        var port = DevicePort.Trim();
        settings.Save(settings.Load() with { DevicePort = port.Length == 0 ? null : port });
        await State.DetectDeviceAsync();
    }

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        await State.CheckForUpdatesAsync(force: true);
        State.UpdateDismissed = false;
    }

    [RelayCommand]
    private async Task RefreshAsync()
    {
        await State.RefreshToolsAsync();
        await State.DetectDeviceAsync();
        OnNavigatedTo();
    }

    [RelayCommand]
    private void OpenDataFolder() =>
        Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { paths.Root }, UseShellExecute = false });

    public string RepositoryUrl => AppLinks.Repository;

    [RelayCommand]
    private static void OpenLink(string url) => AppLinks.Open(url);
}
