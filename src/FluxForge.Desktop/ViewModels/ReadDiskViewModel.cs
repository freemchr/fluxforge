using System.Diagnostics;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxForge.Application.Formats;
using FluxForge.Application.Gw;
using FluxForge.Application.Jobs;
using FluxForge.Application.Safety;
using FluxForge.Application.Settings;
using FluxForge.Desktop.Services;

namespace FluxForge.Desktop.ViewModels;

public sealed record ProfileOption(DiskProfile? Profile, string Title, string Detail)
{
    public static ProfileOption Unknown { get; } = new(null, "Unknown / not sure", "Raw flux only — safest when you don't know the format");
    public static ProfileOption From(DiskProfile p) => new(p, $"{p.System} — {p.DisplayName}", $"diskdef {p.GwFormat} · {p.SectorExtension}");
}

public sealed partial class ReadDiskViewModel : ObservableObject, IPageLifecycle
{
    private readonly JobRunner _jobs;
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _cts;

    public ReadDiskViewModel(AppState state, JobRunner jobs, FormatRegistry formats, ISettingsStore settings, IDialogService dialogs)
    {
        State = state;
        _jobs = jobs;
        _settings = settings;
        _dialogs = dialogs;
        Profiles = [.. formats.Profiles.Select(ProfileOption.From), ProfileOption.Unknown];
        SelectedProfile = Profiles[0];
        var s = settings.Load();
        OutputFolder = s.DefaultOutputFolder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FluxForge");
        Drive = s.Drive;
        state.PropertyChanged += (_, _) => Refresh();
        ResetMap();
    }

    public AppState State { get; }
    public IReadOnlyList<ProfileOption> Profiles { get; }
    public IReadOnlyList<string> Drives { get; } = ["A", "B", "0", "1", "2"];
    public IReadOnlyList<string> RevolutionChoices { get; } = ["Default", "1", "2", "3", "5", "8"];
    public IReadOnlyList<string> RetryChoices { get; } = ["Default", "0", "1", "3", "5", "10", "20"];
    public TrackMapViewModel TrackMap { get; } = new();
    public JobConsoleViewModel Console { get; } = new();

    [ObservableProperty] public partial ProfileOption SelectedProfile { get; set; }
    [ObservableProperty] public partial bool IsPreservation { get; set; } = true;
    [ObservableProperty] public partial string OutputFolder { get; set; }
    [ObservableProperty] public partial string DiskLabel { get; set; } = "";
    [ObservableProperty] public partial string Notes { get; set; } = "";
    [ObservableProperty] public partial string Drive { get; set; }
    [ObservableProperty] public partial string Revolutions { get; set; } = "Default";
    [ObservableProperty] public partial string Retries { get; set; } = "Default";
    [ObservableProperty] public partial bool IsRunning { get; set; }
    [ObservableProperty] public partial JobRecord? LastResult { get; set; }
    [ObservableProperty] public partial string CommandPreview { get; set; } = "";
    [ObservableProperty] public partial IReadOnlyList<SafetyIssue> Issues { get; set; } = [];
    [ObservableProperty] public partial string? BlockedReason { get; set; }

    public bool IsSectorImage
    {
        get => !IsPreservation;
        set => IsPreservation = !value;
    }

    public bool CanUseSectorImage => SelectedProfile.Profile is not null;

    public string CaptureExplanation => IsPreservation
        ? SelectedProfile.Profile is null
            ? "Captures the disk's raw magnetic flux to an SCP file. Nothing is decoded or altered."
            : $"Captures raw flux to an SCP file using --raw, so the measurements are stored exactly as read. FluxForge also decodes {SelectedProfile.Profile.GwFormat} while reading to report bad tracks."
        : $"Decodes sectors into a {SelectedProfile.Profile?.SectorExtension} image for emulators. Copy protection and timing information are lost — keep a raw capture for preservation.";

    public string OutputFileName
    {
        get
        {
            var stem = Sanitize(DiskLabel);
            if (stem.Length == 0) stem = "disk-" + DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
            return stem + (IsPreservation ? ".scp" : SelectedProfile.Profile?.SectorExtension ?? ".scp");
        }
    }

    public string OutputPath => Path.Combine(OutputFolder ?? "", OutputFileName);

    partial void OnSelectedProfileChanged(ProfileOption value)
    {
        if (value.Profile is null) IsPreservation = true; // unknown format: preservation-first default (PRD §11)
        if (!IsRunning) ResetMap();
        Refresh();
    }

    partial void OnIsPreservationChanged(bool value) => Refresh();
    partial void OnOutputFolderChanged(string value) => Refresh();
    partial void OnDiskLabelChanged(string value) => Refresh();
    partial void OnDriveChanged(string value) => Refresh();
    partial void OnRevolutionsChanged(string value) => Refresh();
    partial void OnRetriesChanged(string value) => Refresh();
    partial void OnIsRunningChanged(bool value) => Refresh();

    public void OnNavigatedTo() => Refresh();

    private ReadRequest BuildRequest(string outputPath) => new(
        outputPath,
        IsPreservation ? CaptureMode.Preservation : CaptureMode.SectorImage,
        SelectedProfile.Profile,
        new DeviceTarget(_settings.Load().DevicePort, Drive),
        ParseChoice(Revolutions),
        ParseChoice(Retries));

    private void Refresh()
    {
        OnPropertyChanged(nameof(IsSectorImage));
        OnPropertyChanged(nameof(CanUseSectorImage));
        OnPropertyChanged(nameof(CaptureExplanation));
        OnPropertyChanged(nameof(OutputFileName));
        OnPropertyChanged(nameof(OutputPath));

        var mode = IsPreservation ? CaptureMode.Preservation : CaptureMode.SectorImage;
        var issues = new List<SafetyIssue>();
        try
        {
            var args = GwCommands.Read(BuildRequest(OutputPath));
            CommandPreview = GwCommands.Preview(State.Tools?.ExecutablePath ?? "gw.exe", args);
        }
        catch (GwCommandException e)
        {
            CommandPreview = "";
            issues.Add(new SafetyIssue(true, e.Message));
        }
        if (!Directory.Exists(OutputFolder))
            issues.Add(new SafetyIssue(false, "The output folder will be created."));
        else
            issues.AddRange(OutputGuard.CheckReadOutput(OutputPath, mode, SelectedProfile.Profile, FreeSpace));
        Issues = issues;

        BlockedReason =
            IsRunning ? null :
            !State.ToolsReady ? "Install the Greaseweazle host tools first (Home)." :
            !State.DeviceReady ? "Connect a Greaseweazle — " + State.Device.Message :
            issues.FirstOrDefault(i => i.Blocking)?.Message;
        StartCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private bool CanStart => !IsRunning && BlockedReason is null;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (State.Tools is not { } tools) return;
        Directory.CreateDirectory(OutputFolder);
        var request = BuildRequest(OutputPath);
        var preview = CommandPreview;
        _settings.Save(_settings.Load() with { DefaultOutputFolder = OutputFolder, Drive = Drive });

        _cts = new CancellationTokenSource();
        IsRunning = true;
        LastResult = null;
        ResetMap();
        Console.Start(preview);
        State.BeginJob();
        var progress = new Progress<JobProgress>(p =>
        {
            if (p.Line is { } line) Console.Append(line);
            if (p.Track is { } track)
            {
                TrackMap.Update(track);
                Console.CurrentTrack = $"Cylinder {track.Cylinder}, side {track.Head}";
            }
        });

        try
        {
            LastResult = await Task.Run(() => _jobs.RunReadAsync(new ReadJob(tools, request, State.Device.Info, NullIfEmpty(DiskLabel), NullIfEmpty(Notes)), progress, _cts.Token));
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
            _cts.Dispose();
            _cts = null;
            await State.EndJobAsync();
        }
    }

    private bool CanCancel => IsRunning;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private async Task CancelAsync()
    {
        if (await _dialogs.ConfirmAsync("Stop reading?", "The capture so far will be kept and labelled INCOMPLETE.", "Stop"))
            _cts?.Cancel();
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        if (_dialogs.PickFolder("Choose where to save disk images", Directory.Exists(OutputFolder) ? OutputFolder : null) is { } folder)
            OutputFolder = folder;
    }

    [RelayCommand]
    private void ShowResult()
    {
        if (LastResult?.Artifacts.FirstOrDefault() is { } artifact)
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", artifact.Path }, UseShellExecute = false });
    }

    [RelayCommand]
    private void NewCapture()
    {
        LastResult = null;
        DiskLabel = "";
        Notes = "";
        ResetMap();
        Refresh();
    }

    private void ResetMap()
    {
        var p = SelectedProfile.Profile;
        TrackMap.Reset(p?.Cylinders ?? 84, p?.Heads ?? 2);
    }

    private static int? ParseChoice(string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    internal static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string([.. name.Trim().Select(c => invalid.Contains(c) ? '_' : c)]);
        return cleaned.TrimEnd('.', ' ');
    }

    private static long? FreeSpace(string dir)
    {
        try { return new DriveInfo(Path.GetPathRoot(dir)!).AvailableFreeSpace; }
        catch (Exception e) when (e is ArgumentException or IOException or UnauthorizedAccessException) { return null; }
    }
}
