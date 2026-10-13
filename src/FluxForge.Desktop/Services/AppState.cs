using CommunityToolkit.Mvvm.ComponentModel;
using FluxForge.Application.Devices;
using FluxForge.Application.Settings;
using FluxForge.Application.Tools;

namespace FluxForge.Desktop.Services;

/// <summary>
/// Shared, observable picture of the three separate concepts the PRD insists on:
/// host tools, the physical device, and its firmware.
/// </summary>
public sealed partial class AppState(IHostToolsManager tools, DeviceService devices, ISettingsStore settings) : ObservableObject
{
    private static readonly TimeSpan ReleaseCheckInterval = TimeSpan.FromDays(1);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Tools), nameof(ToolsReady), nameof(IsSimulated), nameof(UpdateAvailable), nameof(ToolsSummary))]
    public partial HostToolsStatus ToolsStatus { get; set; } = new(HostToolsState.Unknown, null, "Checking for Greaseweazle host tools…");

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DeviceReady), nameof(IsSimulated), nameof(DeviceSummary), nameof(FirmwareSummary))]
    public partial DeviceStatus Device { get; set; } = DeviceStatus.Unknown;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(UpdateAvailable))]
    public partial ReleaseInfo? LatestRelease { get; set; }

    [ObservableProperty]
    public partial bool IsJobRunning { get; set; }

    [ObservableProperty]
    public partial bool IsDetecting { get; set; }

    [ObservableProperty]
    public partial string? UpdateCheckMessage { get; set; }

    public HostToolsInstallation? Tools => ToolsStatus.Active;
    public bool ToolsReady => Tools is not null;
    public bool DeviceReady => Device.State == DeviceState.Connected;
    public bool IsSimulated => Tools?.IsSimulated == true || Device.Info?.IsSimulated == true;

    public bool UpdateAvailable =>
        LatestRelease is not null && Tools is { Source: HostToolsSource.Managed, Version: { } v } && v < LatestRelease.Version;

    public string ToolsSummary => Tools is { } t ? $"gw {t.Version?.ToString() ?? "?"} ({t.Source.ToString().ToLowerInvariant()})" : "Not installed";
    public string DeviceSummary => Device.Info is { DeviceFound: true } i ? $"{i.Model} · {i.Port}" : StatusVisuals.For(Device.State).Label;
    public string FirmwareSummary => Device.Info?.Firmware is { } fw ? $"Firmware {fw}" : "Firmware unknown";

    public async Task InitializeAsync()
    {
        await RefreshToolsAsync();
        if (ToolsReady) await DetectDeviceAsync();
        await CheckForUpdatesAsync(force: false);
    }

    public async Task RefreshToolsAsync()
    {
        if (IsJobRunning) return;
        try { ToolsStatus = await tools.DiscoverAsync(CancellationToken.None); }
        catch (Exception e) { ToolsStatus = new HostToolsStatus(HostToolsState.Failed, null, e.Message); }
    }

    public async Task DetectDeviceAsync()
    {
        if (Tools is not { } t || IsJobRunning || IsDetecting) return;
        IsDetecting = true;
        try { Device = await devices.DetectAsync(t, settings.Load().DevicePort, CancellationToken.None); }
        catch (Exception e) { Device = new DeviceStatus(DeviceState.Error, null, e.Message); }
        finally { IsDetecting = false; }
    }

    /// <summary>At most once a day unless forced (PRD §8.8). Network failures are reported, never fatal.</summary>
    public async Task CheckForUpdatesAsync(bool force)
    {
        var s = settings.Load();
        if (!force && s.LastReleaseCheck is { } last && DateTimeOffset.UtcNow - last < ReleaseCheckInterval)
            return;
        try
        {
            LatestRelease = await tools.GetLatestReleaseAsync(CancellationToken.None);
            UpdateCheckMessage = UpdateAvailable ? $"Greaseweazle host tools {LatestRelease.Version} are available." : $"Latest release: {LatestRelease.Version}.";
            settings.Save(settings.Load() with { LastReleaseCheck = DateTimeOffset.UtcNow });
        }
        catch (Exception e) { UpdateCheckMessage = "Could not check for updates: " + e.Message; }
    }

    public void BeginJob()
    {
        IsJobRunning = true;
        Device = Device with { State = DeviceState.Busy, Message = "A job is using the Greaseweazle." };
    }

    public async Task EndJobAsync()
    {
        IsJobRunning = false;
        await DetectDeviceAsync();
    }
}
