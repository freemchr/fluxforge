using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxForge.Application.Gw;
using FluxForge.Application.Jobs;
using FluxForge.Application.Settings;
using FluxForge.Desktop.Services;

namespace FluxForge.Desktop.ViewModels;

/// <summary>
/// Drive and device utilities. Read-only checks (speed, USB, delays) run directly; anything that moves
/// the heads over a cleaning disk or destroys data asks first. Every run is recorded under Activity.
/// </summary>
public sealed partial class DriveToolsViewModel(AppState state, JobRunner jobs, ISettingsStore settings, IDialogService dialogs)
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

    private void NotifyAll()
    {
        OnPropertyChanged(nameof(BlockedReason));
        foreach (var c in new IRelayCommand[] { MeasureRpmCommand, TestBandwidthCommand, ShowDelaysCommand, SeekCommand, ResetCommand, CleanCommand, EraseCommand, CancelCommand })
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

    private bool CanCancel => IsRunning;

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
