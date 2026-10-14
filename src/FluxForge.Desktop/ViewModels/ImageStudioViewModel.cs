using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxForge.Application.Formats;
using FluxForge.Application.Gw;
using FluxForge.Application.Jobs;
using FluxForge.Application.Safety;
using FluxForge.Desktop.Services;

namespace FluxForge.Desktop.ViewModels;

/// <summary>
/// Decode a saved raw flux capture (.scp) into a sector image with any format, without touching the
/// disk again. This is how a capture taken with the wrong format is recovered.
/// </summary>
public sealed partial class ImageStudioViewModel : ObservableObject, IPageLifecycle
{
    private readonly JobRunner _jobs;
    private readonly FormatRegistry _formats;
    private readonly IDialogService _dialogs;
    private CancellationTokenSource? _cts;

    public ImageStudioViewModel(AppState state, JobRunner jobs, FormatRegistry formats, IDialogService dialogs)
    {
        State = state;
        _jobs = jobs;
        _formats = formats;
        _dialogs = dialogs;
        Profiles = [.. formats.Profiles.Select(ProfileOption.From)];
        SelectedProfile = Profiles[0];
        state.PropertyChanged += (_, _) => Refresh();
    }

    public AppState State { get; }
    public IReadOnlyList<ProfileOption> Profiles { get; }
    public TrackMapViewModel TrackMap { get; } = new();
    public JobConsoleViewModel Console { get; } = new();

    [ObservableProperty] public partial string? InputPath { get; set; }
    [ObservableProperty] public partial ProfileOption SelectedProfile { get; set; }
    [ObservableProperty] public partial string OutputFileName { get; set; } = "";
    [ObservableProperty] public partial bool IsRunning { get; set; }
    [ObservableProperty] public partial JobRecord? LastResult { get; set; }
    [ObservableProperty] public partial string? InputSummary { get; set; }
    [ObservableProperty] public partial string? BlockedReason { get; set; }
    [ObservableProperty] public partial IReadOnlyList<SafetyIssue> Issues { get; set; } = [];
    [ObservableProperty] public partial string CommandPreview { get; set; } = "";

    /// <summary>A better-fitting format found by the last decode, offered as a one-click retry.</summary>
    public ProfileOption? Suggestion =>
        LastResult?.SuggestedProfileId is { } id ? Profiles.FirstOrDefault(p => p.Profile?.Id == id) : null;

    public string? InputFile =>
        InputPath?.Trim().Trim('"') is { Length: > 0 } p && Path.IsPathFullyQualified(p) && File.Exists(p) ? p : null;

    public string? OutputPath =>
        InputFile is { } input && OutputFileName.Trim().Length > 0
            ? Path.Combine(Path.GetDirectoryName(input)!, OutputFileName.Trim())
            : null;

    partial void OnInputPathChanged(string? value)
    {
        LastResult = null;
        if (InputFile is { } f)
            InputSummary = $"{Path.GetFileName(f)} · {new FileInfo(f).Length / 1024.0 / 1024.0:0.0} MB raw flux capture";
        else
            InputSummary = string.IsNullOrWhiteSpace(value) ? null : "File not found.";
        SuggestOutputName();
    }

    partial void OnSelectedProfileChanged(ProfileOption value)
    {
        var p = value.Profile;
        if (!IsRunning) TrackMap.Reset(p?.Cylinders ?? 80, p?.Heads ?? 2);
        SuggestOutputName();
    }

    partial void OnOutputFileNameChanged(string value) => Refresh();
    partial void OnIsRunningChanged(bool value) => Refresh();
    partial void OnLastResultChanged(JobRecord? value) => OnPropertyChanged(nameof(Suggestion));

    public void OnNavigatedTo() => Refresh();

    /// <summary>"capture.scp" + Atari ST 800K → "capture (atarist.800).st", numbered if it already exists.</summary>
    private void SuggestOutputName()
    {
        if (InputFile is not { } input || SelectedProfile.Profile is not { } p) { Refresh(); return; }
        var dir = Path.GetDirectoryName(input)!;
        var stem = $"{Path.GetFileNameWithoutExtension(input)} ({p.GwFormat})";
        var name = stem + p.SectorExtension;
        for (var i = 2; File.Exists(Path.Combine(dir, name)); i++)
            name = $"{stem} {i}{p.SectorExtension}";
        OutputFileName = name;
        Refresh();
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(OutputPath));
        var issues = new List<SafetyIssue>();
        CommandPreview = "";
        if (InputFile is { } input && OutputPath is { } output && SelectedProfile.Profile is { } profile)
        {
            if (!GwCommands.RawFluxExtensions.Contains(Path.GetExtension(input)))
                issues.Add(new SafetyIssue(true, "Choose a raw flux capture (.scp). Sector images are already decoded."));
            if (!string.Equals(Path.GetExtension(output), profile.SectorExtension, StringComparison.OrdinalIgnoreCase))
                issues.Add(new SafetyIssue(true, $"{profile.DisplayName} images use the {profile.SectorExtension} extension."));
            issues.AddRange(OutputGuard.CheckReadOutput(output, CaptureMode.SectorImage, profile, _ => null));
            try { CommandPreview = GwCommands.Preview(State.Tools?.ExecutablePath ?? "gw.exe", GwCommands.Convert(input, output, profile)); }
            catch (GwCommandException e) { issues.Add(new SafetyIssue(true, e.Message)); }
        }
        Issues = issues;
        BlockedReason =
            IsRunning ? null :
            InputFile is null ? "Choose a raw capture (.scp) to decode." :
            !State.ToolsReady ? "Install the Greaseweazle host tools first (Home)." :
            issues.FirstOrDefault(i => i.Blocking)?.Message;
        DecodeCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void BrowseInput()
    {
        if (_dialogs.PickFile("Choose a raw flux capture", "Raw flux captures (*.scp)|*.scp|All files (*.*)|*.*") is { } path)
            InputPath = path;
    }

    private bool CanDecode => !IsRunning && BlockedReason is null;

    [RelayCommand(CanExecute = nameof(CanDecode))]
    private async Task DecodeAsync()
    {
        if (State.Tools is not { } tools || InputFile is not { } input || OutputPath is not { } output || SelectedProfile.Profile is not { } profile) return;
        _cts = new CancellationTokenSource();
        IsRunning = true;
        LastResult = null;
        TrackMap.Reset(profile.Cylinders, profile.Heads);
        TrackMap.Start();
        Console.Start(CommandPreview);
        var progress = new Progress<JobProgress>(p =>
        {
            if (p.Line is { } line) Console.Append(line);
            if (p.Track is { } track)
            {
                TrackMap.Advance(track);
                Console.CurrentTrack = $"Cylinder {track.Cylinder}, side {track.Head}";
            }
        });
        try
        {
            LastResult = await Task.Run(() => _jobs.RunDecodeAsync(new DecodeJob(tools, input, output, profile), progress, _cts.Token));
            TrackMap.Stop();
            Console.Stop(LastResult.Status);
            TrackMap.Load(LastResult.Tracks);
        }
        catch (Exception e) when (e is InvalidOperationException or GwCommandException or IOException)
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
            SuggestOutputName();
        }
    }

    private bool CanCancel => IsRunning;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel() => _cts?.Cancel();

    /// <summary>One click: switch to the suggested format and decode again from the same capture.</summary>
    [RelayCommand]
    private async Task DecodeWithSuggestionAsync()
    {
        if (Suggestion is not { } s) return;
        var keep = LastResult;
        SelectedProfile = s;
        LastResult = keep; // keep the explanation visible until the new decode starts
        if (DecodeCommand.CanExecute(null)) await DecodeAsync();
    }

    [RelayCommand]
    private void ShowResult()
    {
        if (LastResult?.Artifacts.LastOrDefault() is { } artifact)
            Process.Start(new ProcessStartInfo("explorer.exe") { ArgumentList = { "/select,", artifact.Path }, UseShellExecute = false });
    }
}
