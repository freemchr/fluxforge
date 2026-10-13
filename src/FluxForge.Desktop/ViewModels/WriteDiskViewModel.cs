using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxForge.Application.Formats;
using FluxForge.Application.Gw;
using FluxForge.Application.Jobs;
using FluxForge.Application.Safety;
using FluxForge.Application.Settings;
using FluxForge.Desktop.Services;

namespace FluxForge.Desktop.ViewModels;

/// <summary>
/// Guarded write flow: inspect image → choose format → explicit "I understand" → separate Write button →
/// final confirmation dialog. Nothing is written without all three.
/// </summary>
public sealed partial class WriteDiskViewModel : ObservableObject, IPageLifecycle
{
    private readonly JobRunner _jobs;
    private readonly FormatRegistry _formats;
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly INavigator _navigator;
    private CancellationTokenSource? _cts;

    public WriteDiskViewModel(AppState state, JobRunner jobs, FormatRegistry formats, ISettingsStore settings, IDialogService dialogs, INavigator navigator)
    {
        State = state;
        _jobs = jobs;
        _formats = formats;
        _settings = settings;
        _dialogs = dialogs;
        _navigator = navigator;
        Drive = settings.Load().Drive;
        state.PropertyChanged += (_, _) => Refresh();
    }

    public AppState State { get; }
    public IReadOnlyList<string> Drives { get; } = ["A", "B", "0", "1", "2"];
    public TrackMapViewModel TrackMap { get; } = new();
    public JobConsoleViewModel Console { get; } = new();

    [ObservableProperty] public partial string? ImagePath { get; set; }
    [ObservableProperty] public partial IReadOnlyList<ProfileOption> ProfileChoices { get; set; } = [];
    [ObservableProperty] public partial ProfileOption? SelectedProfile { get; set; }
    [ObservableProperty] public partial string Drive { get; set; }
    [ObservableProperty] public partial bool Verify { get; set; } = true;
    [ObservableProperty] public partial bool Understood { get; set; }
    [ObservableProperty] public partial bool IsRunning { get; set; }
    [ObservableProperty] public partial IReadOnlyList<SafetyIssue> Issues { get; set; } = [];
    [ObservableProperty] public partial string? BlockedReason { get; set; }
    [ObservableProperty] public partial string CommandPreview { get; set; } = "";
    [ObservableProperty] public partial JobRecord? LastResult { get; set; }
    [ObservableProperty] public partial string? ImageSummary { get; set; }

    public bool IsRawImage => ImagePath is not null && GwCommands.RawFluxExtensions.Contains(Path.GetExtension(ImagePath));

    partial void OnImagePathChanged(string? value)
    {
        Understood = false;
        LastResult = null;
        if (value is null) { ProfileChoices = []; SelectedProfile = null; ImageSummary = null; Refresh(); return; }

        var ext = Path.GetExtension(value);
        var size = File.Exists(value) ? new FileInfo(value).Length : 0;
        if (GwCommands.RawFluxExtensions.Contains(ext))
        {
            ProfileChoices = [ProfileOption.Unknown with { Title = "Raw flux — written as captured", Detail = "" }];
        }
        else
        {
            // Prefer candidates whose expected size matches; still list the rest so the user decides.
            ProfileChoices = [.. _formats.CandidatesForExtension(ext)
                .OrderByDescending(p => p.ExpectedSectorImageBytes == size)
                .Select(ProfileOption.From)];
        }
        // Auto-select only when exactly one candidate fits; ambiguous images require a deliberate choice.
        var exact = ProfileChoices.Where(p => p.Profile is null || p.Profile.ExpectedSectorImageBytes == size).ToList();
        SelectedProfile = exact.Count == 1 ? exact[0] : null;
        ImageSummary = $"{Path.GetFileName(value)} · {ext.TrimStart('.').ToUpperInvariant()} · {size:N0} bytes";
        Refresh();
    }

    partial void OnSelectedProfileChanged(ProfileOption? value)
    {
        Understood = false;
        var p = value?.Profile;
        TrackMap.Reset(p?.Cylinders ?? 84, p?.Heads ?? 2);
        Refresh();
    }

    partial void OnUnderstoodChanged(bool value) => Refresh();
    partial void OnDriveChanged(string value) { Understood = false; Refresh(); }
    partial void OnVerifyChanged(bool value) => Refresh();
    partial void OnIsRunningChanged(bool value) => Refresh();

    public void OnNavigatedTo() => Refresh();

    private WriteRequest? BuildRequest() => ImagePath is null ? null
        : new WriteRequest(ImagePath, SelectedProfile?.Profile, new DeviceTarget(_settings.Load().DevicePort, Drive), Verify);

    private void Refresh()
    {
        OnPropertyChanged(nameof(IsRawImage));
        var issues = new List<SafetyIssue>();
        CommandPreview = "";
        if (ImagePath is not null)
        {
            if (!IsRawImage && SelectedProfile is null)
                issues.Add(new SafetyIssue(true, "Choose the disk format. FluxForge won't guess from the file extension."));
            else
                issues.AddRange(OutputGuard.CheckWriteImage(ImagePath, SelectedProfile?.Profile));
            try
            {
                if (BuildRequest() is { } req && (IsRawImage || SelectedProfile is not null))
                    CommandPreview = GwCommands.Preview(State.Tools?.ExecutablePath ?? "gw.exe", GwCommands.Write(req));
            }
            catch (GwCommandException e) { issues.Add(new SafetyIssue(true, e.Message)); }
        }
        Issues = issues;

        BlockedReason =
            IsRunning ? null :
            ImagePath is null ? "Choose an image to write." :
            !State.ToolsReady ? "Install the Greaseweazle host tools first (Home)." :
            !State.DeviceReady ? "Connect a Greaseweazle — " + State.Device.Message :
            issues.FirstOrDefault(i => i.Blocking)?.Message ??
            (!Understood ? "Tick “I understand this will overwrite data on the floppy” to enable writing." : null);
        WriteCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void BrowseImage()
    {
        if (_dialogs.PickFile("Choose a disk image to write",
                "Disk images (*.adf;*.st;*.img;*.ima;*.d64;*.scp)|*.adf;*.st;*.img;*.ima;*.d64;*.scp|All files (*.*)|*.*") is { } path)
            ImagePath = path;
    }

    [RelayCommand]
    private void ReadOriginalFirst() => _navigator.Navigate(Pages.Read);

    private bool CanWrite => !IsRunning && BlockedReason is null;

    [RelayCommand(CanExecute = nameof(CanWrite))]
    private async Task WriteAsync()
    {
        if (State.Tools is not { } tools || BuildRequest() is not { } request) return;
        var confirmed = await _dialogs.ConfirmAsync("Overwrite this floppy?",
            $"Everything on the disk in drive {Drive} will be replaced with {Path.GetFileName(request.ImagePath)}.\n\n" +
            "This cannot be undone. If the disk may hold anything you care about, read it first.",
            "Overwrite disk");
        if (!confirmed) return;

        _settings.Save(_settings.Load() with { Drive = Drive });
        _cts = new CancellationTokenSource();
        IsRunning = true;
        Console.Start(CommandPreview);
        State.BeginJob();
        var progress = new Progress<JobProgress>(p =>
        {
            if (p.Line is { } line) Console.Append(line);
            if (p.Track is { } track) Console.CurrentTrack = $"Cylinder {track.Cylinder}, side {track.Head}";
        });
        try
        {
            LastResult = await Task.Run(() => _jobs.RunWriteAsync(new WriteJob(tools, request, State.Device.Info), progress, _cts.Token));
            Console.Stop(LastResult.Status);
            TrackMap.Load(LastResult.Tracks);
        }
        catch (Exception e) when (e is DeviceBusyException or InvalidOperationException or GwCommandException or IOException)
        {
            Console.Note("[FluxForge] " + e.Message);
            Console.Stop(JobStatus.Failed);
        }
        finally
        {
            IsRunning = false;
            Understood = false; // every write needs a fresh acknowledgement
            _cts.Dispose();
            _cts = null;
            await State.EndJobAsync();
        }
    }

    private bool CanCancel => IsRunning;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async Task CancelAsync()
    {
        if (await _dialogs.ConfirmAsync("Stop writing?", "Stopping mid-write leaves the floppy in an unknown, probably unusable state.", "Stop write"))
            _cts?.Cancel();
    }
}
