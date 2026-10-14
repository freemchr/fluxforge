using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxForge.Application.Firmware;
using FluxForge.Application.Gw;
using FluxForge.Application.Jobs;
using FluxForge.Application.Settings;
using FluxForge.Application.Tools;
using FluxForge.Desktop.Services;

namespace FluxForge.Desktop.ViewModels;

/// <summary>
/// Drive and device utilities. Read-only checks (speed, USB, delays) run directly; anything that moves
/// the heads over a cleaning disk or destroys data asks first. Every run is recorded under Activity.
/// </summary>
public sealed partial class DriveToolsViewModel(AppState state, JobRunner jobs, ISettingsStore settings, IDialogService dialogs, IFirmwareReleaseSource firmware)
    : ObservableObject, IPageLifecycle
{
    private CancellationTokenSource? _cts;

    public AppState State { get; } = state;
    public JobConsoleViewModel Console { get; } = new();
    public IReadOnlyList<string> Drives { get; } = ["A", "B", "0", "1", "2", "3"];

    [ObservableProperty] public partial string Drive { get; set; } = settings.Load().Drive;
    [ObservableProperty] public partial bool IsRunning { get; set; }
    [ObservableProperty] public partial RpmReading? Rpm { get; set; }
    [ObservableProperty] public partial BandwidthReading? Bandwidth { get; set; }
    [ObservableProperty] public partial IReadOnlyList<(string Name, string Value)> Delays { get; set; } = [];
    [ObservableProperty] public partial string SeekCylinder { get; set; } = "0";
    [ObservableProperty] public partial int CleanPasses { get; set; } = 3;
    [ObservableProperty] public partial bool EraseUnderstood { get; set; }
    [ObservableProperty] public partial string? LastOutcome { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FirmwareAdvice), nameof(FirmwareStatus), nameof(FirmwareUpdateAvailable))]
    public partial ReleaseInfo? LatestFirmware { get; set; }

    [ObservableProperty] public partial bool IsCheckingFirmware { get; set; }
    [ObservableProperty] public partial string FirmwareConfirmation { get; set; } = "";

    /// <summary>True while a firmware flash runs: it must never be cancelled.</summary>
    [ObservableProperty] public partial bool IsFlashing { get; set; }

    public string InstalledFirmware => State.Device.Info is { DeviceFound: true } i
        ? $"{i.Model}, firmware {i.Firmware ?? "unknown"}"
        : "No Greaseweazle connected";

    public FirmwareAdvice FirmwareAdvice => FirmwareAssistant.Advise(State.Device.Info?.Firmware, LatestFirmware);
    public bool FirmwareUpdateAvailable => FirmwareAdvice == FirmwareAdvice.UpdateAvailable;

    public string? FirmwareStatus => LatestFirmware is not { } latest ? null : FirmwareAdvice switch
    {
        FirmwareAdvice.UpToDate => $"Up to date: firmware {latest.Version} is the latest official release ({latest.PublishedAt.ToLocalTime():d MMM yyyy}).",
        FirmwareAdvice.UpdateAvailable => $"Firmware {latest.Version} is available (released {latest.PublishedAt.ToLocalTime():d MMM yyyy}).",
        FirmwareAdvice.NewerThanRelease => $"This board runs newer firmware than the latest stable release ({latest.Version}). Nothing to do.",
        _ => $"The latest official firmware is {latest.Version}. Connect the Greaseweazle to compare.",
    };

    public IReadOnlyList<int> PassChoices { get; } = [1, 2, 3, 5];
    public IReadOnlyList<DelayRow> DelayRows => [.. Delays.Select(d => new DelayRow(d.Name, d.Value))];
    public string? BlockedReason => !State.ToolsReady ? "Install the Greaseweazle host tools first (Home)."
        : !State.DeviceReady ? "Connect a Greaseweazle. " + State.Device.Message : null;

    partial void OnDelaysChanged(IReadOnlyList<(string Name, string Value)> value) => OnPropertyChanged(nameof(DelayRows));
    partial void OnIsRunningChanged(bool value) => NotifyAll();
    partial void OnEraseUnderstoodChanged(bool value) => EraseCommand.NotifyCanExecuteChanged();

    public void OnNavigatedTo()
    {
        State.PropertyChanged -= OnStateChanged;
        State.PropertyChanged += OnStateChanged;
        NotifyAll();
    }

    private void OnStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => NotifyAll();

    partial void OnFirmwareConfirmationChanged(string value) => UpdateFirmwareCommand.NotifyCanExecuteChanged();
    partial void OnLatestFirmwareChanged(ReleaseInfo? value) => UpdateFirmwareCommand.NotifyCanExecuteChanged();
    partial void OnIsFlashingChanged(bool value) => CancelCommand.NotifyCanExecuteChanged();

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(BlockedReason));
        OnPropertyChanged(nameof(InstalledFirmware));
        OnPropertyChanged(nameof(FirmwareAdvice));
        OnPropertyChanged(nameof(FirmwareStatus));
        OnPropertyChanged(nameof(FirmwareUpdateAvailable));
        foreach (var c in new IRelayCommand[] { MeasureRpmCommand, TestBandwidthCommand, ShowDelaysCommand, SeekCommand, ResetCommand, CleanCommand, EraseCommand, CancelCommand, UpdateFirmwareCommand })
            c.NotifyCanExecuteChanged();
    }

    private bool CanRun => !IsRunning && BlockedReason is null;
    private DeviceTarget Target => new(settings.Load().DevicePort, Drive);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task MeasureRpmAsync()
    {
        var r = await RunAsync("Measure drive speed", GwTools.Rpm(Target), TimeSpan.FromSeconds(30));
        Rpm = r is { } x ? GwTools.ParseRpm(x.AllText) : null;
        if (r is not null && Rpm is null) LastOutcome = "No speed reading. Is a disk inserted?";
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task TestBandwidthAsync()
    {
        var r = await RunAsync("Test USB bandwidth", GwTools.Bandwidth(Target), TimeSpan.FromSeconds(60));
        Bandwidth = r is { } x ? GwTools.ParseBandwidth(x.AllText) : null;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ShowDelaysAsync()
    {
        var r = await RunAsync("Show drive delays", GwTools.Delays(Target), TimeSpan.FromSeconds(20));
        Delays = r is { } x ? GwTools.ParseDelays(x.AllText) : [];
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task SeekAsync()
    {
        if (!int.TryParse(SeekCylinder, out var cyl) || cyl is < 0 or > GwTools.MaxSeekCylinder)
        {
            LastOutcome = $"Enter a cylinder from 0 to {GwTools.MaxSeekCylinder}.";
            return;
        }
        await RunAsync($"Seek to cylinder {cyl}", GwTools.Seek(Target, cyl), TimeSpan.FromSeconds(30));
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task ResetAsync() => RunAsync("Reset Greaseweazle", GwTools.Reset(Target), TimeSpan.FromSeconds(20));

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task CleanAsync()
    {
        if (!await dialogs.ConfirmAsync("Clean the drive heads?",
                $"Insert a floppy HEAD-CLEANING disk (moistened as its instructions say) in drive {Drive}. The heads will sweep it {CleanPasses} time(s).\n\nNever run this with a normal disk inserted: it would scrub that disk's surface.",
                "Start cleaning")) return;
        await RunAsync($"Clean heads ({CleanPasses} passes)", GwTools.Clean(Target, CleanPasses), TimeSpan.FromMinutes(5));
    }

    private bool CanErase => CanRun && EraseUnderstood;

    [RelayCommand(CanExecute = nameof(CanErase))]
    private async Task EraseAsync()
    {
        if (!await dialogs.ConfirmAsync("Erase this disk completely?",
                $"Every track of the disk in drive {Drive} will be wiped. Nothing on it can be recovered afterwards.\n\nIf the disk might hold anything you care about, cancel and make a preservation read first.",
                "Erase disk")) return;
        await RunAsync("Erase disk", GwTools.Erase(Target), TimeSpan.FromMinutes(10));
        EraseUnderstood = false; // every erase needs a fresh acknowledgement
    }

    /// <summary>Looks up what <c>gw update</c> would install. Only on request: FluxForge never checks firmware in the background.</summary>
    [RelayCommand]
    private async Task CheckFirmwareAsync()
    {
        IsCheckingFirmware = true;
        try { LatestFirmware = await firmware.GetLatestFirmwareAsync(CancellationToken.None); }
        catch (ToolsInstallException e) { LastOutcome = "Couldn't check for firmware: " + e.Message; }
        finally { IsCheckingFirmware = false; }
    }

    private bool CanUpdateFirmware => CanRun && FirmwareUpdateAvailable && !State.IsJobRunning
        && string.Equals(FirmwareConfirmation.Trim(), "UPDATE", StringComparison.Ordinal);

    [RelayCommand(CanExecute = nameof(CanUpdateFirmware))]
    private async Task UpdateFirmwareAsync()
    {
        if (State.Tools is not { } tools || LatestFirmware is not { } release || State.Device.Info is not { } device) return;
        if (!await dialogs.ConfirmAsync($"Update the firmware to {release.Version}?",
                $"{device.Model} on {device.Port} will be updated from firmware {device.Firmware} to {release.Version}.\n\n" +
                "Keep the USB cable connected and the PC awake until it finishes (about a minute). Don't unplug the board or close FluxForge. " +
                "If the update fails, the official firmware guide explains how to recover.",
                "Update firmware")) return;

        IsRunning = true;
        IsFlashing = true;
        LastOutcome = null;
        var args = GwCommands.UpdateFirmware(release.Version, new DeviceTarget(settings.Load().DevicePort));
        Console.Start(GwCommands.Preview(tools.ExecutablePath, args));
        State.BeginJob();
        try
        {
            var progress = new Progress<JobProgress>(p => { if (p.Line is { } line) Console.Append(line); });
            var record = await Task.Run(() => jobs.RunFirmwareUpdateAsync(tools, release, new DeviceTarget(settings.Load().DevicePort), device, progress));
            Console.Stop(record.Status);
            LastOutcome = $"Firmware update: {StatusVisuals.For(record.Status).Label}. {record.StatusDetail}";
        }
        catch (DeviceBusyException e)
        {
            Console.Note("[FluxForge] " + e.Message);
            Console.Stop(JobStatus.Failed);
        }
        finally
        {
            FirmwareConfirmation = ""; // every flash needs a fresh confirmation
            IsFlashing = false;
            IsRunning = false;
            await State.EndJobAsync(); // re-detects the board, so the new version shows
        }
    }

    [RelayCommand]
    private static void OpenFirmwareGuide() => AppLinks.Open(FirmwareAssistant.RecoveryGuide);

    [RelayCommand]
    private void OpenFirmwareNotes()
    {
        if (LatestFirmware is { } r) AppLinks.Open(r.HtmlUrl.ToString());
    }

    private bool CanCancel => IsRunning && !IsFlashing;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    private async Task<Application.Processes.ProcessResult?> RunAsync(string title, IReadOnlyList<string> args, TimeSpan timeout)
    {
        if (State.Tools is not { } tools) return null;
        settings.Save(settings.Load() with { Drive = Drive });
        _cts = new CancellationTokenSource();
        IsRunning = true;
        LastOutcome = null;
        Console.Start(GwCommands.Preview(tools.ExecutablePath, args));
        State.BeginJob();
        try
        {
            var progress = new Progress<JobProgress>(p => { if (p.Line is { } line) Console.Append(line); });
            var (record, result) = await Task.Run(() => jobs.RunToolAsync(tools, title, args, Target, State.Device.Info, progress, _cts.Token, timeout));
            Console.Stop(record.Status);
            LastOutcome = $"{title}: {StatusVisuals.For(record.Status).Label}. {record.StatusDetail}";
            return record.Status == JobStatus.Succeeded ? result : null;
        }
        catch (DeviceBusyException e)
        {
            Console.Note("[FluxForge] " + e.Message);
            Console.Stop(JobStatus.Failed);
            return null;
        }
        finally
        {
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
            await State.EndJobAsync();
        }
    }
}

public sealed record DelayRow(string Name, string Value);
