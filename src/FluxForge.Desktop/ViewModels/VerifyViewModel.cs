using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxForge.Application.Formats;
using FluxForge.Application.Gw;
using FluxForge.Application.Jobs;
using FluxForge.Application.Safety;
using FluxForge.Application.Settings;
using FluxForge.Application.Verify;
using FluxForge.Desktop.Services;

namespace FluxForge.Desktop.ViewModels;

/// <summary>
/// Read the disk and compare it sector by sector with an image the user chooses. Read only: the disk is
/// never written and the readback only lives in a temp folder for the duration of the job.
/// </summary>
public sealed partial class VerifyViewModel : ObservableObject, IPageLifecycle
{
    private const int MaxListedProblems = 200;

    private readonly JobRunner _jobs;
    private readonly FormatRegistry _formats;
    private readonly ISettingsStore _settings;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _cts;

    public VerifyViewModel(AppState state, JobRunner jobs, FormatRegistry formats, ISettingsStore settings, IDialogService dialogs)
    {
        State = state;
        _jobs = jobs;
        _formats = formats;
        _settings = settings;
        _dialogs = dialogs;
        Drive = settings.Load().Drive;
        state.PropertyChanged += (_, _) => Refresh();
        TrackMap.Reset(80, 2);
    }

    public AppState State { get; }
    public IReadOnlyList<string> Drives { get; } = ["A", "B", "0", "1", "2", "3"];
    public TrackMapViewModel TrackMap { get; } = new() { IsVerify = true };
    public JobConsoleViewModel Console { get; } = new();

    [ObservableProperty] public partial string? ImagePath { get; set; }
    [ObservableProperty] public partial IReadOnlyList<ProfileOption> ProfileChoices { get; set; } = [];
    [ObservableProperty] public partial ProfileOption? SelectedProfile { get; set; }
    [ObservableProperty] public partial string Drive { get; set; }
    [ObservableProperty] public partial bool IsRunning { get; set; }
    [ObservableProperty] public partial IReadOnlyList<SafetyIssue> Issues { get; set; } = [];
    [ObservableProperty] public partial string? BlockedReason { get; set; }
    [ObservableProperty] public partial string CommandPreview { get; set; } = "";
    [ObservableProperty] public partial string? ImageSummary { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Comparison), nameof(ProblemLines), nameof(MoreProblems))]
    public partial JobRecord? LastResult { get; set; }

    public VerifySummary? Comparison => LastResult?.Verify;

    public IReadOnlyList<string> ProblemLines =>
        [.. (Comparison?.Problems ?? []).Take(MaxListedProblems).Select(p => $"Cylinder {p.Cylinder}, side {p.Head}, sector {p.Sector}: {Describe(p.Verdict)}")];

    public string? MoreProblems =>
        Comparison is { Problems.Count: > MaxListedProblems } c ? $"…and {c.Problems.Count - MaxListedProblems:N0} more (full list in the Activity report)." : null;

    private static string Describe(SectorVerdict v) => v switch
    {
        SectorVerdict.Differs => "differs from the image",
        SectorVerdict.UnreadableOnDisk => "could not be read from the disk",
        SectorVerdict.NotInImage => "no data in the image to compare",
        _ => "matches",
    };

    public string? ImageFile =>
        ImagePath?.Trim().Trim('"') is { Length: > 0 } p && Path.IsPathFullyQualified(p) && File.Exists(p) ? p : null;

    public bool IsRawImage => ImageFile is { } f && GwCommands.RawFluxExtensions.Contains(Path.GetExtension(f));

    /// <summary>Open the page ready to check a disk against <paramref name="imagePath"/> (e.g. straight after writing it).</summary>
    public void Prefill(string imagePath, DiskProfile? profile)
    {
        ImagePath = imagePath;
        if (profile is not null && ProfileChoices.FirstOrDefault(p => p.Profile?.Id == profile.Id) is { } match)
            SelectedProfile = match;
    }

    partial void OnImagePathChanged(string? value)
    {
        LastResult = null;
        value = value?.Trim().Trim('"');
        if (ImageFile is not { } file)
        {
            ProfileChoices = [];
            SelectedProfile = null;
            ImageSummary = string.IsNullOrEmpty(value) ? null : "File not found.";
            Refresh();
            return;
        }

        var ext = Path.GetExtension(file);
        var size = new FileInfo(file).Length;
        if (IsRawImage)
        {
            // A raw capture can be decoded with any format; the user says which one the disk uses.
            ProfileChoices = [.. _formats.Profiles.Select(ProfileOption.From)];
            SelectedProfile = null;
            ImageSummary = $"{Path.GetFileName(file)} · raw flux capture, decoded before comparing";
        }
        else
        {
            ProfileChoices = [.. _formats.CandidatesForExtension(ext)
                .OrderByDescending(p => p.ExpectedSectorImageBytes == size)
                .Select(ProfileOption.From)];
            var exact = ProfileChoices.Where(p => p.Profile!.ExpectedSectorImageBytes == size).ToList();
            SelectedProfile = exact.Count == 1 ? exact[0] : null;
            ImageSummary = $"{Path.GetFileName(file)} · {ext.TrimStart('.').ToUpperInvariant()} · {size:N0} bytes";
        }
        Refresh();
    }

    partial void OnSelectedProfileChanged(ProfileOption? value)
    {
        LastResult = null;
        if (!IsRunning) TrackMap.Reset(value?.Profile?.Cylinders ?? 80, value?.Profile?.Heads ?? 2);
        Refresh();
    }

    partial void OnDriveChanged(string value) => Refresh();
    partial void OnIsRunningChanged(bool value) => Refresh();

    public void OnNavigatedTo() => Refresh();

    private DeviceTarget Target => new(_settings.Load().DevicePort, Drive);

    private void Refresh()
    {
        OnPropertyChanged(nameof(IsRawImage));
        var issues = new List<SafetyIssue>();
        CommandPreview = "";
        if (ImageFile is { } image)
        {
            issues.AddRange(OutputGuard.CheckVerifyImage(image, SelectedProfile?.Profile));
            if (SelectedProfile?.Profile is { } profile)
            {
                try
                {
                    var exe = State.Tools?.ExecutablePath ?? "gw.exe";
                    var temp = Path.Combine(Path.GetTempPath(), "fluxforge-verify");
                    var read = GwCommands.Preview(exe, GwCommands.Read(new ReadRequest(Path.Combine(temp, "disk" + profile.SectorExtension),
                        CaptureMode.SectorImage, profile, Target)));
                    var preview = IsRawImage
                        ? GwCommands.Preview(exe, GwCommands.Convert(image, Path.Combine(temp, "image" + profile.SectorExtension), profile)) + Environment.NewLine + read
                        : read;
                    // The real temp folder is created per job; show where it lives without the user's profile path.
                    CommandPreview = preview.Replace(Path.GetTempPath(), @"%TEMP%\", StringComparison.OrdinalIgnoreCase);
                }
                catch (GwCommandException e) { issues.Add(new SafetyIssue(true, e.Message)); }
            }
        }
        Issues = issues;

        BlockedReason =
            IsRunning ? null :
            ImageFile is null ? "Choose the image to compare the disk with." :
            !State.ToolsReady ? "Install the Greaseweazle host tools first (Home)." :
            !State.DeviceReady ? "Connect a Greaseweazle — " + State.Device.Message :
            issues.FirstOrDefault(i => i.Blocking)?.Message;
        VerifyCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void BrowseImage()
    {
        if (_dialogs.PickFile("Choose the image to compare with",
                "Disk images (*.adf;*.st;*.img;*.ima;*.d64;*.scp)|*.adf;*.st;*.img;*.ima;*.d64;*.scp|All files (*.*)|*.*") is { } path)
            ImagePath = path;
    }

    private bool CanVerify => !IsRunning && BlockedReason is null;

    [RelayCommand(CanExecute = nameof(CanVerify))]
    private async Task VerifyAsync()
    {
        if (State.Tools is not { } tools || ImageFile is not { } image || SelectedProfile?.Profile is not { } profile) return;
        _settings.Save(_settings.Load() with { Drive = Drive });
        _cts = new CancellationTokenSource();
        IsRunning = true;
        LastResult = null;
        TrackMap.Reset(profile.Cylinders, profile.Heads);
        Console.Start(CommandPreview);
        State.BeginJob();
        var progress = new Progress<JobProgress>(p =>
        {
            if (p.Line is { } line) Console.Append(line);
            if (p.Track is { } track)
            {
                TrackMap.Advance(track);
                Console.CurrentTrack = $"Read cylinder {track.Cylinder}, side {track.Head}";
            }
        });
        try
        {
            if (!IsRawImage) TrackMap.Start();
            LastResult = await Task.Run(() => _jobs.RunVerifyAsync(new VerifyJob(tools, image, profile, Target, State.Device.Info), progress, _cts.Token));
            TrackMap.Stop();
            Console.Stop(LastResult.Status);
            Console.CurrentTrack = LastResult.Verify is { } v ? $"{v.Matched:N0} of {v.Total:N0} sectors match" : "";
            TrackMap.Reset(profile.Cylinders, profile.Heads);
            TrackMap.Load(LastResult.Tracks);
        }
        catch (Exception e) when (e is DeviceBusyException or InvalidOperationException or GwCommandException or IOException)
        {
            Console.Note("[FluxForge] " + e.Message);
            Console.Stop(JobStatus.Failed);
        }
        finally
        {
            TrackMap.Stop();
            IsRunning = false;
            _cts.Dispose();
            _cts = null;
            await State.EndJobAsync();
        }
    }

    private bool CanCancel => IsRunning;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();
}
