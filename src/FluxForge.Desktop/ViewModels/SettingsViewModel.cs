using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxForge.Application.Settings;
using FluxForge.Application.Tools;
using FluxForge.Desktop.Services;
using FluxForge.Infrastructure.Storage;

namespace FluxForge.Desktop.ViewModels;

public sealed partial class SettingsViewModel(
    AppState state, ToolsSetupViewModel setup, IHostToolsManager tools, ISettingsStore settings,
    ThemeService theme, AppPaths paths) : ObservableObject, IPageLifecycle
{
    public AppState State { get; } = state;
    public ToolsSetupViewModel Setup { get; } = setup;
    public ObservableCollection<ManagedVersion> ManagedVersions { get; } = [];
    public string DataFolder => paths.Root;

    [ObservableProperty] public partial string? CustomGwPath { get; set; }
    [ObservableProperty] public partial string DevicePort { get; set; } = "";
    [ObservableProperty] public partial bool IsDarkTheme { get; set; } = true;

    public string AppVersion => typeof(SettingsViewModel).Assembly.GetName().Version?.ToString() ?? "";

    public void OnNavigatedTo()
    {
        var s = settings.Load();
        CustomGwPath = s.CustomGwPath;
        DevicePort = s.DevicePort ?? "";
        IsDarkTheme = theme.IsDark;
        ManagedVersions.Clear();
        foreach (var v in tools.ListManaged()) ManagedVersions.Add(v);
    }

    partial void OnIsDarkThemeChanged(bool value)
    {
        if (value != theme.IsDark) theme.Apply(value);
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
    private async Task CheckForUpdatesAsync()
    {
        await State.CheckForUpdatesAsync(force: true);
        if (State.UpdateAvailable) Setup.Offer = State.LatestRelease;
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

    [RelayCommand]
    private static void OpenLink(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            Process.Start(new ProcessStartInfo(uri.ToString()) { UseShellExecute = true });
    }
}
